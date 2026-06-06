using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Services;

public class LicenseThresholdMonitorService : ILicenseThresholdMonitorService
{
    // Default safety guardrails (overrideable via Settings(Category='LicenseManagement', ...)).
    private const int DefaultMaxAutoReclaimPerRun = 10;
    private const int CircuitBreakerViolationLimit = 50;

    private readonly string _connectionString;
    private readonly ILicenseRepository _licenseRepo;
    private readonly IAdminNotificationService _notifications;
    private readonly ILicenseBreachActionHandler? _breachHandler;
    private readonly IConfigurationRepository? _configRepo;
    private readonly IGlobalLogger _logger;

    public LicenseThresholdMonitorService(
        IConfiguration configuration,
        ILicenseRepository licenseRepo,
        IAdminNotificationService notifications,
        IGlobalLogger logger,
        ILicenseBreachActionHandler? breachHandler = null,
        IConfigurationRepository? configRepo = null)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _licenseRepo = licenseRepo;
        _breachHandler = breachHandler;
        _notifications = notifications;
        _configRepo = configRepo;
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public async Task<(int newBreaches, int resolvedBreaches)> EvaluateAllPoolsAsync(CancellationToken ct = default)
    {
        // Circuit-breaker ack window — admins set Settings(Category='LicenseManagement',
        // Key='CircuitBreakerAck', Value='YYYY-MM-DD') to re-enable evaluation after
        // a halt. Outside a 24h ack window the breaker stays tripped.
        if (await IsCircuitBreakerActiveAsync())
        {
            _logger.LogWarning("LicenseThresholdMonitor: circuit breaker active — evaluation halted, waiting for admin ack");
            return (0, 0);
        }

        var pools = await _licenseRepo.GetLicensePoolsAsync(null, ct);
        int newBreaches = 0, resolvedBreaches = 0;
        int autoReclaimsTriggered = 0;
        int processedCount = 0;

        var maxAutoReclaim = await ResolveIntSettingAsync(
            "MaxAutoReclaimPerRun", DefaultMaxAutoReclaimPerRun);

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // H4: Cluster-safe serialization. [DisallowConcurrentExecution] is per-scheduler;
        // in a clustered Quartz AdoJobStore deployment two nodes can fire the same trigger
        // on misfire and race InsertBreachAsync, leading to duplicate breaches + duplicate
        // campaigns. sp_getapplock at 'Session' scope holds for this connection's lifetime
        // and auto-releases when the using-block disposes. LockTimeout=0 means we fail fast
        // if another node already holds it — that's the desired behavior; the next run picks
        // up. (V072 has no unique filtered index on (LicensePoolId, ThresholdType) WHERE
        // Resolved=0, so this app-lock is the only serialization in the path.)
        await using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.CommandText = "EXEC sp_getapplock @Resource = 'LicenseThresholdMonitor', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;";
            var lockResult = (int)(await lockCmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
            if (lockResult < 0)
            {
                _logger.LogInformation(
                    "LicenseThresholdMonitor: another instance holds the cluster lock (sp_getapplock result={Result}); skipping run.",
                    lockResult);
                return (0, 0);
            }
        }

        foreach (var pool in pools)
        {
            if (ct.IsCancellationRequested) break;

            // H3: Hoist the circuit breaker to the TOP of each iteration so we trip the
            // moment we cross the threshold — not after firing one more InsertBreachAsync +
            // FireBreachActionsAsync (which is the actual harm-causing call). Without this,
            // the first 50 breaches all fire and disrupt 50 users before the breaker bites.
            if (newBreaches > CircuitBreakerViolationLimit)
            {
                _logger.LogError(
                    "LicenseThresholdMonitor: CIRCUIT BREAKER tripped at {Count} violations (limit {Limit}). Aborting evaluation; {Remaining} pools deferred. Admin ack required.",
                    newBreaches, CircuitBreakerViolationLimit, pools.Count - processedCount);
                await TripCircuitBreakerAsync(newBreaches, ct);
                break;
            }

            processedCount++;
            if (pool.TotalUnits == 0) continue; // can't evaluate

            var breaches = EvaluatePoolInternal(pool);
            var activeBreaches = await GetActivePoolBreachesAsync(conn, pool.Id);

            // Record new breaches
            foreach (var breach in breaches)
            {
                // Skip if there's an identical unresolved breach
                if (activeBreaches.Any(b => b.ThresholdType == breach.ThresholdType))
                    continue;

                // H3: Re-check inside the inner loop too — a pool with multiple breach
                // types can push us over the limit between firings. The actual harm is
                // FireBreachActionsAsync (creates campaigns, sends emails, decrements
                // counters). Do not insert a new breach + fire actions once we've
                // crossed the line.
                if (newBreaches >= CircuitBreakerViolationLimit)
                {
                    _logger.LogError(
                        "LicenseThresholdMonitor: CIRCUIT BREAKER tripped mid-pool at {Count} violations (limit {Limit}). Halting before pool {PoolId} breach {Type}.",
                        newBreaches, CircuitBreakerViolationLimit, pool.Id, breach.ThresholdType);
                    await TripCircuitBreakerAsync(newBreaches, ct);
                    return (newBreaches, resolvedBreaches);
                }

                await InsertBreachAsync(conn, breach);
                await SendBreachNotificationAsync(pool, breach);
                newBreaches++;

                // Per-run cap (Phase 4): only fire reclaim-capable breach actions while
                // we're under the cap. Notifications + breach record still happen above.
                if (autoReclaimsTriggered >= maxAutoReclaim)
                {
                    _logger.LogWarning(
                        "LicenseThresholdMonitor: MaxAutoReclaimPerRun cap of {Cap} reached — deferring breach action for pool {PoolId} to next run",
                        maxAutoReclaim, pool.Id);
                    continue;
                }

                var actionResult = await FireBreachActionsAsync(pool, breach);
                if (actionResult?.CampaignId.HasValue == true && breach.CampaignId == null)
                {
                    breach.CampaignId = actionResult.CampaignId;
                    await UpdateBreachCampaignIdAsync(conn, breach.Id, actionResult.CampaignId.Value);
                }
                if (actionResult?.CampaignCreated == true)
                    autoReclaimsTriggered++;
            }

            // Resolve any active breaches that no longer apply
            foreach (var active in activeBreaches)
            {
                if (!breaches.Any(b => b.ThresholdType == active.ThresholdType))
                {
                    await ResolveBreachInternalAsync(conn, active.Id, "Threshold no longer breached — capacity restored");
                    resolvedBreaches++;
                }
            }
        }

        _logger.LogInformation($"LicenseThresholdMonitor: evaluated {pools.Count} pools — {newBreaches} new breaches, {resolvedBreaches} resolved, {autoReclaimsTriggered} reclaim actions fired");
        return (newBreaches, resolvedBreaches);
    }

    public async Task<bool> EvaluatePoolAsync(Guid poolId, CancellationToken ct = default)
    {
        var pool = await _licenseRepo.GetLicensePoolAsync(poolId, ct);
        if (pool == null || pool.TotalUnits == 0) return false;

        using var conn = CreateConnection();
        var breaches = EvaluatePoolInternal(pool);
        var activeBreaches = await GetActivePoolBreachesAsync(conn, pool.Id);

        bool createdNew = false;
        foreach (var breach in breaches)
        {
            if (activeBreaches.Any(b => b.ThresholdType == breach.ThresholdType)) continue;
            await InsertBreachAsync(conn, breach);
            await SendBreachNotificationAsync(pool, breach);
            var actionResult = await FireBreachActionsAsync(pool, breach);
            if (actionResult?.CampaignId.HasValue == true && breach.CampaignId == null)
            {
                breach.CampaignId = actionResult.CampaignId;
                await UpdateBreachCampaignIdAsync(conn, breach.Id, actionResult.CampaignId.Value);
            }
            createdNew = true;
        }
        return createdNew;
    }

    public async Task<List<LicenseThresholdBreach>> GetActiveBreachesAsync(CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<LicenseThresholdBreach>(@"
            SELECT b.Id, b.LicensePoolId, b.ThresholdType, b.ThresholdValue, b.ActualValue,
                   b.Severity, b.BreachedAt, b.Resolved, b.ResolvedAt, b.ResolvedReason,
                   b.NotificationSent, b.CampaignId, b.ViolationId,
                   p.SkuName AS PoolSkuName, ISNULL(p.FriendlyName, p.SkuName) AS PoolName
            FROM LicenseThresholdBreaches b
            INNER JOIN LicensePools p ON p.Id = b.LicensePoolId
            WHERE b.Resolved = 0
            ORDER BY b.BreachedAt DESC");
        return rows.ToList();
    }

    public async Task<List<LicenseThresholdBreach>> GetBreachesForPoolAsync(Guid poolId, bool includeResolved = true, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT b.Id, b.LicensePoolId, b.ThresholdType, b.ThresholdValue, b.ActualValue,
                   b.Severity, b.BreachedAt, b.Resolved, b.ResolvedAt, b.ResolvedReason,
                   b.NotificationSent, b.CampaignId, b.ViolationId,
                   p.SkuName AS PoolSkuName, ISNULL(p.FriendlyName, p.SkuName) AS PoolName
            FROM LicenseThresholdBreaches b
            INNER JOIN LicensePools p ON p.Id = b.LicensePoolId
            WHERE b.LicensePoolId = @poolId "
            + (includeResolved ? "" : " AND b.Resolved = 0 ")
            + " ORDER BY b.BreachedAt DESC";
        var rows = await conn.QueryAsync<LicenseThresholdBreach>(sql, new { poolId });
        return rows.ToList();
    }

    public async Task ResolveBreachAsync(Guid breachId, string reason, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await ResolveBreachInternalAsync(conn, breachId, reason);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private List<LicenseThresholdBreach> EvaluatePoolInternal(LicensePool pool)
    {
        var breaches = new List<LicenseThresholdBreach>();
        if (pool.TotalUnits == 0) return breaches;

        var utilizationPct = (decimal)pool.ConsumedUnits / pool.TotalUnits * 100m;
        var bufferPct = (decimal)pool.AvailableUnits / pool.TotalUnits * 100m;

        // MinBufferPercent: alert when available units fall below buffer threshold
        if (pool.MinBufferPercent.HasValue && bufferPct < pool.MinBufferPercent.Value)
        {
            breaches.Add(new LicenseThresholdBreach
            {
                LicensePoolId = pool.Id,
                ThresholdType = LicenseThresholdTypes.MinBufferPercent,
                ThresholdValue = pool.MinBufferPercent.Value,
                ActualValue = Math.Round(bufferPct, 2),
                Severity = bufferPct < (pool.MinBufferPercent.Value / 2m) ? "Critical" : "Warning"
            });
        }

        // MaxUtilizationPercent: alert when utilization exceeds threshold
        if (pool.MaxUtilizationPercent.HasValue && utilizationPct > pool.MaxUtilizationPercent.Value)
        {
            breaches.Add(new LicenseThresholdBreach
            {
                LicensePoolId = pool.Id,
                ThresholdType = LicenseThresholdTypes.MaxUtilizationPercent,
                ThresholdValue = pool.MaxUtilizationPercent.Value,
                ActualValue = Math.Round(utilizationPct, 2),
                Severity = utilizationPct > 95m ? "Critical" : "Warning"
            });
        }

        return breaches;
    }

    private async Task<List<LicenseThresholdBreach>> GetActivePoolBreachesAsync(SqlConnection conn, Guid poolId)
    {
        var rows = await conn.QueryAsync<LicenseThresholdBreach>(@"
            SELECT Id, LicensePoolId, ThresholdType, ThresholdValue, ActualValue, Severity,
                   BreachedAt, Resolved, NotificationSent
            FROM LicenseThresholdBreaches
            WHERE LicensePoolId = @poolId AND Resolved = 0",
            new { poolId });
        return rows.ToList();
    }

    private async Task InsertBreachAsync(SqlConnection conn, LicenseThresholdBreach breach)
    {
        breach.Id = Guid.NewGuid();
        breach.BreachedAt = DateTime.UtcNow;

        // Check if there's an active CompliancePolicy with Category='LicenseManagement'
        // targeting this specific pool. If so, create a CompliancePolicyViolation
        // and link it to the breach, enabling auto-campaign creation downstream.
        var violationId = await TryCreateLicensePolicyViolationAsync(conn, breach);
        breach.ViolationId = violationId;

        await conn.ExecuteAsync(@"
            INSERT INTO LicenseThresholdBreaches
                (Id, LicensePoolId, ThresholdType, ThresholdValue, ActualValue, Severity,
                 BreachedAt, Resolved, NotificationSent, ViolationId)
            VALUES
                (@Id, @LicensePoolId, @ThresholdType, @ThresholdValue, @ActualValue, @Severity,
                 @BreachedAt, 0, 0, @ViolationId)",
            breach);
    }

    /// <summary>
    /// Find any active LicenseManagement-category policy whose rules target this
    /// pool+threshold, and create a CompliancePolicyViolation (EntityType="LicensePool").
    /// Returns the violation Id or null if no matching policy exists.
    /// </summary>
    private async Task<Guid?> TryCreateLicensePolicyViolationAsync(SqlConnection conn, LicenseThresholdBreach breach)
    {
        // Find policies tagged Category='LicenseManagement' with a rule targeting
        // this pool (rule.FieldName = LicensePoolId as string).
        var policyId = await conn.QuerySingleOrDefaultAsync<Guid?>(@"
            SELECT TOP 1 p.Id
            FROM CompliancePolicies p
            INNER JOIN CompliancePolicyRule r ON r.CompliancePolicyId = p.Id
            WHERE p.IsActive = 1
              AND p.Category = 'LicenseManagement'
              AND r.IsActive = 1
              AND r.RuleType = 'LicenseCapacity'
              AND (r.FieldName = @poolIdStr OR r.FieldName = '*')",
            new { poolIdStr = breach.LicensePoolId.ToString() });

        if (policyId == null) return null;

        // Check for existing open violation to avoid duplicates
        var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(@"
            SELECT TOP 1 Id FROM CompliancePolicyViolations
            WHERE CompliancePolicyId = @policyId
              AND EntityId = @entityId
              AND EntityType = 'LicensePool'
              AND Status = 'Open'",
            new { policyId, entityId = breach.LicensePoolId });

        if (existingId.HasValue) return existingId;

        // Resolve a friendly pool name (FriendlyName preferred, SkuName fallback) so the
        // violation row in the dashboard reads as "Office 365 E3" instead of a raw GUID.
        var poolName = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT COALESCE(FriendlyName, SkuName) FROM LicensePools WHERE Id = @PoolId",
            new { PoolId = breach.LicensePoolId })
            ?? $"Pool {breach.LicensePoolId}";

        // Create a new violation
        var violationId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO CompliancePolicyViolations
                (Id, CompliancePolicyId, EntityId, EntityType, EntityDisplayName,
                 Severity, Status, ViolationScore, Message, DetectedAt,
                 ActionsExecuted, ActionCount, NotificationCount)
            VALUES
                (@Id, @PolicyId, @EntityId, 'LicensePool', @EntityDisplayName,
                 @Severity, 'Open', @Score, @Message, @DetectedAt,
                 0, 0, 0)",
            new
            {
                Id = violationId,
                PolicyId = policyId.Value,
                EntityId = breach.LicensePoolId,
                EntityDisplayName = poolName,
                Severity = breach.Severity,
                Score = breach.Severity == "Critical" ? 90m : 60m,
                Message = $"License threshold breached: {breach.ThresholdType}={breach.ActualValue}% (limit: {breach.ThresholdValue}%)",
                DetectedAt = DateTime.UtcNow
            });

        _logger.LogInformation($"LicenseThresholdMonitor: created CompliancePolicyViolation {violationId} for breach {breach.Id}");
        return violationId;
    }

    private async Task ResolveBreachInternalAsync(SqlConnection conn, Guid breachId, string reason)
    {
        await conn.ExecuteAsync(@"
            UPDATE LicenseThresholdBreaches
            SET Resolved = 1, ResolvedAt = GETUTCDATE(), ResolvedReason = @reason
            WHERE Id = @breachId AND Resolved = 0",
            new { breachId, reason });
    }

    private async Task SendBreachNotificationAsync(LicensePool pool, LicenseThresholdBreach breach)
    {
        var poolName = pool.FriendlyName ?? pool.SkuName;
        var title = breach.Severity == "Critical"
            ? $"CRITICAL: License threshold breached — {poolName}"
            : $"License threshold warning — {poolName}";

        var message = breach.ThresholdType switch
        {
            LicenseThresholdTypes.MinBufferPercent =>
                $"Pool **{poolName}** has only **{breach.ActualValue}%** available (configured minimum: {breach.ThresholdValue}%). {pool.AvailableUnits} of {pool.TotalUnits} units remaining.",
            LicenseThresholdTypes.MaxUtilizationPercent =>
                $"Pool **{poolName}** is at **{breach.ActualValue}%** utilization (configured max: {breach.ThresholdValue}%). {pool.ConsumedUnits} of {pool.TotalUnits} units in use.",
            _ => $"Pool **{poolName}** breached {breach.ThresholdType} threshold."
        };

        try
        {
            await _notifications.SendSystemAlertAsync(title, message, breach.Severity);
            using var conn = CreateConnection();
            await conn.ExecuteAsync(@"UPDATE LicenseThresholdBreaches SET NotificationSent = 1 WHERE Id = @Id",
                new { breach.Id });
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"LicenseThresholdMonitor: failed to send notification for breach {breach.Id}: {ex.Message}");
        }
    }

    private async Task<LicenseBreachActionResult?> FireBreachActionsAsync(LicensePool pool, LicenseThresholdBreach breach)
    {
        if (_breachHandler == null) return null;
        if (!pool.OnBreachCreateReview && !pool.OnBreachSendEmail && !pool.OnBreachNotifyTeams) return null;

        try
        {
            var actionResult = await _breachHandler.HandleBreachAsync(pool, breach);
            _logger.LogInformation("LicenseThresholdMonitor: Fired breach actions for pool {PoolId} (Review={Review}, Email={Email}, Teams={Teams}, CampaignId={CampaignId})",
                pool.Id, pool.OnBreachCreateReview, pool.OnBreachSendEmail, pool.OnBreachNotifyTeams, actionResult?.CampaignId);
            return actionResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LicenseThresholdMonitor: Breach action handler failed for pool {PoolId}", pool.Id);
            return null;
        }
    }

    private static async Task UpdateBreachCampaignIdAsync(SqlConnection conn, Guid breachId, Guid campaignId)
    {
        await conn.ExecuteAsync(
            "UPDATE LicenseThresholdBreaches SET CampaignId = @CampaignId WHERE Id = @Id AND CampaignId IS NULL",
            new { Id = breachId, CampaignId = campaignId });
    }

    private async Task<int> ResolveIntSettingAsync(string key, int defaultValue)
    {
        if (_configRepo == null) return defaultValue;
        try
        {
            var setting = await _configRepo.GetSettingByCategoryAndKeyAsync("LicenseManagement", key);
            if (setting == null || string.IsNullOrWhiteSpace(setting.Value)) return defaultValue;
            return int.TryParse(setting.Value, out var parsed) ? parsed : defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LicenseThresholdMonitor: failed to resolve int setting {Key} — using default {Default}", key, defaultValue);
            return defaultValue;
        }
    }

    /// <summary>
    /// Returns true when the circuit breaker is tripped and admin has not posted a
    /// fresh ack within the last 24 hours via Settings(Category='LicenseManagement',
    /// Key='CircuitBreakerAck'). When the ack timestamp parses but is older than 24h
    /// it's treated as stale and the breaker stays tripped.
    /// </summary>
    private async Task<bool> IsCircuitBreakerActiveAsync()
    {
        if (_configRepo == null) return false;
        try
        {
            var tripped = await _configRepo.GetSettingByCategoryAndKeyAsync("LicenseManagement", "CircuitBreakerTripped");
            if (tripped == null || !string.Equals(tripped.Value, "true", StringComparison.OrdinalIgnoreCase))
                return false;

            // Tripped — see if a fresh ack has cleared it.
            var ack = await _configRepo.GetSettingByCategoryAndKeyAsync("LicenseManagement", "CircuitBreakerAck");
            if (ack == null || string.IsNullOrWhiteSpace(ack.Value)) return true;

            if (DateTime.TryParse(ack.Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var ackUtc))
            {
                // Reject future-dated acks. Without this upper bound, a clock-skewed
                // admin workstation (or a forged ack) with a tomorrow date would
                // satisfy the freshness window for up to 48 hours.
                var now = DateTime.UtcNow;
                if (ackUtc > now)
                {
                    _logger.LogWarning(
                        "LicenseThresholdMonitor: CircuitBreakerAck timestamp {AckUtc:o} is in the future relative to {Now:o} — ignoring (breaker stays tripped)",
                        ackUtc, now);
                    return true;
                }
                if ((now - ackUtc) <= TimeSpan.FromHours(24))
                {
                    // Ack is fresh — clear the trip flag and proceed.
                    await _configRepo.UpsertSettingAsync("LicenseManagement", "CircuitBreakerTripped", "false");
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LicenseThresholdMonitor: failed to evaluate circuit breaker state; failing closed (treating as tripped) to prevent unguarded reclaim runs");
            return true; // fail-closed
        }
    }

    private async Task TripCircuitBreakerAsync(int violationCount, CancellationToken ct = default)
    {
        try
        {
            if (_configRepo != null)
                await _configRepo.UpsertSettingAsync("LicenseManagement", "CircuitBreakerTripped", "true");

            using var conn = CreateConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO AdminNotifications (Id, NotificationType, Category, Severity, Title, Message,
                    ActionUrl, ActionText, RelatedEntityId, RelatedEntityType, Source, CreatedAt, IsRead, IsDismissed)
                VALUES (@Id, @NotificationType, @Category, @Severity, @Title, @Message,
                    @ActionUrl, @ActionText, @RelatedEntityId, @RelatedEntityType, @Source, GETUTCDATE(), 0, 0)",
                new
                {
                    Id = Guid.NewGuid(),
                    NotificationType = "LicenseCircuitBreaker",
                    Category = "LicenseManagement",
                    Severity = "Critical",
                    Title = "License threshold evaluation halted",
                    Message = $"License threshold evaluation halted: {violationCount} violations in one run exceeds safety threshold of {CircuitBreakerViolationLimit}. Review and acknowledge to resume by setting Settings(Category='LicenseManagement', Key='CircuitBreakerAck') to today's UTC date.",
                    ActionUrl = "/admin/license-center",
                    ActionText = "Review License Center",
                    RelatedEntityId = (Guid?)null,
                    RelatedEntityType = "LicenseManagement",
                    Source = "LicenseThresholdMonitor"
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LicenseThresholdMonitor: failed to record circuit breaker trip");
        }
    }
}
