using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-backed implementation of <see cref="IPendingPasswordResetRepository"/>.
/// </summary>
public class PendingPasswordResetRepository : DapperRepositoryBase, IPendingPasswordResetRepository
{
    public PendingPasswordResetRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public Task<Guid> RequestAsync(Guid objectId, string? requestedBy, string? notes, CancellationToken ct = default)
    {
        if (objectId == Guid.Empty)
            throw new ArgumentException("objectId is required", nameof(objectId));

        return ExecuteAsync(async conn =>
        {
            var id = Guid.NewGuid();
            await conn.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO PendingPasswordResets (Id, ObjectId, RequestedAt, RequestedBy, Status, Notes)
                  VALUES (@Id, @ObjectId, @RequestedAt, @RequestedBy, 'Pending', @Notes);",
                new
                {
                    Id = id,
                    ObjectId = objectId,
                    RequestedAt = DateTime.UtcNow,
                    RequestedBy = requestedBy,
                    Notes = notes,
                },
                cancellationToken: ct));
            return id;
        }, ct);
    }

    public Task<IReadOnlyList<PendingPasswordReset>> GetPendingForObjectAsync(Guid objectId, CancellationToken ct = default)
    {
        return ExecuteAsync(async conn =>
        {
            var rows = await conn.QueryAsync<PendingPasswordReset>(new CommandDefinition(
                @"SELECT Id, ObjectId, RequestedAt, RequestedBy, Status, AppliedAt, Notes
                  FROM PendingPasswordResets
                  WHERE ObjectId = @ObjectId
                  ORDER BY RequestedAt DESC",
                new { ObjectId = objectId },
                cancellationToken: ct));
            return (IReadOnlyList<PendingPasswordReset>)rows.ToList();
        }, ct);
    }
}
