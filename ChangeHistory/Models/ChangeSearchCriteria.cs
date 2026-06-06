namespace ChangeHistory.Models;

/// <summary>
/// Search/filter DTO for querying change history.
/// </summary>
public class ChangeSearchCriteria
{
    public Guid? EntityId { get; set; }
    public string? EntityType { get; set; }
    public string? UserId { get; set; }
    public ChangeOperationType? OperationType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? PropertyName { get; set; }
    public bool? SuccessOnly { get; set; }
    public string? Source { get; set; }
    public Guid? CorrelationId { get; set; }
    public int Limit { get; set; } = 100;
    public int Offset { get; set; } = 0;
}
