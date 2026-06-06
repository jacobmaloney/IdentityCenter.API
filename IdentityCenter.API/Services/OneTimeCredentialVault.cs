using System.Collections.Concurrent;
using IdentityCenter.API.Models;

namespace IdentityCenter.API.Services;

/// <summary>
/// In-memory, single-read store for the freshly-generated tenant admin credential produced during
/// provisioning.
///
/// SECURITY CONTRACT:
///   - The generated admin password is NEVER persisted (no DB column, no log line, no disk). It lives
///     only here, in process memory, and only between the moment provisioning seeds the admin and the
///     first authorized status read.
///   - <see cref="TakeOnce"/> is destructive: the bundle is removed on first read, so a second read
///     (or a different caller) gets nothing. This guarantees the password is surfaced exactly once.
///   - Entries self-expire (<see cref="Ttl"/>) so an un-collected credential does not linger in memory
///     indefinitely if the caller never polls.
///
/// This vault is process-local by design. In a multi-instance deployment the poll must hit the same
/// instance that provisioned (or provisioning returns the bundle inline). That is a Day 4+ concern;
/// the single-API-instance MVP is the current target and is called out in the Day 4 handoff.
/// </summary>
public sealed class OneTimeCredentialVault
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private sealed record Entry(TenantAccessBundle Bundle, DateTime ExpiresAtUtc);

    private readonly ConcurrentDictionary<Guid, Entry> _store = new();

    /// <summary>Stash the one-time bundle for a tenant. Overwrites any prior (un-read) entry.</summary>
    public void Stash(Guid tenantId, TenantAccessBundle bundle)
    {
        _store[tenantId] = new Entry(bundle, DateTime.UtcNow.Add(Ttl));
    }

    /// <summary>
    /// Returns the bundle ONCE and removes it. Returns null if absent or expired. After this call the
    /// credential is gone from memory.
    /// </summary>
    public TenantAccessBundle? TakeOnce(Guid tenantId)
    {
        if (!_store.TryRemove(tenantId, out var entry))
            return null;

        if (DateTime.UtcNow > entry.ExpiresAtUtc)
            return null; // expired — already removed above

        return entry.Bundle;
    }

    /// <summary>Drop a tenant's pending credential without returning it (e.g. on failure cleanup).</summary>
    public void Discard(Guid tenantId) => _store.TryRemove(tenantId, out _);
}
