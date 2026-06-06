using System.Collections.Concurrent;

namespace IdentityCenter.API.Middleware;

/// <summary>
/// Simple sliding window rate limiting middleware.
/// Limits: 100 req/min for agents, 1000 req/min for admin users.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    // Sliding window tracking: key -> list of request timestamps
    private static readonly ConcurrentDictionary<string, SlidingWindow> _requestWindows = new();

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
        var clientKey = GetClientKey(context);
        var rateLimit = GetRateLimit(context);

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
            "admin" or "user" => AdminRateLimit,
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

    public bool TryAddRequest(int limit, int windowSizeSeconds)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
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
