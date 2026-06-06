using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Custom logger provider that captures sync execution logs to the SyncLogBuffer.
    /// Integrates with ASP.NET Core's ILogger infrastructure to intercept log messages.
    /// </summary>
    public class SyncLoggerProvider : ILoggerProvider
    {
        private readonly SyncLogBuffer _logBuffer;
        private readonly ConcurrentDictionary<string, SyncLogger> _loggers = new();
        private Guid? _currentSyncRunId;
        private LogLevel _minimumLevel = LogLevel.Information;

        public SyncLoggerProvider(SyncLogBuffer logBuffer)
        {
            _logBuffer = logBuffer;
        }

        /// <summary>
        /// Sets the current sync run ID that logs should be captured for.
        /// Call this at the start of a sync execution.
        /// </summary>
        public void SetCurrentSyncRun(Guid syncRunId, LogLevel minimumLevel = LogLevel.Debug)
        {
            _currentSyncRunId = syncRunId;
            _minimumLevel = minimumLevel;
            _logBuffer.StartCapture(syncRunId);
        }

        /// <summary>
        /// Clears the current sync run context.
        /// Call this at the end of a sync execution.
        /// </summary>
        public void ClearCurrentSyncRun()
        {
            _currentSyncRunId = null;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new SyncLogger(this, name));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }

        private class SyncLogger : ILogger
        {
            private readonly SyncLoggerProvider _provider;
            private readonly string _categoryName;

            public SyncLogger(SyncLoggerProvider provider, string categoryName)
            {
                _provider = provider;
                _categoryName = categoryName;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                // Only capture logs if we have an active sync run and level meets minimum
                return _provider._currentSyncRunId.HasValue &&
                       logLevel >= _provider._minimumLevel;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var message = formatter(state, exception);
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                // Add exception details if present
                if (exception != null)
                {
                    message = $"{message}\n{exception}";
                }

                // Capture to buffer asynchronously (fire and forget - don't block logging)
                _ = _provider._logBuffer.AddLogAsync(
                    _provider._currentSyncRunId!.Value,
                    logLevel,
                    message,
                    _categoryName);
            }
        }
    }
}
