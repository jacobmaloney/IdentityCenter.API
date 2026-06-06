using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="ILicenseRepository"/>.
/// Schema managed by V056__LicenseMonitoring.sql.
/// </summary>
public class LicenseRepository : ILicenseRepository
{
    private readonly string _defaultConnectionString;
    private readonly IGlobalLogger _logger;
    private readonly DataAccessLibrary.Services.IAuditLogService? _auditLog;

    public LicenseRepository(IConfiguration configuration, IGlobalLogger logger, DataAccessLibrary.Services.IAuditLogService? auditLog = null)
    {
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditLog = auditLog;
    }

    // MULTI-TENANT SEAM (SaaS Day 4): backs ReportsController (TenantDataPolicy); routes through the
    // ambient accessor so a tenant request hits its own DB. Falls back to DefaultConnection otherwise.
    private string _connectionString =>
        DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    // ─────────────────────────────────────────────────────────────────────────
    // License Pools
    // ─────────────────────────────────────────────────────────────────────────

    private const string POOL_SELECT_COLUMNS = @"
            SELECT Id, SourceConnectionId, SkuId, SkuName, SkuPartNumber,
                   TotalUnits, ConsumedUnits, WarningUnits, SuspendedUnits, AvailableUnits,
                   CostPerUnitMonthly, Currency,
                   MinBufferPercent, MaxUtilizationPercent, AlertThreshold, FriendlyName, Notes,
                   BillingPeriod, LicenseType, LastSyncedAt, IsActive,
                   LicenseCategoryId, AutoCreatedFromSync, ReviewFrequencyDays, LastReviewedAt,
                   PoolType, AutoCountObjectClass, AutoCountConnectionId, AutoCountFilter, LastAutoCountAt,
                   OnBreachCreateReview, OnBreachSendEmail, OnBreachNotifyTeams,
                   BreachReviewerId, BreachReviewerName, BreachEmailTemplateId,
                   AutoDenyOnIncomplete,
                   BreachNotifyObjectId, BreachNotifyObjectName, BreachNotifyObjectClass,
                   AutoCountTagId, AutoCountTagIds, AutoCountOUFilter, AutoCountDepartment
            FROM   LicensePools";

    public async Task<List<LicensePool>> GetLicensePoolsAsync(
        Guid? connectionId = null,
        CancellationToken ct = default)
    {
        var baseSql = POOL_SELECT_COLUMNS + " WHERE IsActive = 1";

        var sql = connectionId.HasValue
            ? baseSql + " AND SourceConnectionId = @ConnectionId ORDER BY SkuName;"
            : baseSql + " ORDER BY SkuName;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<LicensePool>(sql,
            connectionId.HasValue ? new { ConnectionId = connectionId.Value } : null);
        return rows.ToList();
    }

    public async Task<LicensePool?> GetLicensePoolAsync(Guid poolId, CancellationToken ct = default)
    {
        var sql = POOL_SELECT_COLUMNS + " WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<LicensePool>(sql, new { Id = poolId });
    }

    public async Task<Guid> CreateManualLicensePoolAsync(
        string sourceLabel,
        string skuName,
        string? skuPartNumber,
        string? friendlyName,
        int totalUnits,
        int initialConsumedUnits,
        decimal? costPerUnitMonthly,
        string? billingPeriod,
        string? licenseType,
        string? notes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceLabel)) throw new ArgumentException("sourceLabel required", nameof(sourceLabel));
        if (string.IsNullOrWhiteSpace(skuName)) throw new ArgumentException("skuName required", nameof(skuName));
        if (totalUnits < 0) throw new ArgumentException("totalUnits must be >= 0", nameof(totalUnits));
        if (initialConsumedUnits < 0) throw new ArgumentException("initialConsumedUnits must be >= 0", nameof(initialConsumedUnits));

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Find or create the synthetic "Manual: <sourceLabel>" DirectoryConnection.
        // ConnectionType = "Manual" so the connector layer ignores it; ConnectionString +
        // Credentials are stored as empty strings since there's nothing to authenticate against.
        var connName = string.Concat("Manual: ", sourceLabel.Trim());
        var connectionId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT TOP 1 Id FROM DirectoryConnections WHERE ConnectionType = 'Manual' AND Name = @Name",
            new { Name = connName });

        if (!connectionId.HasValue)
        {
            connectionId = Guid.NewGuid();
            await conn.ExecuteAsync(@"
                INSERT INTO DirectoryConnections
                    (Id, Name, ConnectionType, ConnectionString, Credentials, Configuration,
                     IsActive, IsAuthoritative, CreatedAt)
                VALUES
                    (@Id, @Name, 'Manual', '', '', NULL, 1, 0, GETUTCDATE())",
                new { Id = connectionId.Value, Name = connName });
            _logger.LogInformation("LicenseRepository.CreateManualLicensePoolAsync: created synthetic connection {ConnectionId} for manual source '{Source}'", connectionId.Value, sourceLabel);
        }

        // SkuId for manual pools is a stable string derived from the source + sku name.
        // This lets the (SourceConnectionId, SkuId) uniqueness rule still hold for manual pools.
        var skuId = string.Concat("manual:", sourceLabel.Trim().ToLowerInvariant(), ":", skuName.Trim().ToLowerInvariant());

        // Reject duplicate manual pool with the same SKU under the same source.
        var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT TOP 1 Id FROM LicensePools WHERE SourceConnectionId = @ConnId AND SkuId = @SkuId",
            new { ConnId = connectionId.Value, SkuId = skuId });
        if (existingId.HasValue)
            throw new InvalidOperationException(string.Concat("A manual pool for SKU '", skuName, "' already exists under source '", sourceLabel, "'."));

        var poolId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO LicensePools
                (Id, SourceConnectionId, SkuId, SkuName, SkuPartNumber, FriendlyName,
                 TotalUnits, ConsumedUnits, WarningUnits, SuspendedUnits,
                 CostPerUnitMonthly, Currency, BillingPeriod, LicenseType, CostCenter,
                 Notes, PoolType, LastSyncedAt, IsActive)
            VALUES
                (@Id, @SourceConnectionId, @SkuId, @SkuName, @SkuPartNumber, @FriendlyName,
                 @TotalUnits, @ConsumedUnits, 0, 0,
                 @CostPerUnitMonthly, 'USD', @BillingPeriod, @LicenseType, NULL,
                 @Notes, 'Manual', GETUTCDATE(), 1)",
            new
            {
                Id = poolId,
                SourceConnectionId = connectionId.Value,
                SkuId = skuId,
                SkuName = skuName.Trim(),
                SkuPartNumber = string.IsNullOrWhiteSpace(skuPartNumber) ? null : skuPartNumber.Trim(),
                FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName.Trim(),
                TotalUnits = totalUnits,
                ConsumedUnits = initialConsumedUnits,
                CostPerUnitMonthly = costPerUnitMonthly,
                BillingPeriod = string.IsNullOrWhiteSpace(billingPeriod) ? "Monthly" : billingPeriod.Trim(),
                LicenseType = string.IsNullOrWhiteSpace(licenseType) ? null : licenseType.Trim(),
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
            });

        _logger.LogInformation("LicenseRepository.CreateManualLicensePoolAsync: created manual pool {PoolId} ({Sku}) under source '{Source}'", poolId, skuName, sourceLabel);
        return poolId;
    }

    public async Task<Guid> UpsertLicensePoolAsync(LicensePool pool, CancellationToken ct = default)
    {
        // Look up existing pool by (SourceConnectionId, SkuId) to determine insert vs update.
        const string lookupSql = @"
            SELECT Id FROM LicensePools
            WHERE SourceConnectionId = @SourceConnectionId AND SkuId = @SkuId;";

        const string insertSql = @"
            INSERT INTO LicensePools
                (Id, SourceConnectionId, SkuId, SkuName, SkuPartNumber,
                 TotalUnits, ConsumedUnits, WarningUnits, SuspendedUnits,
                 CostPerUnitMonthly, Currency, FriendlyName,
                 BillingPeriod, LicenseType, CostCenter, LastSyncedAt, IsActive)
            VALUES
                (@Id, @SourceConnectionId, @SkuId, @SkuName, @SkuPartNumber,
                 @TotalUnits, @ConsumedUnits, @WarningUnits, @SuspendedUnits,
                 @CostPerUnitMonthly, @Currency, @FriendlyName,
                 @BillingPeriod, @LicenseType, @CostCenter, @LastSyncedAt, @IsActive);";

        const string updateSql = @"
            UPDATE LicensePools
            SET  SkuName            = @SkuName,
                 SkuPartNumber      = @SkuPartNumber,
                 TotalUnits         = @TotalUnits,
                 ConsumedUnits      = @ConsumedUnits,
                 WarningUnits       = @WarningUnits,
                 SuspendedUnits     = @SuspendedUnits,
                 CostPerUnitMonthly = @CostPerUnitMonthly,
                 Currency           = @Currency,
                 FriendlyName       = COALESCE(@FriendlyName, FriendlyName),
                 BillingPeriod      = COALESCE(@BillingPeriod, BillingPeriod),
                 LicenseType        = COALESCE(@LicenseType, LicenseType),
                 CostCenter         = COALESCE(@CostCenter, CostCenter),
                 LastSyncedAt       = @LastSyncedAt,
                 IsActive           = @IsActive
            WHERE Id = @Id;";

        pool.LastSyncedAt = DateTime.UtcNow;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(lookupSql,
            new { pool.SourceConnectionId, pool.SkuId });

        if (existingId.HasValue)
        {
            pool.Id = existingId.Value;
            await conn.ExecuteAsync(updateSql, pool);
            _logger.LogInformation("LicenseRepository.UpsertLicensePoolAsync: Updated pool {PoolId} ({SkuName})", pool.Id, pool.SkuName);
        }
        else
        {
            if (pool.Id == Guid.Empty) pool.Id = Guid.NewGuid();
            await conn.ExecuteAsync(insertSql, pool);
            _logger.LogInformation("LicenseRepository.UpsertLicensePoolAsync: Inserted pool {PoolId} ({SkuName})", pool.Id, pool.SkuName);
        }

        return pool.Id;
    }

    public async Task UpdatePoolOwnedUnitsAsync(Guid poolId, int totalUnits, CancellationToken ct = default)
    {
        const string sql = "UPDATE LicensePools SET TotalUnits = @TotalUnits WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { Id = poolId, TotalUnits = totalUnits });
        _logger.LogInformation("LicenseRepository.UpdatePoolOwnedUnitsAsync: Pool {PoolId} set to {Units}", poolId, totalUnits);
        await LogPoolChangeAsync(poolId, "TotalUnits", totalUnits.ToString());
    }

    public async Task UpdatePoolPolicyAsync(
        Guid poolId,
        int? minBufferPercent,
        int? maxUtilizationPercent,
        string? notes,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE LicensePools
            SET  MinBufferPercent      = @MinBufferPercent,
                 MaxUtilizationPercent = @MaxUtilizationPercent,
                 Notes                = @Notes
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            Id = poolId,
            MinBufferPercent = minBufferPercent,
            MaxUtilizationPercent = maxUtilizationPercent,
            Notes = notes
        });

        _logger.LogInformation("LicenseRepository.UpdatePoolPolicyAsync: Updated policy for pool {PoolId}", poolId);
    }

    public async Task UpdatePoolBreachActionsAsync(LicensePool pool, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE LicensePools
            SET  OnBreachCreateReview    = @OnBreachCreateReview,
                 OnBreachSendEmail       = @OnBreachSendEmail,
                 OnBreachNotifyTeams     = @OnBreachNotifyTeams,
                 BreachReviewerId        = @BreachReviewerId,
                 BreachReviewerName      = @BreachReviewerName,
                 BreachEmailTemplateId   = @BreachEmailTemplateId,
                 BreachNotifyObjectId    = @BreachNotifyObjectId,
                 BreachNotifyObjectName  = @BreachNotifyObjectName,
                 BreachNotifyObjectClass = @BreachNotifyObjectClass,
                 AutoCountTagId       = @AutoCountTagId,
                 AutoCountTagIds      = @AutoCountTagIds,
                 AutoCountOUFilter    = @AutoCountOUFilter,
                 AutoCountDepartment  = @AutoCountDepartment
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            pool.Id,
            pool.OnBreachCreateReview,
            pool.OnBreachSendEmail,
            pool.OnBreachNotifyTeams,
            pool.BreachReviewerId,
            pool.BreachReviewerName,
            pool.BreachEmailTemplateId,
            pool.BreachNotifyObjectId,
            pool.BreachNotifyObjectName,
            pool.BreachNotifyObjectClass,
            pool.AutoCountTagId,
            pool.AutoCountTagIds,
            pool.AutoCountOUFilter,
            pool.AutoCountDepartment
        });

        _logger.LogInformation("LicenseRepository.UpdatePoolBreachActionsAsync: Pool {PoolId} — Review={Review} Email={Email} Teams={Teams} Notify={Notify}",
            pool.Id, pool.OnBreachCreateReview, pool.OnBreachSendEmail, pool.OnBreachNotifyTeams, pool.BreachNotifyObjectName ?? "All admins");
        await LogPoolChangeAsync(pool.Id, "BreachActions",
            string.Concat("Review=", pool.OnBreachCreateReview, " Email=", pool.OnBreachSendEmail, " Teams=", pool.OnBreachNotifyTeams));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reclaim Support
    // ─────────────────────────────────────────────────────────────────────────

    public async Task DecrementConsumedUnitsAsync(Guid licensePoolId, int delta, CancellationToken ct = default)
    {
        if (delta <= 0) return;

        const string sql = @"
            UPDATE LicensePools
            SET ConsumedUnits = CASE WHEN ConsumedUnits - @Delta < 0 THEN 0 ELSE ConsumedUnits - @Delta END
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { Id = licensePoolId, Delta = delta });
        _logger.LogInformation("LicenseRepository.DecrementConsumedUnitsAsync: Pool {PoolId} -{Delta}", licensePoolId, delta);
    }

    public async Task DeactivateAssignmentAsync(Guid licensePoolId, Guid objectId, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE LicenseAssignments
            SET IsActive = 0, LastSyncedAt = GETUTCDATE()
            WHERE LicensePoolId = @PoolId
              AND ObjectId = @ObjectId
              AND IsActive = 1;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(sql, new { PoolId = licensePoolId, ObjectId = objectId });
        _logger.LogInformation(
            "LicenseRepository.DeactivateAssignmentAsync: Pool {PoolId} Object {ObjectId} rows={Rows}",
            licensePoolId, objectId, rows);
    }

    public async Task WriteLicenseAssignmentEventAsync(
        Guid licensePoolId,
        Guid objectId,
        Guid? assignmentId,
        string eventType,
        string? actor,
        string? reason,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO LicenseAssignmentEvents
                (Id, AssignmentId, LicensePoolId, ObjectId, EventType, Actor, Reason, Metadata, CreatedAt)
            VALUES
                (@Id, @AssignmentId, @LicensePoolId, @ObjectId, @EventType, @Actor, @Reason, @Metadata, GETUTCDATE());";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            AssignmentId = assignmentId ?? Guid.Empty,
            LicensePoolId = licensePoolId,
            ObjectId = objectId,
            EventType = eventType,
            Actor = actor,
            Reason = reason,
            Metadata = metadataJson
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // License Assignments
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<LicenseAssignment>> GetLicenseAssignmentsAsync(
        Guid poolId,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT la.Id, la.LicensePoolId, la.ObjectId, la.AssignedAt,
                   la.AssignmentSource, la.SourceGroupId, la.LastUsedAt,
                   la.IsActive, la.LastSyncedAt,
                   o.DisplayName   AS UserDisplayName,
                   o.Username      AS Username,
                   o.UserPrincipalName,
                   lp.SkuName
            FROM   LicenseAssignments la
            JOIN   Objects      o  ON o.Id  = la.ObjectId
            JOIN   LicensePools lp ON lp.Id = la.LicensePoolId
            WHERE  la.LicensePoolId = @PoolId
              AND  (@IncludeInactive = 1 OR la.IsActive = 1)
            ORDER BY o.DisplayName;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<LicenseAssignment>(sql,
            new { PoolId = poolId, IncludeInactive = includeInactive ? 1 : 0 });
        return rows.ToList();
    }

    public async Task<List<LicenseAssignment>> GetAssignmentsForObjectAsync(
        Guid objectId,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT la.Id, la.LicensePoolId, la.ObjectId, la.AssignedAt,
                   la.AssignmentSource, la.SourceGroupId, la.LastUsedAt,
                   la.IsActive, la.LastSyncedAt,
                   o.DisplayName   AS UserDisplayName,
                   o.Username      AS Username,
                   o.UserPrincipalName,
                   lp.SkuName
            FROM   LicenseAssignments la
            JOIN   Objects      o  ON o.Id  = la.ObjectId
            JOIN   LicensePools lp ON lp.Id = la.LicensePoolId
            WHERE  la.ObjectId = @ObjectId
              AND  la.IsActive = 1
            ORDER BY lp.SkuName;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<LicenseAssignment>(sql, new { ObjectId = objectId });
        return rows.ToList();
    }

    public async Task<Guid> UpsertLicenseAssignmentAsync(
        LicenseAssignment assignment,
        CancellationToken ct = default)
    {
        const string lookupSql = @"
            SELECT Id FROM LicenseAssignments
            WHERE LicensePoolId = @LicensePoolId AND ObjectId = @ObjectId;";

        const string insertSql = @"
            INSERT INTO LicenseAssignments
                (Id, LicensePoolId, ObjectId, AssignedAt, AssignmentSource,
                 SourceGroupId, LastUsedAt, IsActive, LastSyncedAt)
            VALUES
                (@Id, @LicensePoolId, @ObjectId, @AssignedAt, @AssignmentSource,
                 @SourceGroupId, @LastUsedAt, @IsActive, @LastSyncedAt);";

        const string updateSql = @"
            UPDATE LicenseAssignments
            SET  AssignedAt       = @AssignedAt,
                 AssignmentSource = @AssignmentSource,
                 SourceGroupId    = @SourceGroupId,
                 LastUsedAt       = @LastUsedAt,
                 IsActive         = @IsActive,
                 LastSyncedAt     = @LastSyncedAt
            WHERE Id = @Id;";

        assignment.LastSyncedAt = DateTime.UtcNow;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(lookupSql,
            new { assignment.LicensePoolId, assignment.ObjectId });

        if (existingId.HasValue)
        {
            assignment.Id = existingId.Value;
            await conn.ExecuteAsync(updateSql, assignment);
        }
        else
        {
            if (assignment.Id == Guid.Empty) assignment.Id = Guid.NewGuid();
            await conn.ExecuteAsync(insertSql, assignment);
        }

        return assignment.Id;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sync Support
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<int> DeactivateStaleAssignmentsAsync(
        Guid connectionId,
        DateTime syncedBefore,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE la SET la.IsActive = 0, la.LastSyncedAt = GETUTCDATE()
            FROM LicenseAssignments la
            JOIN LicensePools lp ON lp.Id = la.LicensePoolId
            WHERE lp.SourceConnectionId = @ConnectionId
              AND la.IsActive = 1
              AND la.LastSyncedAt < @SyncedBefore;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(sql, new { ConnectionId = connectionId, SyncedBefore = syncedBefore });

        if (affected > 0)
            _logger.LogInformation("LicenseRepository.DeactivateStaleAssignmentsAsync: Deactivated {Count} stale assignments for connection {ConnectionId}", affected, connectionId);

        return affected;
    }

    public async Task<Dictionary<string, Guid>> ResolveEntraUserIdsAsync(
        Guid connectionId,
        IEnumerable<string> entraUserIds,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT SourceUniqueId, Id
            FROM Objects
            WHERE SourceConnectionId = @ConnectionId
              AND SourceUniqueId IN @UserIds;";

        var allIds = entraUserIds.ToList();
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (!allIds.Any())
            return result;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Chunk into batches of 2000 for Dapper IN clause safety
        foreach (var batch in ChunkList(allIds, 2000))
        {
            var rows = await conn.QueryAsync<(string SourceUniqueId, Guid Id)>(sql,
                new { ConnectionId = connectionId, UserIds = batch });

            foreach (var row in rows)
            {
                result.TryAdd(row.SourceUniqueId, row.Id);
            }
        }

        _logger.LogInformation("LicenseRepository.ResolveEntraUserIdsAsync: Resolved {Resolved}/{Total} Entra user IDs for connection {ConnectionId}",
            result.Count, allIds.Count, connectionId);

        return result;
    }

    public async Task<Dictionary<string, Guid>> GetPoolIdsBySkuAsync(
        Guid connectionId,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT SkuId, Id
            FROM LicensePools
            WHERE SourceConnectionId = @ConnectionId AND IsActive = 1;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<(string SkuId, Guid Id)>(sql, new { ConnectionId = connectionId });
        return rows.ToDictionary(r => r.SkuId, r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static List<List<T>> ChunkList<T>(List<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += chunkSize)
        {
            chunks.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
        }
        return chunks;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Waste Analysis
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<LicenseWasteReport>> GetWastedLicensesAsync(
        int inactiveDays = 90,
        Guid? connectionId = null,
        CancellationToken ct = default)
    {
        // A license is considered wasted when:
        //   LastUsedAt is older than the threshold, OR
        //   LastUsedAt is null and AssignedAt is older than the threshold.
        // The cutoff date is computed in SQL via DATEADD to keep server-side logic consistent.
        const string sql = @"
            SELECT
                la.ObjectId,
                lp.Id                AS LicensePoolId,
                lp.SourceConnectionId,
                o.DisplayName        AS UserDisplayName,
                o.Username,
                o.UserPrincipalName,
                lp.SkuName,
                la.AssignmentSource,
                la.LastUsedAt,
                DATEDIFF(DAY,
                    COALESCE(la.LastUsedAt, la.AssignedAt, lp.LastSyncedAt),
                    GETUTCDATE())    AS DaysInactive,
                lp.CostPerUnitMonthly AS EstimatedMonthlyCost,
                lor.RecommendationType,
                lor.Id               AS RecommendationId
            FROM   LicenseAssignments la
            JOIN   LicensePools  lp ON lp.Id = la.LicensePoolId
            JOIN   Objects        o ON  o.Id = la.ObjectId
            LEFT JOIN LicenseOptimizationRecommendations lor
                   ON lor.ObjectId      = la.ObjectId
                  AND lor.LicensePoolId = la.LicensePoolId
                  AND lor.Status        = 'Pending'
            WHERE  la.IsActive = 1
              AND  lp.IsActive = 1
              AND  (@ConnectionId IS NULL OR lp.SourceConnectionId = @ConnectionId)
              AND  DATEDIFF(DAY,
                       COALESCE(la.LastUsedAt, la.AssignedAt, lp.LastSyncedAt),
                       GETUTCDATE()) >= @InactiveDays
            ORDER BY lp.CostPerUnitMonthly DESC, DaysInactive DESC;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<LicenseWasteReport>(sql,
            new
            {
                InactiveDays = inactiveDays,
                ConnectionId = connectionId
            });
        return rows.ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Service Plans
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<LicenseServicePlan>> GetServicePlansAsync(
        Guid poolId,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT Id, LicensePoolId, ServicePlanId, ServicePlanName,
                   ProvisioningStatus, AppliesTo
            FROM   LicenseServicePlans
            WHERE  LicensePoolId = @PoolId
            ORDER BY ServicePlanName;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<LicenseServicePlan>(sql, new { PoolId = poolId });
        return rows.ToList();
    }

    public async Task ReplaceServicePlansAsync(
        Guid poolId,
        IEnumerable<LicenseServicePlan> plans,
        CancellationToken ct = default)
    {
        const string deleteSql = "DELETE FROM LicenseServicePlans WHERE LicensePoolId = @PoolId;";
        const string insertSql = @"
            INSERT INTO LicenseServicePlans
                (Id, LicensePoolId, ServicePlanId, ServicePlanName, ProvisioningStatus, AppliesTo)
            VALUES
                (@Id, @LicensePoolId, @ServicePlanId, @ServicePlanName, @ProvisioningStatus, @AppliesTo);";

        var planList = plans.ToList();
        foreach (var p in planList)
        {
            p.LicensePoolId = poolId;
            if (p.Id == Guid.Empty) p.Id = Guid.NewGuid();
        }

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(deleteSql, new { PoolId = poolId }, tx);
        await conn.ExecuteAsync(insertSql, planList, tx);

        tx.Commit();
        _logger.LogInformation("LicenseRepository.ReplaceServicePlansAsync: Replaced {Count} service plans for pool {PoolId}", planList.Count, poolId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Usage Snapshots
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<int> CreateSnapshotsForAllPoolsAsync(int inactiveDays = 90, CancellationToken ct = default)
    {
        // One round-trip to grab every active pool id, then one CreateSnapshotAsync per pool.
        // The per-pool MERGE handles the "already snapshotted today" case so we can re-run safely.
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var poolIds = (await conn.QueryAsync<Guid>(
            "SELECT Id FROM LicensePools WHERE IsActive = 1")).ToList();
        var count = 0;
        foreach (var id in poolIds)
        {
            try
            {
                await CreateSnapshotAsync(id, inactiveDays, ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LicenseRepository.CreateSnapshotsForAllPoolsAsync: snapshot failed for pool {PoolId}", id);
            }
        }
        _logger.LogInformation("LicenseRepository.CreateSnapshotsForAllPoolsAsync: snapshotted {Count} of {Total} pools", count, poolIds.Count);
        return count;
    }

    public async Task<int> SeedHistoricalSnapshotsAsync(int daysBack = 90, bool includeExhaustionScenarios = false, CancellationToken ct = default)
    {
        if (daysBack < 1) daysBack = 90;
        if (daysBack > 365) daysBack = 365;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var pools = (await conn.QueryAsync<(Guid Id, int TotalUnits, int ConsumedUnits, decimal? CostPerUnitMonthly)>(
            "SELECT Id, TotalUnits, ConsumedUnits, CostPerUnitMonthly FROM LicensePools WHERE IsActive = 1")).ToList();

        var rng = new Random(12345); // deterministic so reseeding produces the same shape per pool
        var today = DateTime.UtcNow.Date;
        var rows = new List<(Guid PoolId, DateTime SnapshotDate, int TotalUnits, int ConsumedUnits, int WastedUnits, decimal? EstimatedWasteMonthly)>();

        // For "exhaustion scenarios": pick 2-3 pools with TotalUnits >= 25 to trend toward exhaustion.
        // The trainer needs at least one labeled "crossed TotalUnits within 365 days" trajectory to
        // learn from, so dev environments with no production history don't degenerate to all-365 labels.
        var exhaustionScenarioPoolIds = new HashSet<Guid>();
        if (includeExhaustionScenarios)
        {
            var candidates = pools.Where(p => p.TotalUnits >= 25).ToList();
            var pickCount = Math.Min(3, candidates.Count);
            for (int i = 0; i < pickCount && candidates.Count > 0; i++)
            {
                var idx = rng.Next(candidates.Count);
                exhaustionScenarioPoolIds.Add(candidates[idx].Id);
                candidates.RemoveAt(idx);
            }
        }

        foreach (var p in pools)
        {
            // Pre-existing snapshot dates so we never overwrite real history.
            var existingDates = (await conn.QueryAsync<DateTime>(
                "SELECT SnapshotDate FROM LicenseUsageSnapshots WHERE LicensePoolId = @Id AND SnapshotDate >= @Cutoff",
                new { p.Id, Cutoff = today.AddDays(-daysBack) })).ToHashSet();

            var isScenario = exhaustionScenarioPoolIds.Contains(p.Id);

            int startConsumed;
            int endTarget;
            if (isScenario && p.TotalUnits > 0)
            {
                // Override: trend linearly from ~30% utilization at window-start to ~95% at window-end.
                startConsumed = Math.Max(0, (int)(p.TotalUnits * 0.30));
                endTarget = (int)(p.TotalUnits * 0.95);
                _logger.LogInformation("LicenseRepository.SeedHistoricalSnapshotsAsync: pool {PoolId} flagged as exhaustion scenario (start={Start}, end={End}, total={Total})",
                    p.Id, startConsumed, endTarget, p.TotalUnits);
            }
            else
            {
                // Random-walk start: ~70-90% of current, drift toward current consumption.
                var startFraction = 0.7 + rng.NextDouble() * 0.2;
                startConsumed = Math.Max(0, (int)(p.ConsumedUnits * startFraction));
                endTarget = p.ConsumedUnits;
            }

            var current = startConsumed;
            for (int day = daysBack; day >= 0; day--)
            {
                var snapDate = today.AddDays(-day);
                if (existingDates.Contains(snapDate)) continue;

                int dayValue;
                if (isScenario && p.TotalUnits > 0)
                {
                    // Linear interpolation start→end with mild noise; clamped to [0, TotalUnits].
                    var progress = 1.0 - (day / (double)Math.Max(1, daysBack));
                    var basis = startConsumed + (endTarget - startConsumed) * progress;
                    var noise = (rng.NextDouble() - 0.5) * Math.Max(2, p.TotalUnits * 0.02);
                    dayValue = Math.Max(0, (int)(basis + noise));
                    dayValue = Math.Min(dayValue, p.TotalUnits);
                    current = dayValue;
                }
                else
                {
                    // Drift each day toward endTarget with small noise, clamped to [0, TotalUnits*1.05].
                    var drift = (endTarget - current) / Math.Max(1, day + 1);
                    var noise = (int)((rng.NextDouble() - 0.5) * Math.Max(2, endTarget * 0.04));
                    current = Math.Max(0, current + drift + noise);
                    if (p.TotalUnits > 0) current = Math.Min(current, (int)(p.TotalUnits * 1.05));
                    dayValue = current;
                }

                // Synthetic waste: 5-20% of consumed, again with noise. Capped at consumed.
                var wasteFraction = 0.05 + rng.NextDouble() * 0.15;
                var wasted = Math.Min(dayValue, (int)(dayValue * wasteFraction));
                decimal? estWaste = p.CostPerUnitMonthly.HasValue ? wasted * p.CostPerUnitMonthly.Value : (decimal?)null;

                rows.Add((p.Id, snapDate, p.TotalUnits, dayValue, wasted, estWaste));
            }
        }

        if (rows.Count == 0)
        {
            _logger.LogInformation("LicenseRepository.SeedHistoricalSnapshotsAsync: nothing to seed (no pools or all days already covered)");
            return 0;
        }

        // Bulk insert; guard against the unique (PoolId, Date) index by using NOT EXISTS.
        const string insertSql = @"
            INSERT INTO LicenseUsageSnapshots
                (Id, LicensePoolId, SnapshotDate, TotalUnits, ConsumedUnits, WastedUnits, EstimatedWasteMonthly)
            SELECT NEWID(), @PoolId, @SnapshotDate, @TotalUnits, @ConsumedUnits, @WastedUnits, @EstimatedWasteMonthly
            WHERE NOT EXISTS (
                SELECT 1 FROM LicenseUsageSnapshots
                WHERE LicensePoolId = @PoolId AND SnapshotDate = @SnapshotDate
            );";
        var written = await conn.ExecuteAsync(insertSql, rows.Select(r => new
        {
            PoolId = r.PoolId,
            SnapshotDate = r.SnapshotDate,
            TotalUnits = r.TotalUnits,
            ConsumedUnits = r.ConsumedUnits,
            WastedUnits = r.WastedUnits,
            EstimatedWasteMonthly = r.EstimatedWasteMonthly
        }));

        _logger.LogInformation("LicenseRepository.SeedHistoricalSnapshotsAsync: inserted {Written} of {Attempted} synthetic snapshots across {PoolCount} pools",
            written, rows.Count, pools.Count);
        return written;
    }

    public async Task<Guid> CreateSnapshotAsync(
        Guid poolId,
        int inactiveDays = 90,
        CancellationToken ct = default)
    {
        // Calculate WastedUnits from the live assignment table.
        // Uses MERGE so a duplicate snapshot for today is updated rather than rejected.
        const string mergeSql = @"
            DECLARE @Today      DATE          = CAST(GETUTCDATE() AS DATE);
            DECLARE @WastedUnits INT;
            DECLARE @TotalUnits  INT;
            DECLARE @ConsumedUnits INT;
            DECLARE @CostPerUnit DECIMAL(10,2);
            DECLARE @NewId       UNIQUEIDENTIFIER = NEWID();

            SELECT
                @TotalUnits    = TotalUnits,
                @ConsumedUnits = ConsumedUnits,
                @CostPerUnit   = CostPerUnitMonthly
            FROM LicensePools WHERE Id = @PoolId;

            SELECT @WastedUnits = COUNT(*)
            FROM   LicenseAssignments la
            WHERE  la.LicensePoolId = @PoolId
              AND  la.IsActive = 1
              AND  DATEDIFF(DAY,
                       COALESCE(la.LastUsedAt, la.AssignedAt, GETUTCDATE()),
                       GETUTCDATE()) >= @InactiveDays;

            MERGE LicenseUsageSnapshots AS target
            USING (SELECT @PoolId AS LicensePoolId, @Today AS SnapshotDate) AS source
                ON target.LicensePoolId = source.LicensePoolId
               AND target.SnapshotDate  = source.SnapshotDate
            WHEN MATCHED THEN
                UPDATE SET
                    TotalUnits            = @TotalUnits,
                    ConsumedUnits         = @ConsumedUnits,
                    WastedUnits           = @WastedUnits,
                    EstimatedWasteMonthly = CASE WHEN @CostPerUnit IS NOT NULL
                                                 THEN @WastedUnits * @CostPerUnit
                                                 ELSE NULL END
            WHEN NOT MATCHED THEN
                INSERT (Id, LicensePoolId, SnapshotDate, TotalUnits, ConsumedUnits,
                        WastedUnits, EstimatedWasteMonthly)
                VALUES (@NewId, @PoolId, @Today, @TotalUnits, @ConsumedUnits,
                        @WastedUnits,
                        CASE WHEN @CostPerUnit IS NOT NULL
                             THEN @WastedUnits * @CostPerUnit
                             ELSE NULL END);

            SELECT Id FROM LicenseUsageSnapshots
            WHERE LicensePoolId = @PoolId AND SnapshotDate = @Today;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var snapshotId = await conn.QuerySingleAsync<Guid>(mergeSql,
            new { PoolId = poolId, InactiveDays = inactiveDays });

        _logger.LogInformation("LicenseRepository.CreateSnapshotAsync: Snapshot {SnapshotId} created/updated for pool {PoolId}", snapshotId, poolId);
        return snapshotId;
    }

    public async Task<List<LicenseUsageSnapshot>> GetSnapshotsAsync(
        Guid poolId,
        int maxDays = 90,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT s.Id, s.LicensePoolId, s.SnapshotDate,
                   s.TotalUnits, s.ConsumedUnits, s.WastedUnits,
                   s.EstimatedWasteMonthly,
                   lp.SkuName
            FROM   LicenseUsageSnapshots s
            JOIN   LicensePools lp ON lp.Id = s.LicensePoolId
            WHERE  s.LicensePoolId = @PoolId
              AND  s.SnapshotDate  >= CAST(DATEADD(DAY, -@MaxDays, GETUTCDATE()) AS DATE)
            ORDER BY s.SnapshotDate DESC;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<LicenseUsageSnapshot>(sql,
            new { PoolId = poolId, MaxDays = maxDays });
        return rows.ToList();
    }

    public async Task<Dictionary<Guid, List<LicenseUsageSnapshot>>> GetAllRecentSnapshotsAsync(
        int maxDays = 60,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT s.Id, s.LicensePoolId, s.SnapshotDate,
                   s.TotalUnits, s.ConsumedUnits, s.WastedUnits,
                   s.EstimatedWasteMonthly
            FROM   LicenseUsageSnapshots s
            WHERE  s.SnapshotDate >= CAST(DATEADD(DAY, -@MaxDays, GETUTCDATE()) AS DATE)
            ORDER BY s.LicensePoolId, s.SnapshotDate;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<LicenseUsageSnapshot>(sql, new { MaxDays = maxDays });

        var byPool = new Dictionary<Guid, List<LicenseUsageSnapshot>>();
        foreach (var s in rows)
        {
            if (!byPool.TryGetValue(s.LicensePoolId, out var list))
            {
                list = new List<LicenseUsageSnapshot>();
                byPool[s.LicensePoolId] = list;
            }
            list.Add(s);
        }
        return byPool;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dashboard
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<LicenseDashboardSummary> GetDashboardSummaryAsync(
        Guid? connectionId = null,
        int inactiveDays = 90,
        CancellationToken ct = default)
    {
        // Single round-trip: aggregate pools, compute waste count, and pending recs.
        const string sql = @"
            SELECT
                COUNT(DISTINCT lp.Id)               AS PoolCount,
                SUM(lp.TotalUnits)                  AS TotalLicenses,
                SUM(lp.ConsumedUnits)               AS ConsumedLicenses,
                ISNULL(SUM(lp.ConsumedUnits * ISNULL(lp.CostPerUnitMonthly, 0)), 0)
                                                    AS TotalMonthlySpend
            FROM LicensePools lp
            WHERE lp.IsActive = 1
              AND (@ConnectionId IS NULL OR lp.SourceConnectionId = @ConnectionId);

            SELECT COUNT(*) AS WastedLicenses,
                   ISNULL(SUM(lp.CostPerUnitMonthly), 0) AS EstimatedMonthlyWaste
            FROM   LicenseAssignments la
            JOIN   LicensePools lp ON lp.Id = la.LicensePoolId
            WHERE  la.IsActive = 1
              AND  lp.IsActive = 1
              AND  (@ConnectionId IS NULL OR lp.SourceConnectionId = @ConnectionId)
              AND  DATEDIFF(DAY,
                       COALESCE(la.LastUsedAt, la.AssignedAt, lp.LastSyncedAt),
                       GETUTCDATE()) >= @InactiveDays;

            SELECT COUNT(*) AS PendingRecommendations
            FROM   LicenseOptimizationRecommendations lor
            JOIN   Objects o ON o.Id = lor.ObjectId
            WHERE  lor.Status = 'Pending'
              AND  (@ConnectionId IS NULL OR lor.LicensePoolId IN (
                       SELECT Id FROM LicensePools
                       WHERE SourceConnectionId = @ConnectionId));";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        using var multi = await conn.QueryMultipleAsync(sql,
            new { ConnectionId = connectionId, InactiveDays = inactiveDays });

        var poolRow  = await multi.ReadSingleAsync();
        var wasteRow = await multi.ReadSingleAsync();
        var recRow   = await multi.ReadSingleAsync();

        return new LicenseDashboardSummary
        {
            PoolCount               = (int)(poolRow.PoolCount ?? 0),
            TotalLicenses           = (int)(poolRow.TotalLicenses ?? 0),
            ConsumedLicenses        = (int)(poolRow.ConsumedLicenses ?? 0),
            TotalMonthlySpend       = (decimal)(poolRow.TotalMonthlySpend ?? 0m),
            WastedLicenses          = (int)(wasteRow.WastedLicenses ?? 0),
            EstimatedMonthlyWaste   = (decimal)(wasteRow.EstimatedMonthlyWaste ?? 0m),
            PendingRecommendations  = (int)(recRow.PendingRecommendations ?? 0),
            InactiveDaysThreshold   = inactiveDays
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Optimization Recommendations
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<LicenseOptimizationRecommendation>> GetOptimizationRecommendationsAsync(
        string? status = "Pending",
        Guid? connectionId = null,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT lor.Id, lor.ObjectId, lor.LicensePoolId, lor.RecommendationType,
                   lor.CurrentSkuName, lor.RecommendedSkuName, lor.Reason,
                   lor.EstimatedMonthlySavings, lor.Status,
                   lor.CreatedAt, lor.ReviewedBy, lor.ReviewedAt, lor.AppliedAt,
                   o.DisplayName        AS UserDisplayName,
                   o.Username,
                   o.UserPrincipalName
            FROM   LicenseOptimizationRecommendations lor
            JOIN   Objects o ON o.Id = lor.ObjectId
            WHERE  (@Status IS NULL OR lor.Status = @Status)
              AND  (@ConnectionId IS NULL OR lor.LicensePoolId IN (
                       SELECT Id FROM LicensePools
                       WHERE SourceConnectionId = @ConnectionId))
            ORDER BY lor.EstimatedMonthlySavings DESC, lor.CreatedAt DESC;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<LicenseOptimizationRecommendation>(sql,
            new { Status = status, ConnectionId = connectionId });
        return rows.ToList();
    }

    public async Task<LicenseOptimizationRecommendation?> GetRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT lor.Id, lor.ObjectId, lor.LicensePoolId, lor.RecommendationType,
                   lor.CurrentSkuName, lor.RecommendedSkuName, lor.Reason,
                   lor.EstimatedMonthlySavings, lor.Status,
                   lor.CreatedAt, lor.ReviewedBy, lor.ReviewedAt, lor.AppliedAt,
                   o.DisplayName        AS UserDisplayName,
                   o.Username,
                   o.UserPrincipalName
            FROM   LicenseOptimizationRecommendations lor
            JOIN   Objects o ON o.Id = lor.ObjectId
            WHERE  lor.Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<LicenseOptimizationRecommendation>(sql,
            new { Id = recommendationId });
    }

    public async Task<Guid> CreateRecommendationAsync(
        LicenseOptimizationRecommendation recommendation,
        CancellationToken ct = default)
    {
        if (recommendation.Id == Guid.Empty) recommendation.Id = Guid.NewGuid();
        recommendation.CreatedAt = DateTime.UtcNow;

        const string sql = @"
            INSERT INTO LicenseOptimizationRecommendations
                (Id, ObjectId, LicensePoolId, RecommendationType,
                 CurrentSkuName, RecommendedSkuName, Reason,
                 EstimatedMonthlySavings, Status, CreatedAt)
            VALUES
                (@Id, @ObjectId, @LicensePoolId, @RecommendationType,
                 @CurrentSkuName, @RecommendedSkuName, @Reason,
                 @EstimatedMonthlySavings, @Status, @CreatedAt);";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, recommendation);

        _logger.LogInformation("LicenseRepository.CreateRecommendationAsync: Created recommendation {RecId} ({Type}) for object {ObjectId}",
            recommendation.Id, recommendation.RecommendationType, recommendation.ObjectId);

        return recommendation.Id;
    }

    public async Task ApproveRecommendationAsync(
        Guid recommendationId,
        string reviewerName,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE LicenseOptimizationRecommendations
            SET  Status     = 'Approved',
                 ReviewedBy = @ReviewedBy,
                 ReviewedAt = GETUTCDATE()
            WHERE Id = @Id AND Status = 'Pending';";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(sql,
            new { Id = recommendationId, ReviewedBy = reviewerName });

        if (affected == 0)
            _logger.LogWarning("LicenseRepository.ApproveRecommendationAsync: No pending recommendation found for {RecId}", recommendationId);
        else
            _logger.LogInformation("LicenseRepository.ApproveRecommendationAsync: Recommendation {RecId} approved by {Reviewer}", recommendationId, reviewerName);
    }

    public async Task DismissRecommendationAsync(
        Guid recommendationId,
        string reviewerName,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE LicenseOptimizationRecommendations
            SET  Status     = 'Dismissed',
                 ReviewedBy = @ReviewedBy,
                 ReviewedAt = GETUTCDATE()
            WHERE Id = @Id AND Status IN ('Pending', 'Approved');";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(sql,
            new { Id = recommendationId, ReviewedBy = reviewerName });

        if (affected == 0)
            _logger.LogWarning("LicenseRepository.DismissRecommendationAsync: Recommendation {RecId} not found or already applied", recommendationId);
        else
            _logger.LogInformation("LicenseRepository.DismissRecommendationAsync: Recommendation {RecId} dismissed by {Reviewer}", recommendationId, reviewerName);
    }

    public async Task MarkRecommendationAppliedAsync(
        Guid recommendationId,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE LicenseOptimizationRecommendations
            SET  Status    = 'Applied',
                 AppliedAt = GETUTCDATE()
            WHERE Id = @Id AND Status = 'Approved';";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(sql, new { Id = recommendationId });

        if (affected == 0)
            _logger.LogWarning("LicenseRepository.MarkRecommendationAppliedAsync: Recommendation {RecId} not found or not in Approved state", recommendationId);
        else
            _logger.LogInformation("LicenseRepository.MarkRecommendationAppliedAsync: Recommendation {RecId} marked as Applied", recommendationId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V071: License Categories
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<LicenseCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<LicenseCategory>(@"
            SELECT Id, Name, Description, Color, Icon, SortOrder, IsBuiltIn, IsActive, CreatedAt, ModifiedAt
            FROM LicenseCategories WHERE IsActive = 1 ORDER BY SortOrder, Name");
        return rows.ToList();
    }

    public async Task<List<LicenseCategory>> GetCategoriesWithStatsAsync(Guid? connectionId = null, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT c.Id, c.Name, c.Description, c.Color, c.Icon, c.SortOrder, c.IsBuiltIn, c.IsActive, c.CreatedAt, c.ModifiedAt,
                   ISNULL(COUNT(p.Id), 0) AS PoolCount,
                   ISNULL(SUM(CASE
                       WHEN p.BillingPeriod = 'Annual' THEN (p.CostPerUnitMonthly / 12.0) * p.ConsumedUnits
                       ELSE ISNULL(p.CostPerUnitMonthly, 0) * p.ConsumedUnits
                   END), 0) AS TotalMonthlySpend
            FROM LicenseCategories c
            LEFT JOIN LicensePools p ON p.LicenseCategoryId = c.Id AND p.IsActive = 1
                " + (connectionId.HasValue ? " AND p.SourceConnectionId = @connectionId " : "") + @"
            WHERE c.IsActive = 1
            GROUP BY c.Id, c.Name, c.Description, c.Color, c.Icon, c.SortOrder, c.IsBuiltIn, c.IsActive, c.CreatedAt, c.ModifiedAt
            ORDER BY c.SortOrder, c.Name";
        var rows = await conn.QueryAsync<LicenseCategory>(sql, new { connectionId });
        return rows.ToList();
    }

    public async Task<Guid> CreateCategoryAsync(LicenseCategory category, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        if (category.Id == Guid.Empty) category.Id = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO LicenseCategories (Id, Name, Description, Color, Icon, SortOrder, IsBuiltIn, IsActive, CreatedAt)
            VALUES (@Id, @Name, @Description, @Color, @Icon, @SortOrder, 0, 1, GETUTCDATE())",
            category);
        return category.Id;
    }

    public async Task UpdateCategoryAsync(LicenseCategory category, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE LicenseCategories SET
                Name = @Name, Description = @Description, Color = @Color, Icon = @Icon,
                SortOrder = @SortOrder, ModifiedAt = GETUTCDATE()
            WHERE Id = @Id AND IsBuiltIn = 0",
            category);
    }

    public async Task DeleteCategoryAsync(Guid categoryId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE LicenseCategories SET IsActive = 0, ModifiedAt = GETUTCDATE()
            WHERE Id = @categoryId AND IsBuiltIn = 0",
            new { categoryId });
        // Null out category on all pools using it
        await conn.ExecuteAsync(@"
            UPDATE LicensePools SET LicenseCategoryId = NULL WHERE LicenseCategoryId = @categoryId",
            new { categoryId });
    }

    public async Task AssignPoolToCategoryAsync(Guid poolId, Guid? categoryId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE LicensePools SET LicenseCategoryId = @categoryId WHERE Id = @poolId",
            new { poolId, categoryId });
    }

    public async Task<int> BulkAssignPoolsToCategoryAsync(IEnumerable<Guid> poolIds, Guid? categoryId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteAsync(@"
            UPDATE LicensePools SET LicenseCategoryId = @categoryId WHERE Id IN @poolIds",
            new { poolIds = poolIds.ToList(), categoryId });
    }

    private async Task LogPoolChangeAsync(Guid poolId, string propertyName, string? newValue)
    {
        if (_auditLog == null) return;
        try
        {
            await _auditLog.LogChangeAsync(new DataAccessLibrary.Services.ChangeAuditEntry
            {
                OperationType = DataAccessLibrary.Services.ChangeOperationType.Update,
                EntityType = "LicensePool",
                EntityId = poolId,
                PropertyName = propertyName,
                NewValue = newValue,
                Source = "LicenseCenter"
            });
        }
        catch { /* audit is best-effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Enterprise Apps Overview (License Center wedge)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<EnterpriseAppSummary> GetEnterpriseAppSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            // Totals: count + new-this-week (proxy = LastSyncedAt for first sync, since
            // EnterpriseApps has no CreatedAt column — use Objects.CreatedAt via FK when
            // ObjectId is populated, otherwise fall back to LastSyncedAt window).
            var totals = await conn.QuerySingleOrDefaultAsync<(int TotalApps, int NewThisWeek)>(@"
                SELECT
                    COUNT(*) AS TotalApps,
                    SUM(CASE WHEN COALESCE(o.CreatedAt, ea.LastSyncedAt) > DATEADD(DAY, -7, GETUTCDATE())
                             THEN 1 ELSE 0 END) AS NewThisWeek
                FROM EnterpriseApps ea
                LEFT JOIN [Objects] o ON o.Id = ea.ObjectId
                WHERE ea.IsEnabled = 1;");

            if (totals.TotalApps == 0)
                return EnterpriseAppSummary.Empty;

            // Top 10 by sign-in volume (last 30 days). Join SignInLogs.AppId → EnterpriseApps.AppId.
            var topByVolume = (await conn.QueryAsync<EnterpriseAppRow>(@"
                SELECT TOP 10
                    ea.Id,
                    ea.DisplayName,
                    CAST(NULL AS NVARCHAR(500)) AS PublisherDomain,
                    COUNT(sl.Id) AS SignInCount30d,
                    MAX(sl.SignInDateTime) AS LastSignInAt,
                    CAST(0 AS BIT) AS HasHighPermission
                FROM EnterpriseApps ea
                INNER JOIN SignInLogs sl
                    ON sl.AppId = ea.AppId
                    AND sl.SignInDateTime > DATEADD(DAY, -30, GETUTCDATE())
                WHERE ea.IsEnabled = 1
                  AND ea.AppId IS NOT NULL
                GROUP BY ea.Id, ea.DisplayName
                ORDER BY COUNT(sl.Id) DESC;")).ToList();

            // Dormant: no SignInLogs in last 90 days (or AppId never seen). Take 10 newest.
            var dormantRows = (await conn.QueryAsync<EnterpriseAppRow>(@"
                SELECT TOP 10
                    ea.Id,
                    ea.DisplayName,
                    CAST(NULL AS NVARCHAR(500)) AS PublisherDomain,
                    0 AS SignInCount30d,
                    CAST(NULL AS DATETIME2) AS LastSignInAt,
                    CAST(0 AS BIT) AS HasHighPermission
                FROM EnterpriseApps ea
                WHERE ea.IsEnabled = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM SignInLogs sl
                      WHERE sl.AppId = ea.AppId
                        AND sl.SignInDateTime > DATEADD(DAY, -90, GETUTCDATE())
                  )
                ORDER BY ea.LastSyncedAt DESC;")).ToList();

            int dormantCount = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*)
                FROM EnterpriseApps ea
                WHERE ea.IsEnabled = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM SignInLogs sl
                      WHERE sl.AppId = ea.AppId
                        AND sl.SignInDateTime > DATEADD(DAY, -90, GETUTCDATE())
                  );");

            // High-permission: enterprise apps that hold an oAuth2PermissionGrant whose
            // scope string contains one of the HighPrivilegeScopes tokens. The grant
            // sits in Objects with ObjectClass='oAuth2PermissionGrant', and the scope
            // payload lives in ObjectAttributes('scope') with clientId pointing at the
            // service principal's AppId / objectGuid. The grant table may be empty if
            // oauth2 grants have not been synced — return empty list silently.
            var highPerm = new List<EnterpriseAppRow>();
            int highPermCount = 0;
            try
            {
                // Pull all granted scopes + clientId pairs. Set is small (low thousands).
                var grants = (await conn.QueryAsync<(string? ClientId, string? Scope)>(@"
                    SELECT
                        clientAttr.AttributeValue AS ClientId,
                        scopeAttr.AttributeValue  AS Scope
                    FROM [Objects] g
                    LEFT JOIN ObjectAttributes clientAttr
                        ON clientAttr.ObjectId = g.Id AND clientAttr.AttributeName = 'clientId'
                    LEFT JOIN ObjectAttributes scopeAttr
                        ON scopeAttr.ObjectId = g.Id AND scopeAttr.AttributeName = 'scope'
                    WHERE g.ObjectClass = 'oAuth2PermissionGrant'
                      AND g.DeletedAt IS NULL;")).ToList();

                var highPrivClientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (clientId, scope) in grants)
                {
                    if (string.IsNullOrEmpty(clientId)) continue;
                    if (HighPrivilegeScopes.ContainsHighPrivilege(scope))
                        highPrivClientIds.Add(clientId);
                }

                if (highPrivClientIds.Count > 0)
                {
                    // OAuth2 grant.clientId is a servicePrincipal *object id* (Graph),
                    // which lands in Objects.SourceUniqueId for the matching SP. Join
                    // via EnterpriseApps.ServicePrincipalId (also the SP id).
                    var rows = (await conn.QueryAsync<EnterpriseAppRow>(@"
                        SELECT DISTINCT TOP 10
                            ea.Id,
                            ea.DisplayName,
                            CAST(NULL AS NVARCHAR(500)) AS PublisherDomain,
                            0 AS SignInCount30d,
                            CAST(NULL AS DATETIME2) AS LastSignInAt,
                            CAST(1 AS BIT) AS HasHighPermission
                        FROM EnterpriseApps ea
                        WHERE ea.IsEnabled = 1
                          AND ea.ServicePrincipalId IN @ClientIds
                        ORDER BY ea.DisplayName;",
                        new { ClientIds = highPrivClientIds.ToArray() })).ToList();

                    highPerm = rows;
                    highPermCount = await conn.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(DISTINCT ea.Id)
                        FROM EnterpriseApps ea
                        WHERE ea.IsEnabled = 1
                          AND ea.ServicePrincipalId IN @ClientIds;",
                        new { ClientIds = highPrivClientIds.ToArray() });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("GetEnterpriseAppSummaryAsync: high-permission lookup failed (oauth2 grants may not be synced yet): {Message}", ex.Message);
            }

            return new EnterpriseAppSummary(
                TotalApps: totals.TotalApps,
                NewThisWeek: totals.NewThisWeek,
                DormantCount: dormantCount,
                HighPermissionCount: highPermCount,
                TopByVolume: topByVolume,
                TopDormant: dormantRows,
                HighPermission: highPerm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetEnterpriseAppSummaryAsync failed");
            return EnterpriseAppSummary.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CAL Auto-Attribution Candidates (computed on-demand)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<LicenseAttributionCandidate>> GetActivityBasedCandidatesAsync(
        Guid objectId,
        CancellationToken ct = default)
    {
        var results = new List<LicenseAttributionCandidate>();
        if (objectId == Guid.Empty) return results;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // 1. Get the object's class + active/disabled flags. Drop disabled accounts.
        const string objSql = @"
            SELECT TOP 1 ObjectClass, IsActive, UserAccountControl
            FROM   Objects
            WHERE  Id = @Id AND DeletedAt IS NULL;";
        var obj = await conn.QuerySingleOrDefaultAsync<(string? ObjectClass, bool IsActive, int? UserAccountControl)>(
            new CommandDefinition(objSql, new { Id = objectId }, cancellationToken: ct));
        if (obj.ObjectClass == null) return results;
        if (!obj.IsActive) return results;
        if (obj.UserAccountControl.HasValue && (obj.UserAccountControl.Value & 2) != 0) return results;

        var objectClassLower = obj.ObjectClass.ToLowerInvariant();
        // Service principals / gMSAs / MSAs are non-interactive identities and don't consume User CALs
        // in any licensing model — exclude them. Only "user" maps to UserCAL.
        var isUserLike = objectClassLower == "user";
        var isComputer = objectClassLower == "computer";
        if (!isUserLike && !isComputer) return results;

        var targetLicenseType = isUserLike ? "UserCAL" : "DeviceCAL";

        // 2. Pull candidate pools: matching LicenseType, active, not auto-created from sync,
        //    has seats, object NOT already actively assigned, not dismissed.
        //    NOTE: lp.LicenseType comparison relies on the database's case-insensitive collation
        //    (the project default). Binary-collation deployments would need UPPER() on both sides.
        const string poolSql = @"
            SELECT lp.Id, lp.SkuName, lp.FriendlyName, lp.LicenseType, lp.PoolType, lp.AvailableUnits
            FROM   LicensePools lp
            WHERE  lp.IsActive = 1
              AND  lp.AutoCreatedFromSync = 0
              AND  lp.AvailableUnits > 0
              AND  lp.LicenseType = @LicenseType
              AND  NOT EXISTS (
                    SELECT 1 FROM LicenseAssignments la
                    WHERE  la.LicensePoolId = lp.Id
                      AND  la.ObjectId      = @ObjectId
                      AND  la.IsActive      = 1)
              AND  NOT EXISTS (
                    SELECT 1 FROM Settings s
                    WHERE  s.Category = 'LicenseManagement'
                      AND  s.[Key]    = 'DismissedCandidate:' + CAST(@ObjectId AS NVARCHAR(36)) + ':' + CAST(lp.Id AS NVARCHAR(36)));";

        var pools = (await conn.QueryAsync<(Guid Id, string SkuName, string? FriendlyName, string LicenseType, string PoolType, int AvailableUnits)>(
            new CommandDefinition(poolSql, new { LicenseType = targetLicenseType, ObjectId = objectId }, cancellationToken: ct))).ToList();

        if (pools.Count == 0) return results;

        // 3. Compute the activity signal once for the object — same signal applies to every candidate pool of that type.
        string? signalText = null;
        string? confidence = null;
        string? reasonDetail = null;

        if (isUserLike)
        {
            // SignInSummary in last 30d.
            const string signInSql = @"
                SELECT ISNULL(SUM(InteractiveCount), 0) AS InteractiveTotal,
                       ISNULL(SUM(SuccessCount), 0)     AS SuccessTotal,
                       MAX(SummaryDate)                 AS LastDate
                FROM   SignInSummary
                WHERE  ObjectId = @Id
                  AND  SummaryDate >= DATEADD(DAY, -30, GETUTCDATE());";

            var (interactiveTotal, successTotal, lastDate) =
                await conn.QuerySingleAsync<(int InteractiveTotal, int SuccessTotal, DateTime? LastDate)>(
                    new CommandDefinition(signInSql, new { Id = objectId }, cancellationToken: ct));

            if (interactiveTotal >= 10)
            {
                confidence = "High";
                signalText = string.Concat(interactiveTotal.ToString(), " sign-ins last 30 days");
                reasonDetail = "User has heavy interactive sign-in activity in the last 30 days, strongly indicating CAL consumption.";
            }
            else if (interactiveTotal >= 3)
            {
                confidence = "Medium";
                signalText = string.Concat(interactiveTotal.ToString(), " sign-ins last 30 days");
                reasonDetail = "User has moderate interactive sign-in activity in the last 30 days.";
            }
            else if (interactiveTotal >= 1)
            {
                confidence = "Low";
                signalText = string.Concat(interactiveTotal.ToString(), " sign-ins last 30 days");
                reasonDetail = "User has light interactive sign-in activity in the last 30 days.";
            }
            else
            {
                // Fallback: lastLogonTimestamp from ObjectAttributes within 30d.
                const string lastLogonSql = @"
                    SELECT TOP 1 AttributeValue
                    FROM   ObjectAttributes
                    WHERE  ObjectId = @Id
                      AND  AttributeName IN ('lastLogonTimestamp','lastLogon')
                      AND  AttributeValue IS NOT NULL
                    ORDER BY AttributeName DESC;";
                var raw = await conn.QuerySingleOrDefaultAsync<string?>(
                    new CommandDefinition(lastLogonSql, new { Id = objectId }, cancellationToken: ct));

                if (TryParseAdTimestamp(raw, out var ts) && (DateTime.UtcNow - ts).TotalDays <= 30)
                {
                    confidence = "Low";
                    signalText = string.Concat("Last logon ", ts.ToString("MMM dd"));
                    reasonDetail = "User logged on within the last 30 days (directory lastLogonTimestamp).";
                }
            }
        }
        else
        {
            // Device CAL — computer.
            const string compSql = @"
                SELECT TOP 1 IsActive
                FROM   Objects
                WHERE  Id = @Id;";
            var computerActive = await conn.QuerySingleOrDefaultAsync<bool>(
                new CommandDefinition(compSql, new { Id = objectId }, cancellationToken: ct));

            // Get OS + last logon timestamp from attributes.
            const string compAttrSql = @"
                SELECT AttributeName, AttributeValue
                FROM   ObjectAttributes
                WHERE  ObjectId = @Id
                  AND  AttributeName IN ('operatingSystem','lastLogonTimestamp','lastLogon');";
            var attrs = (await conn.QueryAsync<(string AttributeName, string? AttributeValue)>(
                new CommandDefinition(compAttrSql, new { Id = objectId }, cancellationToken: ct))).ToList();

            string? osValue = attrs.FirstOrDefault(a => a.AttributeName == "operatingSystem").AttributeValue;
            string? lastLogonRaw = attrs
                .Where(a => a.AttributeName is "lastLogonTimestamp" or "lastLogon")
                .OrderByDescending(a => a.AttributeName)
                .Select(a => a.AttributeValue)
                .FirstOrDefault();

            DateTime? lastLogon = TryParseAdTimestamp(lastLogonRaw, out var parsed) ? parsed : (DateTime?)null;

            // Holds at least one SqlServerPermission resolved to this Object → boosts confidence to High.
            const string sqlPermSql = @"
                SELECT COUNT(1) FROM SqlServerPermissions WHERE ObjectId = @Id AND IsActive = 1;";
            int sqlPermCount = 0;
            try
            {
                sqlPermCount = await conn.ExecuteScalarAsync<int>(
                    new CommandDefinition(sqlPermSql, new { Id = objectId }, cancellationToken: ct));
            }
            catch
            {
                sqlPermCount = 0;
            }

            var daysSince = lastLogon.HasValue ? (DateTime.UtcNow - lastLogon.Value).TotalDays : double.MaxValue;

            if (computerActive && !string.IsNullOrEmpty(osValue) && daysSince <= 30 && sqlPermCount > 0)
            {
                confidence = "High";
                signalText = string.Concat("Active + ", sqlPermCount.ToString(), " SQL permission(s)");
                reasonDetail = "Computer is active, has an OS, logged on in last 30 days, and holds SQL Server permissions — strongly indicates CAL consumption.";
            }
            else if (computerActive && daysSince <= 30)
            {
                confidence = "Medium";
                signalText = "Active in last 30 days";
                reasonDetail = "Computer logged on within the last 30 days.";
            }
            else if (lastLogon.HasValue && daysSince > 30 && daysSince <= 90)
            {
                confidence = "Low";
                signalText = string.Concat("Last seen ", lastLogon.Value.ToString("MMM dd"));
                reasonDetail = "Computer logged on between 30 and 90 days ago.";
            }
        }

        if (confidence == null) return results;

        // 4. Build candidate rows for every eligible pool, all sharing the computed signal.
        foreach (var p in pools)
        {
            results.Add(new LicenseAttributionCandidate
            {
                PoolId = p.Id,
                PoolName = !string.IsNullOrEmpty(p.FriendlyName) ? p.FriendlyName : p.SkuName,
                LicenseType = p.LicenseType,
                PoolType = p.PoolType,
                SignalText = signalText ?? "",
                Confidence = confidence,
                ReasonDetail = reasonDetail ?? "",
                AvailableUnits = p.AvailableUnits
            });
        }

        // Sort High → Medium → Low.
        var rank = new Dictionary<string, int> { ["High"] = 0, ["Medium"] = 1, ["Low"] = 2 };
        return results
            .OrderBy(c => rank.TryGetValue(c.Confidence, out var r) ? r : 99)
            .ThenBy(c => c.PoolName)
            .ToList();
    }

    /// <summary>
    /// Parses either a Windows FILETIME long (ticks since 1601) or a DateTime string
    /// out of an AD attribute value. Returns true on success.
    /// </summary>
    private static bool TryParseAdTimestamp(string? raw, out DateTime ts)
    {
        ts = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (long.TryParse(raw, out var fileTime) && fileTime > 0)
        {
            try { ts = DateTime.FromFileTimeUtc(fileTime); return true; }
            catch { return false; }
        }
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            ts = parsed;
            return true;
        }
        return false;
    }
}
