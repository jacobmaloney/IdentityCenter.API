using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

public interface ILicenseOptimizationEngine
{
    /// <summary>
    /// Analyzes license usage and generates optimization recommendations.
    /// Call after license sync completes.
    /// </summary>
    Task<int> GenerateRecommendationsAsync(Guid? connectionId = null, CancellationToken ct = default);
}

public class LicenseOptimizationEngine : ILicenseOptimizationEngine
{
    private readonly string _connectionString;
    private readonly ILogger<LicenseOptimizationEngine> _logger;

    // Thresholds
    private const int InactiveDaysForRemoval = 90;
    private const int InactiveDaysForDowngrade = 60;
    private const int StaleRecommendationDays = 30;
    private const int MinDirectAssignmentsForReassign = 10;

    // Approximate monthly costs per SKU tier (USD) used as fallback when CostPerUnitMonthly is null
    private static readonly Dictionary<string, decimal> ApproximateMonthlyCosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTERPRISEPREMIUM"] = 57.00m,
        ["SPE_E5"] = 57.00m,
        ["MICROSOFT_365_E5"] = 57.00m,
        ["DEVELOPERPACK_E5"] = 57.00m,
        ["ENTERPRISEPACK"] = 36.00m,
        ["SPE_E3"] = 36.00m,
        ["MICROSOFT_365_E3"] = 36.00m,
        ["O365_BUSINESS_PREMIUM"] = 12.50m,
        ["SMB_BUSINESS_PREMIUM"] = 12.50m,
        ["O365_BUSINESS_ESSENTIALS"] = 6.00m,
        ["SMB_BUSINESS"] = 6.00m,
    };

    // Downgrade target mapping: current SKU part number -> recommended friendly name + approximate cost
    private static readonly Dictionary<string, (string RecommendedName, decimal ApproxCost)> DowngradeTargets =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTERPRISEPREMIUM"] = ("Microsoft 365 E3", 36.00m),
        ["SPE_E5"] = ("Microsoft 365 E3", 36.00m),
        ["MICROSOFT_365_E5"] = ("Microsoft 365 E3", 36.00m),
        ["DEVELOPERPACK_E5"] = ("Microsoft 365 E3", 36.00m),
        ["ENTERPRISEPACK"] = ("Microsoft 365 Business Basic", 6.00m),
        ["SPE_E3"] = ("Microsoft 365 Business Basic", 6.00m),
        ["MICROSOFT_365_E3"] = ("Microsoft 365 Business Basic", 6.00m),
        ["O365_BUSINESS_PREMIUM"] = ("Microsoft 365 Business Basic", 6.00m),
        ["SMB_BUSINESS_PREMIUM"] = ("Microsoft 365 Business Basic", 6.00m),
    };

    public LicenseOptimizationEngine(
        IConfiguration configuration,
        ILogger<LicenseOptimizationEngine> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<int> GenerateRecommendationsAsync(Guid? connectionId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("LicenseOptimizationEngine: Starting recommendation generation{Scope}",
            connectionId.HasValue ? $" for connection {connectionId}" : " (all connections)");

        int totalGenerated = 0;

        // Step 1: Clean up stale pending recommendations
        try
        {
            var staleRemoved = await CleanStalePendingRecommendationsAsync(ct);
            if (staleRemoved > 0)
                _logger.LogInformation("LicenseOptimizationEngine: Removed {Count} stale pending recommendations", staleRemoved);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LicenseOptimizationEngine: Failed to clean stale recommendations (non-fatal)");
        }

        // Step 2: Generate REMOVE recommendations for inactive users
        try
        {
            var removeCount = await GenerateRemoveRecommendationsAsync(connectionId, ct);
            totalGenerated += removeCount;
            _logger.LogInformation("LicenseOptimizationEngine: Generated {Count} Remove recommendations", removeCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LicenseOptimizationEngine: Failed to generate Remove recommendations (non-fatal)");
        }

        // Step 3: Generate DOWNGRADE recommendations based on M365 usage
        try
        {
            var downgradeCount = await GenerateDowngradeRecommendationsAsync(connectionId, ct);
            totalGenerated += downgradeCount;
            _logger.LogInformation("LicenseOptimizationEngine: Generated {Count} Downgrade recommendations", downgradeCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LicenseOptimizationEngine: Failed to generate Downgrade recommendations (non-fatal)");
        }

        // Step 4: Generate REASSIGN recommendations for direct assignments
        try
        {
            var reassignCount = await GenerateReassignRecommendationsAsync(connectionId, ct);
            totalGenerated += reassignCount;
            _logger.LogInformation("LicenseOptimizationEngine: Generated {Count} Reassign recommendations", reassignCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LicenseOptimizationEngine: Failed to generate Reassign recommendations (non-fatal)");
        }

        _logger.LogInformation("LicenseOptimizationEngine: Completed - generated {Total} total recommendations", totalGenerated);
        return totalGenerated;
    }

    /// <summary>
    /// Removes pending recommendations older than 30 days that were never acted upon.
    /// </summary>
    private async Task<int> CleanStalePendingRecommendationsAsync(CancellationToken ct)
    {
        const string sql = @"
            DELETE FROM LicenseOptimizationRecommendations
            WHERE Status = 'Pending'
              AND CreatedAt < DATEADD(DAY, -@StaleDays, GETUTCDATE());";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.ExecuteAsync(sql, new { StaleDays = StaleRecommendationDays });
    }

    /// <summary>
    /// Generates Remove recommendations for users with licenses who have not signed in for 90+ days.
    /// </summary>
    private async Task<int> GenerateRemoveRecommendationsAsync(Guid? connectionId, CancellationToken ct)
    {
        const string sql = @"
            SELECT la.ObjectId, la.LicensePoolId, lp.SkuName, lp.SkuPartNumber, lp.CostPerUnitMonthly,
                   o.DisplayName, o.Username, o.UserPrincipalName,
                   DATEDIFF(DAY, COALESCE(la.LastUsedAt, la.AssignedAt, la.LastSyncedAt), GETUTCDATE()) AS DaysInactive
            FROM LicenseAssignments la
            JOIN LicensePools lp ON lp.Id = la.LicensePoolId
            JOIN Objects o ON o.Id = la.ObjectId
            WHERE la.IsActive = 1 AND lp.IsActive = 1
              AND (@ConnectionId IS NULL OR lp.SourceConnectionId = @ConnectionId)
              AND DATEDIFF(DAY, COALESCE(la.LastUsedAt, la.AssignedAt, la.LastSyncedAt), GETUTCDATE()) >= @InactiveDays
              AND NOT EXISTS (
                  SELECT 1 FROM LicenseOptimizationRecommendations r
                  WHERE r.ObjectId = la.ObjectId AND r.LicensePoolId = la.LicensePoolId
                  AND r.Status IN ('Pending', 'Approved')
              );";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var candidates = (await conn.QueryAsync<dynamic>(sql,
            new { ConnectionId = connectionId, InactiveDays = InactiveDaysForRemoval })).ToList();

        if (candidates.Count == 0)
            return 0;

        int inserted = 0;
        const string insertSql = @"
            INSERT INTO LicenseOptimizationRecommendations
                (Id, ObjectId, LicensePoolId, RecommendationType,
                 CurrentSkuName, RecommendedSkuName, Reason,
                 EstimatedMonthlySavings, Status, CreatedAt)
            VALUES
                (@Id, @ObjectId, @LicensePoolId, @RecommendationType,
                 @CurrentSkuName, @RecommendedSkuName, @Reason,
                 @EstimatedMonthlySavings, @Status, @CreatedAt);";

        // Insert in chunks of 100 to avoid overwhelming the connection
        foreach (var batch in ChunkList(candidates, 100))
        {
            var recommendations = batch.Select(c =>
            {
                int daysInactive = (int)c.DaysInactive;
                string skuName = (string)c.SkuName;
                string? skuPartNumber = c.SkuPartNumber as string;
                decimal? costPerUnit = c.CostPerUnitMonthly as decimal?;

                // Use actual cost or approximate from known SKU prices
                decimal? savings = costPerUnit;
                if (savings == null && skuPartNumber != null)
                    ApproximateMonthlyCosts.TryGetValue(skuPartNumber, out var approx);

                // Re-attempt with out variable properly
                if (savings == null && skuPartNumber != null &&
                    ApproximateMonthlyCosts.TryGetValue(skuPartNumber, out var approxCost))
                {
                    savings = approxCost;
                }

                return new
                {
                    Id = Guid.NewGuid(),
                    ObjectId = (Guid)c.ObjectId,
                    LicensePoolId = (Guid)c.LicensePoolId,
                    RecommendationType = "Remove",
                    CurrentSkuName = skuName,
                    RecommendedSkuName = (string?)null,
                    Reason = $"No sign-in activity in {daysInactive} days. License assigned but unused.",
                    EstimatedMonthlySavings = savings,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                };
            }).ToList();

            await conn.ExecuteAsync(insertSql, recommendations);
            inserted += recommendations.Count;
        }

        return inserted;
    }

    /// <summary>
    /// Generates Downgrade recommendations for users with premium licenses (E5/E3)
    /// who only use basic features (Exchange/Teams but not SharePoint/OneDrive).
    /// </summary>
    private async Task<int> GenerateDowngradeRecommendationsAsync(Guid? connectionId, CancellationToken ct)
    {
        const string sql = @"
            SELECT la.ObjectId, la.LicensePoolId, lp.SkuName, lp.SkuPartNumber, lp.CostPerUnitMonthly,
                   o.DisplayName, o.Username,
                   m.HasExchangeLicense, m.HasTeamsLicense, m.HasSharePointLicense, m.HasOneDriveLicense,
                   m.TeamsLastActivityDate, m.ExchangeLastActivityDate,
                   m.SharePointLastActivityDate, m.OneDriveLastActivityDate
            FROM LicenseAssignments la
            JOIN LicensePools lp ON lp.Id = la.LicensePoolId
            JOIN Objects o ON o.Id = la.ObjectId
            LEFT JOIN M365UsageReports m ON m.ObjectId = la.ObjectId
            WHERE la.IsActive = 1 AND lp.IsActive = 1
              AND (@ConnectionId IS NULL OR lp.SourceConnectionId = @ConnectionId)
              AND lp.SkuPartNumber IN (
                  'ENTERPRISEPREMIUM', 'SPE_E5', 'ENTERPRISEPACK', 'SPE_E3',
                  'MICROSOFT_365_E5', 'MICROSOFT_365_E3', 'O365_BUSINESS_PREMIUM',
                  'SMB_BUSINESS_PREMIUM', 'DEVELOPERPACK_E5'
              )
              AND m.Id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM LicenseOptimizationRecommendations r
                  WHERE r.ObjectId = la.ObjectId AND r.LicensePoolId = la.LicensePoolId
                  AND r.Status IN ('Pending', 'Approved')
              );";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var candidates = (await conn.QueryAsync<dynamic>(sql,
            new { ConnectionId = connectionId })).ToList();

        if (candidates.Count == 0)
            return 0;

        int inserted = 0;
        const string insertSql = @"
            INSERT INTO LicenseOptimizationRecommendations
                (Id, ObjectId, LicensePoolId, RecommendationType,
                 CurrentSkuName, RecommendedSkuName, Reason,
                 EstimatedMonthlySavings, Status, CreatedAt)
            VALUES
                (@Id, @ObjectId, @LicensePoolId, @RecommendationType,
                 @CurrentSkuName, @RecommendedSkuName, @Reason,
                 @EstimatedMonthlySavings, @Status, @CreatedAt);";

        var recommendations = new List<object>();

        foreach (var c in candidates)
        {
            string? skuPartNumber = c.SkuPartNumber as string;
            if (skuPartNumber == null) continue;

            DateTime? spActivity = c.SharePointLastActivityDate as DateTime?;
            DateTime? odActivity = c.OneDriveLastActivityDate as DateTime?;

            // Downgrade candidate: no SharePoint or OneDrive activity (only uses Exchange + Teams)
            bool isDowngradeCandidate = spActivity == null && odActivity == null;

            if (!isDowngradeCandidate) continue;

            if (!DowngradeTargets.TryGetValue(skuPartNumber, out var target)) continue;

            decimal? currentCost = c.CostPerUnitMonthly as decimal?;
            if (currentCost == null && ApproximateMonthlyCosts.TryGetValue(skuPartNumber, out var approx))
                currentCost = approx;

            decimal? savings = currentCost.HasValue ? currentCost.Value - target.ApproxCost : null;
            // Don't generate a recommendation with zero or negative savings
            if (savings.HasValue && savings.Value <= 0) continue;

            string skuName = (string)c.SkuName;
            var reason = $"User has {skuName} but shows no SharePoint or OneDrive activity. " +
                         $"Only Exchange and Teams features are used. Consider downgrading to {target.RecommendedName}.";

            recommendations.Add(new
            {
                Id = Guid.NewGuid(),
                ObjectId = (Guid)c.ObjectId,
                LicensePoolId = (Guid)c.LicensePoolId,
                RecommendationType = "Downgrade",
                CurrentSkuName = skuName,
                RecommendedSkuName = target.RecommendedName,
                Reason = reason,
                EstimatedMonthlySavings = savings,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            });
        }

        // Insert in chunks
        foreach (var batch in ChunkList(recommendations, 100))
        {
            await conn.ExecuteAsync(insertSql, batch);
            inserted += batch.Count;
        }

        return inserted;
    }

    /// <summary>
    /// Generates Reassign recommendations for license pools where many users have direct assignments
    /// that could benefit from group-based licensing.
    /// </summary>
    private async Task<int> GenerateReassignRecommendationsAsync(Guid? connectionId, CancellationToken ct)
    {
        // Find pools with many direct assignments
        const string sql = @"
            SELECT la.LicensePoolId, lp.SkuName,
                   COUNT(*) AS TotalDirectForPool
            FROM LicenseAssignments la
            JOIN LicensePools lp ON lp.Id = la.LicensePoolId
            WHERE la.IsActive = 1 AND la.AssignmentSource = 'Direct'
              AND (@ConnectionId IS NULL OR lp.SourceConnectionId = @ConnectionId)
              AND NOT EXISTS (
                  SELECT 1 FROM LicenseOptimizationRecommendations r
                  WHERE r.LicensePoolId = la.LicensePoolId
                  AND r.RecommendationType = 'Reassign'
                  AND r.Status IN ('Pending', 'Approved')
              )
            GROUP BY la.LicensePoolId, lp.SkuName
            HAVING COUNT(*) >= @MinDirectAssignments;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var poolCandidates = (await conn.QueryAsync<dynamic>(sql,
            new { ConnectionId = connectionId, MinDirectAssignments = MinDirectAssignmentsForReassign })).ToList();

        if (poolCandidates.Count == 0)
            return 0;

        // For each qualifying pool, pick one representative direct-assigned user to anchor the recommendation
        int inserted = 0;
        const string pickUserSql = @"
            SELECT TOP 1 la.ObjectId
            FROM LicenseAssignments la
            WHERE la.LicensePoolId = @LicensePoolId
              AND la.IsActive = 1
              AND la.AssignmentSource = 'Direct'
            ORDER BY la.LastSyncedAt DESC;";

        const string insertSql = @"
            INSERT INTO LicenseOptimizationRecommendations
                (Id, ObjectId, LicensePoolId, RecommendationType,
                 CurrentSkuName, RecommendedSkuName, Reason,
                 EstimatedMonthlySavings, Status, CreatedAt)
            VALUES
                (@Id, @ObjectId, @LicensePoolId, @RecommendationType,
                 @CurrentSkuName, @RecommendedSkuName, @Reason,
                 @EstimatedMonthlySavings, @Status, @CreatedAt);";

        foreach (var pool in poolCandidates)
        {
            Guid poolId = (Guid)pool.LicensePoolId;
            string skuName = (string)pool.SkuName;
            int directCount = (int)pool.TotalDirectForPool;

            var representativeObjectId = await conn.QuerySingleOrDefaultAsync<Guid?>(pickUserSql,
                new { LicensePoolId = poolId });

            if (representativeObjectId == null) continue;

            var rec = new
            {
                Id = Guid.NewGuid(),
                ObjectId = representativeObjectId.Value,
                LicensePoolId = poolId,
                RecommendationType = "Reassign",
                CurrentSkuName = skuName,
                RecommendedSkuName = (string?)null,
                Reason = $"License directly assigned to {directCount} users. Consider group-based assignment for easier management and consistency.",
                EstimatedMonthlySavings = 0m,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            await conn.ExecuteAsync(insertSql, rec);
            inserted++;
        }

        return inserted;
    }

    private static List<List<T>> ChunkList<T>(IEnumerable<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        var list = source.ToList();
        for (int i = 0; i < list.Count; i += chunkSize)
        {
            chunks.Add(list.GetRange(i, Math.Min(chunkSize, list.Count - i)));
        }
        return chunks;
    }
}
