namespace DataAccessLibrary.Services;

/// <summary>
/// Bridge interface for process approval operations.
/// Defined in DataAccessLibrary so ApprovalRepository can call into Processes project
/// without creating a direct project reference (keeps dependency direction clean).
/// Implemented by ProcessApprovalService in the Processes project.
/// </summary>
public interface IProcessApprovalService
{
    /// <summary>
    /// Resume a paused process instance after approval/denial decision.
    /// </summary>
    Task ResumeProcessAsync(Guid instanceId, string? approvedBy, string? comments, bool approved, CancellationToken ct = default);

    /// <summary>
    /// Get process instance details for the approval details view.
    /// Returns a ProcessInstanceInfo with step logs and workflow info.
    /// </summary>
    Task<ProcessInstanceInfo?> GetInstanceDetailsAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// Update the ApproverId on a process instance (for delegation).
    /// </summary>
    Task SetApproverIdAsync(Guid instanceId, string approverId, CancellationToken ct = default);
}

/// <summary>
/// Process instance details for the approval inbox detail view.
/// </summary>
public class ProcessInstanceInfo
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public string? WorkflowName { get; set; }
    public string? WorkflowDescription { get; set; }
    public string? TargetEntityName { get; set; }
    public string? TargetEntityType { get; set; }
    public Guid? TargetEntityId { get; set; }
    public string? Status { get; set; }
    public string? CurrentNodeName { get; set; }
    public string? ApproverId { get; set; }
    public string? WaitCondition { get; set; }
    public DateTime StartedAt { get; set; }
    public List<ProcessStepInfo> Steps { get; set; } = new();
    public List<ProcessNodePreview> RemainingNodes { get; set; } = new();
}

/// <summary>
/// A single step log entry for process step viewer.
/// </summary>
public class ProcessStepInfo
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public string? NodeType { get; set; }
    public string? NodeName { get; set; }
    public string? Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovalComments { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Preview of a remaining workflow node (future steps).
/// </summary>
public class ProcessNodePreview
{
    public Guid Id { get; set; }
    public string? NodeName { get; set; }
    public string? NodeType { get; set; }
    public int SortOrder { get; set; }
}
