using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based workflow trigger template seeding.
/// Seeds automation templates for scheduled tasks, lifecycle events, and notifications.
/// </summary>
public class DapperTriggerTemplatesSeedService : DapperSeedServiceBase
{
    public DapperTriggerTemplatesSeedService(
        IConfiguration configuration,
        ILogger<DapperTriggerTemplatesSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if templates already exist
        var existingCount = await GetCountAsync(connection, transaction, "WorkflowTriggerTemplates");
        if (existingCount > 0)
        {
            _logger.LogDebug("Trigger templates already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var templates = GetDefaultTriggerTemplates();

        const string insertSql = @"
            INSERT INTO WorkflowTriggerTemplates (
                Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder,
                TemplateJson, CreatedAt, CreatedBy
            )
            VALUES (
                @Id, @Name, @Description, @Category, @Icon, @Color, @IsSystem, @SortOrder,
                @TemplateJson, @CreatedAt, @CreatedBy
            )";

        var created = await BatchInsertAsync(connection, transaction, insertSql, templates);

        sw.Stop();
        LogSeedComplete("WorkflowTriggerTemplates", created, 0, sw.Elapsed);
    }

    private static Dictionary<string, object> CreateAction(string actionType, Dictionary<string, object> config)
    {
        return new Dictionary<string, object>
        {
            ["ActionType"] = actionType,
            ["ActionConfig"] = config
        };
    }

    private static List<object> GetDefaultTriggerTemplates()
    {
        var now = DateTime.UtcNow;
        return new List<object>
        {
            // Compliance Templates
            new
            {
                Id = Guid.NewGuid(),
                Name = "Quarterly Access Review",
                Description = "Automatically create access review campaigns every quarter to maintain SOX compliance",
                Category = "Compliance",
                Icon = "bi-calendar-check",
                Color = "text-info",
                IsSystem = true,
                SortOrder = 1,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "Scheduled",
                    ["CronExpression"] = "0 0 9 1 1,4,7,10 ?",
                    ["Priority"] = 10,
                    ["Actions"] = new[]
                    {
                        CreateAction("CreateAccessReview", new Dictionary<string, object> { ["CampaignName"] = "Quarterly Review - {{Date}}", ["ReviewType"] = "GroupMembership", ["DurationDays"] = 14 }),
                        CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "compliance@company.com", ["Subject"] = "Quarterly Access Review Started", ["Body"] = "A new quarterly access review campaign has been automatically created." })
                    }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Monthly Privileged Access Review",
                Description = "Monthly review of users with elevated/admin privileges for security compliance",
                Category = "Compliance",
                Icon = "bi-shield-check",
                Color = "text-warning",
                IsSystem = true,
                SortOrder = 2,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "Scheduled",
                    ["CronExpression"] = "0 0 9 1 * ?",
                    ["Priority"] = 5,
                    ["Actions"] = new[]
                    {
                        CreateAction("CreateAccessReview", new Dictionary<string, object> { ["CampaignName"] = "Privileged Access Review - {{Date}}", ["ReviewType"] = "PrivilegedAccess", ["DurationDays"] = 7 }),
                        CreateAction("CreateAuditLog", new Dictionary<string, object> { ["Message"] = "Monthly privileged access review initiated" })
                    }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Annual Certification Campaign",
                Description = "Yearly comprehensive access certification for regulatory compliance",
                Category = "Compliance",
                Icon = "bi-award",
                Color = "text-primary",
                IsSystem = true,
                SortOrder = 3,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "Scheduled",
                    ["CronExpression"] = "0 0 9 1 1 ?",
                    ["Priority"] = 1,
                    ["Actions"] = new[]
                    {
                        CreateAction("CreateAccessReview", new Dictionary<string, object> { ["CampaignName"] = "Annual Access Certification {{Date}}", ["ReviewType"] = "AllAccess", ["DurationDays"] = 30 }),
                        CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "executives@company.com", ["Subject"] = "Annual Access Certification Started", ["Body"] = "The annual access certification campaign has begun. Please complete your reviews within 30 days." })
                    }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },

            // Lifecycle Templates
            new
            {
                Id = Guid.NewGuid(),
                Name = "New Employee Onboarding",
                Description = "Trigger workflow when a new user account is created in the directory",
                Category = "Lifecycle",
                Icon = "bi-person-plus",
                Color = "text-success",
                IsSystem = true,
                SortOrder = 10,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "ObjectLifecycle",
                    ["EventTypes"] = new[] { "ObjectCreated" },
                    ["Conditions"] = new[] { new Dictionary<string, object> { ["ConditionType"] = "ObjectClass", ["Operator"] = "Equals", ["Value"] = "user" } },
                    ["Priority"] = 20,
                    ["Actions"] = new[]
                    {
                        CreateAction("StartWorkflow", new Dictionary<string, object> { ["WorkflowName"] = "New Employee Provisioning" }),
                        CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "hr@company.com", ["Subject"] = "New Employee Account Created", ["Body"] = "A new user account has been created and is ready for provisioning." })
                    }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Employee Termination",
                Description = "Trigger immediate access revocation when an employee is terminated",
                Category = "Lifecycle",
                Icon = "bi-person-x",
                Color = "text-danger",
                IsSystem = true,
                SortOrder = 11,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "ObjectLifecycle",
                    ["EventTypes"] = new[] { "ObjectDisabled", "ObjectDeleted" },
                    ["Conditions"] = new[] { new Dictionary<string, object> { ["ConditionType"] = "ObjectClass", ["Operator"] = "Equals", ["Value"] = "user" } },
                    ["Priority"] = 1,
                    ["Actions"] = new[]
                    {
                        CreateAction("StartWorkflow", new Dictionary<string, object> { ["WorkflowName"] = "Emergency Access Revocation" }),
                        CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "security@company.com", ["Subject"] = "URGENT: User Account Disabled", ["Body"] = "A user account has been disabled. Please verify all access has been revoked." }),
                        CreateAction("CreateAuditLog", new Dictionary<string, object> { ["Message"] = "User termination trigger fired - access revocation initiated" })
                    }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Manager Change",
                Description = "Review and update access when an employee's manager changes",
                Category = "Lifecycle",
                Icon = "bi-diagram-3",
                Color = "text-info",
                IsSystem = true,
                SortOrder = 12,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "ObjectLifecycle",
                    ["EventTypes"] = new[] { "ObjectModified" },
                    ["Conditions"] = new[]
                    {
                        new Dictionary<string, object> { ["ConditionType"] = "ObjectClass", ["Operator"] = "Equals", ["Value"] = "user" },
                        new Dictionary<string, object> { ["ConditionType"] = "ObjectAttribute", ["FieldName"] = "manager", ["Operator"] = "Changed", ["Value"] = "" }
                    },
                    ["Priority"] = 30,
                    ["Actions"] = new[] { CreateAction("StartWorkflow", new Dictionary<string, object> { ["WorkflowName"] = "Manager Change Review" }) }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },

            // Security Templates
            new
            {
                Id = Guid.NewGuid(),
                Name = "Sensitive Group Membership Alert",
                Description = "Alert when users are added to security-sensitive groups (Domain Admins, etc.)",
                Category = "Security",
                Icon = "bi-exclamation-triangle",
                Color = "text-danger",
                IsSystem = true,
                SortOrder = 20,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "ObjectLifecycle",
                    ["EventTypes"] = new[] { "GroupMemberAdded" },
                    ["Conditions"] = new[] { new Dictionary<string, object> { ["ConditionType"] = "GroupAttribute", ["FieldName"] = "name", ["Operator"] = "In", ["Value"] = "Domain Admins,Enterprise Admins,Schema Admins,Administrators" } },
                    ["Priority"] = 1,
                    ["Actions"] = new[]
                    {
                        CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "security@company.com", ["Subject"] = "ALERT: Sensitive Group Membership Change", ["Body"] = "A user has been added to a highly privileged group. Please review immediately." }),
                        CreateAction("CreateAuditLog", new Dictionary<string, object> { ["Message"] = "Sensitive group membership change detected" })
                    }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Password Expiration Warning",
                Description = "Send reminder emails when passwords are about to expire",
                Category = "Security",
                Icon = "bi-key",
                Color = "text-warning",
                IsSystem = true,
                SortOrder = 21,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "Scheduled",
                    ["CronExpression"] = "0 0 8 * * ?",
                    ["Priority"] = 50,
                    ["Actions"] = new[] { CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "{{user.email}}", ["Subject"] = "Password Expiration Reminder", ["Body"] = "Your password will expire soon. Please change it to maintain access." }) }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Inactive Account Review",
                Description = "Weekly review of accounts that haven't logged in for 90+ days",
                Category = "Security",
                Icon = "bi-clock-history",
                Color = "text-secondary",
                IsSystem = true,
                SortOrder = 22,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "Scheduled",
                    ["CronExpression"] = "0 0 9 ? * MON",
                    ["Priority"] = 40,
                    ["Actions"] = new[]
                    {
                        CreateAction("CreateAccessReview", new Dictionary<string, object> { ["CampaignName"] = "Inactive Account Review - {{Date}}", ["ReviewType"] = "InactiveAccounts", ["DurationDays"] = 7 }),
                        CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "it-operations@company.com", ["Subject"] = "Weekly Inactive Account Review", ["Body"] = "A new inactive account review has been created. Please review and disable dormant accounts." })
                    }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },

            // Notification Templates
            new
            {
                Id = Guid.NewGuid(),
                Name = "Daily Sync Status Report",
                Description = "Send daily summary of directory synchronization results",
                Category = "Notification",
                Icon = "bi-envelope",
                Color = "text-primary",
                IsSystem = true,
                SortOrder = 30,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "Scheduled",
                    ["CronExpression"] = "0 0 7 * * ?",
                    ["Priority"] = 60,
                    ["Actions"] = new[] { CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "it-admins@company.com", ["Subject"] = "Daily Sync Status Report", ["Body"] = "Daily synchronization summary attached." }) }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Weekly Compliance Summary",
                Description = "Weekly email summary of compliance status and policy violations",
                Category = "Notification",
                Icon = "bi-clipboard-data",
                Color = "text-info",
                IsSystem = true,
                SortOrder = 31,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "Scheduled",
                    ["CronExpression"] = "0 0 8 ? * FRI",
                    ["Priority"] = 50,
                    ["Actions"] = new[]
                    {
                        CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "compliance@company.com", ["Subject"] = "Weekly Compliance Summary", ["Body"] = "Weekly compliance status report attached." }),
                        CreateAction("CreateAuditLog", new Dictionary<string, object> { ["Message"] = "Weekly compliance summary generated and sent" })
                    }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Access Review Reminder",
                Description = "Daily reminder for pending access review items",
                Category = "Notification",
                Icon = "bi-bell",
                Color = "text-warning",
                IsSystem = true,
                SortOrder = 32,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "Scheduled",
                    ["CronExpression"] = "0 0 9 * * MON-FRI",
                    ["Priority"] = 30,
                    ["Actions"] = new[] { CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "{{reviewer.email}}", ["Subject"] = "Pending Access Reviews Reminder", ["Body"] = "You have pending access review items that require your attention." }) }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Sync Completion Webhook",
                Description = "Call external webhook when directory sync completes",
                Category = "Notification",
                Icon = "bi-link-45deg",
                Color = "text-secondary",
                IsSystem = true,
                SortOrder = 40,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "SyncCompletion",
                    ["EventTypes"] = new[] { "SyncProjectCompleted" },
                    ["Priority"] = 50,
                    ["Actions"] = new[] { CreateAction("CallWebhook", new Dictionary<string, object> { ["Url"] = "https://api.example.com/webhook/sync-complete", ["Method"] = "POST" }) }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "Group Owner Notification",
                Description = "Notify group owners when members are added or removed",
                Category = "Notification",
                Icon = "bi-people",
                Color = "text-success",
                IsSystem = true,
                SortOrder = 41,
                TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["TriggerType"] = "ObjectLifecycle",
                    ["EventTypes"] = new[] { "GroupMemberAdded", "GroupMemberRemoved" },
                    ["Priority"] = 40,
                    ["Actions"] = new[] { CreateAction("SendEmail", new Dictionary<string, object> { ["To"] = "{{group.owner.email}}", ["Subject"] = "Group Membership Changed: {{group.name}}", ["Body"] = "A membership change has occurred in a group you own." }) }
                }),
                CreatedAt = now,
                CreatedBy = "System"
            }
        };
    }
}
