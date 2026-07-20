using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ChangeHistory.Services;
using ChangeRecord = ChangeHistory.Models.ChangeRecord;
using ChangeOpType = ChangeHistory.Models.ChangeOperationType;

namespace DataAccessLibrary.Services;

/// <summary>
/// Result of executing a single internal sync step.
/// </summary>
public class StepExecutionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int Found { get; set; }      // Source records found (ObjectsQueried)
    public int Processed { get; set; }
    public int Matched { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<SyncAuditLog> AuditLogs { get; set; } = new();
}

/// <summary>
/// Configuration for matching steps, parsed from step.Configuration JSON.
/// </summary>
public class MatchStepConfig
{
    public string MatchingStrategy { get; set; } = "Composite";
    public int MinConfidence { get; set; } = 75;
    public bool CaseSensitive { get; set; } = false;
}

/// <summary>
/// Executes individual internal sync steps using Dapper for high performance.
/// Each step type has a dedicated execution method.
/// </summary>
public interface IInternalSyncStepExecutor
{
    /// <summary>
    /// Execute a step and return the result.
    /// Routes to appropriate handler based on step.StepType.
    /// </summary>
    Task<StepExecutionResult> ExecuteStepAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId = null,
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// High-performance step executor using Dapper for all database operations.
/// Supports both Object-to-Person and Person-to-Object directions.
/// </summary>
public class InternalSyncStepExecutor : IInternalSyncStepExecutor
{
    private readonly ILogger<InternalSyncStepExecutor> _logger;
    private readonly IChangeHistoryService _changeHistory;
    private readonly HRImport.ADProvisioningStepExecutor? _adProvisioningExecutor;

    public InternalSyncStepExecutor(
        ILogger<InternalSyncStepExecutor> logger,
        IChangeHistoryService changeHistory,
        HRImport.ADProvisioningStepExecutor? adProvisioningExecutor = null)
    {
        _logger = logger;
        _changeHistory = changeHistory;
        _adProvisioningExecutor = adProvisioningExecutor;
    }

    public async Task<StepExecutionResult> ExecuteStepAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId = null,
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing step '{StepName}' ({StepType})", step.Name, step.StepType);

        try
        {
            // Route to appropriate handler based on step type
            var stepType = Enum.TryParse<InternalSyncStepType>(step.StepType, out var parsed)
                ? parsed
                : InternalSyncStepType.ObjectToPersonFieldSync;

            return stepType switch
            {
                // Object to Person direction
                InternalSyncStepType.ObjectToPersonCreate => await ExecuteObjectToPersonCreateAsync(step, connection, stepRunId, progress, cancellationToken),
                InternalSyncStepType.ObjectToPersonFieldSync => await ExecuteObjectToPersonFieldSyncAsync(step, connection, stepRunId, progress, cancellationToken),
                InternalSyncStepType.ManagerResolve => await ExecuteManagerResolveAsync(step, connection, stepRunId, progress, cancellationToken),
                InternalSyncStepType.ManagerAssign => await ExecuteManagerAssignAsync(step, connection, stepRunId, progress, cancellationToken),
                InternalSyncStepType.TagAggregate => await ExecuteTagAggregateAsync(step, connection, stepRunId, progress, cancellationToken),

                // Person to Object direction
                InternalSyncStepType.PersonToObjectCreate => await ExecutePersonToObjectCreateAsync(step, connection, progress, cancellationToken),
                InternalSyncStepType.PersonToObjectUpdate => await ExecutePersonToObjectFieldSyncAsync(step, connection, progress, cancellationToken),
                InternalSyncStepType.PersonToObjectLink => await ExecutePersonToObjectLinkAsync(step, connection, stepRunId, progress, cancellationToken),
                InternalSyncStepType.PersonToObjectFieldSync => await ExecutePersonToObjectFieldSyncAsync(step, connection, progress, cancellationToken),
                InternalSyncStepType.PersonToObjectDeprovision => await ExecutePersonToObjectDeprovisionAsync(step, connection, progress, cancellationToken),

                // AD provisioning (delegates to ADProvisioningStepExecutor)
                InternalSyncStepType.PersonToObjectProvisionAD => await ExecutePersonToObjectProvisionADAsync(step, connection, progress, cancellationToken),

                InternalSyncStepType.ObjectToPersonMatch => await ExecuteObjectToPersonMatchAsync(step, connection, stepRunId, progress, cancellationToken),

                // Deprecated step types - skip gracefully
                InternalSyncStepType.ObjectToPersonLink => SkipDeprecatedStep(step),
                InternalSyncStepType.GroupAggregate => SkipDeprecatedStep(step),

                _ => throw new NotSupportedException($"Step type '{step.StepType}' is not supported")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step '{StepName}' failed: {Message}", step.Name, ex.Message);
            return new StepExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private StepExecutionResult SkipDeprecatedStep(InternalSyncStep step)
    {
        _logger.LogInformation("Skipping deprecated step '{StepName}' ({StepType})", step.Name, step.StepType);
        return new StepExecutionResult { Success = true };
    }

    #region Object to Person Steps

    /// <summary>
    /// Match Objects to existing Identities by configured strategy.
    /// Does NOT create new Identities - only links existing ones.
    /// OPTIMIZED: Pre-loads identities into memory, matches in-memory, batches updates.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteObjectToPersonMatchAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };
        var config = ParseConfig<MatchStepConfig>(step.Configuration);

        // Build object class filter
        var objectClassFilter = string.IsNullOrEmpty(step.ObjectClassFilter) || step.ObjectClassFilter == "*"
            ? ""
            : "AND ObjectClass = @ObjectClass";

        var connectionFilter = step.SourceConnectionId.HasValue
            ? "AND SourceConnectionId = @SourceConnectionId"
            : "";

        // Build tag filter
        var (tagFilterClause, tagFilterParams) = BuildTagFilterClause(step.TagFilter);

        // Build parameters - merge tag filter params with other params
        var parameters = new DynamicParameters();
        parameters.Add("ObjectClass", step.ObjectClassFilter);
        parameters.Add("SourceConnectionId", step.SourceConnectionId);
        if (tagFilterParams != null)
        {
            parameters.AddDynamicParams(tagFilterParams);
        }

        // Count total objects in scope
        var countSql = $@"
            SELECT COUNT(*) FROM Objects
            WHERE 1=1 {objectClassFilter} {connectionFilter} {tagFilterClause}";
        result.Found = await connection.ExecuteScalarAsync<int>(countSql, parameters, commandTimeout: 120);

        // Get unmatched objects
        var sql = $@"
            SELECT Id, Email, Username, FirstName, LastName, DisplayName,
                   Department, JobTitle, Phone, DN, CN, SourceUniqueId,
                   EmployeeId, MobilePhone, Company, Office, UserPrincipalName
            FROM Objects
            WHERE IdentityId IS NULL
              {objectClassFilter}
              {connectionFilter}
              {tagFilterClause}
            ORDER BY FirstSyncedAt";

        var objects = (await connection.QueryAsync<ObjectDto>(sql, parameters, commandTimeout: 120)).ToList();

        result.Processed = result.Found;  // All objects were evaluated (matches convention)
        result.Skipped = result.Found - objects.Count;  // Already-matched objects

        if (objects.Count == 0)
        {
            // Build audit logs for already-matched objects
            await BuildSkippedAuditLogsForLinkedObjectsAsync(
                connection, result, objectClassFilter, connectionFilter, tagFilterClause, parameters);

            _logger.LogInformation("No unmatched objects found for step '{StepName}' (checked {Total} objects, {Skipped} already matched)",
                step.Name, result.Found, result.Skipped);
            return result;
        }

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = $"Loading identities for matching...",
            Total = objects.Count
        });

        // ⚡ OPTIMIZATION: Pre-load ALL identities into memory (single query instead of N queries)
        var identityCache = await LoadIdentityCacheAsync(connection);
        _logger.LogInformation("⚡ Loaded {Count} identities into memory cache for matching", identityCache.Total);

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = $"Matching {objects.Count} objects...",
            Total = objects.Count
        });

        // ⚡ OPTIMIZATION: Collect all links for batch update
        var linksToCreate = new List<(Guid ObjectId, Guid IdentityId)>();
        var changeRecords = new List<ChangeRecord>();

        foreach (var obj in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ⚡ In-memory matching (no database queries!)
            var matchResult = TryMatchObjectInMemory(obj, config, identityCache);

            if (matchResult.IdentityId.HasValue)
            {
                linksToCreate.Add((obj.Id, matchResult.IdentityId.Value));
                result.Matched++;
                changeRecords.Add(new ChangeRecord
                {
                    OperationType = ChangeOpType.LinkIdentity,
                    EntityType = "Object",
                    EntityId = obj.Id,
                    EntityDisplayName = obj.DisplayName ?? obj.Email ?? obj.Username ?? obj.Id.ToString(),
                    PropertyName = "IdentityId",
                    NewValue = matchResult.IdentityId.Value.ToString(),
                    RelatedEntityId = matchResult.IdentityId.Value
                });
            }
            else
            {
                result.Skipped++;
            }
        }

        // ⚡ OPTIMIZATION: Batch update all links in chunks of 500
        if (linksToCreate.Any())
        {
            progress?.Report(new InternalSyncProgress
            {
                Phase = step.Name,
                Message = $"Linking {linksToCreate.Count} matched objects...",
                Processed = objects.Count,
                Total = objects.Count,
                Matched = result.Matched
            });

            var batchSize = 500;
            foreach (var batch in linksToCreate.Chunk(batchSize))
            {
                await connection.ExecuteAsync(
                    "UPDATE Objects SET IdentityId = @IdentityId, MatchConfidence = 95, MatchMethod = 'InternalSync', IsAuthoritative = 1 WHERE Id = @ObjectId",
                    batch.Select(l => new { l.ObjectId, l.IdentityId }));
            }

            _logger.LogInformation("⚡ Batch linked {Count} objects to identities (set as authoritative)", linksToCreate.Count);
        }

        // Record change history
        await RecordChangesAsync(changeRecords, stepRunId);

        // Build audit logs for already-matched objects (skipped)
        await BuildSkippedAuditLogsForLinkedObjectsAsync(
            connection, result, objectClassFilter, connectionFilter, tagFilterClause, parameters);

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = $"Matched {result.Matched}/{result.Found}",
            Processed = result.Found,
            Total = result.Found,
            Matched = result.Matched,
            Skipped = result.Skipped
        });

        _logger.LogInformation("Match step completed: {Found} found, {Matched} matched, {Skipped} skipped (already matched or no match)",
            result.Found, result.Matched, result.Skipped);

        return result;
    }

    /// <summary>
    /// Pre-load all identities into memory for fast matching.
    /// </summary>
    private async Task<IdentityMatchCache> LoadIdentityCacheAsync(SqlConnection connection)
    {
        var identities = await connection.QueryAsync<IdentityLookupDto>(
            @"SELECT Id, LOWER(PrimaryEmail) as Email, LOWER(Username) as Username,
                     LOWER(FirstName) as FirstName, LOWER(LastName) as LastName, Department
              FROM Identities WHERE IsActive = 1");

        var cache = new IdentityMatchCache();
        foreach (var i in identities)
        {
            cache.Total++;
            if (!string.IsNullOrWhiteSpace(i.Email))
                cache.ByEmail[i.Email] = i.Id;
            if (!string.IsNullOrWhiteSpace(i.Username))
                cache.ByUsername[i.Username] = i.Id;
            if (!string.IsNullOrWhiteSpace(i.FirstName) && !string.IsNullOrWhiteSpace(i.LastName))
            {
                var nameKey = $"{i.FirstName}|{i.LastName}|{i.Department ?? ""}";
                cache.ByName[nameKey] = i.Id;
                // Also store without department for fallback
                var nameOnlyKey = $"{i.FirstName}|{i.LastName}|";
                if (!cache.ByName.ContainsKey(nameOnlyKey))
                    cache.ByName[nameOnlyKey] = i.Id;
            }
        }
        return cache;
    }

    /// <summary>
    /// Match object to identity using in-memory cache (no database queries).
    /// </summary>
    private (Guid? IdentityId, string? MatchMethod, int Confidence) TryMatchObjectInMemory(
        ObjectDto obj, MatchStepConfig config, IdentityMatchCache cache)
    {
        var strategy = config.MatchingStrategy?.ToLower() ?? "composite";

        if (strategy == "email" || strategy == "composite")
        {
            if (!string.IsNullOrWhiteSpace(obj.Email) && cache.ByEmail.TryGetValue(obj.Email.ToLower(), out var emailMatch))
                return (emailMatch, "Email", 95);
        }

        if (strategy == "username" || strategy == "composite")
        {
            if (!string.IsNullOrWhiteSpace(obj.Username) && cache.ByUsername.TryGetValue(obj.Username.ToLower(), out var usernameMatch))
                return (usernameMatch, "Username", 90);
        }

        if (strategy == "composite")
        {
            if (!string.IsNullOrWhiteSpace(obj.FirstName) && !string.IsNullOrWhiteSpace(obj.LastName))
            {
                var nameKey = $"{obj.FirstName.ToLower()}|{obj.LastName.ToLower()}|{obj.Department?.ToLower() ?? ""}";
                if (cache.ByName.TryGetValue(nameKey, out var nameMatch))
                    return (nameMatch, "Name", 75);

                // Fallback: match without department
                var nameOnlyKey = $"{obj.FirstName.ToLower()}|{obj.LastName.ToLower()}|";
                if (cache.ByName.TryGetValue(nameOnlyKey, out var nameOnlyMatch))
                    return (nameOnlyMatch, "Name", 70);
            }
        }

        return (null, null, 0);
    }

    private class IdentityMatchCache
    {
        public int Total { get; set; }
        public Dictionary<string, Guid> ByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> ByUsername { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private class IdentityLookupDto
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Department { get; set; }
    }

    /// <summary>
    /// Create Identities for Objects that don't have a linked Identity yet.
    /// Finds all Objects with IdentityId IS NULL (filtered by ObjectClass and tags),
    /// creates a new Identity record for each, and links the Object to it.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteObjectToPersonCreateAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = "Finding unlinked objects to create identities..."
        });

        // Build filters
        var (tagFilterClause, tagFilterParams) = BuildTagFilterClause(step.TagFilter);

        var objectClassFilter = (!string.IsNullOrEmpty(step.ObjectClassFilter) && step.ObjectClassFilter != "*")
            ? "AND ObjectClass = @ObjectClass"
            : "AND ObjectClass = 'user'";

        var connectionFilter = step.SourceConnectionId.HasValue
            ? "AND SourceConnectionId = @SourceConnectionId"
            : "";

        var parameters = new DynamicParameters(tagFilterParams);
        if (!string.IsNullOrEmpty(step.ObjectClassFilter) && step.ObjectClassFilter != "*")
            parameters.Add("ObjectClass", step.ObjectClassFilter);
        if (step.SourceConnectionId.HasValue)
            parameters.Add("SourceConnectionId", step.SourceConnectionId.Value);

        try
        {
            // Find all unlinked objects
            var sql = $@"
                SELECT Id, Email, Username, FirstName, LastName, DisplayName,
                       SourceUniqueId, Department, JobTitle, Phone, DN, CN,
                       EmployeeId, MobilePhone, Company, Office, UserPrincipalName
                FROM Objects
                WHERE IdentityId IS NULL AND IsActive = 1
                      {objectClassFilter} {connectionFilter} {tagFilterClause}
                ORDER BY DisplayName";

            var unlinkedObjects = (await connection.QueryAsync<ObjectDto>(sql, parameters, commandTimeout: 120)).ToList();
            result.Found = unlinkedObjects.Count;

            _logger.LogInformation(
                "ObjectToPersonCreate '{StepName}': Found {Count} unlinked objects to process",
                step.Name, unlinkedObjects.Count);

            if (unlinkedObjects.Count == 0)
            {
                progress?.Report(new InternalSyncProgress
                {
                    Phase = step.Name,
                    Message = "No unlinked objects found — all objects already have identities."
                });
                return result;
            }

            // Also count already-linked objects for reporting
            var linkedCountSql = $@"
                SELECT COUNT(*) FROM Objects
                WHERE IdentityId IS NOT NULL {objectClassFilter} {connectionFilter} {tagFilterClause}";
            result.Skipped = await connection.ExecuteScalarAsync<int>(linkedCountSql, parameters, commandTimeout: 30);

            // Create identities for each unlinked object
            var batchSize = 100;
            var processed = 0;

            foreach (var obj in unlinkedObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Check if an identity with matching email or UPN already exists (avoid duplicates)
                    Guid? existingIdentityId = null;

                    if (!string.IsNullOrEmpty(obj.Email))
                    {
                        existingIdentityId = await connection.ExecuteScalarAsync<Guid?>(
                            "SELECT Id FROM Identities WHERE PrimaryEmail = @Email",
                            new { obj.Email }, commandTimeout: 10);
                    }

                    if (existingIdentityId == null && !string.IsNullOrEmpty(obj.UserPrincipalName))
                    {
                        existingIdentityId = await connection.ExecuteScalarAsync<Guid?>(
                            "SELECT Id FROM Identities WHERE UserPrincipalName = @UPN",
                            new { UPN = obj.UserPrincipalName }, commandTimeout: 10);
                    }

                    if (existingIdentityId == null && !string.IsNullOrEmpty(obj.Username))
                    {
                        existingIdentityId = await connection.ExecuteScalarAsync<Guid?>(
                            "SELECT Id FROM Identities WHERE Username = @Username",
                            new { obj.Username }, commandTimeout: 10);
                    }

                    if (existingIdentityId.HasValue)
                    {
                        // Link to existing identity instead of creating a duplicate
                        await LinkObjectToIdentityAsync(connection, obj.Id, existingIdentityId.Value);
                        result.Matched++;

                        result.AuditLogs.Add(new SyncAuditLog
                        {
                            Id = Guid.NewGuid(),
                            ObjectId = obj.Id,
                            OperationType = "Linked",
                            ObjectDisplayName = obj.DisplayName ?? obj.Email ?? obj.Username,
                            SourceUniqueId = obj.SourceUniqueId,
                            Email = obj.Email,
                            Username = obj.Username,
                            UserPrincipalName = obj.UserPrincipalName,
                            ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "IdentityId", Before = (string?)null, After = existingIdentityId.Value.ToString() } }),
                            ChangeCount = 1,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        // Create new identity from object
                        var newIdentityId = await CreateIdentityFromObjectAsync(connection, obj, "Active");
                        await LinkObjectToIdentityAsync(connection, obj.Id, newIdentityId);
                        result.Created++;

                        result.AuditLogs.Add(new SyncAuditLog
                        {
                            Id = Guid.NewGuid(),
                            ObjectId = obj.Id,
                            OperationType = "Created",
                            ObjectDisplayName = obj.DisplayName ?? obj.Email ?? obj.Username,
                            SourceUniqueId = obj.SourceUniqueId,
                            Email = obj.Email,
                            Username = obj.Username,
                            UserPrincipalName = obj.UserPrincipalName,
                            ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "Identity", Before = (string?)null, After = "New identity created from object" } }),
                            ChangeCount = 1,
                            Timestamp = DateTime.UtcNow
                        });
                    }

                    processed++;
                    result.Processed = processed;

                    if (processed % batchSize == 0)
                    {
                        progress?.Report(new InternalSyncProgress
                        {
                            Phase = step.Name,
                            Message = $"Processed {processed} of {unlinkedObjects.Count} objects ({result.Created} created, {result.Matched} linked)...",
                            Processed = processed,
                            Total = unlinkedObjects.Count
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    _logger.LogWarning(ex, "ObjectToPersonCreate: Failed to process object {ObjectId} ({DisplayName})",
                        obj.Id, obj.DisplayName);

                    result.AuditLogs.Add(new SyncAuditLog
                    {
                        Id = Guid.NewGuid(),
                        ObjectId = obj.Id,
                        OperationType = "Error",
                        ObjectDisplayName = obj.DisplayName ?? obj.Email ?? obj.Username,
                        SourceUniqueId = obj.SourceUniqueId,
                        Email = obj.Email,
                        Username = obj.Username,
                        ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "Error", After = ex.Message } }),
                        ChangeCount = 0,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            // Build skipped audit logs for already-linked objects
            await BuildSkippedAuditLogsForLinkedObjectsAsync(
                connection, result, objectClassFilter, connectionFilter, tagFilterClause, parameters);

            _logger.LogInformation(
                "ObjectToPersonCreate '{StepName}' completed: {Created} created, {Matched} linked to existing, {Skipped} already linked, {Errors} errors",
                step.Name, result.Created, result.Matched, result.Skipped, result.Errors);

            progress?.Report(new InternalSyncProgress
            {
                Phase = step.Name,
                Message = $"Done — {result.Created} identities created, {result.Matched} linked to existing.",
                Processed = processed,
                Total = unlinkedObjects.Count,
                Complete = true
            });
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "ObjectToPersonCreate '{StepName}' failed", step.Name);
        }

        return result;
    }

    /// <summary>
    /// Build "Skipped" audit logs for objects that already have an IdentityId (already linked).
    /// </summary>
    private async Task BuildSkippedAuditLogsForLinkedObjectsAsync(
        SqlConnection connection,
        StepExecutionResult result,
        string objectClassFilter,
        string connectionFilter,
        string tagFilterClause,
        DynamicParameters parameters)
    {
        if (result.Skipped <= 0) return;

        const int maxSkippedAuditLogs = 5000;
        var skippedSql = $@"
            SELECT TOP({maxSkippedAuditLogs}) Id, DisplayName, Email, Username, UserPrincipalName, SourceUniqueId
            FROM Objects
            WHERE IdentityId IS NOT NULL {objectClassFilter} {connectionFilter} {tagFilterClause}";

        var skippedObjects = await connection.QueryAsync<ObjectDto>(skippedSql, parameters, commandTimeout: 120);

        foreach (var obj in skippedObjects)
        {
            result.AuditLogs.Add(new SyncAuditLog
            {
                Id = Guid.NewGuid(),
                ObjectId = obj.Id,
                OperationType = "Skipped",
                ObjectDisplayName = obj.DisplayName ?? obj.Email ?? obj.Username,
                SourceUniqueId = obj.SourceUniqueId,
                Email = obj.Email,
                Username = obj.Username,
                UserPrincipalName = obj.UserPrincipalName,
                ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "IdentityId", After = "Already linked to identity" } }),
                ChangeCount = 0,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private class IdentityCreateDto
    {
        public Guid Id { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PrimaryEmail { get; set; }
        public string? PrimaryPhone { get; set; }
        public string? MobilePhone { get; set; }
        public string? Username { get; set; }
        public string? UserPrincipalName { get; set; }
        public string? Department { get; set; }
        public string? JobTitle { get; set; }
        public string? Company { get; set; }
        public string? Office { get; set; }
        public string? EmployeeId { get; set; }
        public string Status { get; set; } = "Active";
    }

    /// <summary>
    /// Link Objects to Identities without creating new ones.
    /// Uses a simpler matching approach than ObjectToPersonMatch.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteObjectToPersonLinkAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Same as match but with explicit "link only" messaging
        var result = await ExecuteObjectToPersonMatchAsync(step, connection, stepRunId, progress, cancellationToken);
        return result;
    }

    /// <summary>
    /// Sync specific fields from Object to Identity using step mappings.
    /// Uses bulk UPDATE with JOIN for high performance.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteObjectToPersonFieldSyncAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        if (step.Mappings == null || !step.Mappings.Any())
        {
            _logger.LogWarning("FieldSync step '{StepName}' has no mappings configured", step.Name);
            return result;
        }

        var enabledMappings = step.Mappings.Where(m => m.IsEnabled).OrderBy(m => m.MappingOrder).ToList();
        if (!enabledMappings.Any())
        {
            _logger.LogInformation("No enabled mappings for step '{StepName}'", step.Name);
            return result;
        }

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = "Syncing fields from Objects to Identities..."
        });

        // Build tag filter and object class filter (shared across per-field UPDATEs)
        var (tagFilterClause, tagFilterParams) = BuildTagFilterClause(step.TagFilter, "o");

        var objectClassCondition = (!string.IsNullOrEmpty(step.ObjectClassFilter) && step.ObjectClassFilter != "*")
            ? "AND o.ObjectClass = @ObjectClass"
            : "";

        try
        {
            var allChangeRecords = new List<ChangeRecord>();
            var totalRowsAffected = 0;

            // Per-field UPDATE with OUTPUT clause for audit trail
            foreach (var mapping in enabledMappings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Admin-authored mappings control these identifiers; validate before concatenating
                // them into SQL identifier positions. Rejection throws and is caught by the step handler.
                var targetColumn = ValidateObjectColumn(mapping.TargetField);
                var sourceColumn = ValidateObjectColumn(mapping.SourceField);

                // Build the SET and WHERE clause depending on overwrite mode
                string setClause;
                string changeFilter;
                if (mapping.OverwriteExisting)
                {
                    setClause = string.Concat("i.[", targetColumn, "] = o.[", sourceColumn, "]");
                    // Update rows where source differs from target (including NULL → clear the field)
                    changeFilter = string.Concat(
                        "AND (i.[", targetColumn, "] IS NULL AND o.[", sourceColumn, "] IS NOT NULL ",
                        "OR i.[", targetColumn, "] IS NOT NULL AND o.[", sourceColumn, "] IS NULL ",
                        "OR i.[", targetColumn, "] != o.[", sourceColumn, "])");
                }
                else
                {
                    setClause = string.Concat("i.[", targetColumn, "] = COALESCE(i.[", targetColumn, "], o.[", sourceColumn, "])");
                    // Only update rows where target is null and source has a value
                    changeFilter = string.Concat("AND i.[", targetColumn, "] IS NULL AND o.[", sourceColumn, "] IS NOT NULL");
                }

                var perFieldSql = string.Concat(
                    "UPDATE i SET ", setClause, ", i.ModifiedAt = GETUTCDATE() ",
                    "OUTPUT INSERTED.Id, INSERTED.DisplayName, ",
                    "DELETED.[", targetColumn, "] AS OldValue, ",
                    "INSERTED.[", targetColumn, "] AS NewValue ",
                    "FROM Identities i ",
                    "INNER JOIN Objects o ON o.IdentityId = i.Id ",
                    "WHERE o.IdentityId IS NOT NULL ",
                    changeFilter, " ",
                    objectClassCondition, " ",
                    tagFilterClause);

                var parameters = new DynamicParameters();
                if (!string.IsNullOrEmpty(step.ObjectClassFilter) && step.ObjectClassFilter != "*")
                    parameters.Add("ObjectClass", step.ObjectClassFilter);
                if (tagFilterParams != null)
                    parameters.AddDynamicParams(tagFilterParams);

                var changedRows = (await connection.QueryAsync<FieldSyncOutputRow>(
                    perFieldSql, parameters, commandTimeout: 300)).ToList();

                totalRowsAffected += changedRows.Count;

                // Convert to ChangeRecords
                foreach (var row in changedRows)
                {
                    allChangeRecords.Add(new ChangeRecord
                    {
                        OperationType = ChangeOpType.Update,
                        EntityType = "Identity",
                        EntityId = row.Id,
                        EntityDisplayName = row.DisplayName,
                        PropertyName = mapping.TargetField,
                        OldValue = row.OldValue,
                        NewValue = row.NewValue
                    });
                }

                _logger.LogDebug("FieldSync mapping {Source}->{Target}: {Count} changes",
                    mapping.SourceField, mapping.TargetField, changedRows.Count);
            }

            result.Found = totalRowsAffected;
            result.Updated = totalRowsAffected;
            result.Processed = totalRowsAffected;

            // Record all changes to ChangeHistory
            await RecordChangesAsync(allChangeRecords, stepRunId);

            _logger.LogInformation("FieldSync step '{StepName}' completed: {Updated} field changes across {Mappings} mappings ({AuditCount} audit records)",
                step.Name, totalRowsAffected, enabledMappings.Count, allChangeRecords.Count);

            // Auto-discover new organizational field values into FieldLookupValues
            await AutoDiscoverFieldValuesAsync(connection, enabledMappings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FieldSync step '{StepName}' failed", step.Name);
            result.Success = false;
            result.Errors++;
            throw;
        }

        return result;
    }

    /// <summary>
    /// After FieldSync, discover new distinct values for organizational fields and insert
    /// them into FieldLookupValues so they appear in Organization Center and lookup dropdowns.
    /// </summary>
    private async Task AutoDiscoverFieldValuesAsync(SqlConnection connection, List<InternalSyncStepMapping> mappings)
    {
        // Only auto-discover for known organizational fields
        var orgFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Department", "Division", "Company", "Office", "Building", "CostCenter",
            "City", "State", "Country", "IdentityType", "ContractType"
        };

        foreach (var mapping in mappings)
        {
            if (!orgFields.Contains(mapping.TargetField)) continue;

            try
            {
                var fieldName = mapping.TargetField;
                var sql = string.Concat(
                    "INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt) ",
                    "SELECT NEWID(), @FieldName, src.val, 0, 1, GETUTCDATE() ",
                    "FROM (SELECT DISTINCT [", fieldName, "] AS val FROM Identities WHERE [", fieldName, "] IS NOT NULL AND [", fieldName, "] != '') src ",
                    "WHERE NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = @FieldName AND Value = src.val)");

                var inserted = await connection.ExecuteAsync(sql, new { FieldName = fieldName }, commandTimeout: 60);
                if (inserted > 0)
                    _logger.LogInformation("AutoDiscover: Added {Count} new {Field} values to FieldLookupValues", inserted, fieldName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AutoDiscover: Failed to discover values for field {Field}", mapping.TargetField);
            }
        }
    }

    private class FieldSyncOutputRow
    {
        public Guid Id { get; set; }
        public string? DisplayName { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    /// <summary>
    /// Resolve Object.ManagerSourceId (DN) to ManagerObjectId (Guid).
    /// </summary>
    private async Task<StepExecutionResult> ExecuteManagerResolveAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = "Resolving manager object IDs..."
        });

        // Count total objects with manager references
        result.Found = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Objects WHERE ManagerSourceId IS NOT NULL", commandTimeout: 120);

        // Resolve ManagerObjectId from ManagerSourceId (DN)
        var sql = @"
            UPDATE o
            SET o.ManagerObjectId = manager.Id,
                o.LastSyncedAt = GETUTCDATE()
            OUTPUT INSERTED.Id, INSERTED.DisplayName, INSERTED.ManagerSourceId,
                   INSERTED.UserPrincipalName, INSERTED.Email, INSERTED.Username, INSERTED.SourceUniqueId
            FROM Objects o
            INNER JOIN Objects manager ON manager.DN = o.ManagerSourceId
                AND manager.SourceConnectionId = o.SourceConnectionId
            WHERE o.ManagerSourceId IS NOT NULL
              AND o.ManagerObjectId IS NULL
              AND manager.Id IS NOT NULL";

        var resolvedRows = (await connection.QueryAsync<ResolvedManagerRow>(sql, commandTimeout: 120)).ToList();
        result.Updated = resolvedRows.Count;
        result.Processed = result.Found;  // All objects were evaluated (matches external sync convention)
        result.Skipped = result.Found - resolvedRows.Count; // Already resolved + unresolvable

        var changeRecords = resolvedRows.Select(row => new ChangeRecord
        {
            OperationType = ChangeOpType.Update,
            EntityType = "Object",
            EntityId = row.Id,
            EntityDisplayName = row.DisplayName ?? row.Id.ToString(),
            PropertyName = "ManagerObjectId",
            NewValue = row.ManagerSourceId
        }).ToList();

        await RecordChangesAsync(changeRecords, stepRunId);

        // Build audit logs for resolved objects
        foreach (var row in resolvedRows)
        {
            result.AuditLogs.Add(new SyncAuditLog
            {
                Id = Guid.NewGuid(),
                ObjectId = row.Id,
                OperationType = "Updated",
                ObjectDisplayName = row.DisplayName ?? row.Id.ToString(),
                SourceUniqueId = row.SourceUniqueId,
                Email = row.Email,
                Username = row.Username,
                UserPrincipalName = row.UserPrincipalName,
                ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "ManagerObjectId", After = row.ManagerSourceId } }),
                ChangeCount = 1,
                Timestamp = DateTime.UtcNow
            });
        }

        // Build audit logs for already-resolved objects (skipped) — cap at 5000
        const int maxSkippedAuditLogs = 5000;
        var alreadyResolvedSql = $@"
            SELECT TOP({maxSkippedAuditLogs}) Id, DisplayName, Email, Username, UserPrincipalName, SourceUniqueId
            FROM Objects
            WHERE ManagerSourceId IS NOT NULL AND ManagerObjectId IS NOT NULL";
        var alreadyResolved = await connection.QueryAsync<ObjectDto>(alreadyResolvedSql, commandTimeout: 120);

        foreach (var obj in alreadyResolved)
        {
            result.AuditLogs.Add(new SyncAuditLog
            {
                Id = Guid.NewGuid(),
                ObjectId = obj.Id,
                OperationType = "Skipped",
                ObjectDisplayName = obj.DisplayName ?? obj.Email ?? obj.Username,
                SourceUniqueId = obj.SourceUniqueId,
                Email = obj.Email,
                Username = obj.Username,
                UserPrincipalName = obj.UserPrincipalName,
                ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "ManagerObjectId", After = "Manager already resolved" } }),
                ChangeCount = 0,
                Timestamp = DateTime.UtcNow
            });
        }

        // Build audit logs for unresolvable objects (manager DN not found) — remaining skipped
        var resolvedObjectIds = resolvedRows.Select(r => r.Id).ToHashSet();
        var unresolvedSql = $@"
            SELECT TOP({maxSkippedAuditLogs}) Id, DisplayName, Email, Username, UserPrincipalName, SourceUniqueId
            FROM Objects
            WHERE ManagerSourceId IS NOT NULL AND ManagerObjectId IS NULL";
        var unresolvedObjects = await connection.QueryAsync<ObjectDto>(unresolvedSql, commandTimeout: 120);

        foreach (var obj in unresolvedObjects)
        {
            if (resolvedObjectIds.Contains(obj.Id)) continue; // Just resolved above
            result.AuditLogs.Add(new SyncAuditLog
            {
                Id = Guid.NewGuid(),
                ObjectId = obj.Id,
                OperationType = "Skipped",
                ObjectDisplayName = obj.DisplayName ?? obj.Email ?? obj.Username,
                SourceUniqueId = obj.SourceUniqueId,
                Email = obj.Email,
                Username = obj.Username,
                UserPrincipalName = obj.UserPrincipalName,
                ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "ManagerObjectId", After = "Manager DN not found in database" } }),
                ChangeCount = 0,
                Timestamp = DateTime.UtcNow
            });
        }

        _logger.LogInformation("ManagerResolve step completed: checked {Total}, resolved {Resolved}, skipped {Skipped}",
            result.Found, resolvedRows.Count, result.Skipped);

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = $"Resolved {resolvedRows.Count} manager relationships, {result.Skipped} skipped",
            Processed = resolvedRows.Count,
            Skipped = result.Skipped,
            Complete = true
        });

        return result;
    }

    /// <summary>
    /// Assign Identity.ManagerIdentityId from linked Object's ManagerObjectId.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteManagerAssignAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = "Assigning identity managers..."
        });

        // Count total identities in scope (linked to authoritative objects)
        result.Found = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM Identities i
            INNER JOIN Objects authObject ON authObject.IdentityId = i.Id AND authObject.IdentityId IS NOT NULL",
            commandTimeout: 120);

        // Capture rows to assign (for audit logging) then perform the update
        var preAssignSql = @"
            SELECT i.Id AS IdentityId, i.DisplayName, i.UserPrincipalName,
                   managerIdentity.DisplayName AS ManagerName
            FROM Identities i
            INNER JOIN Objects authObject ON authObject.IdentityId = i.Id AND authObject.IdentityId IS NOT NULL
            INNER JOIN Objects managerObject ON managerObject.Id = authObject.ManagerObjectId
            INNER JOIN Identities managerIdentity ON managerIdentity.Id = managerObject.IdentityId
            WHERE i.ManagerIdentityId IS NULL
              AND managerIdentity.Id IS NOT NULL";

        var assignedRows = (await connection.QueryAsync<ManagerAssignRow>(preAssignSql, commandTimeout: 120)).ToList();

        if (assignedRows.Count > 0)
        {
            var assignSql = @"
                UPDATE i
                SET i.ManagerIdentityId = managerIdentity.Id,
                    i.ModifiedAt = GETUTCDATE()
                FROM Identities i
                INNER JOIN Objects authObject ON authObject.IdentityId = i.Id AND authObject.IdentityId IS NOT NULL
                INNER JOIN Objects managerObject ON managerObject.Id = authObject.ManagerObjectId
                INNER JOIN Identities managerIdentity ON managerIdentity.Id = managerObject.IdentityId
                WHERE i.ManagerIdentityId IS NULL
                  AND managerIdentity.Id IS NOT NULL";
            await connection.ExecuteAsync(assignSql, commandTimeout: 120);
        }

        result.Updated = assignedRows.Count;

        var changeRecords = new List<ChangeRecord>();

        foreach (var row in assignedRows)
        {
            changeRecords.Add(new ChangeRecord
            {
                OperationType = ChangeOpType.Update,
                EntityType = "Identity",
                EntityId = row.IdentityId,
                EntityDisplayName = row.DisplayName ?? row.IdentityId.ToString(),
                PropertyName = "ManagerIdentityId",
                NewValue = row.ManagerName
            });
        }

        // Build audit logs for assigned identities (ObjectId = null since these are Identity-level ops, FK is to Objects)
        foreach (var row in assignedRows)
        {
            result.AuditLogs.Add(new SyncAuditLog
            {
                Id = Guid.NewGuid(),
                ObjectId = null,
                OperationType = "Updated",
                ObjectDisplayName = row.DisplayName ?? row.IdentityId.ToString(),
                UserPrincipalName = row.UserPrincipalName,
                ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "ManagerIdentityId", After = row.ManagerName } }),
                ChangeCount = 1,
                Timestamp = DateTime.UtcNow
            });
        }

        // Capture orphaned managers then clear them
        var preClearSql = @"
            SELECT i.Id AS IdentityId, i.DisplayName, i.UserPrincipalName
            FROM Identities i
            INNER JOIN Objects authObject ON authObject.IdentityId = i.Id AND authObject.IdentityId IS NOT NULL
            WHERE i.ManagerIdentityId IS NOT NULL
              AND authObject.ManagerObjectId IS NULL";

        var clearedRows = (await connection.QueryAsync<ManagerAssignRow>(preClearSql, commandTimeout: 120)).ToList();

        if (clearedRows.Count > 0)
        {
            var clearSql = @"
                UPDATE i
                SET i.ManagerIdentityId = NULL,
                    i.ModifiedAt = GETUTCDATE()
                FROM Identities i
                INNER JOIN Objects authObject ON authObject.IdentityId = i.Id AND authObject.IdentityId IS NOT NULL
                WHERE i.ManagerIdentityId IS NOT NULL
                  AND authObject.ManagerObjectId IS NULL";
            await connection.ExecuteAsync(clearSql, commandTimeout: 120);
        }

        result.Processed = result.Found;  // All identities were evaluated (matches external sync convention)
        result.Skipped = result.Found - assignedRows.Count - clearedRows.Count;

        foreach (var row in clearedRows)
        {
            changeRecords.Add(new ChangeRecord
            {
                OperationType = ChangeOpType.Update,
                EntityType = "Identity",
                EntityId = row.IdentityId,
                EntityDisplayName = row.DisplayName ?? row.IdentityId.ToString(),
                PropertyName = "ManagerIdentityId",
                OldValue = "(orphaned)",
                Reason = "Orphan cleared"
            });

            // Build audit log for orphan-cleared identities
            result.AuditLogs.Add(new SyncAuditLog
            {
                Id = Guid.NewGuid(),
                ObjectId = null,
                OperationType = "Updated",
                ObjectDisplayName = row.DisplayName ?? row.IdentityId.ToString(),
                UserPrincipalName = row.UserPrincipalName,
                ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "ManagerIdentityId", Before = "(orphaned)", After = "(cleared)" } }),
                ChangeCount = 1,
                Timestamp = DateTime.UtcNow
            });
        }

        await RecordChangesAsync(changeRecords, stepRunId);

        // Build audit logs for skipped identities (already have correct manager) — cap at 5000
        if (result.Skipped > 0)
        {
            const int maxSkippedAuditLogs = 5000;
            var skippedSql = $@"
                SELECT TOP({maxSkippedAuditLogs}) i.Id AS IdentityId, i.DisplayName, i.UserPrincipalName
                FROM Identities i
                INNER JOIN Objects authObject ON authObject.IdentityId = i.Id AND authObject.IdentityId IS NOT NULL
                WHERE i.ManagerIdentityId IS NOT NULL
                  AND authObject.ManagerObjectId IS NOT NULL";

            var skippedRows = await connection.QueryAsync<ManagerAssignRow>(skippedSql, commandTimeout: 120);

            foreach (var row in skippedRows)
            {
                result.AuditLogs.Add(new SyncAuditLog
                {
                    Id = Guid.NewGuid(),
                    ObjectId = null,
                    OperationType = "Skipped",
                    ObjectDisplayName = row.DisplayName ?? row.IdentityId.ToString(),
                    UserPrincipalName = row.UserPrincipalName,
                    ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "ManagerIdentityId", After = "Manager already assigned" } }),
                    ChangeCount = 0,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        _logger.LogInformation("ManagerAssign step completed: checked {Total}, assigned {Assigned}, orphans cleared {Cleared}, skipped {Skipped}",
            result.Found, assignedRows.Count, clearedRows.Count, result.Skipped);

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = $"Assigned {assignedRows.Count} managers, cleared {clearedRows.Count} orphans, {result.Skipped} skipped",
            Processed = assignedRows.Count + clearedRows.Count,
            Skipped = result.Skipped,
            Complete = true
        });

        return result;
    }

    /// <summary>
    /// Aggregate tags from Objects to Identities.
    /// When an Object has a tag and is linked to an Identity (via Objects.IdentityId),
    /// the Identity inherits that tag. Stale inherited tags are cleaned up.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteTagAggregateAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = "Aggregating tags from Objects to Identities..."
        });

        // Count scope: existing inherited IdentityTags + distinct (IdentityId, TagId) pairs reachable from ObjectTags
        result.Found = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM (
                SELECT it.IdentityId, it.TagId FROM IdentityTags it WHERE it.IsInherited = 1
                UNION
                SELECT DISTINCT o.IdentityId, ot.TagId
                FROM ObjectTags ot
                INNER JOIN Objects o ON o.Id = ot.ObjectId
                WHERE o.IdentityId IS NOT NULL
            ) AS scope", commandTimeout: 120);

        // Pre-query additions: ObjectTag→Object→Identity pairs not already in IdentityTags
        var additionsSql = @"
            SELECT DISTINCT o.IdentityId, ot.TagId, i.DisplayName AS IdentityDisplayName, t.Name AS TagName
            FROM ObjectTags ot
            INNER JOIN Objects o ON o.Id = ot.ObjectId
            INNER JOIN Identities i ON i.Id = o.IdentityId
            INNER JOIN Tags t ON t.Id = ot.TagId
            WHERE o.IdentityId IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM IdentityTags it
                  WHERE it.IdentityId = o.IdentityId AND it.TagId = ot.TagId
              )";

        var additions = (await connection.QueryAsync<TagAggregateRow>(additionsSql, commandTimeout: 120)).ToList();

        // Pre-query removals: inherited IdentityTags with no backing ObjectTag
        var removalsSql = @"
            SELECT it.IdentityId, it.TagId, i.DisplayName AS IdentityDisplayName, t.Name AS TagName
            FROM IdentityTags it
            INNER JOIN Identities i ON i.Id = it.IdentityId
            INNER JOIN Tags t ON t.Id = it.TagId
            WHERE it.IsInherited = 1
              AND NOT EXISTS (
                  SELECT 1 FROM ObjectTags ot
                  INNER JOIN Objects o ON o.Id = ot.ObjectId
                  WHERE o.IdentityId = it.IdentityId AND ot.TagId = it.TagId
              )";

        var removals = (await connection.QueryAsync<TagAggregateRow>(removalsSql, commandTimeout: 120)).ToList();

        // Execute INSERT for new inherited tags
        if (additions.Count > 0)
        {
            var insertSql = @"
                INSERT INTO IdentityTags (Id, IdentityId, TagId, IsInherited, CreatedAt, CreatedBy)
                SELECT NEWID(), src.IdentityId, src.TagId, 1, GETUTCDATE(), 'InternalSync'
                FROM (
                    SELECT DISTINCT o.IdentityId, ot.TagId
                    FROM ObjectTags ot
                    INNER JOIN Objects o ON o.Id = ot.ObjectId
                    WHERE o.IdentityId IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM IdentityTags it
                          WHERE it.IdentityId = o.IdentityId AND it.TagId = ot.TagId
                      )
                ) AS src";
            await connection.ExecuteAsync(insertSql, commandTimeout: 120);
        }

        // Execute DELETE for stale inherited tags
        if (removals.Count > 0)
        {
            var deleteSql = @"
                DELETE FROM IdentityTags
                WHERE IsInherited = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM ObjectTags ot
                      INNER JOIN Objects o ON o.Id = ot.ObjectId
                      WHERE o.IdentityId = IdentityTags.IdentityId AND ot.TagId = IdentityTags.TagId
                  )";
            await connection.ExecuteAsync(deleteSql, commandTimeout: 120);
        }

        result.Created = additions.Count;
        result.Updated = removals.Count;  // Deletions tracked as Updated
        result.Processed = result.Found;
        result.Skipped = result.Found - additions.Count - removals.Count;

        // Build change records
        var changeRecords = new List<ChangeRecord>();

        foreach (var row in additions)
        {
            changeRecords.Add(new ChangeRecord
            {
                OperationType = ChangeOpType.Create,
                EntityType = "IdentityTag",
                EntityId = row.IdentityId,
                EntityDisplayName = row.IdentityDisplayName ?? row.IdentityId.ToString(),
                PropertyName = "Tag",
                NewValue = row.TagName
            });
        }

        foreach (var row in removals)
        {
            changeRecords.Add(new ChangeRecord
            {
                OperationType = ChangeOpType.Delete,
                EntityType = "IdentityTag",
                EntityId = row.IdentityId,
                EntityDisplayName = row.IdentityDisplayName ?? row.IdentityId.ToString(),
                PropertyName = "Tag",
                OldValue = row.TagName,
                Reason = "Stale inherited tag removed"
            });
        }

        // Build audit logs for additions
        foreach (var row in additions)
        {
            result.AuditLogs.Add(new SyncAuditLog
            {
                Id = Guid.NewGuid(),
                ObjectId = null,
                OperationType = "Created",
                ObjectDisplayName = row.IdentityDisplayName ?? row.IdentityId.ToString(),
                ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "InheritedTag", After = row.TagName } }),
                ChangeCount = 1,
                Timestamp = DateTime.UtcNow
            });
        }

        // Build audit logs for removals
        foreach (var row in removals)
        {
            result.AuditLogs.Add(new SyncAuditLog
            {
                Id = Guid.NewGuid(),
                ObjectId = null,
                OperationType = "Deleted",
                ObjectDisplayName = row.IdentityDisplayName ?? row.IdentityId.ToString(),
                ChangeDetails = JsonSerializer.Serialize(new[] { new { Field = "InheritedTag", Before = row.TagName } }),
                ChangeCount = 1,
                Timestamp = DateTime.UtcNow
            });
        }

        await RecordChangesAsync(changeRecords, stepRunId);

        _logger.LogInformation("TagAggregate step completed: scope {Total}, added {Added}, removed {Removed}, skipped {Skipped}",
            result.Found, additions.Count, removals.Count, result.Skipped);

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = $"Inherited {additions.Count} tags, removed {removals.Count} stale tags, {result.Skipped} unchanged",
            Processed = additions.Count + removals.Count,
            Skipped = result.Skipped,
            Complete = true
        });

        return result;
    }

    #endregion

    #region Person to Object Steps

    /// <summary>
    /// Create Objects from Identities (provisioning new accounts).
    /// </summary>
    private async Task<StepExecutionResult> ExecutePersonToObjectCreateAsync(
        InternalSyncStep step,
        SqlConnection connection,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        if (!step.SourceConnectionId.HasValue)
        {
            result.Success = false;
            result.ErrorMessage = "PersonToObjectCreate requires a SourceConnectionId to provision to";
            return result;
        }

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = "Finding identities without linked objects..."
        });

        // Find identities that don't have an object in the target connection
        var sql = @"
            SELECT i.Id, i.Email, i.Username, i.FirstName, i.LastName, i.DisplayName,
                    i.Department, i.JobTitle, i.Phone
            FROM Identities i
            WHERE i.IsActive = 1
              AND NOT EXISTS (
                  SELECT 1 FROM Objects o
                  WHERE o.IdentityId = i.Id
                    AND o.SourceConnectionId = @SourceConnectionId
              )";

        var identities = (await connection.QueryAsync<IdentityDto>(sql, new
        {
            SourceConnectionId = step.SourceConnectionId
        })).ToList();

        result.Found = identities.Count;  // Set source count for ObjectsQueried
        result.Processed = identities.Count;

        if (identities.Count == 0)
        {
            _logger.LogInformation("No identities to provision in step '{StepName}'", step.Name);
            return result;
        }

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = $"Provisioning {identities.Count} accounts...",
            Total = identities.Count
        });

        foreach (var identity in identities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Check if an unlinked object already exists in the target connection (avoid duplicates)
                Guid? existingObjectId = null;

                if (!string.IsNullOrEmpty(identity.Email))
                {
                    existingObjectId = await connection.ExecuteScalarAsync<Guid?>(
                        "SELECT Id FROM Objects WHERE Email = @Email AND SourceConnectionId = @SourceConnectionId AND IdentityId IS NULL",
                        new { identity.Email, SourceConnectionId = step.SourceConnectionId.Value }, commandTimeout: 10);
                }

                if (existingObjectId == null && !string.IsNullOrEmpty(identity.Username))
                {
                    existingObjectId = await connection.ExecuteScalarAsync<Guid?>(
                        "SELECT Id FROM Objects WHERE UserPrincipalName = @UPN AND SourceConnectionId = @SourceConnectionId AND IdentityId IS NULL",
                        new { UPN = identity.Username, SourceConnectionId = step.SourceConnectionId.Value }, commandTimeout: 10);
                }

                if (existingObjectId == null && !string.IsNullOrEmpty(identity.Username))
                {
                    existingObjectId = await connection.ExecuteScalarAsync<Guid?>(
                        "SELECT Id FROM Objects WHERE Username = @Username AND SourceConnectionId = @SourceConnectionId AND IdentityId IS NULL",
                        new { identity.Username, SourceConnectionId = step.SourceConnectionId.Value }, commandTimeout: 10);
                }

                if (existingObjectId.HasValue)
                {
                    // Link to existing object instead of creating a duplicate
                    await LinkObjectToIdentityAsync(connection, existingObjectId.Value, identity.Id);
                    result.Matched++;
                    _logger.LogDebug("Linked existing object {ObjectId} to identity {IdentityId} (dedup match)", existingObjectId.Value, identity.Id);
                }
                else
                {
                    var objectId = await CreateObjectFromIdentityAsync(connection, identity, step.SourceConnectionId.Value);
                    result.Created++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to provision object for identity {IdentityId}", identity.Id);
                result.Errors++;
            }
        }

        _logger.LogInformation("PersonToObjectCreate step completed: {Created} provisioned, {Matched} linked to existing, {Errors} errors",
            result.Created, result.Matched, result.Errors);

        return result;
    }

    /// <summary>
    /// Push Identity field changes to linked Objects.
    /// </summary>
    /// <summary>
    /// Link existing Objects to Identities based on matching criteria.
    /// </summary>
    private async Task<StepExecutionResult> ExecutePersonToObjectLinkAsync(
        InternalSyncStep step,
        SqlConnection connection,
        Guid? stepRunId,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Similar to ObjectToPersonMatch but from Person perspective
        return await ExecuteObjectToPersonMatchAsync(step, connection, stepRunId, progress, cancellationToken);
    }

    /// <summary>
    /// Sync specific fields from Identity to Object using step mappings.
    /// </summary>
    private async Task<StepExecutionResult> ExecutePersonToObjectFieldSyncAsync(
        InternalSyncStep step,
        SqlConnection connection,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        if (step.Mappings == null || !step.Mappings.Any())
        {
            _logger.LogWarning("PersonToObjectFieldSync step '{StepName}' has no mappings configured", step.Name);
            return result;
        }

        // Get linked identities with their objects
        var sql = @"
            SELECT i.Id AS IdentityId, o.Id AS ObjectId,
                   i.Email, i.Username, i.FirstName, i.LastName,
                   i.DisplayName,  i.Department, i.JobTitle, i.Phone
            FROM Identities i
            INNER JOIN Objects o ON o.IdentityId = i.Id
            WHERE i.IsActive = 1";

        var queryParams = new DynamicParameters();
        if (step.SourceConnectionId.HasValue)
        {
            sql += " AND o.SourceConnectionId = @SourceConnectionId";
            queryParams.Add("SourceConnectionId", step.SourceConnectionId.Value);
        }

        var linkedPairs = (await connection.QueryAsync<dynamic>(sql, queryParams)).ToList();
        result.Found = linkedPairs.Count;  // Set source count for ObjectsQueried
        result.Processed = linkedPairs.Count;

        if (linkedPairs.Count == 0)
        {
            _logger.LogInformation("No linked pairs to sync fields for in step '{StepName}'", step.Name);
            return result;
        }

        var enabledMappings = step.Mappings.Where(m => m.IsEnabled).OrderBy(m => m.MappingOrder).ToList();

        foreach (var pair in linkedPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var updates = new List<string>();
                var parameters = new DynamicParameters();
                parameters.Add("ObjectId", (Guid)pair.ObjectId);

                foreach (var mapping in enabledMappings)
                {
                    var sourceValue = GetPropertyValue(pair, mapping.SourceField);

                    if (sourceValue != null || mapping.DefaultValue != null)
                    {
                        var value = sourceValue ?? mapping.DefaultValue;
                        var targetColumn = ValidateObjectColumn(mapping.TargetField);

                        if (mapping.OverwriteExisting)
                        {
                            updates.Add($"[{targetColumn}] = @{targetColumn}");
                        }
                        else
                        {
                            updates.Add($"[{targetColumn}] = COALESCE([{targetColumn}], @{targetColumn})");
                        }

                        parameters.Add(targetColumn, value);
                    }
                }

                if (updates.Any())
                {
                    updates.Add("LastSeenAt = GETUTCDATE()");
                    var updateSql = $"UPDATE Objects SET {string.Join(", ", updates)} WHERE Id = @ObjectId";
                    await connection.ExecuteAsync(updateSql, parameters);
                    result.Updated++;
                }
                else
                {
                    result.Skipped++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync fields for object {ObjectId}", (Guid)pair.ObjectId);
                result.Errors++;
            }
        }

        _logger.LogInformation("PersonToObjectFieldSync step completed: {Updated} updated, {Skipped} skipped, {Errors} errors",
            result.Updated, result.Skipped, result.Errors);

        return result;
    }

    /// <summary>
    /// Deprovision/disable Objects for inactive Identities.
    /// </summary>
    private async Task<StepExecutionResult> ExecutePersonToObjectDeprovisionAsync(
        InternalSyncStep step,
        SqlConnection connection,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = "Finding objects to deprovision..."
        });

        // Count total objects linked to inactive identities (both already and newly deprovisioned)
        result.Found = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Objects o INNER JOIN Identities i ON i.Id = o.IdentityId WHERE i.IsActive = 0",
            commandTimeout: 120);

        // Mark objects as inactive when their identity is inactive
        var sql = @"
            UPDATE o
            SET o.IsActive = 0,
                o.LastSyncedAt = GETUTCDATE()
            FROM Objects o
            INNER JOIN Identities i ON i.Id = o.IdentityId
            WHERE i.IsActive = 0
              AND o.IsActive = 1";

        var deprovisioned = await connection.ExecuteAsync(sql, commandTimeout: 120);
        result.Updated = deprovisioned;
        result.Processed = result.Found;  // All evaluated (matches convention)
        result.Skipped = result.Found - deprovisioned;  // Already inactive

        _logger.LogInformation("PersonToObjectDeprovision step completed: {Deprovisioned} objects deprovisioned",
            deprovisioned);

        return result;
    }

    /// <summary>
    /// Provision AD user accounts from Objects created by HR import.
    /// Delegates to ADProvisioningStepExecutor for actual AD write operations.
    /// </summary>
    private async Task<StepExecutionResult> ExecutePersonToObjectProvisionADAsync(
        InternalSyncStep step,
        SqlConnection connection,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new StepExecutionResult { Success = true };

        if (_adProvisioningExecutor == null)
        {
            _logger.LogWarning("PersonToObjectProvisionAD: ADProvisioningStepExecutor not available, skipping");
            result.ErrorMessage = "AD provisioning service not configured";
            return result;
        }

        progress?.Report(new InternalSyncProgress
        {
            Phase = step.Name,
            Message = "Provisioning AD accounts from Identity data..."
        });

        // Parse step config for AD provisioning parameters
        var config = ParseConfig<Models.ADProvisioningConfig>(step.Configuration);

        // Determine target connection from config or step's source connection
        var targetConnectionId = step.SourceConnectionId ?? Guid.Empty;
        // Check if config JSON has a connectionId override
        if (!string.IsNullOrEmpty(step.Configuration))
        {
            try
            {
                var configDoc = System.Text.Json.JsonDocument.Parse(step.Configuration);
                if (configDoc.RootElement.TryGetProperty("connectionId", out var connProp)
                    && Guid.TryParse(connProp.GetString(), out var configConnId))
                {
                    targetConnectionId = configConnId;
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse step configuration JSON for step {StepName}, using fallback connection", step.Name);
            }
        }

        if (targetConnectionId == Guid.Empty)
        {
            result.ErrorMessage = "No target connection ID configured for AD provisioning step";
            result.Success = false;
            return result;
        }

        var adResult = await _adProvisioningExecutor.ExecuteAsync(
            targetConnectionId,
            config.TargetOU,
            config.UpnSuffix,
            config.SamAccountNamePattern,
            config.DefaultPassword,
            config.EnableAccounts,
            continueOnError: true,
            ct: cancellationToken);

        result.Processed = adResult.TotalToProvision;
        result.Created = adResult.Provisioned;
        result.Errors = adResult.Errors;
        result.Success = adResult.Success;

        if (adResult.ErrorDetails.Count > 0)
        {
            result.ErrorMessage = string.Join("; ", adResult.ErrorDetails.Take(5));
        }

        _logger.LogInformation("PersonToObjectProvisionAD: Provisioned {Provisioned}/{Total}, Errors={Errors}",
            adResult.Provisioned, adResult.TotalToProvision, adResult.Errors);

        return result;
    }

    #endregion

    #region Change History Helpers

    private async Task RecordChangesAsync(List<ChangeRecord> records, Guid? correlationId)
    {
        if (records.Count == 0) return;
        foreach (var r in records)
        {
            r.CorrelationId = correlationId;
            r.Source = "InternalSync";
            r.UserId ??= "System";
            r.UserDisplayName ??= "Internal Sync Engine";
        }
        try { await _changeHistory.RecordBatchAsync(records); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to record {Count} change history entries", records.Count); }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Builds a SQL clause to filter objects by tag names.
    /// </summary>
    /// <param name="tagFilter">Comma-separated list of tag names, or null/* for no filter</param>
    /// <param name="objectTableAlias">Table alias for the Objects table (e.g., "o" for JOIN scenarios)</param>
    /// <returns>SQL clause and parameters object for Dapper</returns>
    private (string clause, object? parameters) BuildTagFilterClause(string? tagFilter, string objectTableAlias = "")
    {
        if (string.IsNullOrWhiteSpace(tagFilter) || tagFilter == "*")
            return ("", null);

        var tagNames = tagFilter.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (tagNames.Count == 0)
            return ("", null);

        // Use EXISTS instead of IN for better performance
        // When no alias provided, use "Objects" as the table reference to avoid ambiguity
        var prefix = string.IsNullOrEmpty(objectTableAlias) ? "Objects." : $"{objectTableAlias}.";
        var clause = $@"AND EXISTS (
            SELECT 1 FROM ObjectTags ot
            INNER JOIN Tags t ON t.Id = ot.TagId
            WHERE ot.ObjectId = {prefix}Id AND t.Name IN @TagNames
        )";

        return (clause, new { TagNames = tagNames });
    }

    private T ParseConfig<T>(string? json) where T : new()
    {
        if (string.IsNullOrEmpty(json))
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    private async Task LinkObjectToIdentityAsync(SqlConnection connection, Guid objectId, Guid identityId)
    {
        const string sql = @"
            UPDATE Objects
            SET IdentityId = @IdentityId, IsAuthoritative = 1, MatchMethod = 'InternalSync', MatchConfidence = 95, LastSeenAt = GETUTCDATE()
            WHERE Id = @ObjectId";

        var rowsAffected = await connection.ExecuteAsync(sql, new { ObjectId = objectId, IdentityId = identityId });
        _logger.LogDebug("Linked Object {ObjectId} to Identity {IdentityId} as authoritative ({RowsAffected} rows)", objectId, identityId, rowsAffected);
    }

    private async Task<Guid> CreateIdentityFromObjectAsync(SqlConnection connection, ObjectDto obj, string defaultStatus)
    {
        var newId = Guid.NewGuid();

        const string sql = @"
            INSERT INTO Identities (
                Id, DisplayName, FirstName, LastName,
                PrimaryEmail, PrimaryPhone, MobilePhone,
                Username, UserPrincipalName,
                Department, JobTitle, Company, Office, EmployeeId,
                Status, IsActive, CreatedAt, ModifiedAt
            )
            VALUES (
                @Id, @DisplayName, @FirstName, @LastName,
                @PrimaryEmail, @PrimaryPhone, @MobilePhone,
                @Username, @UserPrincipalName,
                @Department, @JobTitle, @Company, @Office, @EmployeeId,
                @Status, 1, GETUTCDATE(), GETUTCDATE()
            )";

        await connection.ExecuteAsync(sql, new
        {
            Id = newId,
            DisplayName = obj.DisplayName ?? $"{obj.FirstName} {obj.LastName}".Trim(),
            obj.FirstName,
            obj.LastName,
            PrimaryEmail = obj.Email,
            PrimaryPhone = obj.Phone,
            obj.MobilePhone,
            obj.Username,
            obj.UserPrincipalName,
            obj.Department,
            obj.JobTitle,
            obj.Company,
            obj.Office,
            obj.EmployeeId,
            Status = defaultStatus
        });

        return newId;
    }

    private async Task SetObjectAuthoritativeAsync(SqlConnection connection, Guid objectId)
    {
        const string sql = @"
            UPDATE Objects
            SET IsAuthoritative = 1, LastSeenAt = GETUTCDATE()
            WHERE Id = @ObjectId";

        await connection.ExecuteAsync(sql, new { ObjectId = objectId });
    }

    private async Task<Guid> CreateObjectFromIdentityAsync(SqlConnection connection, IdentityDto identity, Guid sourceConnectionId)
    {
        var newId = Guid.NewGuid();
        var sourceUniqueId = identity.Email ?? identity.Id.ToString();

        const string sql = @"
            INSERT INTO Objects (
                Id, SourceConnectionId, SourceUniqueId, SourceType, ObjectClass,
                Email, Username, FirstName, LastName, DisplayName,
                Department, JobTitle, Phone,
                IdentityId, IsActive, FirstSyncedAt, LastSyncedAt,
                IsAdminSDHolder, PasswordNeverExpires, IsBuiltIn
            )
            VALUES (
                @Id, @SourceConnectionId, @SourceUniqueId, 'IdentityCenter', 'user',
                @Email, @Username, @FirstName, @LastName, @DisplayName,
                @Department, @JobTitle, @Phone,
                @IdentityId, 1, GETUTCDATE(), GETUTCDATE(),
                0, 0, 0
            )";

        await connection.ExecuteAsync(sql, new
        {
            Id = newId,
            SourceConnectionId = sourceConnectionId,
            SourceUniqueId = sourceUniqueId,
            identity.Email,
            identity.Username,
            identity.FirstName,
            identity.LastName,
            DisplayName = identity.DisplayName ?? $"{identity.FirstName} {identity.LastName}".Trim(),
            identity.Department,
            identity.JobTitle,
            identity.Phone,
            IdentityId = identity.Id
        });

        return newId;
    }

    private object? GetPropertyValue(dynamic obj, string propertyName)
    {
        try
        {
            var dict = obj as IDictionary<string, object>;
            if (dict != null && dict.TryGetValue(propertyName, out var value))
                return value;

            var type = obj.GetType();
            var prop = type.GetProperty(propertyName);
            return prop?.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }

    private static readonly Regex _safeColumnRegex = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    /// <summary>
    /// Validates a mapping target column before it is interpolated into a SQL identifier position.
    /// Admin-authored mappings control this value, so anything outside a strict identifier allow-list
    /// is rejected rather than executed.
    /// </summary>
    private static string ValidateObjectColumn(string column)
    {
        if (string.IsNullOrWhiteSpace(column) || !_safeColumnRegex.IsMatch(column))
            throw new InvalidOperationException(
                $"Field sync rejected target column '{column}' — not a valid SQL identifier.");
        return column;
    }

    #endregion

    #region DTOs

    private class ObjectDto
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }
        public string? SourceUniqueId { get; set; }
        public string? Department { get; set; }
        public string? JobTitle { get; set; }
        public string? Phone { get; set; }
        public string? DN { get; set; }
        public string? CN { get; set; }
        public string? EmployeeId { get; set; }
        public string? MobilePhone { get; set; }
        public string? Company { get; set; }
        public string? Office { get; set; }
        public string? UserPrincipalName { get; set; }
    }

    private class ResolvedManagerRow
    {
        public Guid Id { get; set; }
        public string? DisplayName { get; set; }
        public string? ManagerSourceId { get; set; }
        public string? UserPrincipalName { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? SourceUniqueId { get; set; }
    }

    private class ManagerAssignRow
    {
        public Guid IdentityId { get; set; }
        public string? DisplayName { get; set; }
        public string? UserPrincipalName { get; set; }
        public string? ManagerName { get; set; }
    }

    private class TagAggregateRow
    {
        public Guid IdentityId { get; set; }
        public Guid TagId { get; set; }
        public string? IdentityDisplayName { get; set; }
        public string? TagName { get; set; }
    }

    private class IdentityDto
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }  // Maps to PrimaryEmail
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }
        public string? SourceUniqueId { get; set; }
        public string? Department { get; set; }
        public string? JobTitle { get; set; }
        public string? Phone { get; set; }  // Maps to PrimaryPhone
        public string? EmployeeId { get; set; }
        public string? MobilePhone { get; set; }
        public string? Company { get; set; }
        public string? Office { get; set; }
        public string? UserPrincipalName { get; set; }
    }

    #endregion
}
