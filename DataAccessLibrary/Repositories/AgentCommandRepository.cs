using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper implementation of <see cref="IAgentCommandRepository"/>. Schema managed
/// by the V138 + V140 migrations. Tenant routing is implicit via <see cref="DapperRepositoryBase"/>.
/// </summary>
public class AgentCommandRepository : DapperRepositoryBase, IAgentCommandRepository
{
    private const int MaxPayloadBytes = 64 * 1024;
    // ResultJson (untrusted agent output) gets the same 64KB bound as the inbound payload.
    private const int MaxResultJsonBytes = 64 * 1024;

    private const string SelectColumns =
        "Id, CommandType, PayloadJson, Status, RequestedBy, RequestedAt, AckedAt, CompletedAt, Success, ResultMessage, ResultJson, " +
        "TargetAgentId, ClaimedByAgentId, AttemptCount";

    public AgentCommandRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public Task<Guid> CreateAsync(string commandType, string? payloadJson, string? requestedBy, Guid? targetAgentId = null)
        => ExecuteAsync(async conn =>
        {
            if (payloadJson is not null && System.Text.Encoding.UTF8.GetByteCount(payloadJson) > MaxPayloadBytes)
                throw new ArgumentException($"PayloadJson exceeds the {MaxPayloadBytes / 1024}KB limit.", nameof(payloadJson));

            if (targetAgentId is null)
            {
                // Once at least one active agent is registered, broadcasts are dead:
                // every new command must name its target. Enforced here so EVERY
                // create path (UI, API, jobs) gets the rule.
                var anyActiveAgent = await conn.ExecuteScalarAsync<int>(
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM Agents WHERE IsActive = 1) THEN 1 ELSE 0 END;") == 1;
                if (anyActiveAgent)
                    throw new InvalidOperationException(
                        "Untargeted agent commands are not allowed once an active agent is registered. Specify a target agent.");
            }

            var id = Guid.NewGuid();
            await conn.ExecuteAsync(@"
                INSERT INTO AgentCommands (Id, CommandType, PayloadJson, Status, RequestedBy, RequestedAt, TargetAgentId)
                VALUES (@Id, @CommandType, @PayloadJson, 'Pending', @RequestedBy, SYSUTCDATETIME(), @TargetAgentId);",
                new { Id = id, CommandType = commandType, PayloadJson = payloadJson, RequestedBy = requestedBy, TargetAgentId = targetAgentId });

            _logger.LogInformation("AgentCommands: created {CommandType} command {Id} (target agent: {TargetAgentId}) by {RequestedBy}",
                commandType, id, targetAgentId?.ToString() ?? "none/broadcast", requestedBy ?? "unknown");
            return id;
        });

    public Task<AgentCommand?> GetByIdAsync(Guid id)
        => ExecuteAsync(conn => conn.QuerySingleOrDefaultAsync<AgentCommand>(
            $"SELECT {SelectColumns} FROM AgentCommands WHERE Id = @Id;", new { Id = id }));

    public Task<List<AgentCommand>> GetPendingAsync(int max = 10)
        => ExecuteAsync(async conn =>
        {
            var rows = await conn.QueryAsync<AgentCommand>($@"
                SELECT TOP (@Max) {SelectColumns}
                FROM AgentCommands
                WHERE Status = 'Pending' AND TargetAgentId IS NULL
                ORDER BY RequestedAt ASC;",
                new { Max = max });
            return rows.ToList();
        });

    public Task<List<AgentCommand>> ClaimAsync(Guid agentId, int max = 10)
        => ExecuteAsync(async conn =>
        {
            // ONE statement: the CTE scopes only to the UPDATE that follows it, and
            // ROWLOCK/UPDLOCK/READPAST make concurrent claimers skip each other's
            // rows instead of double-claiming or blocking.
            var rows = await conn.QueryAsync<AgentCommand>(@"
                WITH next AS (
                    SELECT TOP (@Max) Id, Status, AckedAt, ClaimedByAgentId, AttemptCount, CommandType, PayloadJson
                    FROM AgentCommands WITH (ROWLOCK, UPDLOCK, READPAST)
                    WHERE Status = 'Pending' AND TargetAgentId = @AgentId
                    ORDER BY RequestedAt ASC
                )
                UPDATE next
                SET Status = 'Acked', AckedAt = SYSUTCDATETIME(), ClaimedByAgentId = @AgentId, AttemptCount = AttemptCount + 1
                OUTPUT inserted.Id, inserted.CommandType, inserted.PayloadJson;",
                new { AgentId = agentId, Max = max });
            return rows.ToList();
        });

    public Task<bool> AckAsync(Guid id)
        => ExecuteAsync(async conn =>
        {
            // Success ONLY when this caller performed the Pending -> Acked transition.
            // The old "row exists" fallback let two pollers both believe they owned
            // the command and execute it twice. Legacy path: untargeted rows only.
            var updated = await conn.ExecuteAsync(@"
                UPDATE AgentCommands
                SET Status = 'Acked', AckedAt = SYSUTCDATETIME(), AttemptCount = AttemptCount + 1
                WHERE Id = @Id AND Status = 'Pending' AND TargetAgentId IS NULL;",
                new { Id = id });
            return updated > 0;
        });

    public Task<bool> CompleteClaimedAsync(Guid id, Guid agentId, bool success, string? message, string? resultJson = null)
        => ExecuteAsync(async conn =>
        {
            var truncated = message is { Length: > 2000 } ? message[..2000] : message;
            var boundedResult = BoundResultJson(resultJson);
            var updated = await conn.ExecuteAsync(@"
                UPDATE AgentCommands
                SET Status = @Status,
                    CompletedAt = SYSUTCDATETIME(),
                    Success = @Success,
                    ResultMessage = @Message,
                    ResultJson = @ResultJson
                WHERE Id = @Id AND ClaimedByAgentId = @AgentId AND Status = 'Acked';",
                new { Id = id, AgentId = agentId, Status = success ? "Completed" : "Failed", Success = success, Message = truncated, ResultJson = boundedResult });
            return updated > 0;
        });

    public Task<bool> CompleteAsync(Guid id, bool success, string? message, string? resultJson = null)
        => ExecuteAsync(async conn =>
        {
            var truncated = message is { Length: > 2000 } ? message[..2000] : message;
            var boundedResult = BoundResultJson(resultJson);
            var updated = await conn.ExecuteAsync(@"
                UPDATE AgentCommands
                SET Status = @Status,
                    CompletedAt = SYSUTCDATETIME(),
                    Success = @Success,
                    ResultMessage = @Message,
                    ResultJson = @ResultJson
                WHERE Id = @Id AND TargetAgentId IS NULL AND Status = 'Acked';",
                new { Id = id, Status = success ? "Completed" : "Failed", Success = success, Message = truncated, ResultJson = boundedResult });
            return updated > 0;
        });

    public Task<bool> CancelIfPendingAsync(Guid id)
        => ExecuteAsync(async conn =>
        {
            // ONLY from Pending — a Claimed/Acked/in-flight command is never cancelled (the agent may
            // be mid-create). The affected-row count is the timeout-race guard (failure #2).
            var updated = await conn.ExecuteAsync(@"
                UPDATE AgentCommands
                SET Status = 'Cancelled', CompletedAt = SYSUTCDATETIME()
                WHERE Id = @Id AND Status = 'Pending';",
                new { Id = id });
            return updated > 0;
        });

    // Reject an oversized result rather than truncate structured JSON into an unparseable fragment.
    private static string? BoundResultJson(string? resultJson)
    {
        if (resultJson is null)
            return null;
        if (System.Text.Encoding.UTF8.GetByteCount(resultJson) > MaxResultJsonBytes)
            throw new ArgumentException($"ResultJson exceeds the {MaxResultJsonBytes / 1024}KB limit.", nameof(resultJson));
        return resultJson;
    }

    public Task<bool> AnyAckedAsync()
        => ExecuteAsync(async conn =>
            await conn.ExecuteScalarAsync<int>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM AgentCommands WHERE AckedAt IS NOT NULL) THEN 1 ELSE 0 END;") == 1);
}
