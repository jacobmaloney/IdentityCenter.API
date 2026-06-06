using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default Teams message templates for notifications, workflows, and access reviews
/// Makes Identity Center instantly ready for Teams communications!
/// </summary>
public class DefaultTeamsTemplatesSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<DefaultTeamsTemplatesSeedService> _logger;

    public DefaultTeamsTemplatesSeedService(
        IConfiguration configuration,
        ILogger<DefaultTeamsTemplatesSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds essential Teams message templates for all notification scenarios
    /// </summary>
    public async Task SeedDefaultTeamsTemplatesAsync()
    {
        _logger.LogInformation("Starting default Teams templates seeding - Teams communications ready!");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var defaultTemplates = new[]
        {
            // ==================== COMPLIANCE TEMPLATES ====================
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "POLICY_VIOLATION",
                Description = "Notification when a policy violation is detected",
                MessageTemplate = @"**Policy Violation Detected**

**Policy:** {PolicyName}
**Entity:** {EntityName}
**Severity:** {Severity}
**Details:** {Message}
**Detected:** {DetectedAt}

Please review and take appropriate action.",
                UseAdaptiveCard = false,
                Category = "Compliance",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "COMPLIANCE_ALERT",
                Description = "General compliance alert notification",
                MessageTemplate = @"**Compliance Alert**

**Policy:** {PolicyName}
**Severity:** {Severity}
**Message:** {Message}

Action may be required.",
                UseAdaptiveCard = false,
                Category = "Compliance",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "MANAGER_REQUIRED_VIOLATION",
                Description = "Notification for manager required policy violations",
                MessageTemplate = @"**Manager Required Violation**

**User:** {EntityName}
**Issue:** No manager assigned
**Policy:** {PolicyName}
**Severity:** {Severity}

Please assign a manager to this user.",
                UseAdaptiveCard = false,
                Category = "Compliance",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },

            // ==================== ACCESS REVIEW TEMPLATES ====================
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "ACCESS_REVIEW_ASSIGNED",
                Description = "Notification when access review is assigned",
                MessageTemplate = @"**Access Review Assigned**

A new access review has been assigned to you.

**Campaign:** {CampaignName}
**Review Type:** {ReviewType}
**Due Date:** {DueDate}

Please review the assigned items.",
                UseAdaptiveCard = false,
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "ACCESS_REVIEW_REMINDER",
                Description = "Reminder for pending access reviews",
                MessageTemplate = @"**Access Review Reminder**

You have pending access reviews that require your attention.

**Campaign:** {CampaignName}
**Due Date:** {DueDate}
**Items Remaining:** {ItemCount}

Please complete your reviews before the deadline.",
                UseAdaptiveCard = false,
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "ACCESS_REVIEW_OVERDUE",
                Description = "Warning when access review is overdue",
                MessageTemplate = @"**Access Review Overdue**

Your access review is overdue!

**Campaign:** {CampaignName}
**Original Due Date:** {DueDate}
**Days Overdue:** {DaysOverdue}

Please complete immediately.",
                UseAdaptiveCard = false,
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },

            // ==================== WORKFLOW TEMPLATES ====================
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WORKFLOW_APPROVAL_REQUEST",
                Description = "Request for workflow approval",
                MessageTemplate = @"**Approval Required**

A request needs your approval.

**Workflow:** {WorkflowName}
**Requestor:** {RequestorName}
**Request:** {RequestDetails}

Please review and approve or deny.",
                UseAdaptiveCard = false,
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WORKFLOW_APPROVED",
                Description = "Notification when workflow is approved",
                MessageTemplate = @"**Request Approved**

Your request has been approved.

**Workflow:** {WorkflowName}
**Approved By:** {ApproverName}
**Comments:** {Comments}",
                UseAdaptiveCard = false,
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WORKFLOW_DENIED",
                Description = "Notification when workflow is denied",
                MessageTemplate = @"**Request Denied**

Your request has been denied.

**Workflow:** {WorkflowName}
**Denied By:** {ApproverName}
**Reason:** {Reason}",
                UseAdaptiveCard = false,
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WORKFLOW_ESCALATED",
                Description = "Notification when workflow is escalated",
                MessageTemplate = @"**Request Escalated**

A request has been escalated and requires your attention.

**Workflow:** {WorkflowName}
**Requestor:** {RequestorName}
**Original Approver:** {OriginalApprover}
**Reason:** {EscalationReason}",
                UseAdaptiveCard = false,
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },

            // ==================== SYSTEM TEMPLATES ====================
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "SYSTEM_ALERT",
                Description = "General system alert notification",
                MessageTemplate = @"**System Alert**

**Type:** {AlertType}
**Message:** {Message}
**Time:** {Timestamp}",
                UseAdaptiveCard = false,
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "ACCOUNT_LOCKED",
                Description = "Notification when account is locked",
                MessageTemplate = @"**Account Locked**

**User:** {UserName}
**Reason:** {Reason}
**Time:** {Timestamp}

Contact IT support if you need assistance.",
                UseAdaptiveCard = false,
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "PASSWORD_EXPIRY_WARNING",
                Description = "Warning for expiring password",
                MessageTemplate = @"**Password Expiring Soon**

Your password will expire in {DaysRemaining} days.

Please change your password before {ExpiryDate} to avoid access issues.",
                UseAdaptiveCard = false,
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WELCOME_NEW_USER",
                Description = "Welcome message for new users",
                MessageTemplate = @"**Welcome to Identity Center!**

Hello {DisplayName},

Your account has been created. Here's your information:
**Username:** {Username}
**Department:** {Department}

You can now access Identity Center using your corporate credentials.",
                UseAdaptiveCard = false,
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },

            // ==================== LIFECYCLE TEMPLATES ====================
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_NEW_HIRE_MANAGER",
                Description = "Notify hiring manager that a new hire account is being provisioned",
                MessageTemplate = @"**New Hire Account Provisioning**

A new hire account is being provisioned for **{EmployeeName}**.

**Department:** {Department}
**Title:** {Title}
**Start Date:** {StartDate}

You will be notified once onboarding is complete.",
                UseAdaptiveCard = false,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_ONBOARDING_COMPLETE",
                Description = "Notify manager that onboarding has completed for a new hire",
                MessageTemplate = @"**Onboarding Complete**

Onboarding has been completed for **{EmployeeName}**.

**Account:** {SamAccountName}
**Email:** {EmailAddress}
**Groups Assigned:** {GroupCount}

The employee can now log in and access assigned resources.",
                UseAdaptiveCard = false,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_DEPT_TRANSFER",
                Description = "Notify managers about an employee department transfer",
                MessageTemplate = @"**Department Transfer**

**{EmployeeName}** has been transferred.

**Previous Department:** {OldDepartment}
**New Department:** {NewDepartment}
**New Title:** {Title}
**Effective Date:** {EffectiveDate}

Group memberships and access have been updated.",
                UseAdaptiveCard = false,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_ROLE_CHANGE",
                Description = "Notify manager that an employee role change has been processed",
                MessageTemplate = @"**Role Change Complete**

The role change for **{EmployeeName}** has been processed.

**Previous Role:** {OldTitle}
**New Role:** {NewTitle}
**Groups Added:** {GroupsAdded}
**Groups Removed:** {GroupsRemoved}

Please verify the employee has the correct access.",
                UseAdaptiveCard = false,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_OFFBOARDING_COMPLETE",
                Description = "Notify manager that offboarding has completed",
                MessageTemplate = @"**Offboarding Complete**

Offboarding has been completed for **{EmployeeName}**.

**Account Status:** Disabled
**Groups Removed:** {GroupsRemoved}
**Email Forwarding:** {ForwardingStatus}
**Processed On:** {ProcessedDate}",
                UseAdaptiveCard = false,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_IMMEDIATE_TERMINATION",
                Description = "Urgent notification for immediate termination processing",
                MessageTemplate = @"**URGENT: Immediate Termination Processed**

An immediate termination has been processed for **{EmployeeName}**.

**Account Status:** Disabled
**Password:** Reset
**Sessions:** Revoked
**All Access:** Removed
**Processed At:** {ProcessedDate}

Please verify all access has been properly revoked.",
                UseAdaptiveCard = false,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new TeamsMessageTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_IT_ONBOARDING",
                Description = "IT onboarding ticket notification for new hires",
                MessageTemplate = @"**IT Onboarding Ticket**

A new IT onboarding ticket has been created:

**Employee:** {EmployeeName}
**Department:** {Department}
**Title:** {Title}
**Start Date:** {StartDate}
**Manager:** {ManagerName}

**Provisioned:** AD: {SamAccountName}, Email: {EmailAddress}, License: {LicenseSku}

Please ensure hardware and additional resources are prepared.",
                UseAdaptiveCard = false,
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        int created = 0;
        int skipped = 0;

        const string checkSql = "SELECT COUNT(*) FROM TeamsMessageTemplates WHERE Name = @Name";
        const string insertSql = @"
            INSERT INTO TeamsMessageTemplates
                (Id, Name, Description, MessageTemplate, UseAdaptiveCard, Category, IsActive, IsBuiltIn, CreatedAt)
            VALUES
                (@Id, @Name, @Description, @MessageTemplate, @UseAdaptiveCard, @Category, @IsActive, @IsBuiltIn, @CreatedAt)";

        foreach (var template in defaultTemplates)
        {
            // Check if template already exists by name
            var existingCount = await connection.ExecuteScalarAsync<int>(checkSql, new { template.Name });
            if (existingCount > 0)
            {
                _logger.LogDebug("Skip: Teams template '{Name}' already exists", template.Name);
                skipped++;
                continue;
            }

            await connection.ExecuteAsync(insertSql, template);
            _logger.LogInformation("Created Teams template '{Name}' ({Category})",
                template.Name, template.Category);
            created++;
        }

        _logger.LogInformation("Teams templates seeding complete! Created: {Created}, Skipped: {Skipped}", created, skipped);
        _logger.LogInformation("Identity Center is now ready for Teams communications!");
    }
}
