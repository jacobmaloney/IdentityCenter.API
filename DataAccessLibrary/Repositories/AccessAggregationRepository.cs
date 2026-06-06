using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-backed implementation of <see cref="IAccessAggregationRepository"/>.
/// All queries honour soft-delete (Objects.DeletedAt IS NULL) and rely on
/// existing schema only — no DDL is owned by this class.
/// </summary>
public class AccessAggregationRepository : DapperRepositoryBase, IAccessAggregationRepository
{
    /// <summary>
    /// Keywords that mark a group as "privileged" for the Risk Summary card.
    /// Matched case-insensitively as substrings.
    /// </summary>
    public static readonly string[] PrivilegedKeywords = new[]
    {
        "domain admin",
        "schema admin",
        "enterprise admin",
        "backup operators",
        "account operators",
        "administrator",
        "privileged",
        "admin"
    };

    public AccessAggregationRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public bool IsPrivilegedGroupName(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return false;
        var lower = groupName.ToLowerInvariant();
        foreach (var kw in PrivilegedKeywords)
        {
            if (lower.Contains(kw)) return true;
        }
        return false;
    }

    public Task<IdentityAccessPayload> GetIdentityAccessAsync(Guid identityId, CancellationToken ct = default)
        => ExecuteAsync(async conn =>
        {
            // 1) Resolve linked Objects for this identity (soft-delete aware).
            var linked = (await conn.QueryAsync<(Guid Id, string? Name, int? Uac)>(new CommandDefinition(
                @"SELECT o.Id, COALESCE(o.DisplayName, o.CN, o.Username, '') AS Name, o.UserAccountControl AS Uac
                  FROM Objects o
                  WHERE o.IdentityId = @IdentityId AND o.DeletedAt IS NULL",
                new { IdentityId = identityId },
                cancellationToken: ct))).ToList();

            if (linked.Count == 0)
            {
                return new IdentityAccessPayload();
            }

            var objectIds = linked.Select(x => x.Id).ToList();
            var hasUnconstrainedDelegation = linked.Any(o => ((o.Uac ?? 0) & 0x80000) != 0);

            return await BuildPayloadAsync(conn, objectIds, linked, hasUnconstrainedDelegation, ct);
        }, ct);

    public Task<IdentityAccessPayload> GetObjectAccessAsync(Guid objectId, CancellationToken ct = default)
        => ExecuteAsync(async conn =>
        {
            var info = await conn.QueryFirstOrDefaultAsync<(Guid Id, string? Name, int? Uac)?>(new CommandDefinition(
                @"SELECT o.Id, COALESCE(o.DisplayName, o.CN, o.Username, '') AS Name, o.UserAccountControl AS Uac
                  FROM Objects o
                  WHERE o.Id = @ObjectId AND o.DeletedAt IS NULL",
                new { ObjectId = objectId },
                cancellationToken: ct));

            if (info == null) return new IdentityAccessPayload();

            var hasUnconstrainedDelegation = ((info.Value.Uac ?? 0) & 0x80000) != 0;
            return await BuildPayloadAsync(conn, new List<Guid> { objectId },
                new List<(Guid, string?, int?)> { (info.Value.Id, info.Value.Name, info.Value.Uac) },
                hasUnconstrainedDelegation, ct);
        }, ct);

    public Task<DateTime?> GetLastReviewedDateAsync(Guid identityId, CancellationToken ct = default)
        => ExecuteAsync(async conn =>
        {
            // Resolve identity + linked object IDs
            var allTargets = (await conn.QueryAsync<Guid>(new CommandDefinition(
                @"SELECT @IdentityId AS Id
                  UNION
                  SELECT o.Id FROM Objects o WHERE o.IdentityId = @IdentityId AND o.DeletedAt IS NULL",
                new { IdentityId = identityId },
                cancellationToken: ct))).ToList();

            if (allTargets.Count == 0) return (DateTime?)null;

            var sql = @"SELECT MAX(CompletedAt)
                        FROM AccessReviewAssignments
                        WHERE ReviewTargetId IN @Targets AND CompletedAt IS NOT NULL";
            return await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
                sql, new { Targets = allTargets }, cancellationToken: ct));
        }, ct);

    private async Task<IdentityAccessPayload> BuildPayloadAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        List<Guid> objectIds,
        List<(Guid Id, string? Name, int? Uac)> linked,
        bool hasUnconstrainedDelegation,
        CancellationToken ct)
    {
        var payload = new IdentityAccessPayload();

        // 2) Pull memberships (direct + nested). Schema: ObjectGroupMemberships
        //    has IsDirect/IsPrimary; the Group row lives in Objects.
        const string groupSql = @"
            SELECT
                gm.GroupId AS GroupId,
                COALESCE(g.DisplayName, g.CN, '') AS GroupName,
                COALESCE(g.SourceType, 'ActiveDirectory') AS GroupSource,
                gm.IsDirect AS IsDirect,
                gm.IsPrimary AS IsPrimary,
                gm.MembershipPath AS ParentGroupName,
                COALESCE(o.DisplayName, o.CN, o.Username, '') AS OwningObjectName
            FROM ObjectGroupMemberships gm
            INNER JOIN Objects g ON g.Id = gm.GroupId AND g.DeletedAt IS NULL
            INNER JOIN Objects o ON o.Id = gm.ObjectId AND o.DeletedAt IS NULL
            WHERE gm.ObjectId IN @Ids
              AND gm.IsActive = 1
              AND gm.RemovedAt IS NULL
            ORDER BY GroupName";

        var groupRows = (await conn.QueryAsync<AccessGroupRow>(new CommandDefinition(
            groupSql, new { Ids = objectIds }, cancellationToken: ct))).ToList();

        foreach (var g in groupRows)
        {
            g.IsPrivileged = IsPrivilegedGroupName(g.GroupName);
        }
        payload.Groups = groupRows;

        // 3) License assignments (active only). Pull pool name + cost via join.
        const string licenseSql = @"
            SELECT
                la.Id AS AssignmentId,
                la.LicensePoolId AS PoolId,
                COALESCE(lp.SkuName, '') AS PoolName,
                lp.SkuName AS SkuName,
                la.AssignedAt AS AssignedAt,
                la.LastUsedAt AS LastUsedAt,
                la.IsActive AS IsActive,
                lp.CostPerUnitMonthly AS CostPerUnitMonthly,
                la.AssignmentSource AS AssignmentSource
            FROM LicenseAssignments la
            INNER JOIN LicensePools lp ON lp.Id = la.LicensePoolId
            WHERE la.ObjectId IN @Ids AND la.IsActive = 1
            ORDER BY lp.SkuName";

        var licRows = (await conn.QueryAsync<AccessLicenseRow>(new CommandDefinition(
            licenseSql, new { Ids = objectIds }, cancellationToken: ct))).ToList();

        // Deduplicate licenses by PoolId — same SKU across multiple linked objects
        // counts once for the user's effective entitlement view.
        payload.Licenses = licRows
            .GroupBy(l => l.PoolId)
            .Select(g => g.First())
            .ToList();

        payload.Summary = new AccessSummary
        {
            DirectoryGroupCount = payload.Groups.Count,
            LicenseCount = payload.Licenses.Count,
            PrivilegedGroupCount = payload.Groups.Count(g => g.IsPrivileged),
            HasUnconstrainedDelegation = hasUnconstrainedDelegation,
            LastReviewedAt = null  // populated separately by caller via GetLastReviewedDateAsync
        };

        return payload;
    }
}
