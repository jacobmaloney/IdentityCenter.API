using System;
using System.ComponentModel.DataAnnotations;

namespace DataAccessLibrary.Models
{
    /// <summary>
    /// Represents a persistent admin notification/message for the admin chat feed.
    /// These messages persist even when admins are offline and can be viewed historically.
    /// </summary>
    public class AdminNotification
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Notification type: PolicyViolation, SystemAlert, SyncStatus, AccessReview, Workflow, Info, Warning, Error
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string NotificationType { get; set; } = "Info";

        /// <summary>
        /// Category for grouping: Compliance, Sync, Security, System, AccessReview, Workflow
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = "System";

        /// <summary>
        /// Severity level: Critical, High, Medium, Low, Info
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Severity { get; set; } = "Info";

        /// <summary>
        /// Short title/summary of the notification
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Detailed message content (supports markdown)
        /// </summary>
        [Required]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Optional link to related entity or action page
        /// </summary>
        [MaxLength(500)]
        public string? ActionUrl { get; set; }

        /// <summary>
        /// Optional action button text
        /// </summary>
        [MaxLength(50)]
        public string? ActionText { get; set; }

        /// <summary>
        /// Related entity ID (e.g., ViolationId, SyncProjectId, etc.)
        /// </summary>
        public Guid? RelatedEntityId { get; set; }

        /// <summary>
        /// Related entity type (e.g., PolicyViolation, SyncProject, AccessReview)
        /// </summary>
        [MaxLength(50)]
        public string? RelatedEntityType { get; set; }

        /// <summary>
        /// Source system that created the notification
        /// </summary>
        [MaxLength(100)]
        public string Source { get; set; } = "System";

        /// <summary>
        /// When the notification was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Has any admin read/acknowledged this notification
        /// </summary>
        public bool IsRead { get; set; } = false;

        /// <summary>
        /// When the notification was first read
        /// </summary>
        public DateTime? ReadAt { get; set; }

        /// <summary>
        /// Which user first read the notification
        /// </summary>
        [MaxLength(256)]
        public string? ReadBy { get; set; }

        /// <summary>
        /// Is this notification dismissed/archived
        /// </summary>
        public bool IsDismissed { get; set; } = false;

        /// <summary>
        /// When the notification was dismissed
        /// </summary>
        public DateTime? DismissedAt { get; set; }

        /// <summary>
        /// Additional metadata as JSON
        /// </summary>
        public string? Metadata { get; set; }
    }
}
