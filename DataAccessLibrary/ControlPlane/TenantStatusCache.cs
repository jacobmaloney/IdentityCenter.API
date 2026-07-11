using System.Collections.Concurrent;

namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Tiny per-instance TTL cache (default 60s) for tenant lifecycle status, consulted by
/// <c>TenantConnectionScopeMiddleware</c> on every tenant-key request, so suspension enforcement
/// costs at most one control-plane read per tenant per minute.
///
/// The suspend/resume/delete endpoints call <see cref="Invalidate"/> so enforcement on THIS node
/// is immediate. MULTI-INSTANCE CAVEAT: the cache is per-node — other API instances keep serving
/// their cached status for up to the TTL (60s max staleness) after a suspend. Acceptable for the
/// current single-instance target; a shared cache/bus is the multi-instance follow-up.
/// </summary>
public sealed class TenantStatusCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    // Value: (status or null for known-missing tenant, absolute expiry).
    private readonly ConcurrentDictionary<Guid, (TenantStatus? Status, DateTime ExpiresAtUtc)> _cache = new();

    /// <summary>True on a fresh hit. <paramref name="status"/> null means "tenant does not exist" (also cached).</summary>
    public bool TryGet(Guid tenantId, out TenantStatus? status)
    {
        status = null;
        if (!_cache.TryGetValue(tenantId, out var entry)) return false;
        if (DateTime.UtcNow >= entry.ExpiresAtUtc)
        {
            _cache.TryRemove(tenantId, out _);
            return false;
        }
        status = entry.Status;
        return true;
    }

    public void Set(Guid tenantId, TenantStatus? status) =>
        _cache[tenantId] = (status, DateTime.UtcNow.Add(Ttl));

    /// <summary>Called by suspend/resume/delete so the next request re-reads the registry.</summary>
    public void Invalidate(Guid tenantId) => _cache.TryRemove(tenantId, out _);
}
