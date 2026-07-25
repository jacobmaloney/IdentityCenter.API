using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Queue of commands for remote scan agents (V138 AgentCommands + V140 targeting).
/// IC writes, agents consume via /api/agent/commands.
///
/// Two consumption paths:
///   - Targeted (per-agent key): <see cref="ClaimAsync"/> atomically claims rows
///     addressed to that agent, then <see cref="CompleteClaimedAsync"/>.
///   - Legacy untargeted (shared key, TargetAgentId IS NULL rows only):
///     <see cref="GetPendingAsync"/> + <see cref="AckAsync"/> + <see cref="CompleteAsync"/>.
/// </summary>
public interface IAgentCommandRepository
{
    /// <summary>
    /// Queues a command. PayloadJson is capped at 64KB. A null
    /// <paramref name="targetAgentId"/> (legacy broadcast) is rejected once at
    /// least one active agent is registered — targeted commands only from then on.
    /// </summary>
    Task<Guid> CreateAsync(string commandType, string? payloadJson, string? requestedBy, Guid? targetAgentId = null);

    Task<AgentCommand?> GetByIdAsync(Guid id);

    /// <summary>Legacy view: pending UNTARGETED commands only, oldest first, capped.</summary>
    Task<List<AgentCommand>> GetPendingAsync(int max = 10);

    /// <summary>
    /// Atomically claims up to <paramref name="max"/> pending commands targeted at
    /// <paramref name="agentId"/> (Pending -> Acked, ClaimedByAgentId stamped,
    /// AttemptCount incremented). Single-statement UPDATE..OUTPUT so two pollers
    /// can never claim the same row.
    /// </summary>
    Task<List<AgentCommand>> ClaimAsync(Guid agentId, int max = 10);

    /// <summary>
    /// Legacy claim of an UNTARGETED command (Pending -> Acked). True ONLY when this
    /// call performed the transition; a lost race or unknown id returns false.
    /// </summary>
    Task<bool> AckAsync(Guid id);

    /// <summary>
    /// Completes a command claimed via <see cref="ClaimAsync"/>. Guarded:
    /// only the claiming agent, only from Acked. False = no transition (uniform 404).
    /// The affected-row count IS the idempotency guard: a duplicate delivery transitions 0 rows.
    /// <paramref name="resultJson"/> (V167) is optional structured, UNTRUSTED agent output.
    /// </summary>
    Task<bool> CompleteClaimedAsync(Guid id, Guid agentId, bool success, string? message, string? resultJson = null);

    /// <summary>
    /// Legacy complete: UNTARGETED rows only, only from Acked. False = no transition.
    /// </summary>
    Task<bool> CompleteAsync(Guid id, bool success, string? message, string? resultJson = null);

    /// <summary>
    /// Atomically cancels a command ONLY if it is still Pending (Pending -> Cancelled). A claimed /
    /// in-flight command is NEVER cancelled — the agent may be mid-create. True only when this call
    /// performed the transition; the affected-row count is the safety guard against the timeout-race
    /// orphan (failure #2).
    /// </summary>
    Task<bool> CancelIfPendingAsync(Guid id);

    /// <summary>True when any command has ever been acked — cheap "an agent has connected" signal.</summary>
    Task<bool> AnyAckedAsync();
}
