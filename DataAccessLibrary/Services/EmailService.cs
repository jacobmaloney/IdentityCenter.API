using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace DataAccessLibrary.Services;

/// <summary>
/// High-performance email service with database-driven SMTP configuration
/// Supports templates, queuing, and retry logic
/// Based on proven IdentityServer implementation with enhancements
/// </summary>
public class EmailService : IEmailService
{
    private readonly string _connectionString;
    private readonly ISMTPRepository _smtpRepository;
    private readonly IGlobalLogger _logger;
    private readonly SystemConfigurationService _configService;
    private readonly IBrandingService? _brandingService;

    public EmailService(
        IConfiguration configuration,
        ISMTPRepository smtpRepository,
        IGlobalLogger logger,
        SystemConfigurationService configService,
        IBrandingService? brandingService = null)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _smtpRepository = smtpRepository;
        _logger = logger;
        _configService = configService;
        _brandingService = brandingService;
    }

    private string GetProductName() => _brandingService?.ProductName ?? "Identity Center";

    public async Task<bool> SendEmailAsync(string toAddress, string subject, string body, bool isHtml = true)
    {
        _logger.LogMethodEntry(nameof(SendEmailAsync), new { toAddress, subject });

        try
        {
            var smtpConfig = await _smtpRepository.GetDefaultAsync();
            if (smtpConfig == null)
            {
                _logger.LogWarning("No default SMTP configuration found");
                return false;
            }

            return await SendEmailInternalAsync(smtpConfig, toAddress, subject, body, isHtml);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(SendEmailAsync), ex);
            return false;
        }
        finally
        {
            _logger.LogMethodExit(nameof(SendEmailAsync));
        }
    }

    public async Task<bool> SendEmailAsync(Guid smtpConfigId, string toAddress, string subject, string body, bool isHtml = true)
    {
        _logger.LogMethodEntry(nameof(SendEmailAsync), new { smtpConfigId, toAddress, subject });

        try
        {
            var smtpConfig = await _smtpRepository.GetByIdAsync(smtpConfigId);
            if (smtpConfig == null)
            {
                _logger.LogWarning("SMTP configuration not found: {SmtpConfigId}", smtpConfigId);
                return false;
            }

            return await SendEmailInternalAsync(smtpConfig, toAddress, subject, body, isHtml);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(SendEmailAsync), ex);
            return false;
        }
        finally
        {
            _logger.LogMethodExit(nameof(SendEmailAsync));
        }
    }

    public async Task<bool> SendTemplatedEmailAsync(string templateName, string toAddress, Dictionary<string, string> variables)
    {
        _logger.LogMethodEntry(nameof(SendTemplatedEmailAsync), new { templateName, toAddress });

        try
        {
            var template = await GetTemplateAsync(templateName);
            if (template == null)
            {
                _logger.LogWarning("Email template not found: {TemplateName}", templateName);
                return false;
            }

            var subject = ReplaceVariables(template.Subject, variables);
            var body = ReplaceVariables(template.Body, variables);

            // Get portal URL from system configuration
            var config = await _configService.GetConfigurationAsync();
            var portalUrl = config.PortalUrl ?? "https://localhost";

            // Replace portal URL placeholder
            body = body.Replace("[PORTAL_URL]", portalUrl);

            return await SendEmailAsync(toAddress, subject, body, isHtml: true);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(SendTemplatedEmailAsync), ex);
            return false;
        }
        finally
        {
            _logger.LogMethodExit(nameof(SendTemplatedEmailAsync));
        }
    }

    public async Task<Guid> QueueEmailAsync(string toAddress, string subject, string body,
        string? templateId = null, string? relatedEntityType = null, Guid? relatedEntityId = null)
    {
        _logger.LogMethodEntry(nameof(QueueEmailAsync), new { toAddress, subject });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var queueItem = new EmailQueueItem
            {
                Id = Guid.NewGuid(),
                ToAddress = toAddress,
                Subject = subject,
                Body = body,
                IsHtml = true,
                Status = "Pending",
                RetryCount = 0,
                MaxRetries = 3,
                TemplateId = templateId,
                RelatedEntityType = relatedEntityType,
                RelatedEntityId = relatedEntityId,
                CreatedAt = DateTime.UtcNow
            };

            const string sql = @"
                INSERT INTO EmailQueue
                (Id, ToAddress, ToDisplayName, Subject, Body, IsHtml, Status, RetryCount, MaxRetries,
                 TemplateId, RelatedEntityType, RelatedEntityId, CreatedAt)
                VALUES
                (@Id, @ToAddress, @ToDisplayName, @Subject, @Body, @IsHtml, @Status, @RetryCount, @MaxRetries,
                 @TemplateId, @RelatedEntityType, @RelatedEntityId, @CreatedAt)";

            await connection.ExecuteAsync(sql, queueItem);

            _logger.LogInformation("Email queued successfully: {EmailId}", queueItem.Id);
            return queueItem.Id;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(QueueEmailAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(QueueEmailAsync));
        }
    }

    public async Task<Guid> QueueEmailAsync(string templateName, string? toAddress, string subject,
        Dictionary<string, string> variables, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(QueueEmailAsync), new { templateName, toAddress, subject });

        try
        {
            // Get template if it exists
            var template = await GetTemplateAsync(templateName);
            string body;

            if (template != null)
            {
                // Use template body and replace variables
                body = ReplaceVariables(template.Body, variables);
                subject = ReplaceVariables(template.Subject, variables);
            }
            else
            {
                // Create basic body from variables
                var variablesList = string.Join("<br/>", variables.Select(v => $"<strong>{v.Key}:</strong> {v.Value}"));
                body = $@"<html><body style='font-family: Arial, sans-serif;'>
                    <div style='padding: 20px;'>
                        <h2>{subject}</h2>
                        {variablesList}
                    </div>
                </body></html>";
            }

            // Get portal URL from system configuration
            var config = await _configService.GetConfigurationAsync();
            var portalUrl = config.PortalUrl ?? "https://localhost";
            body = body.Replace("[PORTAL_URL]", portalUrl);

            // If no toAddress specified, get admin emails from configuration
            if (string.IsNullOrWhiteSpace(toAddress))
            {
                // Get admin notification emails from system config or use a default
                toAddress = config.AdminNotificationEmail ?? "admin@identitycenter.local";
                _logger.LogDebug("Using admin email for notification: {AdminEmail}", toAddress);
            }

            // Queue the email using existing method
            return await QueueEmailAsync(toAddress, subject, body, templateName, "Policy", null);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(QueueEmailAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(QueueEmailAsync));
        }
    }

    public async Task ProcessEmailQueueAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(ProcessEmailQueueAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get pending emails
            const string selectSql = @"
                SELECT TOP 50 * FROM EmailQueue
                WHERE Status = 'Pending' AND RetryCount < MaxRetries
                ORDER BY CreatedAt";

            var pendingEmails = (await connection.QueryAsync<EmailQueueItem>(selectSql)).ToList();

            _logger.LogInformation("Processing {Count} pending emails", pendingEmails.Count);

            foreach (var email in pendingEmails)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Email queue processing cancelled");
                    break;
                }

                // Update status to Sending
                await connection.ExecuteAsync(
                    "UPDATE EmailQueue SET Status = 'Sending' WHERE Id = @Id",
                    new { email.Id });

                var success = await SendEmailAsync(email.ToAddress, email.Subject, email.Body, email.IsHtml);

                if (success)
                {
                    // Mark as sent
                    await connection.ExecuteAsync(@"
                        UPDATE EmailQueue
                        SET Status = 'Sent', SentAt = GETUTCDATE(), ProcessedAt = GETUTCDATE()
                        WHERE Id = @Id",
                        new { email.Id });

                    _logger.LogInformation("Email sent successfully: {EmailId}", email.Id);
                }
                else
                {
                    // Increment retry count and mark as failed if max retries reached
                    var newRetryCount = email.RetryCount + 1;
                    var newStatus = newRetryCount >= email.MaxRetries ? "Failed" : "Pending";

                    await connection.ExecuteAsync(@"
                        UPDATE EmailQueue
                        SET Status = @Status, RetryCount = @RetryCount, ProcessedAt = GETUTCDATE(),
                            ErrorMessage = 'Failed to send email'
                        WHERE Id = @Id",
                        new { email.Id, Status = newStatus, RetryCount = newRetryCount });

                    _logger.LogWarning("Email send failed (retry {Retry}/{Max}): {EmailId}",
                        newRetryCount, email.MaxRetries, email.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(ProcessEmailQueueAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(ProcessEmailQueueAsync));
        }
    }

    public async Task<(bool Success, string Message)> TestSmtpConfigurationAsync(Guid smtpConfigId, string testEmailAddress)
    {
        _logger.LogMethodEntry(nameof(TestSmtpConfigurationAsync), new { smtpConfigId, testEmailAddress });

        try
        {
            var config = await _smtpRepository.GetByIdAsync(smtpConfigId);
            if (config == null)
            {
                return (false, "SMTP configuration not found");
            }

            var result = await TestSmtpConfigurationAsync(config, testEmailAddress);

            // Update test result in database
            await _smtpRepository.UpdateTestResultAsync(smtpConfigId, result.Success, result.Message);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(TestSmtpConfigurationAsync), ex);
            return (false, $"Error testing configuration: {ex.Message}");
        }
        finally
        {
            _logger.LogMethodExit(nameof(TestSmtpConfigurationAsync));
        }
    }

    public async Task<(bool Success, string Message)> TestSmtpConfigurationAsync(SMTPConfiguration config, string testEmailAddress)
    {
        _logger.LogMethodEntry(nameof(TestSmtpConfigurationAsync), new { config.DisplayName, testEmailAddress });

        try
        {
            var productName = GetProductName();
            var subject = string.Concat("SMTP Configuration Test - ", productName);
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; color: white;'>
                        <h2>SMTP Configuration Test</h2>
                    </div>
                    <div style='padding: 20px; background: #f5f5f5;'>
                        <p>This is a test email from {productName} to verify SMTP configuration.</p>
                        <p><strong>Configuration:</strong> {config.DisplayName}</p>
                        <p><strong>Server:</strong> {config.Server}:{config.Port}</p>
                        <p><strong>SSL Enabled:</strong> {config.EnableSsl}</p>
                        <p><strong>From Address:</strong> {config.FromAddress}</p>
                        <p><strong>Test Time:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
                        <p>If you received this email, your SMTP configuration is working correctly!</p>
                    </div>
                </body>
                </html>";

            var success = await SendEmailInternalAsync(config, testEmailAddress, subject, body, isHtml: true);

            if (success)
            {
                return (true, $"Test email sent successfully to {testEmailAddress}");
            }
            else
            {
                return (false, "Failed to send test email. Check SMTP configuration and credentials.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(TestSmtpConfigurationAsync), ex);
            return (false, $"Error: {ex.Message}");
        }
        finally
        {
            _logger.LogMethodExit(nameof(TestSmtpConfigurationAsync));
        }
    }

    public async Task<EmailTemplate?> GetTemplateAsync(string templateName)
    {
        _logger.LogMethodEntry(nameof(GetTemplateAsync), new { templateName });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT * FROM EmailTemplates
                WHERE Name = @TemplateName AND IsActive = 1";

            var template = await connection.QueryFirstOrDefaultAsync<EmailTemplate>(sql,
                new { TemplateName = templateName });

            if (template != null)
            {
                _logger.LogInformation("Found email template: {TemplateName}", templateName);
            }
            else
            {
                _logger.LogWarning("Email template not found: {TemplateName}", templateName);
            }

            return template;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetTemplateAsync), ex);
            return null;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetTemplateAsync));
        }
    }

    public async Task SeedDefaultTemplatesAsync()
    {
        _logger.LogMethodEntry(nameof(SeedDefaultTemplatesAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var templates = GetDefaultTemplates();

            foreach (var template in templates)
            {
                // Check if template already exists
                var exists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM EmailTemplates WHERE Name = @Name) THEN 1 ELSE 0 END AS BIT)",
                    new { template.Name });

                if (!exists)
                {
                    const string sql = @"
                        INSERT INTO EmailTemplates (Id, Name, Subject, Body, Category, IsActive, IsBuiltIn, CreatedAt)
                        VALUES (@Id, @Name, @Subject, @Body, @Category, @IsActive, 1, GETUTCDATE())";

                    await connection.ExecuteAsync(sql, template);
                    _logger.LogInformation("Seeded email template: {TemplateName}", template.Name);
                }
            }

            _logger.LogInformation("Email template seeding completed");
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(SeedDefaultTemplatesAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(SeedDefaultTemplatesAsync));
        }
    }

    public async Task<int> SendCampaignAssignmentEmailsAsync(Guid campaignId, List<AccessReviewAssignment> assignments)
    {
        _logger.LogMethodEntry(nameof(SendCampaignAssignmentEmailsAsync), new { campaignId, assignmentCount = assignments.Count });

        int successCount = 0;

        try
        {
            if (assignments == null || assignments.Count == 0)
            {
                _logger.LogWarning("No assignments provided for campaign {CampaignId}", campaignId);
                return 0;
            }

            // Load campaign details
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var campaign = await connection.QueryFirstOrDefaultAsync<Campaign>(
                "SELECT * FROM Campaigns WHERE Id = @CampaignId",
                new { CampaignId = campaignId });

            if (campaign == null)
            {
                _logger.LogWarning("Campaign not found: {CampaignId}", campaignId);
                return 0;
            }

            if (!campaign.AssignmentEmailTemplateId.HasValue)
            {
                _logger.LogInformation("Campaign {CampaignId} has no assignment email template configured. Skipping emails.", campaignId);
                return 0;
            }

            // Load the email template
            var template = await connection.QueryFirstOrDefaultAsync<EmailTemplate>(
                "SELECT * FROM EmailTemplates WHERE Id = @TemplateId AND IsActive = 1",
                new { TemplateId = campaign.AssignmentEmailTemplateId.Value });

            if (template == null)
            {
                _logger.LogWarning("Email template not found or inactive: {TemplateId}", campaign.AssignmentEmailTemplateId.Value);
                return 0;
            }

            _logger.LogInformation("Sending {Count} assignment emails for campaign {CampaignName} using template {TemplateName}",
                assignments.Count, campaign.Name, template.Name);

            // Group assignments by reviewer
            var assignmentsByReviewer = assignments
                .GroupBy(a => new { a.ReviewerId, a.ReviewerEmail, a.ReviewerName })
                .ToList();

            // Send email to each reviewer
            foreach (var reviewerGroup in assignmentsByReviewer)
            {
                if (string.IsNullOrEmpty(reviewerGroup.Key.ReviewerEmail))
                {
                    _logger.LogWarning("Skipping reviewer {ReviewerId} - no email address", reviewerGroup.Key.ReviewerId);
                    continue;
                }

                var reviewerAssignments = reviewerGroup.ToList();

                // Build variable dictionary
                var variables = new Dictionary<string, string>
                {
                    { "ReviewerName", reviewerGroup.Key.ReviewerName ?? "Reviewer" },
                    { "CampaignName", campaign.Name },
                    { "Count", reviewerAssignments.Count.ToString() },
                    { "ItemCount", reviewerAssignments.Count.ToString() },
                    { "AssignmentCount", reviewerAssignments.Count.ToString() },
                    { "DueDate", campaign.DueDate?.ToString("MMMM dd, yyyy") ?? campaign.EndDate.ToString("MMMM dd, yyyy") },
                    { "ReviewUrl", "[PORTAL_URL]/approvals/inbox" },
                    { "ReviewLink", "[PORTAL_URL]/approvals/inbox" },
                    { "Priority", "Normal" }
                };

                // Replace variables in subject and body
                var subject = ReplaceVariables(template.Subject, variables);
                var body = ReplaceVariables(template.Body, variables);

                // Get portal URL directly from database (using Dapper to avoid EF context disposal issues)
                var portalUrl = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT PortalUrl FROM SystemConfigurations WHERE Id = 1") ?? "https://localhost";

                // Replace portal URL placeholder
                body = body.Replace("[PORTAL_URL]", portalUrl);
                subject = subject.Replace("[PORTAL_URL]", portalUrl);

                // Send the email with CC from campaign
                var smtpConfig = await _smtpRepository.GetDefaultAsync();
                bool success = false;
                if (smtpConfig != null)
                {
                    success = await SendEmailInternalAsync(smtpConfig, reviewerGroup.Key.ReviewerEmail, subject, body, isHtml: true, ccAddresses: campaign.NotificationCcEmails);
                }

                if (success)
                {
                    successCount++;
                    _logger.LogInformation("Sent assignment email to {ReviewerEmail} for {Count} reviews",
                        reviewerGroup.Key.ReviewerEmail, reviewerAssignments.Count);
                }
                else
                {
                    _logger.LogWarning("Failed to send assignment email to {ReviewerEmail}",
                        reviewerGroup.Key.ReviewerEmail);
                }
            }

            _logger.LogInformation("Campaign assignment emails sent: {SuccessCount}/{TotalReviewers} successful",
                successCount, assignmentsByReviewer.Count);

            return successCount;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(SendCampaignAssignmentEmailsAsync), ex);
            return successCount;
        }
        finally
        {
            _logger.LogMethodExit(nameof(SendCampaignAssignmentEmailsAsync));
        }
    }

    #region Private Helper Methods

    private async Task<bool> SendEmailInternalAsync(SMTPConfiguration config, string toAddress, string subject,
        string body, bool isHtml, string? ccAddresses = null)
    {
        try
        {
            _logger.LogDebug("Sending email via SMTP: {Server}:{Port}", config.Server, config.Port);

            using var smtpClient = new SmtpClient(config.Server, config.Port)
            {
                EnableSsl = config.EnableSsl,
                Credentials = new NetworkCredential(config.Username, config.Password)
            };

            using var message = new MailMessage
            {
                From = new MailAddress(config.FromAddress, config.FromDisplayName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };

            // Support multiple recipients separated by semicolon or comma
            var recipients = toAddress.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var recipient in recipients)
            {
                message.To.Add(new MailAddress(recipient.Trim()));
            }

            // Add CC addresses if provided
            if (!string.IsNullOrEmpty(ccAddresses))
            {
                var ccList = ccAddresses.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var cc in ccList)
                {
                    var trimmed = cc.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        message.CC.Add(new MailAddress(trimmed));
                    }
                }
            }

            // Add Reply-To if configured
            if (!string.IsNullOrEmpty(config.ReplyToAddress))
            {
                message.ReplyToList.Add(new MailAddress(config.ReplyToAddress, config.ReplyToDisplayName));
            }

            await smtpClient.SendMailAsync(message);

            _logger.LogInformation("Email sent successfully to {ToAddress} via {SmtpConfig}",
                toAddress, config.DisplayName);

            return true;
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP error sending email to {ToAddress}: {Message}",
                toAddress, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {ToAddress}", toAddress);
            return false;
        }
    }

    private string ReplaceVariables(string text, Dictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(text) || variables == null || variables.Count == 0)
        {
            return text;
        }

        // Replace variables in format {VariableName}
        return Regex.Replace(text, @"\{(\w+)\}", match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private List<EmailTemplate> GetDefaultTemplates()
    {
        return new List<EmailTemplate>
        {
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_ASSIGNED",
                Subject = "You have been assigned {Count} access reviews",
                Body = GetReviewAssignedTemplate(),
                Category = "AccessReview",
                IsActive = true
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_DUE_SOON",
                Subject = "Reminder: {Count} access reviews due in {Days} days",
                Body = GetReviewDueSoonTemplate(),
                Category = "AccessReview",
                IsActive = true
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_OVERDUE",
                Subject = "URGENT: {Count} overdue access reviews require immediate attention",
                Body = GetReviewOverdueTemplate(),
                Category = "AccessReview",
                IsActive = true
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "REVIEW_COMPLETED",
                Subject = "Thank you - Access review completed",
                Body = GetReviewCompletedTemplate(),
                Category = "AccessReview",
                IsActive = true
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "CAMPAIGN_STARTED",
                Subject = "New access review campaign: {CampaignName}",
                Body = GetCampaignStartedTemplate(),
                Category = "AccessReview",
                IsActive = true
            },
            // Compliance Policy Templates
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "PolicyViolationAlert",
                Subject = "[Compliance Alert] Policy Violations Detected",
                Body = GetPolicyViolationAlertTemplate(),
                Category = "Compliance",
                IsActive = true
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "DailyComplianceSummary",
                Subject = "[Daily Summary] Compliance Report",
                Body = GetDailyComplianceSummaryTemplate(),
                Category = "Compliance",
                IsActive = true
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "FrameworkStatusChange",
                Subject = "[Framework Update] Status Changed",
                Body = GetFrameworkStatusChangeTemplate(),
                Category = "Compliance",
                IsActive = true
            },
            // Provisioning / Credential Delivery Templates
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "CREDENTIAL_USERNAME",
                Subject = "New account created for {{displayName}}",
                Body = GetCredentialUsernameTemplate(),
                Category = "Provisioning",
                IsActive = true
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "CREDENTIAL_PASSWORD",
                Subject = "Your temporary password",
                Body = GetCredentialPasswordTemplate(),
                Category = "Provisioning",
                IsActive = true
            },
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                Name = "CREDENTIAL_COMBINED",
                Subject = "Account credentials for {{displayName}}",
                Body = GetCredentialCombinedTemplate(),
                Category = "Provisioning",
                IsActive = true
            }
        };
    }

    private string GetReviewAssignedTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; color: white;'>
                <h2>Access Review Assignment</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>Hello {ReviewerName},</p>
                <p>You have been assigned <strong>{Count} access reviews</strong> as part of the <strong>{CampaignName}</strong> campaign.</p>
                <p><strong>Due Date:</strong> {DueDate}</p>
                <p><strong>Priority:</strong> {Priority}</p>
                <p>Please review the assigned accesses and approve or deny them based on business necessity.</p>
                <div style='margin: 20px 0;'>
                    <a href='[PORTAL_URL]/approvals/inbox'
                       style='background: #667eea; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                        Start Reviews
                    </a>
                </div>
                <p>Thank you for your attention to access governance!</p>
            </div>
        </body></html>";
    }

    private string GetReviewDueSoonTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: #f39c12; padding: 20px; color: white;'>
                <h2>Access Review Reminder</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>Hello {ReviewerName},</p>
                <p>This is a reminder that you have <strong>{Count} access reviews</strong> due in <strong>{Days} days</strong>.</p>
                <p><strong>Campaign:</strong> {CampaignName}</p>
                <p><strong>Due Date:</strong> {DueDate}</p>
                <p><strong>Completion:</strong> {CompletedCount}/{TotalCount} reviews completed</p>
                <div style='margin: 20px 0;'>
                    <a href='[PORTAL_URL]/approvals/inbox'
                       style='background: #f39c12; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                        Complete Reviews
                    </a>
                </div>
            </div>
        </body></html>";
    }

    private string GetReviewOverdueTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: #e74c3c; padding: 20px; color: white;'>
                <h2>URGENT: Overdue Access Reviews</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>Hello {ReviewerName},</p>
                <p><strong style='color: #e74c3c;'>URGENT:</strong> You have <strong>{Count} overdue access reviews</strong> that require immediate attention.</p>
                <p><strong>Campaign:</strong> {CampaignName}</p>
                <p><strong>Original Due Date:</strong> {DueDate}</p>
                <p><strong>Days Overdue:</strong> {DaysOverdue}</p>
                <p>Please complete these reviews as soon as possible to maintain compliance.</p>
                <div style='margin: 20px 0;'>
                    <a href='[PORTAL_URL]/approvals/inbox'
                       style='background: #e74c3c; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                        Complete NOW
                    </a>
                </div>
            </div>
        </body></html>";
    }

    private string GetReviewCompletedTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: #27ae60; padding: 20px; color: white;'>
                <h2>Access Review Completed</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>Hello {ReviewerName},</p>
                <p>Thank you for completing your access review assignment!</p>
                <p><strong>Campaign:</strong> {CampaignName}</p>
                <p><strong>Reviews Completed:</strong> {Count}</p>
                <p><strong>Approved:</strong> {ApprovedCount}</p>
                <p><strong>Denied:</strong> {DeniedCount}</p>
                <p><strong>Completion Date:</strong> {CompletionDate}</p>
                <p>Your diligence in reviewing access helps maintain our security posture.</p>
            </div>
        </body></html>";
    }

    private string GetCampaignStartedTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; color: white;'>
                <h2>New Access Review Campaign</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>Hello {AdminName},</p>
                <p>A new access review campaign has been initiated:</p>
                <p><strong>Campaign:</strong> {CampaignName}</p>
                <p><strong>Description:</strong> {Description}</p>
                <p><strong>Scope:</strong> {Scope}</p>
                <p><strong>Reviewers Assigned:</strong> {ReviewerCount}</p>
                <p><strong>Total Reviews:</strong> {TotalReviews}</p>
                <p><strong>Due Date:</strong> {DueDate}</p>
                <div style='margin: 20px 0;'>
                    <a href='[PORTAL_URL]/AccessReview/Campaigns'
                       style='background: #667eea; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                        View Campaign
                    </a>
                </div>
            </div>
        </body></html>";
    }

    private string GetPolicyViolationAlertTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: #dc3545; padding: 20px; color: white;'>
                <h2>Compliance Policy Violations Detected</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>Policy violations have been detected that require attention.</p>

                <table style='width:100%; margin:15px 0; border-collapse:collapse;'>
                    <tr><td style='padding:5px 10px; font-weight:bold;'>Policy:</td><td style='padding:5px 10px;'>{PolicyName}</td></tr>
                    <tr><td style='padding:5px 10px; font-weight:bold;'>Framework(s):</td><td style='padding:5px 10px;'>{FrameworkCodes}</td></tr>
                    <tr><td style='padding:5px 10px; font-weight:bold;'>Evaluated:</td><td style='padding:5px 10px;'>{EvaluationDate}</td></tr>
                </table>

                <div style='background:#fff; padding:15px; border-radius:5px; margin:15px 0;'>
                    <h3 style='margin-top:0;'>Violation Summary</h3>
                    <div style='display:flex; gap:20px; flex-wrap:wrap;'>
                        <div style='text-align:center; padding:10px;'>
                            <div style='font-size:24px; font-weight:bold; color:#dc3545;'>{CriticalCount}</div>
                            <div style='font-size:12px; color:#666;'>Critical</div>
                        </div>
                        <div style='text-align:center; padding:10px;'>
                            <div style='font-size:24px; font-weight:bold; color:#fd7e14;'>{HighCount}</div>
                            <div style='font-size:12px; color:#666;'>High</div>
                        </div>
                        <div style='text-align:center; padding:10px;'>
                            <div style='font-size:24px; font-weight:bold; color:#ffc107;'>{MediumCount}</div>
                            <div style='font-size:12px; color:#666;'>Medium</div>
                        </div>
                        <div style='text-align:center; padding:10px;'>
                            <div style='font-size:24px; font-weight:bold; color:#6c757d;'>{LowCount}</div>
                            <div style='font-size:12px; color:#666;'>Low</div>
                        </div>
                    </div>
                    <div style='margin-top:10px; font-size:14px;'><strong>Total Violations:</strong> {TotalViolations}</div>
                </div>

                <h3>Violation Details</h3>
                {ViolationDetails}

                @if({HasMoreViolations})
                {
                    <p style='color:#666; font-style:italic;'>...and {RemainingCount} more violations. View all in the portal.</p>
                }

                <div style='margin: 20px 0;'>
                    <a href='[PORTAL_URL]/compliance/policies'
                       style='background: #dc3545; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                        View All Violations
                    </a>
                </div>
            </div>
        </body></html>";
    }

    private string GetDailyComplianceSummaryTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; color: white;'>
                <h2>Daily Compliance Summary</h2>
                <div style='font-size:14px; opacity:0.9;'>{Date}</div>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <div style='display:flex; gap:20px; flex-wrap:wrap; margin-bottom:20px;'>
                    <div style='background:#fff; padding:15px; border-radius:5px; flex:1; min-width:150px; text-align:center;'>
                        <div style='font-size:28px; font-weight:bold; color:#dc3545;'>{TotalViolations}</div>
                        <div style='font-size:12px; color:#666;'>Today's Violations</div>
                    </div>
                    <div style='background:#fff; padding:15px; border-radius:5px; flex:1; min-width:150px; text-align:center;'>
                        <div style='font-size:28px; font-weight:bold; color:#fd7e14;'>{CriticalViolations}</div>
                        <div style='font-size:12px; color:#666;'>Critical</div>
                    </div>
                    <div style='background:#fff; padding:15px; border-radius:5px; flex:1; min-width:150px; text-align:center;'>
                        <div style='font-size:28px; font-weight:bold; color:#28a745;'>{ActiveFrameworks}</div>
                        <div style='font-size:12px; color:#666;'>Active Frameworks</div>
                    </div>
                    <div style='background:#fff; padding:15px; border-radius:5px; flex:1; min-width:150px; text-align:center;'>
                        <div style='font-size:28px; font-weight:bold; color:#667eea;'>{PoliciesEvaluated}</div>
                        <div style='font-size:12px; color:#666;'>Policies Evaluated</div>
                    </div>
                </div>

                <div style='background:#fff; padding:15px; border-radius:5px; margin-bottom:15px;'>
                    <h3 style='margin-top:0;'>Active Frameworks</h3>
                    <p>{FrameworkList}</p>
                </div>

                <h3>Top Violations Today</h3>
                {TopViolations}

                <div style='margin: 20px 0;'>
                    <a href='[PORTAL_URL]/compliance'
                       style='background: #667eea; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                        View Full Report
                    </a>
                </div>
            </div>
        </body></html>";
    }

    private string GetFrameworkStatusChangeTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: #17a2b8; padding: 20px; color: white;'>
                <h2>Framework Status Changed</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>A compliance framework status has been updated.</p>

                <table style='width:100%; margin:15px 0; border-collapse:collapse;'>
                    <tr><td style='padding:5px 10px; font-weight:bold;'>Framework:</td><td style='padding:5px 10px;'>{FrameworkName}</td></tr>
                    <tr><td style='padding:5px 10px; font-weight:bold;'>Code:</td><td style='padding:5px 10px;'>{FrameworkCode}</td></tr>
                    <tr><td style='padding:5px 10px; font-weight:bold;'>Action:</td><td style='padding:5px 10px;'><strong>{Action}</strong></td></tr>
                    <tr><td style='padding:5px 10px; font-weight:bold;'>Changed At:</td><td style='padding:5px 10px;'>{ChangedAt}</td></tr>
                    <tr><td style='padding:5px 10px; font-weight:bold;'>Affected Policies:</td><td style='padding:5px 10px;'>{AffectedPolicies}</td></tr>
                </table>

                <p style='color:#666;'>{Description}</p>

                <div style='margin: 20px 0;'>
                    <a href='[PORTAL_URL]/compliance/frameworks'
                       style='background: #17a2b8; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                        View Frameworks
                    </a>
                </div>
            </div>
        </body></html>";
    }

    private string GetCredentialUsernameTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: linear-gradient(135deg, #10b981 0%, #059669 100%); padding: 20px; color: white;'>
                <h2>New Account Created</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>A new account has been provisioned for <strong>{{displayName}}</strong>.</p>

                <table style='width:100%; margin:15px 0; border-collapse:collapse; background:white; border-radius:5px;'>
                    <tr><td style='padding:10px 15px; font-weight:bold; border-bottom:1px solid #eee;'>Username:</td>
                        <td style='padding:10px 15px; border-bottom:1px solid #eee;'><code>{{samAccountName}}</code></td></tr>
                    <tr><td style='padding:10px 15px; font-weight:bold; border-bottom:1px solid #eee;'>User Principal Name:</td>
                        <td style='padding:10px 15px; border-bottom:1px solid #eee;'><code>{{upn}}</code></td></tr>
                    <tr><td style='padding:10px 15px; font-weight:bold;'>Status:</td>
                        <td style='padding:10px 15px;'>{{provisioningStatus}}</td></tr>
                </table>

                <p>The temporary password will be delivered in a separate email for security purposes.</p>
                <p style='color:#666; font-size:12px;'>This is an automated message from Identity Center.</p>
            </div>
        </body></html>";
    }

    private string GetCredentialPasswordTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%); padding: 20px; color: white;'>
                <h2>Temporary Password</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>A temporary password has been set for the newly provisioned account.</p>

                <div style='background:white; border:2px solid #f59e0b; border-radius:5px; padding:15px; margin:15px 0; text-align:center;'>
                    <p style='margin:0 0 5px 0; font-weight:bold; color:#92400e;'>Temporary Password</p>
                    <code style='font-size:18px; letter-spacing:1px;'>{{password}}</code>
                </div>

                <div style='background:#fef3c7; border-left:4px solid #f59e0b; padding:12px 15px; margin:15px 0;'>
                    <strong>Important:</strong>
                    <ul style='margin:5px 0 0 0; padding-left:20px;'>
                        <li>This password must be changed at first login.</li>
                        <li>Delete this email after the password has been used.</li>
                        <li>Do not forward this email to anyone.</li>
                    </ul>
                </div>

                <p style='color:#666; font-size:12px;'>This is an automated message from Identity Center. This password cannot be re-sent.</p>
            </div>
        </body></html>";
    }

    private string GetCredentialCombinedTemplate()
    {
        return @"<html><body style='font-family: Arial, sans-serif;'>
            <div style='background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); padding: 20px; color: white;'>
                <h2>Account Credentials for {{displayName}}</h2>
            </div>
            <div style='padding: 20px; background: #f5f5f5;'>
                <p>A new account has been provisioned. Below are the login credentials.</p>

                <table style='width:100%; margin:15px 0; border-collapse:collapse; background:white; border-radius:5px;'>
                    <tr><td style='padding:10px 15px; font-weight:bold; border-bottom:1px solid #eee;'>Username:</td>
                        <td style='padding:10px 15px; border-bottom:1px solid #eee;'><code>{{samAccountName}}</code></td></tr>
                    <tr><td style='padding:10px 15px; font-weight:bold; border-bottom:1px solid #eee;'>User Principal Name:</td>
                        <td style='padding:10px 15px; border-bottom:1px solid #eee;'><code>{{upn}}</code></td></tr>
                    <tr><td style='padding:10px 15px; font-weight:bold;'>Temporary Password:</td>
                        <td style='padding:10px 15px;'><code style='font-size:16px;'>{{password}}</code></td></tr>
                </table>

                <div style='background:#fef3c7; border-left:4px solid #f59e0b; padding:12px 15px; margin:15px 0;'>
                    <strong>Important:</strong>
                    <ul style='margin:5px 0 0 0; padding-left:20px;'>
                        <li>This password must be changed at first login.</li>
                        <li>Delete this email after the credentials have been recorded.</li>
                        <li>Do not forward this email to anyone.</li>
                    </ul>
                </div>

                <p style='color:#666; font-size:12px;'>This is an automated message from Identity Center. These credentials cannot be re-sent.</p>
            </div>
        </body></html>";
    }

    #endregion
}
