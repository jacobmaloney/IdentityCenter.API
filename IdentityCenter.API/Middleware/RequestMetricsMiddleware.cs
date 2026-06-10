using System.Diagnostics;
using IdentityCenter.API.Services;

namespace IdentityCenter.API.Middleware;

/// <summary>
/// Records one telemetry sample per request (remote host, final status code, latency) into the
/// in-memory <see cref="RequestMetricsStore"/> backing the /admin dashboard's live traffic graph.
///
/// Placement (Program.cs): AFTER UseAuthentication and BEFORE RateLimitingMiddleware, so that
/// 429s and 401s are captured with the rest of the traffic. The admin UI's own requests
/// (/admin, /_blazor, static assets) are excluded — watching the dashboard must not light up
/// the dashboard.
/// </summary>
public class RequestMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestMetricsStore _store;

    public RequestMetricsMiddleware(RequestDelegate next, RequestMetricsStore store)
    {
        _next = next;
        _store = store;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExcluded(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _store.Record(
                ResolveHost(context),
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static bool IsExcluded(PathString path) =>
        path.StartsWithSegments("/admin")
        || path.StartsWithSegments("/_blazor")
        || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/css")
        || path.StartsWithSegments("/favicon.ico");

    /// <summary>
    /// The "host" series key: the calling machine. First X-Forwarded-For hop when present
    /// (proxy deployments), otherwise the remote IP. Same resolution the rate limiter and the
    /// API-key audit trail use, so the graph correlates with the logs.
    /// </summary>
    private static string ResolveHost(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
