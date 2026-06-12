namespace DataAccessLibrary.Models;

/// <summary>
/// A queued command for a remote scan agent (e.g. Conduit's SQL Discovery poller).
/// Lifecycle: Pending -> Acked (agent claimed it) -> Completed / Failed.
/// Schema: V138 AgentCommands + V140 targeting (TargetAgentId NULL = legacy broadcast).
/// </summary>
public class AgentCommand
{
    public Guid Id { get; set; }
    public string CommandType { get; set; } = "";
    public string? PayloadJson { get; set; }
    public string Status { get; set; } = "Pending";
    public string? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? AckedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool? Success { get; set; }
    public string? ResultMessage { get; set; }

    /// <summary>Registered agent this command is addressed to. NULL = legacy untargeted broadcast.</summary>
    public Guid? TargetAgentId { get; set; }

    /// <summary>Agent that won the atomic claim (set by ClaimAsync).</summary>
    public Guid? ClaimedByAgentId { get; set; }

    public int AttemptCount { get; set; }
}
