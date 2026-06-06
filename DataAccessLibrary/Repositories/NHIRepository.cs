using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-backed implementation of <see cref="INHIRepository"/>. Read-only
/// against Objects/ObjectAttributes; read/write against the V110 NHI tables.
/// No directory write-back — that's owned by ObjectWriteBackService.
/// </summary>
public class NHIRepository : DapperRepositoryBase, INHIRepository
{
    /// <summary>
    /// Re-usable WHERE fragment that selects only NHIs from the Objects table.
    /// Translation note (per CLAUDE.md): the Objects column is `Username`,
    /// not `SamAccountName`. The `[_]` escape preserves literal underscore in
    /// LIKE pattern matching under T-SQL.
    /// </summary>
    public const string NhiWhereClause = @"
        o.DeletedAt IS NULL AND (
            o.ObjectClass IN ('serviceprincipal', 'gmsa', 'msa')
            OR (o.ObjectClass = 'user' AND EXISTS (
                SELECT 1 FROM ObjectAttributes oa
                WHERE oa.ObjectId = o.Id AND oa.AttributeName = 'servicePrincipalName'))
            OR (o.ObjectClass = 'user' AND (
                o.Username LIKE 'svc-%' OR o.Username LIKE 'sa-%'
                OR o.Username LIKE 'service-%' OR o.Username LIKE '%[_]svc'
                OR o.Username LIKE '%[_]sa' OR o.DisplayName LIKE 'Service Account%'))
        )";

    public NHIRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public Task<IEnumerable<IdentityObject>> GetNHIsAsync(string? filter = null, CancellationToken ct = default)
        => ExecuteAsync(async conn =>
        {
            var extraWhere = (filter ?? "All") switch
            {
                "ServicePrincipal" => " AND o.ObjectClass = 'serviceprincipal'",
                "GMSA"             => " AND o.ObjectClass IN ('gmsa', 'msa')",
                "ServiceAccount"   => " AND o.ObjectClass = 'user'",
                "Unowned"          => " AND NOT EXISTS (SELECT 1 FROM NHIOwnership n WHERE n.ObjectId = o.Id)",
                "Privileged"       => " AND ((o.UserAccountControl & 524288) <> 0 OR EXISTS (" +
                                      "  SELECT 1 FROM ObjectGroupMemberships gm" +
                                      "  INNER JOIN Objects g ON g.Id = gm.GroupId AND g.DeletedAt IS NULL" +
                                      "  WHERE gm.ObjectId = o.Id AND gm.IsActive = 1 AND gm.RemovedAt IS NULL" +
                                      "  AND (LOWER(g.DisplayName) LIKE '%admin%' OR LOWER(g.CN) LIKE '%admin%' OR LOWER(g.DisplayName) LIKE '%privileged%')" +
                                      "))",
                _                  => string.Empty
            };

            var sql = $@"
                SELECT o.Id, o.IdentityId, o.SourceConnectionId, o.SourceUniqueId, o.SourceType,
                       o.ObjectClass, o.DisplayName, o.Email, o.Username, o.FirstName, o.LastName,
                       o.Department, o.JobTitle, o.DN, o.CN,
                       o.IsActive, o.IsAuthoritative, o.MatchConfidence, o.MatchMethod,
                       o.FirstSyncedAt, o.LastSyncedAt, o.LastSeenAt, o.DeletedAt,
                       o.PasswordLastSet, o.IsBuiltIn, o.IsAdminSDHolder,
                       o.PasswordNeverExpires, o.UserAccountControl
                FROM Objects o
                WHERE {NhiWhereClause}{extraWhere}
                ORDER BY COALESCE(o.DisplayName, o.CN, o.Username) ASC";

            return await conn.QueryAsync<IdentityObject>(new CommandDefinition(sql, cancellationToken: ct));
        }, ct);

    public Task<NHIOwnership?> GetOwnershipAsync(Guid objectId, CancellationToken ct = default)
        => ExecuteAsync(async conn =>
        {
            return await conn.QueryFirstOrDefaultAsync<NHIOwnership>(new CommandDefinition(
                "SELECT * FROM NHIOwnership WHERE ObjectId = @ObjectId",
                new { ObjectId = objectId },
                cancellationToken: ct));
        }, ct);

    public Task SetOwnerAsync(Guid objectId, Guid? ownerId, string ownerName, string assignedBy, CancellationToken ct = default)
        => ExecuteNonQueryAsync(async conn =>
        {
            // UNIQUE(ObjectId): UPSERT via MERGE — keeps history clean (one row per NHI).
            const string sql = @"
                MERGE INTO NHIOwnership AS T
                USING (SELECT @ObjectId AS ObjectId) AS S
                ON (T.ObjectId = S.ObjectId)
                WHEN MATCHED THEN UPDATE SET
                    T.OwnerId    = @OwnerId,
                    T.OwnerName  = @OwnerName,
                    T.AssignedAt = @AssignedAt,
                    T.AssignedBy = @AssignedBy
                WHEN NOT MATCHED THEN INSERT (ObjectId, OwnerId, OwnerName, AssignedAt, AssignedBy)
                    VALUES (@ObjectId, @OwnerId, @OwnerName, @AssignedAt, @AssignedBy);";

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                ObjectId  = objectId,
                OwnerId   = ownerId,
                OwnerName = ownerName,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = assignedBy
            }, cancellationToken: ct));
        }, ct);

    public Task<NHIAttestation?> GetLatestAttestationAsync(Guid objectId, CancellationToken ct = default)
        => ExecuteAsync(async conn =>
        {
            return await conn.QueryFirstOrDefaultAsync<NHIAttestation>(new CommandDefinition(
                @"SELECT TOP 1 * FROM NHIAttestation
                  WHERE ObjectId = @ObjectId
                  ORDER BY AttestedAt DESC",
                new { ObjectId = objectId },
                cancellationToken: ct));
        }, ct);

    public Task RecordAttestationAsync(Guid objectId, string attestedBy, string? notes, CancellationToken ct = default)
        => ExecuteNonQueryAsync(async conn =>
        {
            var now = DateTime.UtcNow;
            var nextDue = now.AddDays(90);
            const string sql = @"
                INSERT INTO NHIAttestation (Id, ObjectId, AttestedBy, AttestedAt, Notes, NextDueDate)
                VALUES (@Id, @ObjectId, @AttestedBy, @AttestedAt, @Notes, @NextDueDate);";
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = Guid.NewGuid(),
                ObjectId = objectId,
                AttestedBy = attestedBy,
                AttestedAt = now,
                Notes = notes,
                NextDueDate = nextDue
            }, cancellationToken: ct));
        }, ct);

    public Task<NHISummaryStats> GetSummaryStatsAsync(CancellationToken ct = default)
        => ExecuteAsync(async conn =>
        {
            // Single round-trip: one CTE for "all NHIs", everything else aggregates over it.
            var sql = $@"
                WITH NHI AS (
                    SELECT o.Id, o.PasswordLastSet, o.PasswordNeverExpires, o.UserAccountControl
                    FROM Objects o
                    WHERE {NhiWhereClause}
                )
                SELECT
                    (SELECT COUNT(*) FROM NHI) AS TotalNHIs,
                    (SELECT COUNT(*) FROM NHI n WHERE EXISTS (SELECT 1 FROM NHIOwnership o2 WHERE o2.ObjectId = n.Id)) AS Owned,
                    (SELECT COUNT(*) FROM NHI n WHERE NOT EXISTS (SELECT 1 FROM NHIOwnership o2 WHERE o2.ObjectId = n.Id)) AS Unowned,
                    (SELECT COUNT(*) FROM NHI n WHERE n.PasswordLastSet IS NOT NULL AND n.PasswordLastSet < DATEADD(day, -365, GETUTCDATE())) AS WithExpiredPasswords,
                    (SELECT COUNT(*) FROM NHI n WHERE n.PasswordNeverExpires = 1) AS WithNeverExpiringPasswords,
                    (SELECT COUNT(*) FROM NHI n WHERE EXISTS (
                        SELECT 1 FROM ObjectGroupMemberships gm
                        INNER JOIN Objects g ON g.Id = gm.GroupId AND g.DeletedAt IS NULL
                        WHERE gm.ObjectId = n.Id AND gm.IsActive = 1 AND gm.RemovedAt IS NULL
                          AND (LOWER(COALESCE(g.DisplayName, g.CN, '')) LIKE '%admin%'
                               OR LOWER(COALESCE(g.DisplayName, g.CN, '')) LIKE '%privileged%')
                    )) AS WithAdminRights,
                    (SELECT COUNT(DISTINCT n.Id) FROM NHI n
                        INNER JOIN ObjectAttributes oa ON oa.ObjectId = n.Id AND oa.AttributeName = 'servicePrincipalName') AS WithSPNs,
                    (SELECT COUNT(*) FROM NHI n WHERE NOT EXISTS (
                        SELECT 1 FROM NHIAttestation a
                        WHERE a.ObjectId = n.Id AND a.NextDueDate > GETUTCDATE()
                    )) AS AttestationOverdue;";

            return await conn.QueryFirstAsync<NHISummaryStats>(new CommandDefinition(sql, cancellationToken: ct))
                   ?? new NHISummaryStats();
        }, ct);
}
