using System.Collections.Concurrent;

namespace IdentityCenter.API.Services;

/// <summary>
/// In-process, in-memory request telemetry backing the /admin dashboard's live traffic graph.
///
/// Design (deliberately small — no Prometheus stack, no DB, no migration):
///   - The <see cref="Middleware.RequestMetricsMiddleware"/> records one sample per request:
///     remote host, status class, latency.
///   - Samples are aggregated into fixed 5-second buckets per remote host
///     (ConcurrentDictionary keyed by (host, bucketStart)).
///   - Retention is 30 minutes; older buckets are swept opportunistically on write.
///   - Host cardinality is capped: after <see cref="MaxHosts"/> distinct hosts, new hosts fold
///     into "other" so a scan/flood cannot balloon memory.
///
/// Everything is lost on restart by design — this is a live diagnostic view, not an audit store
/// (ChangeAuditLogs / Serilog files remain the durable records).
/// </summary>
public sealed class RequestMetricsStore
{
    public const int BucketSeconds = 5;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
    private const int MaxHosts = 50;
    private const string OverflowHost = "(other)";

    private sealed class Bucket
    {
        private long _count;
        private long _errors;        // 4xx
        private long _failures;      // 5xx
        private long _totalDurationMs;

        public void Record(int statusCode, long durationMs)
        {
            Interlocked.Increment(ref _count);
            if (statusCode >= 500) Interlocked.Increment(ref _failures);
            else if (statusCode >= 400) Interlocked.Increment(ref _errors);
            Interlocked.Add(ref _totalDurationMs, durationMs);
        }

        public long Count => Interlocked.Read(ref _count);
        public long Errors => Interlocked.Read(ref _errors);
        public long Failures => Interlocked.Read(ref _failures);
        public long TotalDurationMs => Interlocked.Read(ref _totalDurationMs);
    }

    private readonly ConcurrentDictionary<(string Host, long BucketStart), Bucket> _buckets = new();
    private readonly ConcurrentDictionary<string, byte> _knownHosts = new(StringComparer.OrdinalIgnoreCase);
    private long _lastSweepUnix;

    public void Record(string host, int statusCode, long durationMs)
    {
        if (string.IsNullOrEmpty(host)) host = "unknown";

        if (!_knownHosts.ContainsKey(host))
        {
            if (_knownHosts.Count >= MaxHosts) host = OverflowHost;
            _knownHosts.TryAdd(host, 0);
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bucketStart = nowUnix - (nowUnix % BucketSeconds);

        var bucket = _buckets.GetOrAdd((host, bucketStart), _ => new Bucket());
        bucket.Record(statusCode, durationMs);

        SweepIfDue(nowUnix);
    }

    /// <summary>One series per host over the requested window, with zero-filled gaps — chart-ready.</summary>
    public IReadOnlyList<HostSeries> GetSeries(TimeSpan window)
    {
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var currentBucket = nowUnix - (nowUnix % BucketSeconds);
        var fromBucket = currentBucket - ((long)window.TotalSeconds / BucketSeconds) * BucketSeconds;

        var byHost = new Dictionary<string, Dictionary<long, Bucket>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _buckets)
        {
            if (kvp.Key.BucketStart < fromBucket) continue;
            if (!byHost.TryGetValue(kvp.Key.Host, out var map))
            {
                map = new Dictionary<long, Bucket>();
                byHost[kvp.Key.Host] = map;
            }
            map[kvp.Key.BucketStart] = kvp.Value;
        }

        var result = new List<HostSeries>(byHost.Count);
        foreach (var (host, map) in byHost)
        {
            var points = new List<MetricPoint>();
            for (var b = fromBucket; b <= currentBucket; b += BucketSeconds)
            {
                if (map.TryGetValue(b, out var bucket))
                {
                    points.Add(new MetricPoint(
                        DateTimeOffset.FromUnixTimeSeconds(b).UtcDateTime,
                        bucket.Count, bucket.Errors, bucket.Failures,
                        bucket.Count > 0 ? bucket.TotalDurationMs / bucket.Count : 0));
                }
                else
                {
                    points.Add(new MetricPoint(
                        DateTimeOffset.FromUnixTimeSeconds(b).UtcDateTime, 0, 0, 0, 0));
                }
            }
            result.Add(new HostSeries(host, points));
        }

        // Busiest hosts first so chart legend ordering is stable and meaningful.
        return result.OrderByDescending(s => s.Total).ToList();
    }

    /// <summary>Aggregate snapshot for the dashboard stat cards.</summary>
    public MetricsSummary GetSummary(TimeSpan window)
    {
        var fromUnix = DateTimeOffset.UtcNow.Add(-window).ToUnixTimeSeconds();
        long count = 0, errors = 0, failures = 0, totalMs = 0;
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in _buckets)
        {
            if (kvp.Key.BucketStart < fromUnix) continue;
            count += kvp.Value.Count;
            errors += kvp.Value.Errors;
            failures += kvp.Value.Failures;
            totalMs += kvp.Value.TotalDurationMs;
            hosts.Add(kvp.Key.Host);
        }

        return new MetricsSummary(count, errors, failures,
            count > 0 ? totalMs / count : 0, hosts.Count);
    }

    private void SweepIfDue(long nowUnix)
    {
        var last = Interlocked.Read(ref _lastSweepUnix);
        if (nowUnix - last < 60) return;
        if (Interlocked.CompareExchange(ref _lastSweepUnix, nowUnix, last) != last) return;

        var cutoff = nowUnix - (long)Retention.TotalSeconds;
        foreach (var key in _buckets.Keys)
        {
            if (key.BucketStart < cutoff)
                _buckets.TryRemove(key, out _);
        }
    }
}

public sealed record MetricPoint(DateTime TimeUtc, long Count, long Errors, long Failures, long AvgDurationMs);

public sealed record HostSeries(string Host, IReadOnlyList<MetricPoint> Points)
{
    public long Total => Points.Sum(p => p.Count);
}

public sealed record MetricsSummary(long Count, long Errors, long Failures, long AvgDurationMs, int HostCount);
