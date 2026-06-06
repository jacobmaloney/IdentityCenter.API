using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Service interface for sending emails with template support
/// Integrates with SMTP configuration from database
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email using the default SMTP configuration
    /// </summary>
    Task<bool> SendEmailAsync(string toAddress, string subject, string body, bool isHtml = true);

    /// <summary>
    /// Sends an email using a specific SMTP configuration
    /// </summary>
    Task<bool> SendEmailAsync(Guid smtpConfigId, string toAddress, string subject, string body, bool isHtml = true);

    /// <summary>
    /// Sends an email using a template with variable substitution
    /// </summary>
    Task<bool> SendTemplatedEmailAsync(string templateName, string toAddress, Dictionary<string, string> variables);

    /// <summary>
    /// Queues an email for background processing with retry logic
    /// </summary>
    Task<Guid> QueueEmailAsync(string toAddress, string subject, string body, string? templateId = null,
        string? relatedEntityType = null, Guid? relatedEntityId = null);

    /// <summary>
    /// Queues an email using a template with variable substitution for background processing
    /// </summary>
    /// <param name="templateName">Name of the email template to use</param>
    /// <param name="toAddress">Recipient email address (null = use admin emails from config)</param>
    /// <param name="subject">Email subject line</param>
    /// <param name="variables">Template variables for substitution</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Queue item ID</returns>
    Task<Guid> QueueEmailAsync(string templateName, string? toAddress, string subject,
        Dictionary<string, string> variables, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes pending emails in the queue
    /// </summary>
    Task ProcessEmailQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests SMTP configuration by sending a test email
    /// </summary>
    Task<(bool Success, string Message)> TestSmtpConfigurationAsync(Guid smtpConfigId, string testEmailAddress);

    /// <summary>
    /// Tests SMTP configuration without saving it to database
    /// </summary>
    Task<(bool Success, string Message)> TestSmtpConfigurationAsync(SMTPConfiguration config, string testEmailAddress);

    /// <summary>
    /// Gets an email template by name
    /// </summary>
    Task<EmailTemplate?> GetTemplateAsync(string templateName);

    /// <summary>
    /// Creates default access review email templates if they don't exist
    /// </summary>
    Task SeedDefaultTemplatesAsync();
    /// <summary>
    /// Sends assignment emails for an access review campaign to all reviewers
    /// </summary>
    Task<int> SendCampaignAssignmentEmailsAsync(Guid campaignId, List<AccessReviewAssignment> assignments);
}
