using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Services;

/// <summary>
/// Refreshes ConsumedUnits on AutoCount license pools by counting Objects matching the pool's filter.
/// Also auto-creates standard AD license pools (User CALs, Device CALs, Server CALs)
/// for connections that don't have them yet.
/// </summary>
public interface ILicenseAutoCountService
{
    /// <summary>Refresh consumed counts on all AutoCount pools.</summary>
    Task RefreshAllAutoCountsAsync(CancellationToken ct = default);

    /// <summary>Ensure standard AD CAL pools exist for a given connection.</summary>
    Task EnsureAdCalPoolsAsync(Guid connectionId, string connectionName, CancellationToken ct = default);

    /// <summary>Get object class counts for a connection (for the auto-create wizard).</summary>
    Task<List<(string ObjectClass, int Count)>> GetObjectClassCountsAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>Create AutoCount pools for selected object classes on a connection.</summary>
    Task<int> CreatePoolsForConnectionAsync(Guid connectionId, string connectionName, List<string> objectClasses, CancellationToken ct = default);
}

public class LicenseAutoCountService : ILicenseAutoCountService
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public LicenseAutoCountService(IConfiguration config, IGlobalLogger logger)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing");
        _logger = logger;
    }

    public async Task EnsureAdCalPoolsAsync(Guid connectionId, string connectionName, CancellationToken ct = default)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Check if pools already exist for this connection
        var existing = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM LicensePools WHERE SourceConnectionId = @ConnId AND PoolType = 'AutoCount'",
            new { ConnId = connectionId });

        if (existing > 0)
        {
            _logger.LogInformation("LicenseAutoCount: AD CAL pools already exist for connection {Name}", connectionName);
            return;
        }

        var pools = new (string SkuName, string SkuPartNumber, string ObjectClass, string FriendlyName, string LicenseType, string? Filter)[]
        {
            (string.Concat(connectionName, " — User CALs"), "AD_USER_CAL", "user",
             "Active Directory User Client Access Licenses", "UserCAL", null),
            (string.Concat(connectionName, " — Device CALs"), "AD_DEVICE_CAL", "computer",
             "Active Directory Device Client Access Licenses", "DeviceCAL", null),
            (string.Concat(connectionName, " — Windows Server Licenses"), "AD_SERVER_LICENSE", "computer",
             "Windows Server Operating System Licenses", "ServerCAL", "operatingSystem LIKE '%Server%'")
        };

        foreach (var p in pools)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO LicensePools
                    (Id, SourceConnectionId, SkuId, SkuName, SkuPartNumber, FriendlyName,
                     TotalUnits, ConsumedUnits, WarningUnits, SuspendedUnits,
                     PoolType, AutoCountObjectClass, AutoCountConnectionId, AutoCountFilter,
                     LicenseType, IsActive, AutoCreatedFromSync, LastSyncedAt)
                VALUES
                    (NEWID(), @ConnId, @SkuPartNumber, @SkuName, @SkuPartNumber, @FriendlyName,
                     0, 0, 0, 0,
                     'AutoCount', @ObjectClass, @ConnId, @Filter,
                     @LicenseType, 1, 1, GETUTCDATE())",
                new
                {
                    ConnId = connectionId,
                    p.SkuName,
                    p.SkuPartNumber,
                    p.FriendlyName,
                    p.ObjectClass,
                    p.LicenseType,
                    Filter = p.Filter
                });

            _logger.LogInformation("LicenseAutoCount: created pool '{PoolName}' for connection {Connection}",
                p.SkuName, connectionName);
        }

        // Immediately refresh the counts
        await RefreshConnectionAutoCountsAsync(conn, connectionId, ct);
    }

    public async Task RefreshAllAutoCountsAsync(CancellationToken ct = default)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var pools = (await conn.QueryAsync<LicensePool>(
            "SELECT * FROM LicensePools WHERE PoolType = 'AutoCount' AND IsActive = 1")).ToList();

        if (pools.Count == 0)
        {
            _logger.LogInformation("LicenseAutoCount: no AutoCount pools found");
            return;
        }

        _logger.LogInformation("LicenseAutoCount: refreshing {Count} AutoCount pools", pools.Count);

        foreach (var pool in pools)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var count = await CountObjectsForPoolAsync(conn, pool);

                await conn.ExecuteAsync(@"
                    UPDATE LicensePools
                    SET ConsumedUnits = @Count,
                        LastAutoCountAt = GETUTCDATE(),
                        LastSyncedAt = GETUTCDATE()
                    WHERE Id = @Id",
                    new { Id = pool.Id, Count = count });

                _logger.LogInformation("LicenseAutoCount: {PoolName} = {Count} (was {Old})",
                    pool.SkuName, count, pool.ConsumedUnits);

                // Auto-detect over-limit: if consumed just exceeded owned, fire breach actions
                if (count > pool.TotalUnits && pool.TotalUnits > 0 && pool.ConsumedUnits <= pool.TotalUnits)
                {
                    _logger.LogWarning("LicenseAutoCount: {PoolName} just exceeded limit ({Count} > {Owned})",
                        pool.SkuName, count, pool.TotalUnits);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LicenseAutoCount: failed to count for pool {PoolName}", pool.SkuName);
            }
        }
    }

    private async Task RefreshConnectionAutoCountsAsync(SqlConnection conn, Guid connectionId, CancellationToken ct)
    {
        var pools = (await conn.QueryAsync<LicensePool>(
            "SELECT * FROM LicensePools WHERE PoolType = 'AutoCount' AND AutoCountConnectionId = @ConnId AND IsActive = 1",
            new { ConnId = connectionId })).ToList();

        foreach (var pool in pools)
        {
            var count = await CountObjectsForPoolAsync(conn, pool);
            await conn.ExecuteAsync(@"
                UPDATE LicensePools
                SET ConsumedUnits = @Count, LastAutoCountAt = GETUTCDATE(), LastSyncedAt = GETUTCDATE()
                WHERE Id = @Id",
                new { Id = pool.Id, Count = count });
        }
    }

    private async Task<int> CountObjectsForPoolAsync(SqlConnection conn, LicensePool pool)
    {
        if (string.IsNullOrEmpty(pool.AutoCountObjectClass)) return 0;

        // Base query: count active, non-deleted objects of this class in this connection
        var sql = @"
            SELECT COUNT(DISTINCT o.Id) FROM Objects o
            LEFT JOIN ObjectAttributes oa ON oa.ObjectId = o.Id
            WHERE o.ObjectClass = @ObjectClass
              AND o.IsActive = 1
              AND o.DeletedAt IS NULL";

        if (pool.AutoCountConnectionId.HasValue)
            sql += " AND o.SourceConnectionId = @ConnectionId";

        // Apply the optional extra filter via an attribute value check
        if (!string.IsNullOrEmpty(pool.AutoCountFilter) && pool.AutoCountFilter.Contains("operatingSystem"))
            sql += " AND oa.AttributeName = 'operatingSystem' AND oa.AttributeValue LIKE '%Server%'";

        // Tag filter — count objects that have ANY of the selected tags (OR semantics).
        var poolTagIds = pool.AutoCountTagIdList;
        if (poolTagIds.Count > 0)
            sql += " AND EXISTS (SELECT 1 FROM ObjectTags ot WHERE ot.ObjectId = o.Id AND ot.TagId IN @TagIds)";

        // OU filter — only count objects whose DN contains this OU path
        if (!string.IsNullOrEmpty(pool.AutoCountOUFilter))
            sql += " AND o.DN LIKE @OUFilter";

        // Department filter — only count objects in this department
        if (!string.IsNullOrEmpty(pool.AutoCountDepartment))
            sql += " AND o.Department = @Department";

        // Dedup: exclude objects manually assigned to a DIFFERENT active pool
        sql += @" AND NOT EXISTS (
            SELECT 1 FROM LicenseAssignments la
            INNER JOIN LicensePools lp2 ON lp2.Id = la.LicensePoolId
            WHERE la.ObjectId = o.Id AND la.LicensePoolId != @PoolId
              AND la.IsActive = 1 AND lp2.IsActive = 1)";

        return await conn.ExecuteScalarAsync<int>(sql, new
        {
            PoolId = pool.Id,
            ObjectClass = pool.AutoCountObjectClass,
            ConnectionId = pool.AutoCountConnectionId,
            TagIds = poolTagIds,
            OUFilter = !string.IsNullOrEmpty(pool.AutoCountOUFilter) ? string.Concat("%", pool.AutoCountOUFilter, "%") : null,
            Department = pool.AutoCountDepartment
        });
    }

    public async Task<List<(string ObjectClass, int Count)>> GetObjectClassCountsAsync(Guid connectionId, CancellationToken ct = default)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = await conn.QueryAsync<(string ObjectClass, int Count)>(@"
            SELECT ObjectClass, COUNT(*) AS [Count]
            FROM Objects
            WHERE SourceConnectionId = @ConnId
              AND DeletedAt IS NULL
              AND IsActive = 1
              AND ObjectClass IS NOT NULL
            GROUP BY ObjectClass
            ORDER BY COUNT(*) DESC",
            new { ConnId = connectionId });

        return rows.ToList();
    }

    public async Task<int> CreatePoolsForConnectionAsync(Guid connectionId, string connectionName, List<string> objectClasses, CancellationToken ct = default)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        int created = 0;
        foreach (var objClass in objectClasses)
        {
            // Check if pool already exists for this connection + object class
            var exists = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM LicensePools
                WHERE SourceConnectionId = @ConnId AND AutoCountObjectClass = @ObjClass AND PoolType = 'AutoCount'",
                new { ConnId = connectionId, ObjClass = objClass });

            if (exists > 0) continue;

            // Determine license type from object class
            var licenseType = objClass switch
            {
                "user" => "UserCAL",
                "computer" => "DeviceCAL",
                "group" => "GroupLicense",
                "serviceprincipal" => "ServicePrincipal",
                "contact" => "ContactLicense",
                _ => objClass
            };

            var friendlyName = objClass switch
            {
                "user" => "User Client Access Licenses",
                "computer" => "Device Client Access Licenses",
                "group" => "Group Licenses",
                "serviceprincipal" => "Service Principal Licenses",
                "contact" => "Contact Licenses",
                _ => string.Concat(char.ToUpper(objClass[0]), objClass[1..], " Licenses")
            };

            var skuPartNumber = string.Concat(connectionName.Replace(" ", "_").ToUpperInvariant()[..Math.Min(connectionName.Length, 10)], "_", objClass.ToUpperInvariant());

            await conn.ExecuteAsync(@"
                INSERT INTO LicensePools
                    (Id, SourceConnectionId, SkuId, SkuName, SkuPartNumber, FriendlyName,
                     TotalUnits, ConsumedUnits, WarningUnits, SuspendedUnits,
                     PoolType, AutoCountObjectClass, AutoCountConnectionId,
                     LicenseType, IsActive, AutoCreatedFromSync, LastSyncedAt)
                VALUES
                    (@Id, @ConnId, @SkuId, @SkuName, @SkuPart, @Friendly,
                     0, 0, 0, 0,
                     'AutoCount', @ObjClass, @ConnId,
                     @LicType, 1, 1, GETUTCDATE())",
                new
                {
                    Id = Guid.NewGuid(),
                    ConnId = connectionId,
                    SkuId = string.Concat("AUTO_", skuPartNumber),
                    SkuName = string.Concat(connectionName, " — ", friendlyName),
                    SkuPart = skuPartNumber,
                    Friendly = friendlyName,
                    ObjClass = objClass,
                    LicType = licenseType
                });
            created++;

            _logger.LogInformation("LicenseAutoCount: Created {LicType} pool for {ObjClass} on {Conn}",
                licenseType, objClass, connectionName);
        }

        // Immediately refresh counts for the new pools
        if (created > 0)
            await RefreshAllAutoCountsAsync(ct);

        return created;
    }
}
