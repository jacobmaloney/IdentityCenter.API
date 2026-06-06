using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based email template seeding.
/// Seeds default notification templates for access reviews, workflows, and compliance.
/// </summary>
public class DapperEmailTemplatesSeedService : DapperSeedServiceBase
{
    public DapperEmailTemplatesSeedService(
        IConfiguration configuration,
        ILogger<DapperEmailTemplatesSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if templates already exist
        var existingCount = await GetCountAsync(connection, transaction, "EmailTemplates", "IsBuiltIn = 1");
        if (existingCount >= 17)
        {
            _logger.LogDebug("Email templates already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var templates = GetDefaultEmailTemplates();

        const string insertSql = @"
            INSERT INTO EmailTemplates (Id, Name, Subject, Body, Category, IsActive, IsBuiltIn, CreatedAt)
            SELECT @Id, @Name, @Subject, @Body, @Category, @IsActive, @IsBuiltIn, @CreatedAt
            WHERE NOT EXISTS (SELECT 1 FROM EmailTemplates WHERE Name = @Name)";

        int created = 0;
        foreach (var template in templates)
        {
            var rowsAffected = await InsertAsync(connection, transaction, insertSql, template);
            if (rowsAffected > 0) created++;
        }

        sw.Stop();
        LogSeedComplete("EmailTemplates", created, templates.Count - created, sw.Elapsed);
    }

    private static List<object> GetDefaultEmailTemplates()
    {
        var now = DateTime.UtcNow;
        return new List<object>
        {
            new
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_ASSIGNED",
                Subject = "Action Required: You have {Count} access reviews pending",
                Body = @"<html><body>
<h2>Access Review Assignment</h2>
<p>Dear {ReviewerName},</p>
<p>You have been assigned <strong>{Count}</strong> access review(s) that require your attention.</p>
<p><strong>Campaign:</strong> {CampaignName}<br/>
<strong>Due Date:</strong> {DueDate}</p>
<p><a href='{ReviewUrl}'>Click here to complete your reviews</a></p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_DUE",
                Subject = "Reminder: Access reviews due in {DaysRemaining} days",
                Body = @"<html><body>
<h2>Access Review Reminder</h2>
<p>Dear {ReviewerName},</p>
<p>You have <strong>{Count}</strong> access review(s) due in <strong>{DaysRemaining}</strong> days.</p>
<p><strong>Campaign:</strong> {CampaignName}<br/>
<strong>Due Date:</strong> {DueDate}</p>
<p><a href='{ReviewUrl}'>Complete your reviews now</a></p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_OVERDUE",
                Subject = "URGENT: Access reviews are overdue",
                Body = @"<html><body>
<h2 style='color: #dc2626;'>Overdue Access Reviews</h2>
<p>Dear {ReviewerName},</p>
<p>You have <strong>{Count}</strong> overdue access review(s) that require immediate attention.</p>
<p><strong>Campaign:</strong> {CampaignName}<br/>
<strong>Original Due Date:</strong> {DueDate}<br/>
<strong>Days Overdue:</strong> {DaysOverdue}</p>
<p><a href='{ReviewUrl}'>Complete your reviews immediately</a></p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "POLICY_VIOLATION",
                Subject = "Compliance Alert: {PolicyName} violation detected",
                Body = @"<html><body>
<h2 style='color: #dc2626;'>Policy Violation Detected</h2>
<p>Dear {RecipientName},</p>
<p>A compliance policy violation has been detected:</p>
<p><strong>Policy:</strong> {PolicyName}<br/>
<strong>Entity:</strong> {EntityName}<br/>
<strong>Severity:</strong> {Severity}<br/>
<strong>Details:</strong> {ViolationDetails}</p>
<p><a href='{ViolationUrl}'>View violation details</a></p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Compliance",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "DORMANT_WARNING_45DAY",
                Subject = "Notice: Your account shows no login activity for 45+ days",
                Body = @"<html><body>
<h2>Account Activity Notice</h2>
<p>Dear {UserName},</p>
<p>Your account has shown no login activity for the past 45 days.</p>
<p>If you still require access to organizational systems, please log in within the next 45 days to keep your account active.</p>
<p>If you no longer need access, no action is required.</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "DORMANT_REVIEW_REQUIRED",
                Subject = "Action Required: Dormant account review for {UserName}",
                Body = @"<html><body>
<h2>Dormant Account Review Required</h2>
<p>Dear {ManagerName},</p>
<p>The following account has been inactive for 90+ days and requires your review:</p>
<p><strong>User:</strong> {UserName}<br/>
<strong>Department:</strong> {Department}<br/>
<strong>Last Login:</strong> {LastLoginDate}<br/>
<strong>Days Inactive:</strong> {DaysInactive}</p>
<p>Please certify whether this account should remain active or be disabled.</p>
<p><a href='{ReviewUrl}'>Review Account</a></p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "ACCOUNT_AUTO_DISABLED",
                Subject = "Notice: Account auto-disabled due to inactivity",
                Body = @"<html><body>
<h2>Account Disabled</h2>
<p>Dear {ManagerName},</p>
<p>The following account has been automatically disabled due to 180+ days of inactivity:</p>
<p><strong>User:</strong> {UserName}<br/>
<strong>Department:</strong> {Department}<br/>
<strong>Last Login:</strong> {LastLoginDate}</p>
<p>If this account is still needed, please contact IT to have it re-enabled.</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "APPROVAL_REQUEST",
                Subject = "Approval Required: {RequestType} from {RequesterName}",
                Body = @"<html><body>
<h2>Approval Request</h2>
<p>Dear {ApproverName},</p>
<p>A new approval request requires your attention:</p>
<p><strong>Request Type:</strong> {RequestType}<br/>
<strong>Requester:</strong> {RequesterName}<br/>
<strong>Details:</strong> {RequestDetails}</p>
<p><a href='{ApprovalUrl}'>Review and Respond</a></p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "APPROVAL_COMPLETED",
                Subject = "Your request has been {Decision}",
                Body = @"<html><body>
<h2>Request {Decision}</h2>
<p>Dear {RequesterName},</p>
<p>Your request has been <strong>{Decision}</strong>.</p>
<p><strong>Request Type:</strong> {RequestType}<br/>
<strong>Decision By:</strong> {ApproverName}<br/>
<strong>Comments:</strong> {Comments}</p>
<p><a href='{RequestUrl}'>View Request Details</a></p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "WELCOME_USER",
                Subject = "Welcome to Identity Center",
                Body = @"<html><body>
<h2>Welcome to Identity Center</h2>
<p>Dear {UserName},</p>
<p>Your Identity Center account has been created.</p>
<p>You can now access the self-service portal to:</p>
<ul>
<li>View your access and permissions</li>
<li>Request additional access</li>
<li>Manage your profile</li>
</ul>
<p><a href='{PortalUrl}'>Access Identity Center</a></p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },

            // === LIFECYCLE TEMPLATES ===
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_NEW_HIRE_MANAGER",
                Subject = "New hire account being provisioned: {EmployeeName}",
                Body = @"<html><body>
<h2>New Hire Account Provisioning</h2>
<p>Dear {ManagerName},</p>
<p>A new hire account is being provisioned for <strong>{EmployeeName}</strong>.</p>
<p><strong>Department:</strong> {Department}<br/>
<strong>Title:</strong> {Title}<br/>
<strong>Start Date:</strong> {StartDate}</p>
<p>You will receive a follow-up notification once onboarding is complete.</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_ONBOARDING_COMPLETE",
                Subject = "Onboarding complete: {EmployeeName}",
                Body = @"<html><body>
<h2>Onboarding Complete</h2>
<p>Dear {ManagerName},</p>
<p>Onboarding has been completed for <strong>{EmployeeName}</strong>.</p>
<p><strong>Account:</strong> {SamAccountName}<br/>
<strong>Email:</strong> {EmailAddress}<br/>
<strong>Groups Assigned:</strong> {GroupCount}</p>
<p>The employee can now log in and access assigned resources.</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_DEPT_TRANSFER",
                Subject = "Department transfer: {EmployeeName} to {NewDepartment}",
                Body = @"<html><body>
<h2>Department Transfer Notification</h2>
<p>Dear {ManagerName},</p>
<p><strong>{EmployeeName}</strong> has been transferred.</p>
<p><strong>Previous Department:</strong> {OldDepartment}<br/>
<strong>New Department:</strong> {NewDepartment}<br/>
<strong>New Title:</strong> {Title}<br/>
<strong>Effective Date:</strong> {EffectiveDate}</p>
<p>Group memberships and access have been updated accordingly.</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_ROLE_CHANGE",
                Subject = "Role change complete: {EmployeeName}",
                Body = @"<html><body>
<h2>Role Change Complete</h2>
<p>Dear {ManagerName},</p>
<p>The role change for <strong>{EmployeeName}</strong> has been processed.</p>
<p><strong>Previous Role:</strong> {OldTitle}<br/>
<strong>New Role:</strong> {NewTitle}<br/>
<strong>Groups Added:</strong> {GroupsAdded}<br/>
<strong>Groups Removed:</strong> {GroupsRemoved}</p>
<p>Please verify the employee has the correct access for their new role.</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_OFFBOARDING_COMPLETE",
                Subject = "Offboarding complete: {EmployeeName}",
                Body = @"<html><body>
<h2>Offboarding Complete</h2>
<p>Dear {ManagerName},</p>
<p>Offboarding has been completed for <strong>{EmployeeName}</strong>.</p>
<p><strong>Account Status:</strong> Disabled<br/>
<strong>Groups Removed:</strong> {GroupsRemoved}<br/>
<strong>Email Forwarding:</strong> {ForwardingStatus}<br/>
<strong>Processed On:</strong> {ProcessedDate}</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_IMMEDIATE_TERMINATION",
                Subject = "URGENT: Immediate termination — {EmployeeName}",
                Body = @"<html><body>
<h2 style='color: #dc2626;'>Immediate Termination Processed</h2>
<p>Dear {RecipientName},</p>
<p>An immediate termination has been processed for <strong>{EmployeeName}</strong>.</p>
<p><strong>Account Status:</strong> Disabled<br/>
<strong>Password:</strong> Reset<br/>
<strong>Sessions:</strong> Revoked<br/>
<strong>All Access:</strong> Removed<br/>
<strong>Processed At:</strong> {ProcessedDate}</p>
<p>Please review and confirm all access has been properly revoked.</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_IT_ONBOARDING",
                Subject = "IT onboarding ticket: {EmployeeName}",
                Body = @"<html><body>
<h2>IT Onboarding Ticket</h2>
<p>A new IT onboarding ticket has been created:</p>
<p><strong>Employee:</strong> {EmployeeName}<br/>
<strong>Department:</strong> {Department}<br/>
<strong>Title:</strong> {Title}<br/>
<strong>Start Date:</strong> {StartDate}<br/>
<strong>Manager:</strong> {ManagerName}</p>
<p><strong>Provisioned Items:</strong></p>
<ul>
<li>AD Account: {SamAccountName}</li>
<li>Email: {EmailAddress}</li>
<li>License: {LicenseSku}</li>
</ul>
<p>Please ensure hardware and additional resources are prepared.</p>
<p>Thank you,<br/>Identity Center</p>
</body></html>",
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = now
            }
        };
    }
}
