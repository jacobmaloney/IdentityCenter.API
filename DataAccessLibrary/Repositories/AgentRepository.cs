using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class AgentRepository : DapperRepositoryBase, IAgentRepository
{
    public AgentRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public async Task<RemoteAgent?> GetAgentByIdAsync(Guid agentId)
    {
        const string sql = @"
            SELECT
                Id, AgentName, Description, MachineName, IpAddress, Version,
                OperatingSystem, Status, SupportedJobTypes, MaxConcurrentJobs,
                CurrentJobCount, LastHeartbeat, LastJobClaimed, LastJobCompleted,
                TotalJobsProcessed, TotalJobsFailed, IsEnabled, RegisteredAt,
                ConfigUpdatedAt, ConfigurationJson, Tags, Priority
            FROM RemoteAgents
            WHERE Id = @AgentId
        ";

        return await ExecuteAsync(async connection =>
            await connection.QuerySingleOrDefaultAsync<RemoteAgent>(sql, new { AgentId = agentId }));
    }

    public async Task<List<RemoteAgentStatus>> GetAgentStatusesAsync()
    {
        const string sql = @"
            SELECT
                Id, AgentName, MachineName, Status, Version,
                LastHeartbeat, CurrentJobCount, MaxConcurrentJobs,
                TotalJobsProcessed, TotalJobsFailed, IsEnabled
            FROM RemoteAgents
            ORDER BY LastHeartbeat DESC
        ";

        return await ExecuteAsync(async connection =>
        {
            var agents = await connection.QueryAsync<RemoteAgent>(sql);

            return agents.Select(a => new RemoteAgentStatus
            {
                Id = a.Id,
                AgentName = a.AgentName,
                MachineName = a.MachineName,
                Status = a.Status,
                Version = a.Version,
                LastHeartbeat = a.LastHeartbeat,
                CurrentJobCount = a.CurrentJobCount,
                MaxConcurrentJobs = a.MaxConcurrentJobs,
                TotalJobsProcessed = a.TotalJobsProcessed,
                TotalJobsFailed = a.TotalJobsFailed,
                SuccessRate = a.TotalJobsProcessed > 0
                    ? (double)(a.TotalJobsProcessed - a.TotalJobsFailed) / a.TotalJobsProcessed * 100
                    : 100,
                IsEnabled = a.IsEnabled
            }).ToList();
        });
    }

    public async Task<Guid> RegisterAgentAsync(RemoteAgent agent, string apiKeyHash)
    {
        // The agent's API key hash is stored in the NOT-NULL ApiKeyHash column.
        // Without this the INSERT throws on a fresh DB (the column has no default).
        // The plaintext key is generated and returned once by the caller; only the
        // hash ever reaches the database.
        agent.ApiKeyHash = apiKeyHash;

        const string checkSql = "SELECT Id FROM RemoteAgents WHERE AgentName = @AgentName";

        return await ExecuteAsync(async connection =>
        {
            var existingId = await connection.QuerySingleOrDefaultAsync<Guid?>(checkSql, new { agent.AgentName });

            if (existingId.HasValue)
            {
                const string updateSql = @"
                    UPDATE RemoteAgents
                    SET
                        Description = @Description,
                        MachineName = @MachineName,
                        IpAddress = @IpAddress,
                        Version = @Version,
                        OperatingSystem = @OperatingSystem,
                        Status = 'Online',
                        SupportedJobTypes = @SupportedJobTypes,
                        MaxConcurrentJobs = @MaxConcurrentJobs,
                        LastHeartbeat = GETUTCDATE(),
                        ConfigurationJson = @ConfigurationJson,
                        Tags = @Tags,
                        Priority = @Priority,
                        ApiKeyHash = @ApiKeyHash
                    WHERE Id = @Id
                ";

                agent.Id = existingId.Value;
                await connection.ExecuteAsync(updateSql, agent);

                _logger.LogInformation("Agent re-registered: {AgentName} ({AgentId})", agent.AgentName, agent.Id);
                return agent.Id;
            }

            // Preserve a caller-assigned Id (the registration endpoint assigns it
            // up front so the agent's API key can be linked to this agent before
            // the row is written). Fall back to a fresh Guid only if unset.
            if (agent.Id == Guid.Empty)
                agent.Id = Guid.NewGuid();
            agent.RegisteredAt = DateTime.UtcNow;
            agent.Status = "Online";
            agent.LastHeartbeat = DateTime.UtcNow;

            const string insertSql = @"
                INSERT INTO RemoteAgents (
                    Id, AgentName, Description, MachineName, IpAddress, Version,
                    OperatingSystem, Status, SupportedJobTypes, MaxConcurrentJobs,
                    CurrentJobCount, LastHeartbeat, TotalJobsProcessed, TotalJobsFailed,
                    IsEnabled, RegisteredAt, ConfigurationJson, Tags, Priority, ApiKeyHash
                )
                VALUES (
                    @Id, @AgentName, @Description, @MachineName, @IpAddress, @Version,
                    @OperatingSystem, @Status, @SupportedJobTypes, @MaxConcurrentJobs,
                    0, @LastHeartbeat, 0, 0,
                    @IsEnabled, @RegisteredAt, @ConfigurationJson, @Tags, @Priority, @ApiKeyHash
                )
            ";

            await connection.ExecuteAsync(insertSql, agent);

            _logger.LogInformation("New agent registered: {AgentName} ({AgentId})", agent.AgentName, agent.Id);
            return agent.Id;
        });
    }

    public async Task UpdateHeartbeatAsync(AgentHeartbeat heartbeat)
    {
        const string sql = @"
            UPDATE RemoteAgents
            SET
                Status = @Status,
                CurrentJobCount = @CurrentJobCount,
                LastHeartbeat = GETUTCDATE()
            WHERE Id = @AgentId
        ";

        await ExecuteNonQueryAsync(async connection =>
            await connection.ExecuteAsync(sql, new
            {
                heartbeat.AgentId,
                heartbeat.Status,
                heartbeat.CurrentJobCount
            }));
    }

    public async Task<bool> UpdateAgentStatusAsync(Guid agentId, string status)
    {
        const string sql = @"
            UPDATE RemoteAgents
            SET Status = @Status
            WHERE Id = @AgentId
        ";

        var affected = await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(sql, new { AgentId = agentId, Status = status }));

        return affected > 0;
    }

    public async Task<List<RemoteAgent>> GetOnlineAgentsAsync()
    {
        const string sql = @"
            SELECT
                Id, AgentName, Description, MachineName, IpAddress, Version,
                OperatingSystem, Status, SupportedJobTypes, MaxConcurrentJobs,
                CurrentJobCount, LastHeartbeat, LastJobClaimed, LastJobCompleted,
                TotalJobsProcessed, TotalJobsFailed, IsEnabled, RegisteredAt,
                ConfigUpdatedAt, ConfigurationJson, Tags, Priority
            FROM RemoteAgents
            WHERE IsEnabled = 1
              AND LastHeartbeat >= DATEADD(MINUTE, -5, GETUTCDATE())
            ORDER BY Priority DESC, LastHeartbeat DESC
        ";

        return await ExecuteAsync(async connection =>
        {
            var agents = await connection.QueryAsync<RemoteAgent>(sql);
            return agents.ToList();
        });
    }
}
