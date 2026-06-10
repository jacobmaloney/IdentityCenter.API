using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace IdentityCenter.API.Services;

/// <summary>
/// Serilog sink that keeps the most recent log events in a bounded in-memory ring buffer and
/// raises an event per entry, so the /admin/logs Blazor page can stream logs LIVE without
/// touching the file system. The rolling FILE sink remains the durable record; this buffer is
/// a live diagnostic window only (lost on restart, capped at <see cref="Capacity"/> entries).
///
/// Registered as a static singleton instance because Log.Logger is configured in Program.cs
/// BEFORE the DI container is built; the same instance is then exposed through DI for the UI.
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    public const int Capacity = 2000;

    public static InMemoryLogSink Instance { get; } = new();

    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private long _nextId;

    /// <summary>Raised per appended entry. Subscribers must be fast and never throw.</summary>
    public event Action<LogEntry>? EntryAppended;

    private InMemoryLogSink() { }

    public void Emit(LogEvent logEvent)
    {
        var entry = new LogEntry(
            Interlocked.Increment(ref _nextId),
            logEvent.Timestamp.UtcDateTime,
            logEvent.Level,
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString(),
            logEvent.Properties.TryGetValue("SourceContext", out var sc)
                ? sc.ToString().Trim('"')
                : null);

        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }

        try { EntryAppended?.Invoke(entry); }
        catch { /* a faulty subscriber must never break logging */ }
    }

    /// <summary>Snapshot of the buffer, oldest first.</summary>
    public IReadOnlyList<LogEntry> Snapshot() => _entries.ToArray();
}

/// <summary>One captured log event. Message/Exception are rendered TEXT — the UI must render
/// them as text (Blazor's default @-encoding), never as markup, because synced directory data
/// (e.g. displayName) can land in log messages (the IC stored-XSS class of bug).</summary>
public sealed record LogEntry(
    long Id,
    DateTime TimestampUtc,
    LogEventLevel Level,
    string Message,
    string? Exception,
    string? SourceContext);
