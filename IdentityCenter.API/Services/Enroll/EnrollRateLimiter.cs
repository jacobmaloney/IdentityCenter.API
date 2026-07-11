namespace IdentityCenter.API.Services.Enroll;

/// <summary>
/// Per-IP agent-enroll attempt limiter (singleton). DEDICATED to POST /api/agent/enroll —
/// deliberately separate from (and tighter than) the coarse anonymous limit in
/// RateLimitingMiddleware, which still applies as the outer layer. Reuses the tested
/// <see cref="SlidingWindowCounter"/>. Keys are ALWAYS the trust-aware resolved client IP
/// (ClientIp.Resolve), never a raw X-Forwarded-For. Limit read live from
/// <c>Enroll:MaxPerIpPerHour</c> (default 10).
/// </summary>
public sealed class EnrollRateLimiter
{
    public const int DefaultMaxPerIpPerHour = 10;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly SlidingWindowCounter _counter = new();
    private readonly IConfiguration _configuration;

    public EnrollRateLimiter(IConfiguration configuration) => _configuration = configuration;

    public bool TryAcquire(string clientIp, out int retryAfterSeconds)
    {
        var limit = _configuration.GetValue<int?>("Enroll:MaxPerIpPerHour") ?? DefaultMaxPerIpPerHour;
        if (limit <= 0) limit = DefaultMaxPerIpPerHour;

        var allowed = _counter.TryAcquire(clientIp, limit, Window, DateTime.UtcNow, out var retryAfter);
        retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
        return allowed;
    }
}
