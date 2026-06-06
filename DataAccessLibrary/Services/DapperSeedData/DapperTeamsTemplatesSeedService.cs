using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based Teams message template seeding.
/// Seeds default notification templates for Teams bot and webhook integrations.
/// </summary>
public class DapperTeamsTemplatesSeedService : DapperSeedServiceBase
{
    public DapperTeamsTemplatesSeedService(
        IConfiguration configuration,
        ILogger<DapperTeamsTemplatesSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if templates already exist
        var existingCount = await GetCountAsync(connection, transaction, "TeamsMessageTemplates", "IsBuiltIn = 1");
        if (existingCount >= 15)
        {
            _logger.LogDebug("Teams templates already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var templates = GetDefaultTeamsTemplates();

        const string insertSql = @"
            INSERT INTO TeamsMessageTemplates (
                Id, Name, Description, MessageTemplate, UseAdaptiveCard, AdaptiveCardJson,
                Category, IsActive, IsBuiltIn, CreatedAt
            )
            SELECT @Id, @Name, @Description, @MessageTemplate, @UseAdaptiveCard, @AdaptiveCardJson,
                   @Category, @IsActive, @IsBuiltIn, @CreatedAt
            WHERE NOT EXISTS (SELECT 1 FROM TeamsMessageTemplates WHERE Name = @Name)";

        int created = 0;
        foreach (var template in templates)
        {
            var rowsAffected = await InsertAsync(connection, transaction, insertSql, template);
            if (rowsAffected > 0) created++;
        }

        sw.Stop();
        LogSeedComplete("TeamsMessageTemplates", created, templates.Count - created, sw.Elapsed);
    }

    private static List<object> GetDefaultTeamsTemplates()
    {
        var now = DateTime.UtcNow;
        return new List<object>
        {
            new
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_ASSIGNED",
                Description = "Notification when access reviews are assigned",
                MessageTemplate = "You have {Count} access reviews pending for campaign '{CampaignName}'. Due: {DueDate}. Review now: {ReviewUrl}",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_REMINDER",
                Description = "Reminder for pending access reviews",
                MessageTemplate = "Reminder: {Count} access reviews are due in {DaysRemaining} days. Campaign: '{CampaignName}'. Complete now: {ReviewUrl}",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "POLICY_VIOLATION",
                Description = "Alert when compliance policy violation is detected",
                MessageTemplate = "Policy Violation Alert: {PolicyName} - {EntityName}. Severity: {Severity}. Details: {ViolationDetails}",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Compliance",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "APPROVAL_REQUEST",
                Description = "Notification for pending approval requests",
                MessageTemplate = "Approval Required: {RequestType} from {RequesterName}. Details: {RequestDetails}. Respond: {ApprovalUrl}",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "APPROVAL_COMPLETED",
                Description = "Notification when approval is completed",
                MessageTemplate = "Your request has been {Decision} by {ApproverName}. Request: {RequestType}. Comments: {Comments}",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "SYNC_COMPLETE",
                Description = "Notification when directory sync completes",
                MessageTemplate = "Directory sync completed: {ProjectName}. Created: {Created}, Updated: {Updated}, Deleted: {Deleted}. Duration: {Duration}",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "SYNC_FAILED",
                Description = "Alert when directory sync fails",
                MessageTemplate = "Directory sync FAILED: {ProjectName}. Error: {ErrorMessage}. Check logs for details.",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "DORMANT_ACCOUNT",
                Description = "Alert for dormant account detection",
                MessageTemplate = "Dormant Account Alert: {UserName} has been inactive for {DaysInactive} days. Last login: {LastLoginDate}. Action: {ActionTaken}",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },

            // === LIFECYCLE TEMPLATES ===
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_NEW_HIRE_MANAGER",
                Description = "Notify hiring manager that a new hire account is being provisioned",
                MessageTemplate = "New hire account being provisioned for {EmployeeName}. Department: {Department}, Title: {Title}, Start Date: {StartDate}. You will be notified once onboarding is complete.",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_ONBOARDING_COMPLETE",
                Description = "Notify manager that onboarding has completed for a new hire",
                MessageTemplate = "Onboarding complete for {EmployeeName}. Account: {SamAccountName}, Email: {EmailAddress}, Groups Assigned: {GroupCount}. The employee can now log in.",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_DEPT_TRANSFER",
                Description = "Notify managers about an employee department transfer",
                MessageTemplate = "Department transfer: {EmployeeName} moved from {OldDepartment} to {NewDepartment}. New Title: {Title}. Effective: {EffectiveDate}. Group memberships updated.",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_ROLE_CHANGE",
                Description = "Notify manager that an employee role change has been processed",
                MessageTemplate = "Role change complete for {EmployeeName}. Previous: {OldTitle}, New: {NewTitle}. Groups added: {GroupsAdded}, Groups removed: {GroupsRemoved}. Please verify access.",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_OFFBOARDING_COMPLETE",
                Description = "Notify manager that offboarding has completed",
                MessageTemplate = "Offboarding complete for {EmployeeName}. Account disabled, {GroupsRemoved} groups removed. Email forwarding: {ForwardingStatus}. Processed: {ProcessedDate}.",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_IMMEDIATE_TERMINATION",
                Description = "Urgent notification for immediate termination processing",
                MessageTemplate = "URGENT: Immediate termination processed for {EmployeeName}. Account disabled, password reset, all sessions revoked, all access removed. Processed: {ProcessedDate}. Please verify.",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_IT_ONBOARDING",
                Description = "IT onboarding ticket notification for new hires",
                MessageTemplate = "IT Onboarding: {EmployeeName} ({Department}, {Title}). Start: {StartDate}. Manager: {ManagerName}. AD: {SamAccountName}, Email: {EmailAddress}, License: {LicenseSku}. Please prepare hardware.",
                UseAdaptiveCard = false,
                AdaptiveCardJson = (string?)null,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            }
        };
    }
}
