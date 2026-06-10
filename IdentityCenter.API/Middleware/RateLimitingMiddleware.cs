using System.Collections.Concurrent;

namespace IdentityCenter.API.Middleware;

/// <summary>
/// Simple sliding window rate limiting middleware.
/// Limits: 100 req/min for agents, 1000 req/min for admin users.
///
/// MUST be registered AFTER UseAuthentication() — the per-key limits are driven by the
/// authenticated principal's claims (key_type / NameIdentifier). When this middleware ran
/// before authentication (pre-2026-06-09), context.User was always empty, so every caller —
/// including authenticated agents and Conduit — was keyed by IP at the ANONYMOUS limit
/// (30/min). That was the 2026-06-09 review HIGH; the registration order in Program.cs is
/// now the fix's other half.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    // Sliding window tracking: key -> list of request timestamps
    private static readonly ConcurrentDictionary<string, SlidingWindow> _requestWindows = new();

    // Eviction: windows idle longer than this are dropped so the dictionary cannot grow
    // unbounded under IP churn (review finding: it previously never evicted).
    private static readonly TimeSpan IdleEvictionAge = TimeSpan.FromMinutes(10);
    private static DateTime _lastEvictionSweep = DateTime.UtcNow;
    private static readonly object _evictionLock = new();

    // Rate limits per minute
    private const int AgentRateLimit = 100;
    private const int AdminRateLimit = 1000;
    private const int AnonymousRateLimit = 30;
    private const int WindowSizeSeconds = 60;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // The browser-facing admin UI is exempt from the API rate limiter:
        //  - static assets + the Blazor SignalR hub generate legitimate bursts that would
        //    instantly trip the anonymous limit (a page load is ~10 requests);
        //  - the login POST has its own dedicated per-IP throttle inside the login page
        //    (LoginAttemptThrottle) plus ASP.NET Identity account lockout;
        //  - everything else under /admin requires an authenticated admin cookie anyway.
        // /api/* and all other surfaces remain rate limited exactly as before.
        if (IsAdminUiSurface(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var clientKey = GetClientKey(context);
        var rateLimit = GetRateLimit(context);

        EvictIdleWindows();

        var window = _requestWindows.GetOrAdd(clientKey, _ => new SlidingWindow());

        if (!window.TryAddRequest(rateLimit, WindowSizeSeconds))
        {
            _logger.LogWarning("Rate limit exceeded for {ClientKey}. Limit: {Limit}/min", clientKey, rateLimit);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = "60";
            context.Response.Headers["X-RateLimit-Limit"] = rateLimit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = "0";

            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded",
                message = $"Too many requests. Limit is {rateLimit} requests per minute.",
                retryAfter = 60
            });
            return;
        }

        // Add rate limit headers
        context.Response.OnStarting(() =>
        {
            var remaining = Math.Max(0, rateLimit - window.GetCurrentCount(WindowSizeSeconds));
            context.Response.Headers["X-RateLimit-Limit"] = rateLimit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static bool IsAdminUiSurface(PathString path) =>
        path.StartsWithSegments("/admin")
        || path.StartsWithSegments("/_blazor")
        || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/css")
        || path.StartsWithSegments("/favicon.ico");

    /// <summary>
    /// Drops windows that have been idle past <see cref="IdleEvictionAge"/>. Sweeps at most
    /// once per minute, and only one thread sweeps at a time; everyone else proceeds.
    /// </summary>
    private static void EvictIdleWindows()
    {
        var now = DateTime.UtcNow;
        if (now - _lastEvictionSweep < TimeSpan.FromMinutes(1)) return;

        if (!System.Threading.Monitor.TryEnter(_evictionLock)) return;
        try
        {
            if (now - _lastEvictionSweep < TimeSpan.FromMinutes(1)) return;
            _lastEvictionSweep = now;

            foreach (var entry in _requestWindows)
            {
                if (now - entry.Value.LastSeenUtc > IdleEvictionAge)
                    _requestWindows.TryRemove(entry.Key, out _);
            }
        }
        finally
        {
            System.Threading.Monitor.Exit(_evictionLock);
        }
    }

    private static string GetClientKey(HttpContext context)
    {
        // Use API key ID if authenticated, otherwise use IP
        var keyId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(keyId))
        {
            return $"key:{keyId}";
        }

        // Fall back to IP address
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var ip = !string.IsNullOrEmpty(forwardedFor)
            ? forwardedFor.Split(',')[0].Trim()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return $"ip:{ip}";
    }

    private static int GetRateLimit(HttpContext context)
    {
        var keyType = context.User?.FindFirst("key_type")?.Value;

        return keyType?.ToLower() switch
        {
            "agent" => AgentRateLimit,
            "admin" or "user" or "controlplaneadmin" or "tenant" => AdminRateLimit,
            _ => AnonymousRateLimit
        };
    }
}

/// <summary>
/// Thread-safe sliding window for rate limiting
/// </summary>
public class SlidingWindow
{
    private readonly object _lock = new();
    private readonly Queue<DateTime> _requests = new();

    /// <summary>Last time this window saw any activity (UTC). Drives idle eviction.</summary>
    public DateTime LastSeenUtc { get; private set; } = DateTime.UtcNow;

    public bool TryAddRequest(int limit, int windowSizeSeconds)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            LastSeenUtc = now;
            var cutoff = now.AddSeconds(-windowSizeSeconds);

            // Remove old requests outside the window
            while (_requests.Count > 0 && _requests.Peek() < cutoff)
            {
                _requests.Dequeue();
            }

            // Check if we're at the limit
            if (_requests.Count >= limit)
            {
                return false;
            }

            // Add the new request
            _requests.Enqueue(now);
            return true;
        }
    }

    public int GetCurrentCount(int windowSizeSeconds)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-windowSizeSeconds);

            // Remove old requests outside the window
            while (_requests.Count > 0 && _requests.Peek() < cutoff)
            {
                _requests.Dequeue();
            }

            return _requests.Count;
        }
    }
}
