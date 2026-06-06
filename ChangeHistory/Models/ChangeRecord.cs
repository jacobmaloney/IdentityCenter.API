namespace ChangeHistory.Models;

/// <summary>
/// Single model that maps directly to the ChangeAuditLogs table.
/// Replaces the redundant ChangeAuditLog (EF) + ChangeAuditEntry (DTO) pair.
/// </summary>
public class ChangeRecord
{
    public long Id { get; set; }

    // WHEN
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // WHO
    public string? UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public string? UserEmail { get; set; }
    public string? IpAddress { get; set; }

    // WHAT
    public ChangeOperationType OperationType { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? EntityDisplayName { get; set; }
    public string? PropertyName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    // Related entity (group membership, campaigns, etc.)
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityName { get; set; }

    // WHY
    public string? Reason { get; set; }
    public string? TicketNumber { get; set; }
    public Guid? ApprovedBy { get; set; }
    public string? ApproverName { get; set; }

    // Result
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }

    // Correlation
    public Guid? CorrelationId { get; set; }
    public string? Source { get; set; }

    // WHO-on-behalf — when a system/automated actor performs a write authorized
    // by a human reviewer, UserId stays "system" and these capture the human.
    public string? OnBehalfOfUserId { get; set; }
    public string? OnBehalfOfDisplayName { get; set; }
}
