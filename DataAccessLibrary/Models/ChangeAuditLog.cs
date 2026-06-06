using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DataAccessLibrary.Services;

namespace DataAccessLibrary.Models
{
    /// <summary>
    /// Detailed audit log entry for tracking changes to directory objects.
    /// Captures Who, What, When, and Why for every change.
    /// </summary>
    [Table("ChangeAuditLogs")]
    public class ChangeAuditLog
    {
        public long Id { get; set; }

        // WHEN - Timestamp of the change
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // WHO - Identity of the person making the change
        [MaxLength(256)]
        public string? UserId { get; set; }

        [MaxLength(256)]
        public string? UserDisplayName { get; set; }

        [MaxLength(256)]
        public string? UserEmail { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        // WHAT - Details of the change
        public ChangeOperationType OperationType { get; set; }

        [MaxLength(50)]
        public string? EntityType { get; set; }  // Object, Person, Group, Identity

        public Guid? EntityId { get; set; }

        [MaxLength(256)]
        public string? EntityDisplayName { get; set; }

        [MaxLength(100)]
        public string? PropertyName { get; set; }  // Specific property changed (e.g., "displayName", "email")

        [MaxLength(2000)]
        public string? OldValue { get; set; }

        [MaxLength(2000)]
        public string? NewValue { get; set; }

        // For group membership and relationship changes
        public Guid? RelatedEntityId { get; set; }

        [MaxLength(256)]
        public string? RelatedEntityName { get; set; }

        // WHY - Reason for the change (for approval workflows)
        [MaxLength(500)]
        public string? Reason { get; set; }

        [MaxLength(100)]
        public string? TicketNumber { get; set; }  // ServiceNow, Jira, etc.

        public Guid? ApprovedBy { get; set; }

        [MaxLength(256)]
        public string? ApproverName { get; set; }

        // Result
        public bool Success { get; set; } = true;

        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        // Correlation for related changes (batch operations)
        public Guid? CorrelationId { get; set; }

        [MaxLength(50)]
        public string? Source { get; set; }  // "ChatUI", "SyncEngine", "API", etc.

        // WHO-on-behalf — when a system/automated actor performs a write authorized
        // by a human reviewer, UserId stays "system" and these capture the human.
        [MaxLength(256)]
        public string? OnBehalfOfUserId { get; set; }

        [MaxLength(256)]
        public string? OnBehalfOfDisplayName { get; set; }

        /// <summary>
        /// Convert to ChangeAuditEntry DTO
        /// </summary>
        public ChangeAuditEntry ToEntry()
        {
            return new ChangeAuditEntry
            {
                Id = Id,
                Timestamp = Timestamp,
                UserId = UserId,
                UserDisplayName = UserDisplayName,
                UserEmail = UserEmail,
                IpAddress = IpAddress,
                OperationType = OperationType,
                EntityType = EntityType,
                EntityId = EntityId,
                EntityDisplayName = EntityDisplayName,
                PropertyName = PropertyName,
                OldValue = OldValue,
                NewValue = NewValue,
                RelatedEntityId = RelatedEntityId,
                RelatedEntityName = RelatedEntityName,
                Reason = Reason,
                TicketNumber = TicketNumber,
                ApprovedBy = ApprovedBy,
                ApproverName = ApproverName,
                Success = Success,
                ErrorMessage = ErrorMessage,
                CorrelationId = CorrelationId,
                Source = Source,
                OnBehalfOfUserId = OnBehalfOfUserId,
                OnBehalfOfDisplayName = OnBehalfOfDisplayName
            };
        }

        /// <summary>
        /// Create from ChangeAuditEntry DTO
        /// </summary>
        public static ChangeAuditLog FromEntry(ChangeAuditEntry entry)
        {
            return new ChangeAuditLog
            {
                Timestamp = entry.Timestamp,
                UserId = entry.UserId,
                UserDisplayName = entry.UserDisplayName,
                UserEmail = entry.UserEmail,
                IpAddress = entry.IpAddress,
                OperationType = entry.OperationType,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                EntityDisplayName = entry.EntityDisplayName,
                PropertyName = entry.PropertyName,
                OldValue = TruncateIfNeeded(entry.OldValue, 2000),
                NewValue = TruncateIfNeeded(entry.NewValue, 2000),
                RelatedEntityId = entry.RelatedEntityId,
                RelatedEntityName = entry.RelatedEntityName,
                Reason = entry.Reason,
                TicketNumber = entry.TicketNumber,
                ApprovedBy = entry.ApprovedBy,
                ApproverName = entry.ApproverName,
                Success = entry.Success,
                ErrorMessage = TruncateIfNeeded(entry.ErrorMessage, 1000),
                CorrelationId = entry.CorrelationId,
                Source = entry.Source,
                OnBehalfOfUserId = entry.OnBehalfOfUserId,
                OnBehalfOfDisplayName = entry.OnBehalfOfDisplayName
            };
        }

        private static string? TruncateIfNeeded(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length > maxLength ? value.Substring(0, maxLength - 3) + "..." : value;
        }
    }
}
