using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// High-performance Dapper-based repository for Approval Center operations.
/// Implements aggressive caching, stored procedures, and performance monitoring.
///
/// Performance Targets:
/// - Inbox load: 200ms for 50 items
/// - Details load: 100ms
/// - Decision submission: 500ms
/// - Bulk approve (10 items): 3 seconds
/// - Stats query: 100ms (cached)
/// </summary>
public class ApprovalRepository : IApprovalRepository
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;
    private readonly IMemoryCache _cache;
    private readonly DataAccessLibrary.Services.IProcessApprovalService? _processApprovalService;

    // Cache configuration
    private const int CacheExpirationMinutes = 5;
    private const int StatsCacheExpirationMinutes = 15;
    private const int CommandTimeoutSeconds = 30;

    // Performance thresholds for monitoring
    private const int SlowQueryThresholdMs = 1000;
    private const int CriticalQueryThresholdMs = 3000;

    public ApprovalRepository(
        IConfiguration configuration,
        IGlobalLogger logger,
        IMemoryCache cache,
        DataAccessLibrary.Services.IProcessApprovalService? processApprovalService = null)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
        _cache = cache;
        _processApprovalService = processApprovalService;
    }

    #region GetPendingApprovalsAsync

    public async Task<ApprovalInboxResult> GetPendingApprovalsAsync(
        Guid approverId,
        ApprovalFilter filter,
        ApprovalPagination pagination,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var cacheKey = GetInboxCacheKey(approverId, filter, pagination);

        try
        {
            // Try cache first (L1 in-memory)
            if (_cache.TryGetValue<ApprovalInboxResult>(cacheKey, out var cachedResult) && cachedResult != null)
            {
                _logger.LogDebug("Approval inbox cache HIT for approver {ApproverId}", approverId);
                return cachedResult;
            }

            _logger.LogDebug("Approval inbox cache MISS for approver {ApproverId} - querying database", approverId);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@ApproverId", approverId);
            parameters.Add("@ApprovalType", filter.ApprovalType);
            parameters.Add("@RiskLevel", filter.RiskLevel);
            parameters.Add("@CampaignId", filter.CampaignId);
            parameters.Add("@ResourceType", filter.ResourceType);
            parameters.Add("@OnlyOverdue", filter.OnlyOverdue ?? false);
            parameters.Add("@SearchTerm", filter.SearchTerm);
            parameters.Add("@SortBy", filter.SortBy);
            parameters.Add("@SortDirection", filter.SortDirection);
            parameters.Add("@PageNumber", pagination.PageNumber);
            parameters.Add("@PageSize", pagination.PageSize);

            var command = new CommandDefinition(
                "usp_GetPendingApprovals",
                parameters,
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken,
                commandTimeout: CommandTimeoutSeconds);

            using var multi = await connection.QueryMultipleAsync(command).ConfigureAwait(false);

            // First result set: total count
            var totalCount = await multi.ReadFirstAsync<int>().ConfigureAwait(false);

            // Second result set: approval items
            var approvals = (await multi.ReadAsync<ApprovalInboxItem>().ConfigureAwait(false)).ToList();

            var result = new ApprovalInboxResult
            {
                Approvals = approvals,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            };

            // Cache for 5 minutes
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheExpirationMinutes))
                .SetPriority(CacheItemPriority.Normal);

            _cache.Set(cacheKey, result, cacheOptions);

            stopwatch.Stop();
            LogPerformance("GetPendingApprovals", stopwatch.ElapsedMilliseconds, approvals.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending approvals for approver {ApproverId}", approverId);
            throw;
        }
    }

    #endregion

    #region GetApprovalDetailsAsync

    public async Task<ApprovalDetails?> GetApprovalDetailsAsync(
        Guid approvalId,
        string approvalType,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@ApprovalId", approvalId);
            parameters.Add("@ApprovalType", approvalType);

            var command = new CommandDefinition(
                "usp_GetApprovalDetails",
                parameters,
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken,
                commandTimeout: CommandTimeoutSeconds);

            using var multi = await connection.QueryMultipleAsync(command).ConfigureAwait(false);

            // Result sets vary based on approval type
            var details = new ApprovalDetails
            {
                ApprovalId = approvalId,
                ApprovalType = approvalType
            };

            if (approvalType == "ReviewAssignment")
            {
                // Set 1: Assignment
                details.ReviewAssignment = await multi.ReadFirstOrDefaultAsync<AccessReviewAssignment>().ConfigureAwait(false);
                if (details.ReviewAssignment == null) return null;

                // Set 2: Campaign
                details.Campaign = await multi.ReadFirstOrDefaultAsync<Campaign>().ConfigureAwait(false);

                // Set 3: Target Person
                details.TargetPerson = await multi.ReadFirstOrDefaultAsync<PersonIdentity>().ConfigureAwait(false);

                // Set 4: Previous Decisions
                details.PreviousDecisions = (await multi.ReadAsync<ReviewDecisionHistory>().ConfigureAwait(false)).ToList();

                // Set 5: Risk Analysis (from JSON)
                var riskJson = await multi.ReadFirstOrDefaultAsync<string>().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(riskJson))
                {
                    details.RiskAnalysis = JsonSerializer.Deserialize<RiskAnalysis>(riskJson);
                }
            }
            else if (approvalType == "AccessRequest")
            {
                // Set 1: Access Request
                details.AccessRequest = await multi.ReadFirstOrDefaultAsync<AccessRequest>().ConfigureAwait(false);
                if (details.AccessRequest == null) return null;

                // Set 2: Target Resource Info (JSON)
                details.TargetResourceInfo = await multi.ReadFirstOrDefaultAsync<string>().ConfigureAwait(false);

                // Set 3: Requester Info
                details.TargetPerson = await multi.ReadFirstOrDefaultAsync<PersonIdentity>().ConfigureAwait(false);
            }
            else if (approvalType == "ProcessApproval" && _processApprovalService != null)
            {
                details.ProcessInstance = await _processApprovalService.GetInstanceDetailsAsync(approvalId, cancellationToken);
                if (details.ProcessInstance == null) return null;
            }

            stopwatch.Stop();
            LogPerformance("GetApprovalDetails", stopwatch.ElapsedMilliseconds, 1);

            return details;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting approval details for {ApprovalId}", approvalId);
            throw;
        }
    }

    #endregion

    #region ApproveAsync

    public async Task<ApprovalResult> ApproveAsync(
        ApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            var nowUtc = DateTime.UtcNow;

            if (decision.ApprovalType == "ReviewAssignment")
            {
                // Get current state
                var assignment = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT Status, CampaignId, Decision, RiskScore, RiskLevel
                      FROM AccessReviewAssignments WITH (UPDLOCK)
                      WHERE Id = @ApprovalId",
                    new { decision.ApprovalId }, transaction).ConfigureAwait(false);

                if (assignment == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Review assignment not found" };
                if ((string)assignment.Status != "Pending")
                    return new ApprovalResult { Success = false, ErrorMessage = "Review assignment already processed with status: " + assignment.Status };

                // Update assignment
                await connection.ExecuteAsync(
                    @"UPDATE AccessReviewAssignments
                      SET Status = 'Completed', Decision = 'Approved',
                          Justification = @Justification, Comments = @Comments, CompletedAt = @NowUtc
                      WHERE Id = @ApprovalId",
                    new { decision.ApprovalId, decision.Justification, decision.Comments, NowUtc = nowUtc }, transaction).ConfigureAwait(false);

                // Update campaign stats
                await connection.ExecuteAsync(
                    @"UPDATE Campaigns SET
                          CompletedAssignments = (SELECT COUNT(*) FROM AccessReviewAssignments WHERE CampaignId = @CampaignId AND Status = 'Completed'),
                          CompletionPercentage = (SELECT CAST(COUNT(*) AS DECIMAL(5,2)) * 100.0 / NULLIF(TotalAssignments, 0) FROM AccessReviewAssignments WHERE CampaignId = @CampaignId AND Status = 'Completed'),
                          ModifiedAt = @NowUtc, ModifiedBy = @DecisionMakerName
                      WHERE Id = @CampaignId",
                    new { CampaignId = (Guid)assignment.CampaignId, NowUtc = nowUtc, decision.DecisionMakerName }, transaction).ConfigureAwait(false);

                // Insert decision history
                await connection.ExecuteAsync(
                    @"INSERT INTO ReviewDecisionHistory
                          (Id, AssignmentId, CampaignId, Decision, PreviousDecision, Justification, Comments,
                           DecisionMakerId, DecisionMakerName, DecisionMakerEmail, DecisionDate,
                           IpAddress, UserAgent, RiskScoreAtDecision, RiskLevelAtDecision, WasEscalated, WasDelegated)
                      VALUES
                          (@Id, @ApprovalId, @CampaignId, 'Approved', @PreviousDecision, @Justification, @Comments,
                           @DecisionMakerId, @DecisionMakerName, @DecisionMakerEmail, @NowUtc,
                           @IpAddress, @UserAgent, @RiskScore, @RiskLevel, 0, 0)",
                    new
                    {
                        Id = Guid.NewGuid(),
                        decision.ApprovalId,
                        CampaignId = (Guid)assignment.CampaignId,
                        PreviousDecision = (string?)assignment.Decision,
                        decision.Justification,
                        Comments = decision.Comments ?? ("Approved by " + decision.DecisionMakerName),
                        decision.DecisionMakerId,
                        decision.DecisionMakerName,
                        decision.DecisionMakerEmail,
                        NowUtc = nowUtc,
                        decision.IpAddress,
                        decision.UserAgent,
                        RiskScore = (int?)assignment.RiskScore ?? 0,
                        RiskLevel = (string?)assignment.RiskLevel
                    }, transaction).ConfigureAwait(false);

                transaction.Commit();
                InvalidateCache(decision.DecisionMakerId);

                stopwatch.Stop();
                LogPerformance("ApproveAssignment", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = nowUtc };
            }
            else if (decision.ApprovalType == "AccessRequest")
            {
                var currentStatus = await connection.ExecuteScalarAsync<string>(
                    "SELECT Status FROM AccessRequests WITH (UPDLOCK) WHERE Id = @ApprovalId",
                    new { decision.ApprovalId }, transaction).ConfigureAwait(false);

                if (currentStatus == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Access request not found" };
                if (currentStatus != "Pending")
                    return new ApprovalResult { Success = false, ErrorMessage = "Access request already processed with status: " + currentStatus };

                await connection.ExecuteAsync(
                    @"UPDATE AccessRequests SET Status = 'Approved',
                          ApproverId = CAST(@DecisionMakerId AS NVARCHAR(450)),
                          ApprovedAt = @NowUtc, ApprovalComments = @Justification
                      WHERE Id = @ApprovalId",
                    new { decision.ApprovalId, decision.DecisionMakerId, NowUtc = nowUtc, decision.Justification }, transaction).ConfigureAwait(false);

                // Create user access grant
                await connection.ExecuteAsync(
                    @"INSERT INTO UserAccess (Id, UserId, ResourceType, ResourceId, ResourceName, GrantedAt, GrantedBy, ExpiresAt, IsActive, AccessRequestId)
                      SELECT NEWID(), RequesterId, ResourceType, ResourceId, ResourceName, @NowUtc,
                             CAST(@DecisionMakerId AS NVARCHAR(450)),
                             CASE WHEN DurationDays > 0 THEN DATEADD(DAY, DurationDays, @NowUtc) ELSE NULL END, 1, Id
                      FROM AccessRequests WHERE Id = @ApprovalId",
                    new { decision.ApprovalId, decision.DecisionMakerId, NowUtc = nowUtc }, transaction).ConfigureAwait(false);

                transaction.Commit();
                InvalidateCache(decision.DecisionMakerId);

                stopwatch.Stop();
                LogPerformance("ApproveAccessRequest", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = nowUtc };
            }
            else if (decision.ApprovalType == "ProcessApproval")
            {
                if (_processApprovalService == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Process approval service not available" };

                // Verify instance exists and is waiting
                var status = await connection.ExecuteScalarAsync<string>(
                    "SELECT Status FROM ProcessInstances WHERE Id = @ApprovalId",
                    new { decision.ApprovalId }, transaction).ConfigureAwait(false);

                if (status == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Process instance not found" };
                if (status != "WaitingForApproval")
                    return new ApprovalResult { Success = false, ErrorMessage = "Process instance not waiting for approval, status: " + status };

                transaction.Commit();

                // Resume process outside transaction (engine manages its own)
                await _processApprovalService.ResumeProcessAsync(
                    decision.ApprovalId, decision.DecisionMakerName, decision.Justification, true, cancellationToken);

                InvalidateCache(decision.DecisionMakerId);

                stopwatch.Stop();
                LogPerformance("ApproveProcessApproval", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = nowUtc };
            }

            return new ApprovalResult { Success = false, ErrorMessage = "Invalid approval type: " + decision.ApprovalType };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving {ApprovalType} {ApprovalId}",
                decision.ApprovalType, decision.ApprovalId);
            throw;
        }
    }

    #endregion

    #region DenyAsync

    public async Task<ApprovalResult> DenyAsync(
        ApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            var nowUtc = DateTime.UtcNow;

            if (decision.ApprovalType == "ReviewAssignment")
            {
                var assignment = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT Status, CampaignId, Decision, RiskScore, RiskLevel
                      FROM AccessReviewAssignments WITH (UPDLOCK)
                      WHERE Id = @ApprovalId",
                    new { decision.ApprovalId }, transaction).ConfigureAwait(false);

                if (assignment == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Review assignment not found" };
                if ((string)assignment.Status != "Pending")
                    return new ApprovalResult { Success = false, ErrorMessage = "Review assignment already processed with status: " + assignment.Status };

                await connection.ExecuteAsync(
                    @"UPDATE AccessReviewAssignments
                      SET Status = 'Completed', Decision = 'Denied',
                          Justification = @Justification, Comments = @Comments, CompletedAt = @NowUtc
                      WHERE Id = @ApprovalId",
                    new { decision.ApprovalId, decision.Justification, decision.Comments, NowUtc = nowUtc }, transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(
                    @"UPDATE Campaigns SET
                          CompletedAssignments = (SELECT COUNT(*) FROM AccessReviewAssignments WHERE CampaignId = @CampaignId AND Status = 'Completed'),
                          CompletionPercentage = (SELECT CAST(COUNT(*) AS DECIMAL(5,2)) * 100.0 / NULLIF(TotalAssignments, 0) FROM AccessReviewAssignments WHERE CampaignId = @CampaignId AND Status = 'Completed'),
                          ModifiedAt = @NowUtc, ModifiedBy = @DecisionMakerName
                      WHERE Id = @CampaignId",
                    new { CampaignId = (Guid)assignment.CampaignId, NowUtc = nowUtc, decision.DecisionMakerName }, transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(
                    @"INSERT INTO ReviewDecisionHistory
                          (Id, AssignmentId, CampaignId, Decision, PreviousDecision, Justification, Comments,
                           DecisionMakerId, DecisionMakerName, DecisionMakerEmail, DecisionDate,
                           IpAddress, UserAgent, RiskScoreAtDecision, RiskLevelAtDecision, WasEscalated, WasDelegated)
                      VALUES
                          (@Id, @ApprovalId, @CampaignId, 'Denied', @PreviousDecision, @Justification, @Comments,
                           @DecisionMakerId, @DecisionMakerName, @DecisionMakerEmail, @NowUtc,
                           @IpAddress, @UserAgent, @RiskScore, @RiskLevel, 0, 0)",
                    new
                    {
                        Id = Guid.NewGuid(),
                        decision.ApprovalId,
                        CampaignId = (Guid)assignment.CampaignId,
                        PreviousDecision = (string?)assignment.Decision,
                        decision.Justification,
                        Comments = decision.Comments ?? ("Denied by " + decision.DecisionMakerName),
                        decision.DecisionMakerId,
                        decision.DecisionMakerName,
                        decision.DecisionMakerEmail,
                        NowUtc = nowUtc,
                        decision.IpAddress,
                        decision.UserAgent,
                        RiskScore = (int?)assignment.RiskScore ?? 0,
                        RiskLevel = (string?)assignment.RiskLevel
                    }, transaction).ConfigureAwait(false);

                transaction.Commit();
                InvalidateCache(decision.DecisionMakerId);

                stopwatch.Stop();
                LogPerformance("DenyAssignment", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = nowUtc };
            }
            else if (decision.ApprovalType == "AccessRequest")
            {
                var currentStatus = await connection.ExecuteScalarAsync<string>(
                    "SELECT Status FROM AccessRequests WITH (UPDLOCK) WHERE Id = @ApprovalId",
                    new { decision.ApprovalId }, transaction).ConfigureAwait(false);

                if (currentStatus == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Access request not found" };
                if (currentStatus != "Pending")
                    return new ApprovalResult { Success = false, ErrorMessage = "Access request already processed with status: " + currentStatus };

                await connection.ExecuteAsync(
                    @"UPDATE AccessRequests SET Status = 'Denied',
                          ApproverId = CAST(@DecisionMakerId AS NVARCHAR(450)),
                          ApprovedAt = @NowUtc, ApprovalComments = @Justification
                      WHERE Id = @ApprovalId",
                    new { decision.ApprovalId, decision.DecisionMakerId, NowUtc = nowUtc, decision.Justification }, transaction).ConfigureAwait(false);

                transaction.Commit();
                InvalidateCache(decision.DecisionMakerId);

                stopwatch.Stop();
                LogPerformance("DenyAccessRequest", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = nowUtc };
            }
            else if (decision.ApprovalType == "ProcessApproval")
            {
                if (_processApprovalService == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Process approval service not available" };

                var status = await connection.ExecuteScalarAsync<string>(
                    "SELECT Status FROM ProcessInstances WHERE Id = @ApprovalId",
                    new { decision.ApprovalId }, transaction).ConfigureAwait(false);

                if (status == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Process instance not found" };
                if (status != "WaitingForApproval")
                    return new ApprovalResult { Success = false, ErrorMessage = "Process instance not waiting for approval, status: " + status };

                transaction.Commit();

                await _processApprovalService.ResumeProcessAsync(
                    decision.ApprovalId, decision.DecisionMakerName, decision.Justification, false, cancellationToken);

                InvalidateCache(decision.DecisionMakerId);

                stopwatch.Stop();
                LogPerformance("DenyProcessApproval", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = nowUtc };
            }

            return new ApprovalResult { Success = false, ErrorMessage = "Invalid approval type: " + decision.ApprovalType };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error denying {ApprovalType} {ApprovalId}",
                decision.ApprovalType, decision.ApprovalId);
            throw;
        }
    }

    #endregion

    #region DelegateAsync

    public async Task<ApprovalResult> DelegateAsync(
        Guid approvalId,
        string approvalType,
        Guid delegateToId,
        string reason,
        Guid delegatedById,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            if (approvalType == "ReviewAssignment")
            {
                var assignment = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT Status FROM AccessReviewAssignments WITH (UPDLOCK) WHERE Id = @ApprovalId",
                    new { ApprovalId = approvalId }, transaction).ConfigureAwait(false);

                if (assignment == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Review assignment not found" };
                if ((string)assignment.Status != "Pending")
                    return new ApprovalResult { Success = false, ErrorMessage = "Cannot delegate - already processed" };

                await connection.ExecuteAsync(
                    @"UPDATE AccessReviewAssignments
                      SET DelegatedTo = @DelegateToId, DelegatedBy = @DelegatedById,
                          DelegatedAt = GETUTCDATE(), DelegationReason = @Reason
                      WHERE Id = @ApprovalId",
                    new { ApprovalId = approvalId, DelegateToId = delegateToId, DelegatedById = delegatedById, Reason = reason }, transaction).ConfigureAwait(false);

                transaction.Commit();
                InvalidateCache(delegatedById);
                InvalidateCache(delegateToId);

                stopwatch.Stop();
                LogPerformance("DelegateReviewAssignment", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = DateTime.UtcNow };
            }
            else if (approvalType == "AccessRequest")
            {
                var currentStatus = await connection.ExecuteScalarAsync<string>(
                    "SELECT Status FROM AccessRequests WITH (UPDLOCK) WHERE Id = @ApprovalId",
                    new { ApprovalId = approvalId }, transaction).ConfigureAwait(false);

                if (currentStatus == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Access request not found" };
                if (currentStatus != "Pending")
                    return new ApprovalResult { Success = false, ErrorMessage = "Cannot delegate - already processed" };

                await connection.ExecuteAsync(
                    @"UPDATE AccessRequests
                      SET ApproverId = CAST(@DelegateToId AS NVARCHAR(450))
                      WHERE Id = @ApprovalId",
                    new { ApprovalId = approvalId, DelegateToId = delegateToId }, transaction).ConfigureAwait(false);

                transaction.Commit();
                InvalidateCache(delegatedById);
                InvalidateCache(delegateToId);

                stopwatch.Stop();
                LogPerformance("DelegateAccessRequest", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = DateTime.UtcNow };
            }
            else if (approvalType == "ProcessApproval")
            {
                var status = await connection.ExecuteScalarAsync<string>(
                    "SELECT Status FROM ProcessInstances WITH (UPDLOCK) WHERE Id = @ApprovalId",
                    new { ApprovalId = approvalId }, transaction).ConfigureAwait(false);

                if (status == null)
                    return new ApprovalResult { Success = false, ErrorMessage = "Process instance not found" };
                if (status != "WaitingForApproval")
                    return new ApprovalResult { Success = false, ErrorMessage = "Cannot delegate - not waiting for approval" };

                await connection.ExecuteAsync(
                    @"UPDATE ProcessInstances
                      SET ApproverId = CAST(@DelegateToId AS NVARCHAR(450))
                      WHERE Id = @ApprovalId",
                    new { ApprovalId = approvalId, DelegateToId = delegateToId }, transaction).ConfigureAwait(false);

                transaction.Commit();
                InvalidateCache(delegatedById);
                InvalidateCache(delegateToId);

                stopwatch.Stop();
                LogPerformance("DelegateProcessApproval", stopwatch.ElapsedMilliseconds, 1);
                return new ApprovalResult { Success = true, ProcessedAt = DateTime.UtcNow };
            }

            return new ApprovalResult { Success = false, ErrorMessage = "Delegation not supported for type: " + approvalType };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error delegating approval {ApprovalId} to {DelegateToId}",
                approvalId, delegateToId);
            throw;
        }
    }

    #endregion

    #region BulkApproveAsync

    public async Task<BulkApprovalResult> BulkApproveAsync(
        List<Guid> approvalIds,
        string approvalType,
        string justification,
        Guid approverId,
        string approverName,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            var nowUtc = DateTime.UtcNow;
            var successCount = 0;
            var failures = new List<BulkApprovalFailure>();

            if (approvalType == "ReviewAssignment")
            {
                foreach (var approvalId in approvalIds)
                {
                    var assignment = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT Status, CampaignId, Decision, RiskScore, RiskLevel FROM AccessReviewAssignments WHERE Id = @Id",
                        new { Id = approvalId }, transaction).ConfigureAwait(false);

                    if (assignment == null)
                    {
                        failures.Add(new BulkApprovalFailure { ApprovalId = approvalId, ErrorMessage = "Not found" });
                        continue;
                    }
                    if ((string)assignment.Status != "Pending")
                    {
                        failures.Add(new BulkApprovalFailure { ApprovalId = approvalId, ErrorMessage = "Already processed: " + assignment.Status });
                        continue;
                    }

                    await connection.ExecuteAsync(
                        @"UPDATE AccessReviewAssignments
                          SET Status = 'Completed', Decision = 'Approved',
                              Justification = @Justification, Comments = 'Bulk approved', CompletedAt = @NowUtc
                          WHERE Id = @Id AND Status = 'Pending'",
                        new { Id = approvalId, Justification = justification, NowUtc = nowUtc }, transaction).ConfigureAwait(false);

                    await connection.ExecuteAsync(
                        @"INSERT INTO ReviewDecisionHistory
                              (Id, AssignmentId, CampaignId, Decision, PreviousDecision, Justification, Comments,
                               DecisionMakerId, DecisionMakerName, DecisionDate, IpAddress, UserAgent,
                               RiskScoreAtDecision, RiskLevelAtDecision, WasEscalated, WasDelegated)
                          VALUES
                              (@Id, @AssignmentId, @CampaignId, 'Approved', @PreviousDecision, @Justification, @Comments,
                               @ApproverId, @ApproverName, @NowUtc, @IpAddress, @UserAgent,
                               @RiskScore, @RiskLevel, 0, 0)",
                        new
                        {
                            Id = Guid.NewGuid(),
                            AssignmentId = approvalId,
                            CampaignId = (Guid)assignment.CampaignId,
                            PreviousDecision = (string?)assignment.Decision,
                            Justification = justification,
                            Comments = "Bulk approved by " + approverName,
                            ApproverId = approverId,
                            ApproverName = approverName,
                            NowUtc = nowUtc,
                            IpAddress = ipAddress,
                            UserAgent = userAgent,
                            RiskScore = (int?)assignment.RiskScore ?? 0,
                            RiskLevel = (string?)assignment.RiskLevel
                        }, transaction).ConfigureAwait(false);

                    successCount++;
                }

                // Update campaign stats for all affected campaigns
                var affectedCampaignIds = await connection.QueryAsync<Guid>(
                    "SELECT DISTINCT CampaignId FROM AccessReviewAssignments WHERE Id IN @Ids",
                    new { Ids = approvalIds }, transaction).ConfigureAwait(false);

                foreach (var campaignId in affectedCampaignIds)
                {
                    await connection.ExecuteAsync(
                        @"UPDATE Campaigns SET
                              CompletedAssignments = (SELECT COUNT(*) FROM AccessReviewAssignments WHERE CampaignId = @CampaignId AND Status = 'Completed'),
                              CompletionPercentage = (SELECT CAST(COUNT(*) AS DECIMAL(5,2)) * 100.0 / NULLIF(TotalAssignments, 0) FROM AccessReviewAssignments WHERE CampaignId = @CampaignId AND Status = 'Completed'),
                              ModifiedAt = @NowUtc, ModifiedBy = @ApproverName
                          WHERE Id = @CampaignId",
                        new { CampaignId = campaignId, NowUtc = nowUtc, ApproverName = approverName }, transaction).ConfigureAwait(false);
                }
            }
            else if (approvalType == "AccessRequest")
            {
                foreach (var approvalId in approvalIds)
                {
                    var updated = await connection.ExecuteAsync(
                        @"UPDATE AccessRequests SET Status = 'Approved',
                              ApproverId = CAST(@ApproverId AS NVARCHAR(450)),
                              ApprovedAt = @NowUtc, ApprovalComments = @Justification
                          WHERE Id = @Id AND Status = 'Pending'",
                        new { Id = approvalId, ApproverId = approverId, NowUtc = nowUtc, Justification = justification }, transaction).ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await connection.ExecuteAsync(
                            @"INSERT INTO UserAccess (Id, UserId, ResourceType, ResourceId, ResourceName, GrantedAt, GrantedBy, ExpiresAt, IsActive, AccessRequestId)
                              SELECT NEWID(), RequesterId, ResourceType, ResourceId, ResourceName, @NowUtc,
                                     CAST(@ApproverId AS NVARCHAR(450)),
                                     CASE WHEN DurationDays > 0 THEN DATEADD(DAY, DurationDays, @NowUtc) ELSE NULL END, 1, Id
                              FROM AccessRequests WHERE Id = @Id",
                            new { Id = approvalId, ApproverId = approverId, NowUtc = nowUtc }, transaction).ConfigureAwait(false);
                        successCount++;
                    }
                    else
                    {
                        failures.Add(new BulkApprovalFailure { ApprovalId = approvalId, ErrorMessage = "Not found or already processed" });
                    }
                }
            }

            transaction.Commit();
            InvalidateCache(approverId);

            stopwatch.Stop();
            LogPerformance("BulkApprove", stopwatch.ElapsedMilliseconds, approvalIds.Count);

            _logger.LogInformation(
                "Bulk approval completed: {SuccessCount}/{TotalCount} successful in {ElapsedMs}ms",
                successCount, approvalIds.Count, stopwatch.ElapsedMilliseconds);

            return new BulkApprovalResult
            {
                TotalRequested = approvalIds.Count,
                SuccessCount = successCount,
                FailureCount = failures.Count,
                Failures = failures,
                ProcessedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk approving {Count} approvals", approvalIds.Count);
            throw;
        }
    }

    #endregion

    #region GetApprovalStatsAsync

    public async Task<ApprovalStats> GetApprovalStatsAsync(
        Guid approverId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var cacheKey = $"approval_stats_{approverId}";

        try
        {
            // Try cache first (longer TTL for stats)
            if (_cache.TryGetValue<ApprovalStats>(cacheKey, out var cachedStats) && cachedStats != null)
            {
                _logger.LogDebug("Approval stats cache HIT for approver {ApproverId}", approverId);
                return cachedStats;
            }

            _logger.LogDebug("Approval stats cache MISS for approver {ApproverId} - querying database", approverId);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                "usp_GetApprovalStats",
                new { ApproverId = approverId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken,
                commandTimeout: CommandTimeoutSeconds);

            var stats = await connection.QueryFirstAsync<ApprovalStats>(command).ConfigureAwait(false);

            // Cache for 15 minutes
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(StatsCacheExpirationMinutes))
                .SetPriority(CacheItemPriority.High);

            _cache.Set(cacheKey, stats, cacheOptions);

            stopwatch.Stop();
            LogPerformance("GetApprovalStats", stopwatch.ElapsedMilliseconds, 1);

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting approval stats for approver {ApproverId}", approverId);
            throw;
        }
    }

    #endregion

    #region GetUrgentApprovalsAsync

    public async Task<List<ApprovalInboxItem>> GetUrgentApprovalsAsync(
        Guid approverId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                "usp_GetUrgentApprovals",
                new { ApproverId = approverId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken,
                commandTimeout: CommandTimeoutSeconds);

            var urgentApprovals = (await connection.QueryAsync<ApprovalInboxItem>(command).ConfigureAwait(false)).ToList();

            stopwatch.Stop();
            LogPerformance("GetUrgentApprovals", stopwatch.ElapsedMilliseconds, urgentApprovals.Count);

            return urgentApprovals;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting urgent approvals for approver {ApproverId}", approverId);
            throw;
        }
    }

    #endregion

    #region Cache Management

    public void InvalidateCache(Guid approverId)
    {
        // Remove all cache entries for this approver
        var patterns = new[]
        {
            $"approval_inbox_{approverId}_*",
            $"approval_stats_{approverId}"
        };

        foreach (var pattern in patterns)
        {
            _cache.Remove(pattern);
        }

        _logger.LogDebug("Invalidated approval cache for approver {ApproverId}", approverId);
    }

    public void InvalidateAllCaches()
    {
        // This would require a cache key tracking mechanism
        // For now, we rely on TTL expiration
        _logger.LogInformation("All approval caches will expire based on TTL");
    }

    private string GetInboxCacheKey(Guid approverId, ApprovalFilter filter, ApprovalPagination pagination)
    {
        var filterHash = $"{filter.ApprovalType}_{filter.RiskLevel}_{filter.CampaignId}_" +
                        $"{filter.ResourceType}_{filter.OnlyOverdue}_{filter.SearchTerm}_" +
                        $"{filter.SortBy}_{filter.SortDirection}";
        var paginationHash = $"{pagination.PageNumber}_{pagination.PageSize}";

        return $"approval_inbox_{approverId}_{filterHash}_{paginationHash}";
    }

    #endregion

    #region Performance Monitoring

    private void LogPerformance(string operation, long elapsedMs, int recordCount)
    {
        var recordsPerSecond = recordCount > 0 ? (recordCount / (elapsedMs / 1000.0)) : 0;

        if (elapsedMs > CriticalQueryThresholdMs)
        {
            _logger.LogWarning(
                "CRITICAL SLOW QUERY: {Operation} took {ElapsedMs}ms for {RecordCount} records ({RecordsPerSec:F0} rec/sec)",
                operation, elapsedMs, recordCount, recordsPerSecond);
        }
        else if (elapsedMs > SlowQueryThresholdMs)
        {
            _logger.LogWarning(
                "Slow query: {Operation} took {ElapsedMs}ms for {RecordCount} records ({RecordsPerSec:F0} rec/sec)",
                operation, elapsedMs, recordCount, recordsPerSecond);
        }
        else
        {
            _logger.LogDebug(
                "Query performance: {Operation} completed in {ElapsedMs}ms for {RecordCount} records ({RecordsPerSec:F0} rec/sec)",
                operation, elapsedMs, recordCount, recordsPerSecond);
        }
    }

    #endregion
}
