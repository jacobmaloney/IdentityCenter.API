using System;
using System.Threading.Tasks;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Extended audit service for system-level events including jobs, syncs, and configuration changes.
    /// Provides comprehensive logging for compliance and troubleshooting.
    /// </summary>
    public interface ISystemAuditService : IAuditLogService
    {
        // ============================================================================
        // JOB EXECUTION AUDITING
        // ============================================================================

        /// <summary>
        /// Log when a scheduled job starts execution
        /// </summary>
        Task LogJobStartedAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? triggeredBy = null, string? triggerType = null);

        /// <summary>
        /// Log when a scheduled job completes successfully
        /// </summary>
        Task LogJobCompletedAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            int itemsProcessed = 0, int itemsSucceeded = 0, int itemsFailed = 0,
            TimeSpan? duration = null, string? resultSummary = null);

        /// <summary>
        /// Log when a scheduled job fails
        /// </summary>
        Task LogJobFailedAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? errorMessage = null, string? exceptionDetails = null, TimeSpan? duration = null);

        /// <summary>
        /// Log when a scheduled job is cancelled
        /// </summary>
        Task LogJobCancelledAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? reason = null, TimeSpan? duration = null);

        /// <summary>
        /// Log when a job is scheduled or rescheduled
        /// </summary>
        Task LogJobScheduledAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? cronExpression = null, DateTime? nextRunTime = null, string? scheduledBy = null);

        /// <summary>
        /// Log when a job is unscheduled
        /// </summary>
        Task LogJobUnscheduledAsync(string jobType, string jobName, Guid? relatedEntityId = null,
            string? unscheduledBy = null, string? reason = null);

        // ============================================================================
        // SYNC OPERATION AUDITING
        // ============================================================================

        /// <summary>
        /// Log when a sync project execution starts
        /// </summary>
        Task LogSyncStartedAsync(Guid projectId, string projectName, Guid runId,
            string? triggeredBy = null, string? triggerType = null);

        /// <summary>
        /// Log when a sync project execution completes
        /// </summary>
        Task LogSyncCompletedAsync(Guid projectId, string projectName, Guid runId,
            int objectsCreated = 0, int objectsUpdated = 0, int objectsSkipped = 0,
            int objectsFailed = 0, TimeSpan? duration = null);

        /// <summary>
        /// Log when a sync project execution fails
        /// </summary>
        Task LogSyncFailedAsync(Guid projectId, string projectName, Guid runId,
            string? errorMessage = null, string? stepName = null, TimeSpan? duration = null);

        // ============================================================================
        // CONFIGURATION CHANGE AUDITING
        // ============================================================================

        /// <summary>
        /// Log when a system setting is changed
        /// </summary>
        Task LogSettingChangedAsync(string category, string key, string? oldValue, string? newValue,
            string? changedBy = null, string? reason = null);

        /// <summary>
        /// Log when a directory connection is created, updated, or deleted
        /// </summary>
        Task LogConnectionChangeAsync(ChangeOperationType operationType, Guid connectionId,
            string connectionName, string? changedBy = null, string? details = null);

        /// <summary>
        /// Log when a connection is tested
        /// </summary>
        Task LogConnectionTestedAsync(Guid connectionId, string connectionName,
            bool success, string? errorMessage = null, string? testedBy = null);

        // ============================================================================
        // ACCESS REVIEW AUDITING
        // ============================================================================

        /// <summary>
        /// Log access review campaign lifecycle events
        /// </summary>
        Task LogCampaignEventAsync(ChangeOperationType operationType, Guid campaignId,
            string campaignName, string? performedBy = null, string? details = null);

        /// <summary>
        /// Log access review decision
        /// </summary>
        Task LogReviewDecisionAsync(Guid campaignId, Guid assignmentId, Guid reviewerId,
            string decision, string? justification = null, Guid? targetEntityId = null,
            string? targetEntityName = null);

        /// <summary>
        /// Log when review reminder is sent
        /// </summary>
        Task LogReviewReminderSentAsync(Guid campaignId, Guid assignmentId, Guid reviewerId,
            string reviewerEmail, int reminderNumber);

        // ============================================================================
        // POLICY AUDITING
        // ============================================================================

        /// <summary>
        /// Log policy evaluation event
        /// </summary>
        Task LogPolicyEvaluatedAsync(Guid policyId, string policyName, int violationsFound,
            int entitiesEvaluated, TimeSpan? duration = null);

        /// <summary>
        /// Log policy violation detected
        /// </summary>
        Task LogPolicyViolationAsync(Guid policyId, string policyName, Guid entityId,
            string entityType, string entityName, string violationDetails);

        /// <summary>
        /// Log policy violation resolved
        /// </summary>
        Task LogPolicyViolationResolvedAsync(Guid violationId, Guid policyId,
            string resolvedBy, string? resolution = null);

        // ============================================================================
        // REPORT AUDITING
        // ============================================================================

        /// <summary>
        /// Log report execution
        /// </summary>
        Task LogReportExecutedAsync(Guid reportId, string reportName, Guid? scheduleId = null,
            int rowsReturned = 0, string? executedBy = null, bool emailed = false,
            TimeSpan? duration = null, string? errorMessage = null);

        // ============================================================================
        // SYSTEM MAINTENANCE AUDITING
        // ============================================================================

        /// <summary>
        /// Log system maintenance event
        /// </summary>
        Task LogMaintenanceEventAsync(ChangeOperationType operationType, string taskType,
            string? details = null, int? recordsAffected = null, TimeSpan? duration = null);

        // ============================================================================
        // USER/AUTHENTICATION AUDITING
        // ============================================================================

        /// <summary>
        /// Log user login attempt
        /// </summary>
        Task LogLoginAttemptAsync(string username, bool success, string? ipAddress = null,
            string? userAgent = null, string? failureReason = null);

        /// <summary>
        /// Log user logout
        /// </summary>
        Task LogLogoutAsync(string userId, string? username = null, string? ipAddress = null);

        /// <summary>
        /// Log user role assignment change
        /// </summary>
        Task LogRoleChangeAsync(ChangeOperationType operationType, string userId,
            string username, string roleName, string? changedBy = null);
    }
}
