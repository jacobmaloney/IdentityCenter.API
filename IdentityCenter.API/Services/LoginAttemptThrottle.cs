using System.Collections.Concurrent;
using IdentityCenter.API.Middleware;

namespace IdentityCenter.API.Services;

/// <summary>
/// Per-IP throttle for the /admin/login POST. Complements (does not replace) ASP.NET Identity
/// account lockout: lockout protects a single account from password guessing; this throttle
/// protects against cross-account spraying and brute force from one source.
///
/// The admin UI surface is exempt from the global RateLimitingMiddleware (Blazor traffic would
/// trip it), so the login POST needs its own gate — this is it. 10 attempts/min per IP, with
/// idle eviction so the dictionary stays bounded.
/// </summary>
public sealed class LoginAttemptThrottle
{
    private const int AttemptsPerMinute = 10;
    private const int WindowSeconds = 60;
    private static readonly TimeSpan IdleEvictionAge = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();
    private DateTime _lastSweepUtc = DateTime.UtcNow;
    private readonly object _sweepLock = new();

    /// <summary>True if this attempt is allowed; false if the source IP must wait.</summary>
    public bool TryAcquire(string sourceIp)
    {
        if (string.IsNullOrEmpty(sourceIp)) sourceIp = "unknown";

        SweepIfDue();

        var window = _windows.GetOrAdd(sourceIp, _ => new SlidingWindow());
        return window.TryAddRequest(AttemptsPerMinute, WindowSeconds);
    }

    private void SweepIfDue()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSweepUtc < TimeSpan.FromMinutes(5)) return;
        if (!System.Threading.Monitor.TryEnter(_sweepLock)) return;
        try
        {
            if (now - _lastSweepUtc < TimeSpan.FromMinutes(5)) return;
            _lastSweepUtc = now;
            foreach (var entry in _windows)
            {
                if (now - entry.Value.LastSeenUtc > IdleEvictionAge)
                    _windows.TryRemove(entry.Key, out _);
            }
        }
        finally
        {
            System.Threading.Monitor.Exit(_sweepLock);
        }
    }
}
