using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Comprehensive audit service for all system events including jobs, syncs, and configuration changes.
    /// Extends the base AuditLogService with system-level event logging.
    /// </summary>
    public class SystemAuditService : ISystemAuditService
    {
        private readonly string _defaultConnectionString;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IGlobalLogger _logger;

        public SystemAuditService(
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor,
            IGlobalLogger logger)
        {
            _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        // MULTI-TENANT SEAM (SaaS Day 4): audit writes follow the current request's tenant so a tenant's
        // audit rows land in the tenant DB. Falls back to DefaultConnection for control-plane/legacy.
        private string _connectionString =>
            DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

        private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

        #region Base IAuditLogService Implementation

        public Task LogChangeAsync(ChangeAuditEntry entry)
        {
            if (entry == null) return Task.CompletedTask;

            // Fire-and-forget to prevent blocking sync operations
            // Use ThreadPool.QueueUserWorkItem to avoid Task.Run connection issues
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using var connection = CreateConnection();
                    connection.Open();

                    var log = ChangeAuditLog.FromEntry(entry);

                    const string sql = @"
                        INSERT INTO ChangeAuditLogs
                            (Timestamp, OperationType, EntityType, EntityId, EntityDisplayName,
                             RelatedEntityId, RelatedEntityName, PropertyName, OldValue, NewValue,
                             Reason, ErrorMessage, UserId, UserDisplayName, IpAddress, Source, Success)
                        VALUES
                            (@Timestamp, @OperationType, @EntityType, @EntityId, @EntityDisplayName,
                             @RelatedEntityId, @RelatedEntityName, @PropertyName, @OldValue, @NewValue,
                             @Reason, @ErrorMessage, @UserId, @UserDisplayName, @IpAddress, @Source, @Success)";

                    connection.Execute(sql, log, commandTimeout: 30);
                }
                catch
                {
                    // Silently ignore - audit logging should never block operations
                }
            });

            return Task.CompletedTask;
        }

        public async Task LogChangesAsync(IEnumerable<ChangeAuditEntry> entries)
        {
            if (entries == null || !entries.Any()) return;

            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();

                try
                {
                    var logs = entries.Select(ChangeAuditLog.FromEntry).ToList();

                    const string sql = @"
                        INSERT INTO ChangeAuditLogs
                            (Timestamp, OperationType, EntityType, EntityId, EntityDisplayName,
                             RelatedEntityId, RelatedEntityName, PropertyName, OldValue, NewValue,
                             Reason, ErrorMessage, UserId, UserDisplayName, IpAddress, Source, Success)
                        VALUES
                            (@Timestamp, @OperationType, @EntityType, @EntityId, @EntityDisplayName,
                             @RelatedEntityId, @RelatedEntityName, @PropertyName, @OldValue, @NewValue,
                             @Reason, @ErrorMessage, @UserId, @UserDisplayName, @IpAddress, @Source, @Success)";

                    await connection.ExecuteAsync(sql, logs, transaction, commandTimeout: 30);

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to batch log {Count} audit entries", entries.Count());
            }
        }

        public async Task<List<ChangeAuditEntry>> GetObjectHistoryAsync(Guid objectId, int limit = 50)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();

            const string sql = @"
                SELECT TOP (@Limit) *
                FROM ChangeAuditLogs
                WHERE EntityId = @ObjectId OR RelatedEntityId = @ObjectId
                ORDER BY Timestamp DESC";

            var logs = await connection.QueryAsync<ChangeAuditLog>(sql, new { ObjectId = objectId, Limit = limit });

            return logs.Select(l => l.ToEntry()).ToList();
        }

        public async Task<List<ChangeAuditEntry>> GetPersonHistoryAsync(Guid personId, int limit = 50)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();

            // Get person's linked object IDs (IdentityId refers to the Person/Identity)
            const string linkedObjectsSql = @"
                SELECT Id FROM Objects WHERE IdentityId = @PersonId";

            var linkedObjectIds = (await connection.QueryAsync<Guid>(linkedObjectsSql, new { PersonId = personId })).ToList();

            var allIds = linkedObjectIds.Append(personId).ToList();

            const string sql = @"
                SELECT TOP (@Limit) *
                FROM ChangeAuditLogs
                WHERE EntityId IN @AllIds OR RelatedEntityId IN @AllIds
                ORDER BY Timestamp DESC";

            var logs = await connection.QueryAsync<ChangeAuditLog>(sql, new { AllIds = allIds, Limit = limit });

            return logs.Select(l => l.ToEntry()).ToList();
        }

        public async Task<List<ChangeAuditEntry>> GetUserActivityAsync(string userId, int limit = 50)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();

            const string sql = @"
                SELECT TOP (@Limit) *
                FROM ChangeAuditLogs
                WHERE UserId = @UserId
                ORDER BY Timestamp DESC";

            var logs = await connection.QueryAsync<ChangeAuditLog>(sql, new { UserId = userId, Limit = limit });

            return logs.Select(l => l.ToEntry()).ToList();
        }

        public async Task<List<ChangeAuditEntry>> GetRecentChangesAsync(int limit = 100)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();

            const string sql = @"
                SELECT TOP (@Limit) *
                FROM ChangeAuditLogs
                ORDER BY Timestamp DESC";

            var logs = await connection.QueryAsync<ChangeAuditLog>(sql, new { Limit = limit });

            return logs.Select(l => l.ToEntry()).ToList();
        }

        public async Task<List<ChangeAuditEntry>> SearchHistoryAsync(ChangeAuditSearchCriteria criteria)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();

            var sqlBuilder = new StringBuilder("SELECT * FROM ChangeAuditLogs WHERE 1=1");
            var parameters = new DynamicParameters();

            if (criteria.EntityId.HasValue)
            {
                sqlBuilder.Append(" AND (EntityId = @EntityId OR RelatedEntityId = @EntityId)");
                parameters.Add("EntityId", criteria.EntityId.Value);
            }

            if (!string.IsNullOrEmpty(criteria.EntityType))
            {
                sqlBuilder.Append(" AND EntityType = @EntityType");
                parameters.Add("EntityType", criteria.EntityType);
            }

            if (!string.IsNullOrEmpty(criteria.UserId))
            {
                sqlBuilder.Append(" AND UserId = @UserId");
                parameters.Add("UserId", criteria.UserId);
            }

            if (criteria.OperationType.HasValue)
            {
                sqlBuilder.Append(" AND OperationType = @OperationType");
                parameters.Add("OperationType", criteria.OperationType.Value);
            }

            if (criteria.FromDate.HasValue)
            {
                sqlBuilder.Append(" AND Timestamp >= @FromDate");
                parameters.Add("FromDate", criteria.FromDate.Value);
            }

            if (criteria.ToDate.HasValue)
            {
                sqlBuilder.Append(" AND Timestamp <= @ToDate");
                parameters.Add("ToDate", criteria.ToDate.Value);
            }

            if (!string.IsNullOrEmpty(criteria.PropertyName))
            {
                sqlBuilder.Append(" AND PropertyName = @PropertyName");
                parameters.Add("PropertyName", criteria.PropertyName);
            }

            if (criteria.SuccessOnly.HasValue)
            {
                sqlBuilder.Append(" AND Success = @Success");
                parameters.Add("Success", criteria.SuccessOnly.Value);
            }

            if (!string.IsNullOrEmpty(criteria.Source))
            {
                sqlBuilder.Append(" AND Source = @Source");
                parameters.Add("Source", criteria.Source);
            }

            sqlBuilder.Append(" ORDER BY Timestamp DESC");
            sqlBuilder.Append(" OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY");
            parameters.Add("Offset", criteria.Offset);
            parameters.Add("Limit", criteria.Limit);

            var logs = await connection.QueryAsync<ChangeAuditLog>(sqlBuilder.ToString(), parameters);

            return logs.Select(l => l.ToEntry()).ToList();
        }

        #endregion

        #region Job Execution Auditing

        public async Task LogJobStartedAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? triggeredBy = null, string? triggerType = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.JobExecutionStarted,
                EntityType = "Job",
                EntityId = relatedEntityId,
                EntityDisplayName = jobName,
                PropertyName = jobType,
                NewValue = JsonSerializer.Serialize(new { TriggerType = triggerType ?? "Scheduled", TriggeredBy = triggeredBy ?? userId ?? "System" }),
                UserId = userId ?? triggeredBy ?? "System",
                UserDisplayName = userName ?? triggeredBy ?? "System",
                IpAddress = ipAddress,
                Source = "Scheduler",
                Success = true
            });
        }

        public async Task LogJobCompletedAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            int itemsProcessed = 0, int itemsSucceeded = 0, int itemsFailed = 0,
            TimeSpan? duration = null, string? resultSummary = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.JobExecutionCompleted,
                EntityType = "Job",
                EntityId = relatedEntityId,
                EntityDisplayName = jobName,
                PropertyName = jobType,
                NewValue = JsonSerializer.Serialize(new
                {
                    ItemsProcessed = itemsProcessed,
                    ItemsSucceeded = itemsSucceeded,
                    ItemsFailed = itemsFailed,
                    DurationMs = duration?.TotalMilliseconds,
                    Summary = resultSummary
                }),
                UserId = "System",
                UserDisplayName = "System",
                Source = "Scheduler",
                Success = true
            });
        }

        public async Task LogJobFailedAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? errorMessage = null, string? exceptionDetails = null, TimeSpan? duration = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.JobExecutionFailed,
                EntityType = "Job",
                EntityId = relatedEntityId,
                EntityDisplayName = jobName,
                PropertyName = jobType,
                NewValue = JsonSerializer.Serialize(new { DurationMs = duration?.TotalMilliseconds }),
                ErrorMessage = errorMessage,
                UserId = "System",
                UserDisplayName = "System",
                Source = "Scheduler",
                Success = false,
                Reason = exceptionDetails?.Length > 500 ? exceptionDetails.Substring(0, 500) : exceptionDetails
            });
        }

        public async Task LogJobCancelledAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? reason = null, TimeSpan? duration = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.JobExecutionCancelled,
                EntityType = "Job",
                EntityId = relatedEntityId,
                EntityDisplayName = jobName,
                PropertyName = jobType,
                NewValue = JsonSerializer.Serialize(new { DurationMs = duration?.TotalMilliseconds }),
                Reason = reason,
                UserId = "System",
                UserDisplayName = "System",
                Source = "Scheduler",
                Success = true
            });
        }

        public async Task LogJobScheduledAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? cronExpression = null, DateTime? nextRunTime = null, string? scheduledBy = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.JobScheduled,
                EntityType = "Job",
                EntityId = relatedEntityId,
                EntityDisplayName = jobName,
                PropertyName = jobType,
                NewValue = JsonSerializer.Serialize(new { CronExpression = cronExpression, NextRunTime = nextRunTime }),
                UserId = userId ?? scheduledBy ?? "System",
                UserDisplayName = userName ?? scheduledBy ?? "System",
                IpAddress = ipAddress,
                Source = "Scheduler",
                Success = true
            });
        }

        public async Task LogJobUnscheduledAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? unscheduledBy = null, string? reason = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.JobUnscheduled,
                EntityType = "Job",
                EntityId = relatedEntityId,
                EntityDisplayName = jobName,
                PropertyName = jobType,
                Reason = reason,
                UserId = userId ?? unscheduledBy ?? "System",
                UserDisplayName = userName ?? unscheduledBy ?? "System",
                IpAddress = ipAddress,
                Source = "Scheduler",
                Success = true
            });
        }

        #endregion

        #region Sync Operation Auditing

        public async Task LogSyncStartedAsync(Guid projectId, string projectName, Guid runId,
            string? triggeredBy = null, string? triggerType = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.SyncExecutionStarted,
                EntityType = "SyncProject",
                EntityId = projectId,
                EntityDisplayName = projectName,
                RelatedEntityId = runId,
                RelatedEntityName = $"Run {runId}",
                NewValue = JsonSerializer.Serialize(new { TriggerType = triggerType, TriggeredBy = triggeredBy ?? userId }),
                UserId = userId ?? triggeredBy ?? "System",
                UserDisplayName = userName ?? triggeredBy ?? "System",
                IpAddress = ipAddress,
                Source = "SyncEngine",
                Success = true
            });
        }

        public async Task LogSyncCompletedAsync(Guid projectId, string projectName, Guid runId,
            int objectsCreated = 0, int objectsUpdated = 0, int objectsSkipped = 0,
            int objectsFailed = 0, TimeSpan? duration = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.SyncExecutionCompleted,
                EntityType = "SyncProject",
                EntityId = projectId,
                EntityDisplayName = projectName,
                RelatedEntityId = runId,
                RelatedEntityName = $"Run {runId}",
                NewValue = JsonSerializer.Serialize(new
                {
                    ObjectsCreated = objectsCreated,
                    ObjectsUpdated = objectsUpdated,
                    ObjectsSkipped = objectsSkipped,
                    ObjectsFailed = objectsFailed,
                    DurationMs = duration?.TotalMilliseconds
                }),
                UserId = "System",
                UserDisplayName = "System",
                Source = "SyncEngine",
                Success = objectsFailed == 0
            });
        }

        public async Task LogSyncFailedAsync(Guid projectId, string projectName, Guid runId,
            string? errorMessage = null, string? stepName = null, TimeSpan? duration = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.SyncExecutionFailed,
                EntityType = "SyncProject",
                EntityId = projectId,
                EntityDisplayName = projectName,
                RelatedEntityId = runId,
                RelatedEntityName = stepName ?? $"Run {runId}",
                ErrorMessage = errorMessage,
                NewValue = JsonSerializer.Serialize(new { DurationMs = duration?.TotalMilliseconds }),
                UserId = "System",
                UserDisplayName = "System",
                Source = "SyncEngine",
                Success = false
            });
        }

        #endregion

        #region Configuration Change Auditing

        public async Task LogSettingChangedAsync(string category, string key, string? oldValue, string? newValue,
            string? changedBy = null, string? reason = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.SettingChanged,
                EntityType = "Setting",
                EntityDisplayName = $"{category}.{key}",
                PropertyName = key,
                OldValue = MaskSensitiveValue(key, oldValue),
                NewValue = MaskSensitiveValue(key, newValue),
                Reason = reason,
                UserId = userId ?? changedBy ?? "System",
                UserDisplayName = userName ?? changedBy ?? "System",
                IpAddress = ipAddress,
                Source = "Configuration",
                Success = true
            });
        }

        public async Task LogConnectionChangeAsync(ChangeOperationType operationType, Guid connectionId,
            string connectionName, string? changedBy = null, string? details = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = operationType,
                EntityType = "DirectoryConnection",
                EntityId = connectionId,
                EntityDisplayName = connectionName,
                NewValue = details,
                UserId = userId ?? changedBy ?? "System",
                UserDisplayName = userName ?? changedBy ?? "System",
                IpAddress = ipAddress,
                Source = "Configuration",
                Success = true
            });
        }

        public async Task LogConnectionTestedAsync(Guid connectionId, string connectionName,
            bool success, string? errorMessage = null, string? testedBy = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.ConnectionTested,
                EntityType = "DirectoryConnection",
                EntityId = connectionId,
                EntityDisplayName = connectionName,
                ErrorMessage = errorMessage,
                UserId = userId ?? testedBy ?? "System",
                UserDisplayName = userName ?? testedBy ?? "System",
                IpAddress = ipAddress,
                Source = "Configuration",
                Success = success
            });
        }

        #endregion

        #region Access Review Auditing

        public async Task LogCampaignEventAsync(ChangeOperationType operationType, Guid campaignId,
            string campaignName, string? performedBy = null, string? details = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = operationType,
                EntityType = "Campaign",
                EntityId = campaignId,
                EntityDisplayName = campaignName,
                NewValue = details,
                UserId = userId ?? performedBy ?? "System",
                UserDisplayName = userName ?? performedBy ?? "System",
                IpAddress = ipAddress,
                Source = "AccessReview",
                Success = true
            });
        }

        public async Task LogReviewDecisionAsync(Guid campaignId, Guid assignmentId, Guid reviewerId,
            string decision, string? justification = null, Guid? targetEntityId = null,
            string? targetEntityName = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.ReviewDecisionMade,
                EntityType = "AccessReviewAssignment",
                EntityId = assignmentId,
                EntityDisplayName = targetEntityName ?? $"Assignment {assignmentId}",
                RelatedEntityId = campaignId,
                RelatedEntityName = $"Campaign {campaignId}",
                PropertyName = "Decision",
                NewValue = decision,
                Reason = justification,
                UserId = userId ?? reviewerId.ToString(),
                UserDisplayName = userName ?? reviewerId.ToString(),
                IpAddress = ipAddress,
                Source = "AccessReview",
                Success = true
            });
        }

        public async Task LogReviewReminderSentAsync(Guid campaignId, Guid assignmentId, Guid reviewerId,
            string reviewerEmail, int reminderNumber)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.ReviewReminderSent,
                EntityType = "AccessReviewAssignment",
                EntityId = assignmentId,
                EntityDisplayName = $"Assignment {assignmentId}",
                RelatedEntityId = campaignId,
                NewValue = JsonSerializer.Serialize(new { ReviewerEmail = reviewerEmail, ReminderNumber = reminderNumber }),
                UserId = "System",
                UserDisplayName = "System",
                Source = "AccessReview",
                Success = true
            });
        }

        #endregion

        #region Policy Auditing

        public async Task LogPolicyEvaluatedAsync(Guid policyId, string policyName, int violationsFound,
            int entitiesEvaluated, TimeSpan? duration = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.PolicyEvaluated,
                EntityType = "CompliancePolicy",
                EntityId = policyId,
                EntityDisplayName = policyName,
                NewValue = JsonSerializer.Serialize(new
                {
                    ViolationsFound = violationsFound,
                    EntitiesEvaluated = entitiesEvaluated,
                    DurationMs = duration?.TotalMilliseconds
                }),
                UserId = "System",
                UserDisplayName = "System",
                Source = "PolicyEngine",
                Success = true
            });
        }

        public async Task LogPolicyViolationAsync(Guid policyId, string policyName, Guid entityId,
            string entityType, string entityName, string violationDetails)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.PolicyViolationDetected,
                EntityType = entityType,
                EntityId = entityId,
                EntityDisplayName = entityName,
                RelatedEntityId = policyId,
                RelatedEntityName = policyName,
                NewValue = violationDetails,
                UserId = "System",
                UserDisplayName = "System",
                Source = "PolicyEngine",
                Success = true
            });
        }

        public async Task LogPolicyViolationResolvedAsync(Guid violationId, Guid policyId,
            string resolvedBy, string? resolution = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.PolicyViolationResolved,
                EntityType = "PolicyViolation",
                EntityId = violationId,
                RelatedEntityId = policyId,
                NewValue = resolution,
                UserId = userId ?? resolvedBy,
                UserDisplayName = userName ?? resolvedBy,
                IpAddress = ipAddress,
                Source = "PolicyEngine",
                Success = true
            });
        }

        #endregion

        #region Report Auditing

        public async Task LogReportExecutedAsync(Guid reportId, string reportName, Guid? scheduleId = null,
            int rowsReturned = 0, string? executedBy = null, bool emailed = false,
            TimeSpan? duration = null, string? errorMessage = null)
        {
            var (userId, userName, ipAddress) = GetCurrentUserContext();
            var success = string.IsNullOrEmpty(errorMessage);

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.ReportExecuted,
                EntityType = "Report",
                EntityId = reportId,
                EntityDisplayName = reportName,
                RelatedEntityId = scheduleId,
                NewValue = JsonSerializer.Serialize(new
                {
                    RowsReturned = rowsReturned,
                    Emailed = emailed,
                    DurationMs = duration?.TotalMilliseconds
                }),
                ErrorMessage = errorMessage,
                UserId = userId ?? executedBy ?? "System",
                UserDisplayName = userName ?? executedBy ?? "System",
                IpAddress = ipAddress,
                Source = scheduleId.HasValue ? "Scheduler" : "UI",
                Success = success
            });
        }

        #endregion

        #region System Maintenance Auditing

        public async Task LogMaintenanceEventAsync(ChangeOperationType operationType, string taskType,
            string? details = null, int? recordsAffected = null, TimeSpan? duration = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = operationType,
                EntityType = "SystemMaintenance",
                EntityDisplayName = taskType,
                PropertyName = taskType,
                NewValue = JsonSerializer.Serialize(new
                {
                    RecordsAffected = recordsAffected,
                    DurationMs = duration?.TotalMilliseconds,
                    Details = details
                }),
                UserId = "System",
                UserDisplayName = "System",
                Source = "Maintenance",
                Success = true
            });
        }

        #endregion

        #region User/Authentication Auditing

        public async Task LogLoginAttemptAsync(string username, bool success, string? ipAddress = null,
            string? userAgent = null, string? failureReason = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = success ? ChangeOperationType.LoginSuccess : ChangeOperationType.LoginFailed,
                EntityType = "User",
                EntityDisplayName = username,
                PropertyName = "Login",
                ErrorMessage = failureReason,
                IpAddress = ipAddress ?? GetCurrentIpAddress(),
                NewValue = JsonSerializer.Serialize(new { UserAgent = userAgent }),
                UserId = username,
                UserDisplayName = username,
                Source = "Authentication",
                Success = success
            });
        }

        public async Task LogLogoutAsync(string userId, string? username = null, string? ipAddress = null)
        {
            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.LogoutSuccess,
                EntityType = "User",
                EntityDisplayName = username ?? userId,
                PropertyName = "Logout",
                IpAddress = ipAddress ?? GetCurrentIpAddress(),
                UserId = userId,
                UserDisplayName = username ?? userId,
                Source = "Authentication",
                Success = true
            });
        }

        public async Task LogRoleChangeAsync(ChangeOperationType operationType, string userId,
            string username, string roleName, string? changedBy = null)
        {
            var (currentUserId, currentUserName, ipAddress) = GetCurrentUserContext();

            await LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = operationType,
                EntityType = "User",
                EntityDisplayName = username,
                PropertyName = "Role",
                NewValue = roleName,
                UserId = currentUserId ?? changedBy ?? "System",
                UserDisplayName = currentUserName ?? changedBy ?? "System",
                IpAddress = ipAddress,
                RelatedEntityName = userId,
                Source = "UserManagement",
                Success = true
            });
        }

        #endregion

        #region Helper Methods

        private (string? UserId, string? UserName, string? IpAddress) GetCurrentUserContext()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated != true)
                return (null, null, null);

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value
                ?? httpContext.User.Identity?.Name;
            var ipAddress = GetCurrentIpAddress();

            return (userId, userName, ipAddress);
        }

        private string? GetCurrentIpAddress()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            return httpContext?.Connection?.RemoteIpAddress?.ToString();
        }

        private static string? MaskSensitiveValue(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var sensitiveKeys = new[] { "password", "secret", "key", "token", "connectionstring", "credential" };
            if (sensitiveKeys.Any(k => key.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "********";
            }

            return value;
        }

        #endregion
    }
}
