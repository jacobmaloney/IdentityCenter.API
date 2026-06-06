using System.Security.Claims;
using DataAccessLibrary.Repositories;
using DataAccessLibrary.Services.Modules;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Reads Settings(Category='Modules', Key=&lt;moduleKey&gt;) through
/// <see cref="IConfigurationRepository"/>. Cached in-memory for 60 seconds so
/// the License Center overview doesn't slam the Settings table on every render.
/// Treats any value not parseable as <c>true</c> as disabled.
/// </summary>
public class ModuleGateService : IModuleGate
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const string CategoryName = "Modules";

    private readonly IConfigurationRepository _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ModuleGateService> _logger;
    private readonly ISystemAuditService? _audit;

    public ModuleGateService(
        IConfigurationRepository config,
        IMemoryCache cache,
        ILogger<ModuleGateService> logger,
        ISystemAuditService? audit = null)
    {
        _config = config;
        _cache = cache;
        _logger = logger;
        _audit = audit;
    }

    public async Task<bool> IsEnabledAsync(string moduleKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleKey)) return false;

        var cacheKey = "ModuleGate:" + moduleKey;
        if (_cache.TryGetValue<bool>(cacheKey, out var cached))
            return cached;

        var enabled = false;
        try
        {
            var setting = await _config.GetSettingByCategoryAndKeyAsync(CategoryName, moduleKey);
            if (setting != null && bool.TryParse(setting.Value, out var parsed))
                enabled = parsed;
            else if (setting == null)
            {
                var def = ModuleCatalog.Find(moduleKey);
                if (def != null) enabled = def.DefaultEnabled;
            }
        }
        catch
        {
            // Default-off on any read failure — better to hide a wedge than throw mid-render.
            enabled = false;
        }

        _cache.Set(cacheKey, enabled, CacheTtl);
        return enabled;
    }

    public async Task<IReadOnlyList<ModuleState>> GetAllAsync(CancellationToken ct = default)
    {
        var results = new List<ModuleState>(ModuleCatalog.All.Count);
        foreach (var def in ModuleCatalog.All)
        {
            bool enabled = def.DefaultEnabled;
            DateTime? modAt = null;
            string? modBy = null;
            try
            {
                var setting = await _config.GetSettingByCategoryAndKeyAsync(CategoryName, def.Key);
                if (setting != null)
                {
                    if (bool.TryParse(setting.Value, out var parsed)) enabled = parsed;
                    modAt = setting.ModifiedAt;
                    modBy = setting.ModifiedBy;
                }
            }
            catch
            {
                enabled = def.DefaultEnabled;
            }
            results.Add(new ModuleState(def, enabled, modAt, modBy));
        }
        return results;
    }

    public async Task SetEnabledAsync(string moduleKey, bool enabled, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (user is null || !user.IsInRole("Admin"))
            throw new UnauthorizedAccessException("Module toggle requires Admin role.");

        if (string.IsNullOrWhiteSpace(moduleKey))
            throw new ArgumentException("Module key required", nameof(moduleKey));

        var def = ModuleCatalog.Find(moduleKey);
        if (def == null)
            throw new ArgumentException($"Unknown module: {moduleKey}", nameof(moduleKey));

        var actor = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? user.Identity?.Name
                    ?? "unknown";

        // Dependency-block check: when disabling, refuse if any module declaring this
        // moduleKey in its DependsOn is currently enabled. Hoisted from the page so the
        // contract holds for any caller (SignalR/API) — not just the Modules.razor page.
        if (!enabled)
        {
            foreach (var other in ModuleCatalog.All)
            {
                if (string.Equals(other.Key, moduleKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!other.DependsOn.Contains(moduleKey, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (await IsEnabledAsync(other.Key, ct))
                {
                    throw new InvalidOperationException(
                        $"Cannot disable {moduleKey}: dependent module {other.DisplayName} is currently enabled. Disable {other.DisplayName} first.");
                }
            }
        }

        string? oldValueString = null;
        try
        {
            var existing = await _config.GetSettingByCategoryAndKeyAsync(CategoryName, moduleKey);
            oldValueString = existing?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read existing Settings row for Modules.{ModuleKey} prior to toggle; old value will be null", moduleKey);
        }

        var newValueString = enabled ? "true" : "false";

        await _config.UpsertSettingAsync(CategoryName, moduleKey, newValueString, dataType: "bool");

        // Invalidate cache so the next read reflects the change immediately.
        _cache.Remove("ModuleGate:" + moduleKey);

        if (_audit != null)
        {
            try
            {
                await _audit.LogSettingChangedAsync(
                    CategoryName,
                    moduleKey,
                    oldValueString,
                    newValueString,
                    changedBy: actor,
                    reason: $"Module {(enabled ? "enabled" : "disabled")} by {actor}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write Settings audit row for Modules.{ModuleKey} ({Old}->{New}); module toggle still applied", moduleKey, oldValueString, newValueString);
            }
        }
    }
}
