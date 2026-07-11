using System.Collections.Concurrent;

namespace IdentityCenter.API.Services.Enroll;

/// <summary>
/// Pure sliding-window counter keyed by an arbitrary string (the resolved client IP for enroll).
/// Time is injected so the window math is unit-testable. Thread-safe.
///
/// NOTE (fork): in the IdentityCenter repo this type lives in Services/Signup (it backs the signup
/// limiter there too). The fork has no signup surface, so it lives beside its only consumer.
/// </summary>
public sealed class SlidingWindowCounter
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();
    private DateTime _lastSweepUtc = DateTime.MinValue;
    private readonly object _sweepLock = new();

    /// <summary>
    /// Records an attempt if under <paramref name="limit"/> within <paramref name="window"/>.
    /// Returns false (without recording) when the limit is reached; <paramref name="retryAfter"/>
    /// is how long until the oldest in-window attempt ages out.
    /// </summary>
    public bool TryAcquire(string key, int limit, TimeSpan window, DateTime nowUtc, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        Sweep(window, nowUtc);

        var queue = _windows.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (queue)
        {
            var cutoff = nowUtc - window;
            while (queue.Count > 0 && queue.Peek() <= cutoff)
                queue.Dequeue();

            if (queue.Count >= limit)
            {
                retryAfter = queue.Peek() + window - nowUtc;
                if (retryAfter < TimeSpan.FromSeconds(1)) retryAfter = TimeSpan.FromSeconds(1);
                return false;
            }

            queue.Enqueue(nowUtc);
            return true;
        }
    }

    /// <summary>Drops keys whose entries have all aged out, so IP churn cannot grow the map unbounded.</summary>
    private void Sweep(TimeSpan window, DateTime nowUtc)
    {
        if (nowUtc - _lastSweepUtc < TimeSpan.FromMinutes(5)) return;
        if (!Monitor.TryEnter(_sweepLock)) return;
        try
        {
            if (nowUtc - _lastSweepUtc < TimeSpan.FromMinutes(5)) return;
            _lastSweepUtc = nowUtc;
            var cutoff = nowUtc - window;
            foreach (var entry in _windows)
            {
                lock (entry.Value)
                {
                    while (entry.Value.Count > 0 && entry.Value.Peek() <= cutoff)
                        entry.Value.Dequeue();
                    if (entry.Value.Count == 0)
                        _windows.TryRemove(entry.Key, out _);
                }
            }
        }
        finally
        {
            Monitor.Exit(_sweepLock);
        }
    }
}
