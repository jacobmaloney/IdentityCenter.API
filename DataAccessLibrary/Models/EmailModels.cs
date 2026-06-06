using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models
{
    /// <summary>
    /// SMTP Configuration Model
    /// Stores email server settings with encrypted credentials
    /// Based on proven IdentityServer implementation
    /// </summary>
    [Table("SMTPConfiguration")]
    public class SMTPConfiguration
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsDefault { get; set; } = true;
        public bool IsActive { get; set; } = true;

        // Server Settings (Encrypted in database)
        [Required]
        public string Server { get; set; } = string.Empty;  // smtp.gmail.com (encrypted)

        [Range(1, 65535)]
        public int Port { get; set; } = 587;

        public bool EnableSsl { get; set; } = true;

        // Authentication (Encrypted in database)
        [Required]
        public string Username { get; set; } = string.Empty;  // user@domain.com (encrypted)

        [Required]
        public string Password { get; set; } = string.Empty;  // password (encrypted)

        // Email Settings
        [Required]
        [StringLength(255)]
        [EmailAddress]
        public string FromAddress { get; set; } = "noreply@identitycenter.local";

        // Default fallback display name; admin overrides via SMTP configuration
        // page or branding settings.
        [StringLength(200)]
        public string? FromDisplayName { get; set; } = "Identity Center";

        [StringLength(255)]
        [EmailAddress]
        public string? ReplyToAddress { get; set; }

        [StringLength(200)]
        public string? ReplyToDisplayName { get; set; }

        // Audit Fields
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(255)]
        public string? CreatedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        [StringLength(255)]
        public string? ModifiedBy { get; set; }

        // Test Information
        public DateTime? LastTestDate { get; set; }
        public string? LastTestResult { get; set; }
        public bool? LastTestSuccess { get; set; }
    }

    /// <summary>
    /// Email Template Model
    /// Stores reusable HTML email templates for various notifications
    /// </summary>
    [Table("EmailTemplates")]
    public class EmailTemplate
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;  // REVIEW_ASSIGNED, REVIEW_DUE, etc.

        [Required]
        [StringLength(500)]
        public string Subject { get; set; } = string.Empty;  // "You have {Count} reviews pending"

        [Required]
        public string Body { get; set; } = string.Empty;  // HTML template with {variables}

        [StringLength(100)]
        public string? Category { get; set; }  // AccessReview, Workflow, System, etc.

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Whether this is a built-in system template (cannot be deleted, protected from modification)
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedAt { get; set; }
    }

    /// <summary>
    /// Email Queue Model
    /// Tracks emails to be sent (for background processing)
    /// </summary>
    [Table("EmailQueue")]
    public class EmailQueueItem
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(255)]
        public string ToAddress { get; set; } = string.Empty;

        [StringLength(200)]
        public string? ToDisplayName { get; set; }

        [Required]
        [StringLength(500)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        public bool IsHtml { get; set; } = true;

        // Status tracking
        public string Status { get; set; } = "Pending";  // Pending, Sending, Sent, Failed
        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;

        public DateTime? SentAt { get; set; }
        public string? ErrorMessage { get; set; }

        // Metadata
        public string? TemplateId { get; set; }
        public string? RelatedEntityType { get; set; }  // Assignment, Campaign, etc.
        public Guid? RelatedEntityId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
    }
    /// <summary>
    /// Teams Message Template Model
    /// Stores reusable Teams message templates for notifications
    /// Supports both plain text and Adaptive Card JSON formats
    /// </summary>
    [Table("TeamsMessageTemplates")]
    public class TeamsMessageTemplate
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;  // POLICY_VIOLATION, REVIEW_ASSIGNED, etc.

        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Plain text message template with {variables}
        /// Used when UseAdaptiveCard is false
        /// </summary>
        [Required]
        public string MessageTemplate { get; set; } = string.Empty;

        /// <summary>
        /// Whether to use Adaptive Card format instead of plain text
        /// </summary>
        public bool UseAdaptiveCard { get; set; } = false;

        /// <summary>
        /// Adaptive Card JSON template with {variables}
        /// Only used when UseAdaptiveCard is true
        /// </summary>
        public string? AdaptiveCardJson { get; set; }

        [StringLength(100)]
        public string? Category { get; set; }  // Compliance, AccessReview, Workflow, System, etc.

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Whether this is a built-in system template (cannot be deleted, protected from modification)
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedAt { get; set; }
    }

    /// <summary>
    /// Teams Message Queue Model
    /// Tracks Teams messages to be sent (for background processing)
    /// </summary>
    [Table("TeamsMessageQueue")]
    public class TeamsMessageQueueItem
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Target: User principal name, Teams channel webhook URL, or user email
        /// </summary>
        [Required]
        [StringLength(500)]
        public string Recipient { get; set; } = string.Empty;

        /// <summary>
        /// Recipient type: User, Channel, Webhook
        /// </summary>
        [Required]
        [StringLength(50)]
        public string RecipientType { get; set; } = "User";

        /// <summary>
        /// Message content (plain text or rendered Adaptive Card JSON)
        /// </summary>
        [Required]
        public string MessageContent { get; set; } = string.Empty;

        /// <summary>
        /// Whether the message content is Adaptive Card JSON
        /// </summary>
        public bool IsAdaptiveCard { get; set; } = false;

        // Status tracking
        [StringLength(50)]
        public string Status { get; set; } = "Pending";  // Pending, Sending, Sent, Failed

        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;

        public DateTime? SentAt { get; set; }
        public string? ErrorMessage { get; set; }

        // Metadata
        public Guid? TemplateId { get; set; }
        public string? RelatedEntityType { get; set; }  // PolicyViolation, Assignment, Campaign, etc.
        public Guid? RelatedEntityId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
    }
}
