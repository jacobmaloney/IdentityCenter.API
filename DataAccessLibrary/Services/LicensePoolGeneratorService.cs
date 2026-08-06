using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Services;

/// <summary>
/// Auto-generates license pools from discovery data. Uses raw Dapper for
/// bulk analytic queries (counting servers per connection/edition).
/// </summary>
public class LicensePoolGeneratorService : ILicensePoolGeneratorService
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    // SQL Server category ID from V071 seed
    private static readonly Guid SqlServerCategoryId = new("C0710000-0000-0000-0000-000000000003");
    private static readonly Guid DevTestCategoryId = new("C0710000-0000-0000-0000-000000000005");

    public LicensePoolGeneratorService(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> GenerateSqlPoolsAsync(Guid connectionId, CancellationToken ct = default)
    {
        var created = 0;
        try
        {
            using var conn = new SqlConnection(_connectionString);

            // Aggregate discovered SQL servers by edition for this connection
            var discovery = (await conn.QueryAsync<SqlDiscoveryRow>(@"
                SELECT
                    ed.AttributeValue AS Edition,
                    COUNT(DISTINCT o.Id) AS ServerCount,
                    ISNULL(SUM(CAST(c.AttributeValue AS INT)), 0) AS TotalCores
                FROM Objects o
                INNER JOIN ObjectAttributes ed ON ed.ObjectId = o.Id AND ed.AttributeName = 'sqlServerEdition'
                LEFT JOIN ObjectAttributes c ON c.ObjectId = o.Id AND c.AttributeName = 'cpuCores'
                WHERE o.ObjectClass = 'computer'
                  AND o.DeletedAt IS NULL
                  AND o.SourceConnectionId = @connectionId
                GROUP BY ed.AttributeValue",
                new { connectionId })).ToList();

            if (!discovery.Any())
            {
                _logger.LogInformation($"LicensePoolGenerator: no SQL servers discovered for connection {connectionId}");
                return 0;
            }

            // Per-core pricing from Settings(SqlLicense/*) — single source of truth.
            var costs = await SqlLicenseCostSettings.LoadAsync(conn);

            foreach (var row in discovery)
            {
                if (ct.IsCancellationRequested) break;

                // Enterprise & Standard get per-core + instance pools
                if (row.Edition == "Enterprise" || row.Edition == "Standard")
                {
                    created += await UpsertCorePoolAsync(conn, connectionId, row.Edition, row.TotalCores, row.ServerCount, costs);
                    created += await UpsertInstancePoolAsync(conn, connectionId, row.Edition, row.ServerCount, 0m, SqlServerCategoryId);
                }
                else if (row.Edition == "Express")
                {
                    created += await UpsertInstancePoolAsync(conn, connectionId, "Express", row.ServerCount, 0m, SqlServerCategoryId);
                }
                else if (row.Edition == "Developer")
                {
                    // Developer counts as Dev/Test category and shows as warning (prod risk)
                    created += await UpsertInstancePoolAsync(conn, connectionId, "Developer", row.ServerCount, 0m, DevTestCategoryId);
                }
            }

            _logger.LogInformation($"LicensePoolGenerator: generated/updated {created} SQL pool(s) for connection {connectionId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LicensePoolGenerator: GenerateSqlPoolsAsync failed");
            throw;
        }
        return created;
    }

    private async Task<int> UpsertCorePoolAsync(SqlConnection conn, Guid connectionId, string edition, int totalCores, int serverCount, SqlLicenseCostSettings.CostSet costs)
    {
        var skuId = $"SQL-{edition.ToUpper()[..3]}-CORE-{connectionId}";
        var costPerCoreAnnual = costs.PerCoreAnnualFor(edition);

        var affected = await conn.ExecuteAsync(@"
            MERGE LicensePools AS tgt
            USING (SELECT @skuId AS SkuId) AS src ON tgt.SkuId = src.SkuId
            WHEN MATCHED THEN UPDATE SET
                ConsumedUnits = @cores,
                LastSyncedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN INSERT (
                Id, SourceConnectionId, SkuId, SkuName, SkuPartNumber, FriendlyName,
                TotalUnits, ConsumedUnits, WarningUnits, SuspendedUnits,
                CostPerUnitMonthly, Currency, BillingPeriod, LicenseType,
                LicenseCategoryId, AutoCreatedFromSync, ReviewFrequencyDays, IsActive, LastSyncedAt
            ) VALUES (
                NEWID(), @connectionId, @skuId,
                @skuName, @skuPartNumber, @friendlyName,
                @totalUnits, @cores, 0, 0,
                @costPerUnit, 'USD', 'Annual', 'ServerCAL',
                @categoryId, 1, 90, 1, GETUTCDATE()
            );",
            new
            {
                skuId,
                connectionId,
                skuName = $"SQL Server {edition} — Core Licenses",
                skuPartNumber = $"SQL_{edition.ToUpper()[..3]}_CORE",
                friendlyName = $"SQL Server {edition} (per-core)",
                totalUnits = totalCores + (edition == "Enterprise" ? 200 : 100), // pretend buffer
                cores = totalCores,
                costPerUnit = costPerCoreAnnual,
                categoryId = SqlServerCategoryId
            });

        return affected;
    }

    private async Task<int> UpsertInstancePoolAsync(SqlConnection conn, Guid connectionId, string edition, int serverCount, decimal costPerUnit, Guid categoryId)
    {
        var skuId = $"SQL-{edition.ToUpper()[..3]}-SERVERS-{connectionId}";
        var warningUnits = edition == "Developer" ? serverCount : 0; // Developer in prod = warning

        var friendlyName = edition switch
        {
            "Express" => "SQL Server Express (free)",
            "Developer" => "SQL Server Developer (non-prod only)",
            _ => $"SQL {edition} Servers"
        };

        var skuName = edition switch
        {
            "Developer" => "SQL Server Developer — Instances (NOT for production)",
            "Express" => "SQL Server Express — Instances",
            _ => $"SQL Server {edition} — Server Instances"
        };

        var affected = await conn.ExecuteAsync(@"
            MERGE LicensePools AS tgt
            USING (SELECT @skuId AS SkuId) AS src ON tgt.SkuId = src.SkuId
            WHEN MATCHED THEN UPDATE SET
                ConsumedUnits = @serverCount,
                WarningUnits = @warningUnits,
                LastSyncedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN INSERT (
                Id, SourceConnectionId, SkuId, SkuName, SkuPartNumber, FriendlyName,
                TotalUnits, ConsumedUnits, WarningUnits, SuspendedUnits,
                CostPerUnitMonthly, Currency, BillingPeriod, LicenseType,
                LicenseCategoryId, AutoCreatedFromSync, ReviewFrequencyDays, IsActive, LastSyncedAt
            ) VALUES (
                NEWID(), @connectionId, @skuId,
                @skuName, @skuPartNumber, @friendlyName,
                @totalUnits, @serverCount, @warningUnits, 0,
                @costPerUnit, 'USD', 'Annual', 'ServerCAL',
                @categoryId, 1, 90, 1, GETUTCDATE()
            );",
            new
            {
                skuId,
                connectionId,
                skuName,
                skuPartNumber = $"SQL_{edition.ToUpper()}",
                friendlyName,
                totalUnits = serverCount + (edition == "Express" ? 50 : 0),
                serverCount,
                warningUnits,
                costPerUnit,
                categoryId
            });

        return affected;
    }

    public async Task<int> RegeneratePoolsForConnectionAsync(Guid connectionId, CancellationToken ct = default)
    {
        // Future: add Exchange, SharePoint, etc. For now, just SQL.
        return await GenerateSqlPoolsAsync(connectionId, ct);
    }

    public async Task<int> RegenerateAllPoolsAsync(CancellationToken ct = default)
    {
        var total = 0;
        using var conn = new SqlConnection(_connectionString);
        var connections = await conn.QueryAsync<Guid>(@"
            SELECT Id FROM DirectoryConnections WHERE IsActive = 1");

        foreach (var connectionId in connections)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                total += await GenerateSqlPoolsAsync(connectionId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"LicensePoolGenerator: regeneration failed for connection {connectionId}: {ex.Message}");
            }
        }

        _logger.LogInformation($"LicensePoolGenerator: total pools generated/updated across all connections: {total}");
        return total;
    }

    private class SqlDiscoveryRow
    {
        public string Edition { get; set; } = string.Empty;
        public int ServerCount { get; set; }
        public int TotalCores { get; set; }
    }
}
