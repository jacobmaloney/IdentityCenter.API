using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// In-memory buffer for sync execution logs.
    /// Captures log messages during sync execution and broadcasts them via SignalR.
    /// Provides conditional persistence - only saves to database on errors.
    /// </summary>
    public class SyncLogBuffer
    {
        private readonly ConcurrentDictionary<Guid, List<SyncLogEntry>> _buffers = new();
        private readonly IHubContext<SyncLoggingHub>? _hubContext;

        public SyncLogBuffer(IHubContext<SyncLoggingHub>? hubContext = null)
        {
            _hubContext = hubContext;
        }

        /// <summary>
        /// Starts capturing logs for a specific sync run.
        /// </summary>
        public void StartCapture(Guid syncRunId)
        {
            _buffers.TryAdd(syncRunId, new List<SyncLogEntry>());
        }

        /// <summary>
        /// Adds a log entry to the buffer and broadcasts it via SignalR.
        /// </summary>
        public async Task AddLogAsync(Guid syncRunId, LogLevel logLevel, string message, string? category = null)
        {
            if (!_buffers.TryGetValue(syncRunId, out var buffer))
            {
                return; // Buffer not started for this run
            }

            var entry = new SyncLogEntry
            {
                Timestamp = DateTime.Now,
                LogLevel = logLevel,
                Category = category,
                Message = message
            };

            buffer.Add(entry);

            // Broadcast to SignalR clients if hub context is available
            if (_hubContext != null)
            {
                try
                {
                    await _hubContext.Clients
                        .Group($"SyncRun_{syncRunId}")
                        .SendAsync("ReceiveLog", new
                        {
                            timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                            level = entry.LogLevel.ToString().ToUpper(),
                            category = entry.Category,
                            message = entry.Message
                        });
                }
                catch
                {
                    // Silently ignore SignalR errors - logging shouldn't break sync
                }
            }
        }

        /// <summary>
        /// Gets all buffered logs for a sync run as formatted text.
        /// </summary>
        public string GetFormattedLogs(Guid syncRunId)
        {
            if (!_buffers.TryGetValue(syncRunId, out var buffer))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var entry in buffer)
            {
                sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {entry.LogLevel.ToString().ToUpper()}: {entry.Message}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Clears the buffer for a sync run (called on successful completion).
        /// </summary>
        public void ClearBuffer(Guid syncRunId)
        {
            _buffers.TryRemove(syncRunId, out _);
        }

        /// <summary>
        /// Gets the number of log entries for a specific sync run.
        /// </summary>
        public int GetLogCount(Guid syncRunId)
        {
            return _buffers.TryGetValue(syncRunId, out var buffer) ? buffer.Count : 0;
        }

        /// <summary>
        /// Gets all log entries for a specific sync run.
        /// </summary>
        public List<SyncLogEntry> GetLogs(Guid syncRunId)
        {
            return _buffers.TryGetValue(syncRunId, out var buffer)
                ? new List<SyncLogEntry>(buffer)
                : new List<SyncLogEntry>();
        }
    }

    /// <summary>
    /// Represents a single log entry in the buffer.
    /// </summary>
    public class SyncLogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel LogLevel { get; set; }
        public string? Category { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
