using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default email templates for notifications, workflows, and access reviews
/// Makes Identity Center instantly ready for professional communications!
/// </summary>
public class DefaultEmailTemplatesSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<DefaultEmailTemplatesSeedService> _logger;

    public DefaultEmailTemplatesSeedService(
        IConfiguration configuration,
        ILogger<DefaultEmailTemplatesSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds essential email templates for all notification scenarios
    /// </summary>
    public async Task SeedDefaultEmailTemplatesAsync()
    {
        _logger.LogInformation("Starting default email templates seeding - professional communications ready!");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var defaultTemplates = new[]
        {
            // ==================== ACCESS REVIEW TEMPLATES ====================
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_ASSIGNED",
                Subject = "Action Required: Access Review Assigned - {CampaignName}",
                Body = GetReviewAssignedTemplate(),
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_REMINDER",
                Subject = "Reminder: Access Review Due Soon - {CampaignName}",
                Body = GetReviewReminderTemplate(),
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_OVERDUE",
                Subject = "URGENT: Access Review Overdue - {CampaignName}",
                Body = GetReviewOverdueTemplate(),
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_COMPLETE",
                Subject = "Access Review Completed - {CampaignName}",
                Body = GetReviewCompleteTemplate(),
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "ACCESS_REVOKED",
                Subject = "Notice: Your Access Has Been Revoked - {GroupName}",
                Body = GetAccessRevokedTemplate(),
                Category = "AccessReview",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },

            // ==================== WORKFLOW TEMPLATES ====================
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WORKFLOW_APPROVAL_REQUEST",
                Subject = "Approval Required: {RequestType} - {RequesterName}",
                Body = GetWorkflowApprovalRequestTemplate(),
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WORKFLOW_APPROVED",
                Subject = "Request Approved: {RequestType}",
                Body = GetWorkflowApprovedTemplate(),
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WORKFLOW_DENIED",
                Subject = "Request Denied: {RequestType}",
                Body = GetWorkflowDeniedTemplate(),
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WORKFLOW_ESCALATED",
                Subject = "Escalation Notice: {RequestType} Requires Your Attention",
                Body = GetWorkflowEscalatedTemplate(),
                Category = "Workflow",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },

            // ==================== SYSTEM TEMPLATES ====================
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "WELCOME_NEW_USER",
                Subject = "Welcome to Identity Center - {DisplayName}",
                Body = GetWelcomeNewUserTemplate(),
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "PASSWORD_EXPIRY_WARNING",
                Subject = "Password Expiration Notice - Action Required",
                Body = GetPasswordExpiryWarningTemplate(),
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "ACCOUNT_LOCKED",
                Subject = "Security Alert: Account Locked",
                Body = GetAccountLockedTemplate(),
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "GENERIC_NOTIFICATION",
                Subject = "{Subject}",
                Body = GetGenericNotificationTemplate(),
                Category = "System",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },

            // ==================== COMPLIANCE TEMPLATES ====================
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "COMPLIANCE_VIOLATION",
                Subject = "Compliance Alert: Policy Violation Detected",
                Body = GetComplianceViolationTemplate(),
                Category = "Compliance",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "AUDIT_NOTIFICATION",
                Subject = "Audit Report Ready: {ReportName}",
                Body = GetAuditNotificationTemplate(),
                Category = "Compliance",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },

            // ==================== LIFECYCLE TEMPLATES ====================
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_NEW_HIRE_MANAGER",
                Subject = "New hire account being provisioned: {EmployeeName}",
                Body = GetLifecycleNewHireManagerTemplate(),
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_ONBOARDING_COMPLETE",
                Subject = "Onboarding complete: {EmployeeName}",
                Body = GetLifecycleOnboardingCompleteTemplate(),
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_DEPT_TRANSFER",
                Subject = "Department transfer: {EmployeeName} to {NewDepartment}",
                Body = GetLifecycleDeptTransferTemplate(),
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_ROLE_CHANGE",
                Subject = "Role change complete: {EmployeeName}",
                Body = GetLifecycleRoleChangeTemplate(),
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_OFFBOARDING_COMPLETE",
                Subject = "Offboarding complete: {EmployeeName}",
                Body = GetLifecycleOffboardingCompleteTemplate(),
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_IMMEDIATE_TERMINATION",
                Subject = "URGENT: Immediate termination — {EmployeeName}",
                Body = GetLifecycleImmediateTerminationTemplate(),
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "LIFECYCLE_IT_ONBOARDING",
                Subject = "IT onboarding ticket: {EmployeeName}",
                Body = GetLifecycleItOnboardingTemplate(),
                Category = "Lifecycle",
                IsActive = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        int created = 0;
        int skipped = 0;

        const string checkSql = "SELECT COUNT(*) FROM EmailTemplates WHERE Name = @Name";
        const string insertSql = @"
            INSERT INTO EmailTemplates (Id, Name, Subject, Body, Category, IsActive, IsBuiltIn, CreatedAt)
            VALUES (@Id, @Name, @Subject, @Body, @Category, @IsActive, @IsBuiltIn, @CreatedAt)";

        foreach (var template in defaultTemplates)
        {
            // Check if template already exists by name
            var existingCount = await connection.ExecuteScalarAsync<int>(checkSql, new { template.Name });
            if (existingCount > 0)
            {
                _logger.LogDebug("Skip: Email template '{Name}' already exists", template.Name);
                skipped++;
                continue;
            }

            await connection.ExecuteAsync(insertSql, template);
            _logger.LogInformation("Created email template '{Name}' ({Category})",
                template.Name, template.Category);
            created++;
        }

        _logger.LogInformation("Email templates seeding complete! Created: {Created}, Skipped: {Skipped}", created, skipped);
        _logger.LogInformation("Identity Center is now ready for professional email communications!");
    }

    // ==================== TEMPLATE HTML METHODS ====================

    private static string GetReviewAssignedTemplate() => @"<!DOCTYPE html>
<html>
<head>
<style>
body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }
.header { background: #0d6efd; color: white; padding: 20px; text-align: center; }
.header h1 { margin: 0; }
.content { padding: 20px; }
.button { background: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; }
.footer { background: #f8f9fa; padding: 15px; text-align: center; font-size: 12px; color: #666; }
ul { list-style: none; padding: 0; }
ul li { padding: 8px 0; border-bottom: 1px solid #eee; }
ul li:last-child { border-bottom: none; }
</style>
</head>
<body>
<div class=""header""><h1>Access Review Assigned</h1></div>
<div class=""content"">
<p>Hello {ReviewerName},</p>
<p>You have been assigned to review access for the following campaign:</p>
<ul>
<li><strong>Campaign:</strong> {CampaignName}</li>
<li><strong>Due Date:</strong> {DueDate}</li>
<li><strong>Items to Review:</strong> {ItemCount}</li>
</ul>
<p>Please complete your review before the due date to ensure compliance.</p>
<p><a href=""{ReviewUrl}"" class=""button"" style=""background: #0d6efd; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; font-weight: bold; font-size: 16px;"">Start Review</a></p>
</div>
<div class=""footer"">This is an automated message from Identity Center.</div>
</body>
</html>";

    private static string GetReviewReminderTemplate() => @"<!DOCTYPE html>
<html>
<head>
<style>
body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }
.header { background: #f59e0b; color: white; padding: 20px; text-align: center; }
.header h1 { margin: 0; }
.content { padding: 20px; }
.button { background: #f59e0b; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; }
.footer { background: #f8f9fa; padding: 15px; text-align: center; font-size: 12px; color: #666; }
ul { list-style: none; padding: 0; }
ul li { padding: 8px 0; border-bottom: 1px solid #eee; }
ul li:last-child { border-bottom: none; }
</style>
</head>
<body>
<div class=""header""><h1>Review Reminder</h1></div>
<div class=""content"">
<p>Hello {ReviewerName},</p>
<p>This is a reminder that your access review is due soon:</p>
<ul>
<li><strong>Campaign:</strong> {CampaignName}</li>
<li><strong>Due Date:</strong> {DueDate}</li>
<li><strong>Remaining Items:</strong> {RemainingCount}</li>
</ul>
<p>Please complete your review to maintain compliance.</p>
<p><a href=""{ReviewUrl}"" class=""button"" style=""background: #f59e0b; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; font-weight: bold; font-size: 16px;"">Continue Review</a></p>
</div>
<div class=""footer"">This is an automated message from Identity Center.</div>
</body>
</html>";

    private static string GetReviewOverdueTemplate() => @"<!DOCTYPE html>
<html>
<head>
<style>
body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }
.header { background: #dc2626; color: white; padding: 20px; text-align: center; }
.header h1 { margin: 0; }
.content { padding: 20px; }
.button { background: #dc2626; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; }
.footer { background: #f8f9fa; padding: 15px; text-align: center; font-size: 12px; color: #666; }
.urgent { color: #dc2626; font-weight: bold; }
ul { list-style: none; padding: 0; }
ul li { padding: 8px 0; border-bottom: 1px solid #eee; }
ul li:last-child { border-bottom: none; }
</style>
</head>
<body>
<div class=""header""><h1>URGENT: Review Overdue</h1></div>
<div class=""content"">
<p>Hello {ReviewerName},</p>
<p class=""urgent"">Your access review is now overdue and requires immediate attention!</p>
<ul>
<li><strong>Campaign:</strong> {CampaignName}</li>
<li><strong>Original Due Date:</strong> {DueDate}</li>
<li><strong>Days Overdue:</strong> {DaysOverdue}</li>
<li><strong>Remaining Items:</strong> {RemainingCount}</li>
</ul>
<p>Please complete this review immediately to avoid compliance issues.</p>
<p><a href=""{ReviewUrl}"" class=""button"" style=""background: #dc3545; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; font-weight: bold; font-size: 16px;"">Complete Review Now</a></p>
</div>
<div class=""footer"">This is an automated message from Identity Center.</div>
</body>
</html>";

    private static string GetReviewCompleteTemplate() => @"<!DOCTYPE html>
<html>
<head>
<style>
body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }
.header { background: #10b981; color: white; padding: 20px; text-align: center; }
.header h1 { margin: 0; }
.content { padding: 20px; }
.footer { background: #f8f9fa; padding: 15px; text-align: center; font-size: 12px; color: #666; }
ul { list-style: none; padding: 0; }
ul li { padding: 8px 0; border-bottom: 1px solid #eee; }
ul li:last-child { border-bottom: none; }
</style>
</head>
<body>
<div class=""header""><h1>Review Completed</h1></div>
<div class=""content"">
<p>Hello {ReviewerName},</p>
<p>Thank you for completing your access review!</p>
<ul>
<li><strong>Campaign:</strong> {CampaignName}</li>
<li><strong>Items Reviewed:</strong> {ItemCount}</li>
<li><strong>Approved:</strong> {ApprovedCount}</li>
<li><strong>Revoked:</strong> {RevokedCount}</li>
<li><strong>Completion Date:</strong> {CompletionDate}</li>
</ul>
<p>Your review decisions have been recorded and any revocations will be processed.</p>
</div>
<div class=""footer"">This is an automated message from Identity Center.</div>
</body>
</html>";

    private static string GetAccessRevokedTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#6b7280;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Access Revoked</h1></div>
<div class='content'>
<p>Hello {UserName},</p>
<p>Your access to the following resource has been revoked as part of an access review:</p>
<ul>
<li><strong>Resource:</strong> {GroupName}</li>
<li><strong>Reason:</strong> {Reason}</li>
<li><strong>Effective Date:</strong> {EffectiveDate}</li>
</ul>
<p>If you believe this was done in error, please contact your manager or IT support.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetWorkflowApprovalRequestTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#8b5cf6;color:white;padding:20px;text-align:center}.content{padding:20px}.button{background:#10b981;color:white;padding:12px 24px;text-decoration:none;border-radius:5px;display:inline-block;margin-right:10px}.button-deny{background:#dc2626}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Approval Required</h1></div>
<div class='content'>
<p>Hello {ApproverName},</p>
<p>A request requires your approval:</p>
<ul>
<li><strong>Request Type:</strong> {RequestType}</li>
<li><strong>Requester:</strong> {RequesterName}</li>
<li><strong>Details:</strong> {RequestDetails}</li>
<li><strong>Submitted:</strong> {SubmittedDate}</li>
</ul>
<p><a href='{ApproveUrl}' class='button'>Approve</a> <a href='{DenyUrl}' class='button button-deny'>Deny</a></p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetWorkflowApprovedTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#10b981;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Request Approved</h1></div>
<div class='content'>
<p>Hello {RequesterName},</p>
<p>Great news! Your request has been approved:</p>
<ul>
<li><strong>Request Type:</strong> {RequestType}</li>
<li><strong>Approved By:</strong> {ApproverName}</li>
<li><strong>Approval Date:</strong> {ApprovalDate}</li>
<li><strong>Comments:</strong> {Comments}</li>
</ul>
<p>Your request is now being processed.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetWorkflowDeniedTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#dc2626;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Request Denied</h1></div>
<div class='content'>
<p>Hello {RequesterName},</p>
<p>Unfortunately, your request has been denied:</p>
<ul>
<li><strong>Request Type:</strong> {RequestType}</li>
<li><strong>Denied By:</strong> {ApproverName}</li>
<li><strong>Denial Date:</strong> {DenialDate}</li>
<li><strong>Reason:</strong> {Reason}</li>
</ul>
<p>If you have questions, please contact your manager.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetWorkflowEscalatedTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#f59e0b;color:white;padding:20px;text-align:center}.content{padding:20px}.button{background:#f59e0b;color:white;padding:12px 24px;text-decoration:none;border-radius:5px;display:inline-block}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Escalation Notice</h1></div>
<div class='content'>
<p>Hello {ApproverName},</p>
<p>A request has been escalated to you and requires your attention:</p>
<ul>
<li><strong>Request Type:</strong> {RequestType}</li>
<li><strong>Requester:</strong> {RequesterName}</li>
<li><strong>Original Approver:</strong> {OriginalApprover}</li>
<li><strong>Escalation Reason:</strong> {EscalationReason}</li>
</ul>
<p><a href='{ReviewUrl}' class='button'>Review Request</a></p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetWelcomeNewUserTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#0d6efd;color:white;padding:20px;text-align:center}.content{padding:20px}.button{background:#0d6efd;color:white;padding:12px 24px;text-decoration:none;border-radius:5px;display:inline-block}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Welcome to Identity Center!</h1></div>
<div class='content'>
<p>Hello {DisplayName},</p>
<p>Welcome aboard! Your account has been created in Identity Center.</p>
<ul>
<li><strong>Username:</strong> {Username}</li>
<li><strong>Email:</strong> {Email}</li>
<li><strong>Department:</strong> {Department}</li>
</ul>
<p>You can access the system using your corporate credentials.</p>
<p><a href='{LoginUrl}' class='button'>Get Started</a></p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetPasswordExpiryWarningTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#f59e0b;color:white;padding:20px;text-align:center}.content{padding:20px}.button{background:#f59e0b;color:white;padding:12px 24px;text-decoration:none;border-radius:5px;display:inline-block}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Password Expiring Soon</h1></div>
<div class='content'>
<p>Hello {DisplayName},</p>
<p>Your password will expire in {DaysRemaining} days.</p>
<p>Please change your password before {ExpirationDate} to avoid being locked out.</p>
<p><a href='{ChangePasswordUrl}' class='button'>Change Password</a></p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetAccountLockedTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#dc2626;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Account Locked</h1></div>
<div class='content'>
<p>Hello {DisplayName},</p>
<p>Your account has been locked due to multiple failed login attempts.</p>
<ul>
<li><strong>Lock Time:</strong> {LockTime}</li>
<li><strong>Failed Attempts:</strong> {FailedAttempts}</li>
<li><strong>Source IP:</strong> {SourceIP}</li>
</ul>
<p>If this was not you, please contact IT security immediately.</p>
<p>To unlock your account, please contact the IT Help Desk.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetGenericNotificationTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#0d6efd;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>{Subject}</h1></div>
<div class='content'>
<p>Hello {RecipientName},</p>
<p>{Message}</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetComplianceViolationTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#dc2626;color:white;padding:20px;text-align:center}.content{padding:20px}.button{background:#dc2626;color:white;padding:12px 24px;text-decoration:none;border-radius:5px;display:inline-block}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}.alert{background:#fef2f2;border-left:4px solid #dc2626;padding:15px;margin:15px 0}</style></head>
<body>
<div class='header'><h1>Compliance Violation Detected</h1></div>
<div class='content'>
<p>Hello {RecipientName},</p>
<div class='alert'>
<strong>A compliance violation has been detected that requires your attention.</strong>
</div>
<ul>
<li><strong>Policy:</strong> {PolicyName}</li>
<li><strong>Violation Type:</strong> {ViolationType}</li>
<li><strong>Affected User/Resource:</strong> {AffectedEntity}</li>
<li><strong>Detection Time:</strong> {DetectionTime}</li>
<li><strong>Severity:</strong> {Severity}</li>
</ul>
<p>Please investigate and remediate this violation immediately.</p>
<p><a href='{DetailsUrl}' class='button'>View Details</a></p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetAuditNotificationTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#14b8a6;color:white;padding:20px;text-align:center}.content{padding:20px}.button{background:#14b8a6;color:white;padding:12px 24px;text-decoration:none;border-radius:5px;display:inline-block}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}</style></head>
<body>
<div class='header'><h1>Audit Report Ready</h1></div>
<div class='content'>
<p>Hello {RecipientName},</p>
<p>A new audit report is available for your review:</p>
<ul>
<li><strong>Report:</strong> {ReportName}</li>
<li><strong>Period:</strong> {ReportPeriod}</li>
<li><strong>Generated:</strong> {GeneratedDate}</li>
<li><strong>Framework:</strong> {Framework}</li>
</ul>
<p><a href='{ReportUrl}' class='button'>View Report</a></p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    // ==================== LIFECYCLE TEMPLATE HTML METHODS ====================

    private static string GetLifecycleNewHireManagerTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#10b981;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}ul{list-style:none;padding:0}ul li{padding:8px 0;border-bottom:1px solid #eee}ul li:last-child{border-bottom:none}</style></head>
<body>
<div class='header'><h1>New Hire Account Provisioning</h1></div>
<div class='content'>
<p>Hello {ManagerName},</p>
<p>A new hire account is being provisioned for <strong>{EmployeeName}</strong>.</p>
<ul>
<li><strong>Department:</strong> {Department}</li>
<li><strong>Title:</strong> {Title}</li>
<li><strong>Start Date:</strong> {StartDate}</li>
</ul>
<p>You will receive a follow-up notification once onboarding is complete.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetLifecycleOnboardingCompleteTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#10b981;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}ul{list-style:none;padding:0}ul li{padding:8px 0;border-bottom:1px solid #eee}ul li:last-child{border-bottom:none}</style></head>
<body>
<div class='header'><h1>Onboarding Complete</h1></div>
<div class='content'>
<p>Hello {ManagerName},</p>
<p>Onboarding has been completed for <strong>{EmployeeName}</strong>.</p>
<ul>
<li><strong>Account:</strong> {SamAccountName}</li>
<li><strong>Email:</strong> {EmailAddress}</li>
<li><strong>Groups Assigned:</strong> {GroupCount}</li>
</ul>
<p>The employee can now log in and access assigned resources.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetLifecycleDeptTransferTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#0d6efd;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}ul{list-style:none;padding:0}ul li{padding:8px 0;border-bottom:1px solid #eee}ul li:last-child{border-bottom:none}</style></head>
<body>
<div class='header'><h1>Department Transfer</h1></div>
<div class='content'>
<p>Hello {ManagerName},</p>
<p><strong>{EmployeeName}</strong> has been transferred.</p>
<ul>
<li><strong>Previous Department:</strong> {OldDepartment}</li>
<li><strong>New Department:</strong> {NewDepartment}</li>
<li><strong>New Title:</strong> {Title}</li>
<li><strong>Effective Date:</strong> {EffectiveDate}</li>
</ul>
<p>Group memberships and access have been updated accordingly.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetLifecycleRoleChangeTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#0d6efd;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}ul{list-style:none;padding:0}ul li{padding:8px 0;border-bottom:1px solid #eee}ul li:last-child{border-bottom:none}</style></head>
<body>
<div class='header'><h1>Role Change Complete</h1></div>
<div class='content'>
<p>Hello {ManagerName},</p>
<p>The role change for <strong>{EmployeeName}</strong> has been processed.</p>
<ul>
<li><strong>Previous Role:</strong> {OldTitle}</li>
<li><strong>New Role:</strong> {NewTitle}</li>
<li><strong>Groups Added:</strong> {GroupsAdded}</li>
<li><strong>Groups Removed:</strong> {GroupsRemoved}</li>
</ul>
<p>Please verify the employee has the correct access for their new role.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetLifecycleOffboardingCompleteTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#6b7280;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}ul{list-style:none;padding:0}ul li{padding:8px 0;border-bottom:1px solid #eee}ul li:last-child{border-bottom:none}</style></head>
<body>
<div class='header'><h1>Offboarding Complete</h1></div>
<div class='content'>
<p>Hello {ManagerName},</p>
<p>Offboarding has been completed for <strong>{EmployeeName}</strong>.</p>
<ul>
<li><strong>Account Status:</strong> Disabled</li>
<li><strong>Groups Removed:</strong> {GroupsRemoved}</li>
<li><strong>Email Forwarding:</strong> {ForwardingStatus}</li>
<li><strong>Processed On:</strong> {ProcessedDate}</li>
</ul>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetLifecycleImmediateTerminationTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#dc2626;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}.alert{background:#fef2f2;border-left:4px solid #dc2626;padding:15px;margin:15px 0}ul{list-style:none;padding:0}ul li{padding:8px 0;border-bottom:1px solid #eee}ul li:last-child{border-bottom:none}</style></head>
<body>
<div class='header'><h1>Immediate Termination Processed</h1></div>
<div class='content'>
<p>Hello {RecipientName},</p>
<div class='alert'><strong>An immediate termination has been processed for {EmployeeName}.</strong></div>
<ul>
<li><strong>Account Status:</strong> Disabled</li>
<li><strong>Password:</strong> Reset</li>
<li><strong>Sessions:</strong> Revoked</li>
<li><strong>All Access:</strong> Removed</li>
<li><strong>Processed At:</strong> {ProcessedDate}</li>
</ul>
<p>Please review and confirm all access has been properly revoked.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";

    private static string GetLifecycleItOnboardingTemplate() => @"<!DOCTYPE html>
<html>
<head><style>body{font-family:Arial,sans-serif;line-height:1.6;color:#333}.header{background:#8b5cf6;color:white;padding:20px;text-align:center}.content{padding:20px}.footer{background:#f8f9fa;padding:15px;text-align:center;font-size:12px;color:#666}ul{list-style:none;padding:0}ul li{padding:8px 0;border-bottom:1px solid #eee}ul li:last-child{border-bottom:none}</style></head>
<body>
<div class='header'><h1>IT Onboarding Ticket</h1></div>
<div class='content'>
<p>A new IT onboarding ticket has been created:</p>
<ul>
<li><strong>Employee:</strong> {EmployeeName}</li>
<li><strong>Department:</strong> {Department}</li>
<li><strong>Title:</strong> {Title}</li>
<li><strong>Start Date:</strong> {StartDate}</li>
<li><strong>Manager:</strong> {ManagerName}</li>
</ul>
<p><strong>Provisioned Items:</strong></p>
<ul>
<li>AD Account: {SamAccountName}</li>
<li>Email: {EmailAddress}</li>
<li>License: {LicenseSku}</li>
</ul>
<p>Please ensure hardware and additional resources are prepared.</p>
</div>
<div class='footer'>This is an automated message from Identity Center.</div>
</body></html>";
}
