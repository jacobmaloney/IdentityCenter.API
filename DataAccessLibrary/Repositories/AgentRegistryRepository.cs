using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper implementation of <see cref="IAgentRegistryRepository"/>. Schema managed
/// by the V140 migration.
/// </summary>
public class AgentRegistryRepository : DapperRepositoryBase, IAgentRegistryRepository
{
    private const string SelectColumns =
        "Id, Name, Location, Capabilities, Version, LastSeenAt, LastSeenFromIp, IsActive, CreatedAt";

    public AgentRegistryRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public Task<Guid> CreateAsync(string name, string? location)
        => ExecuteAsync(async conn =>
        {
            var id = Guid.NewGuid();
            await conn.ExecuteAsync(@"
                INSERT INTO Agents (Id, Name, Location, IsActive, CreatedAt)
                VALUES (@Id, @Name, @Location, 1, SYSUTCDATETIME());",
                new { Id = id, Name = name, Location = location });
            _logger.LogInformation("Agent registered: {Name} ({AgentId})", name, id);
            return id;
        });

    public Task<Agent> CreateOrGetWithIdAsync(Guid id, string name, string? location, string? capabilities, bool active = false)
        => ExecuteAsync(async conn =>
        {
            // Idempotent, keyed on the caller-supplied Id (a Conduit instance GUID). Uses the
            // same INSERT...WHERE NOT EXISTS guard as the ObjectsController auto-register, so
            // both registration orders converge to ONE row keyed on Id with the same trust
            // columns (Id, IsActive=0): if a bulk push already auto-registered this id, we
            // insert nothing and the SELECT below returns that existing row untouched — its
            // IsActive and Name are preserved. Unlike the bulk path this is enrollment, not a
            // liveness signal, so the existing-row branch intentionally does NOT refresh
            // LastSeenAt/Version (the two paths' INSERT column sets are not identical).
            var inserted = await conn.ExecuteAsync(@"
                INSERT INTO Agents (Id, Name, Location, Capabilities, IsActive, CreatedAt)
                SELECT @Id, @Name, @Location, @Capabilities, @IsActive, SYSUTCDATETIME()
                WHERE NOT EXISTS (SELECT 1 FROM Agents WHERE Id = @Id);",
                new { Id = id, Name = name, Location = location, Capabilities = capabilities, IsActive = active });

            if (inserted > 0)
                _logger.LogInformation("Agent registered with caller-supplied id: {Name} ({AgentId}), IsActive={IsActive}", name, id, active);
            else
                _logger.LogInformation("Agent {AgentId} already registered — returning existing row unchanged", id);

            return await conn.QuerySingleAsync<Agent>(
                $"SELECT {SelectColumns} FROM Agents WHERE Id = @Id;", new { Id = id });
        });

    public Task<Agent?> GetByIdAsync(Guid id)
        => ExecuteAsync(conn => conn.QuerySingleOrDefaultAsync<Agent>(
            $"SELECT {SelectColumns} FROM Agents WHERE Id = @Id;", new { Id = id }));

    public Task<List<Agent>> GetAllAsync()
        => ExecuteAsync(async conn =>
        {
            var rows = await conn.QueryAsync<Agent>(
                $"SELECT {SelectColumns} FROM Agents ORDER BY Name;");
            return rows.ToList();
        });

    public Task<List<Agent>> GetActiveAsync()
        => ExecuteAsync(async conn =>
        {
            var rows = await conn.QueryAsync<Agent>(
                $"SELECT {SelectColumns} FROM Agents WHERE IsActive = 1 ORDER BY Name;");
            return rows.ToList();
        });

    public Task<bool> SetActiveAsync(Guid id, bool isActive)
        => ExecuteAsync(async conn =>
        {
            var updated = await conn.ExecuteAsync(
                "UPDATE Agents SET IsActive = @IsActive WHERE Id = @Id;",
                new { Id = id, IsActive = isActive });
            if (updated > 0)
                _logger.LogInformation("Agent {AgentId} set IsActive={IsActive}", id, isActive);
            return updated > 0;
        });

    public Task<bool> AnyActiveAsync()
        => ExecuteAsync(async conn =>
            await conn.ExecuteScalarAsync<int>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM Agents WHERE IsActive = 1) THEN 1 ELSE 0 END;") == 1);

    public Task<Agent?> HeartbeatAsync(Guid id, string? version, string? capabilitiesJson, string? fromIp)
        => ExecuteAsync(async conn =>
        {
            // UPDATE-only by design: heartbeats may never create registry rows.
            // Capabilities: null = "no change" — an empty/filtered-out heartbeat
            // list must not wipe a previously stored value.
            var rows = await conn.QueryAsync<Agent>($@"
                UPDATE Agents
                SET Version = @Version,
                    Capabilities = COALESCE(@Capabilities, Capabilities),
                    LastSeenAt = SYSUTCDATETIME(),
                    LastSeenFromIp = @FromIp
                OUTPUT inserted.Id, inserted.Name, inserted.Location, inserted.Capabilities,
                       inserted.Version, inserted.LastSeenAt, inserted.LastSeenFromIp,
                       inserted.IsActive, inserted.CreatedAt
                WHERE Id = @Id AND IsActive = 1;",
                new { Id = id, Version = version, Capabilities = capabilitiesJson, FromIp = fromIp });
            return rows.SingleOrDefault();
        });
}
