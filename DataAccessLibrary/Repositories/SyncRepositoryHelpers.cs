using Logging;
using Microsoft.Data.SqlClient;
using System.Diagnostics;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Shared helper utilities for sync repository classes.
/// Contains retry logic and performance tracking used across all sync repositories.
/// </summary>
internal static class SyncRepositoryHelpers
{
    /// <summary>
    /// Maximum number of retry attempts for transient SQL errors.
    /// </summary>
    internal const int MaxRetries = 3;

    /// <summary>
    /// SQL Server transient error codes that warrant automatic retry.
    /// -2: Timeout, -1: Connection error, 1205: Deadlock, 40197/40501/40613: Azure transient
    /// 49918/49919/49920: Resource limit reached
    /// </summary>
    internal static readonly int[] TransientErrorCodes = { -2, -1, 1205, 40197, 40501, 40613, 49918, 49919, 49920, 233, 10053, 10054, 10060, 10061, 64, 121 };

    /// <summary>
    /// Determines if a SQL exception represents a transient error that can be retried.
    /// </summary>
    internal static bool IsTransientError(SqlException ex)
    {
        if (TransientErrorCodes.Contains(ex.Number))
            return true;

        var message = ex.Message?.ToLowerInvariant() ?? "";
        if (message.Contains("physical connection is not usable") ||
            message.Contains("transport-level error") ||
            message.Contains("connection was closed") ||
            message.Contains("connection forcibly closed") ||
            message.Contains("server is not responding"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Executes a database operation with automatic retry for transient SQL errors.
    /// Uses exponential backoff (1s, 2s, 4s) between retries.
    /// </summary>
    internal static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        IGlobalLogger logger,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt > 1)
                {
                    logger.LogDebug("Executing {Operation}, attempt {Attempt}/{MaxRetries}",
                        operationName, attempt, MaxRetries);
                }

                return await operation();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SqlException ex) when (IsTransientError(ex) && attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

                logger.LogWarning(ex,
                    "TRANSIENT ERROR on {Operation}, attempt {Attempt}/{MaxRetries}. " +
                    "SQL Error: {ErrorNumber} ({ErrorMessage}). Retrying in {DelayMs}ms",
                    operationName, attempt, MaxRetries, ex.Number, ex.Message, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException($"ExecuteWithRetryAsync for {operationName} failed after {MaxRetries} attempts");
    }

    /// <summary>
    /// Executes a void database operation with automatic retry for transient SQL errors.
    /// </summary>
    internal static async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        string operationName,
        IGlobalLogger logger,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true;
        }, operationName, logger, cancellationToken);
    }

    /// <summary>
    /// Performance tracker for monitoring database operation durations.
    /// Logs warnings for operations exceeding threshold.
    /// </summary>
    internal sealed class PerformanceTracker : IDisposable
    {
        private readonly IGlobalLogger _logger;
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;
        private readonly object? _metadata;
        private readonly int _slowThresholdMs;
        private bool _disposed;

        public PerformanceTracker(
            IGlobalLogger logger,
            string operationName,
            object? metadata = null,
            int slowThresholdMs = 2000)
        {
            _logger = logger;
            _operationName = operationName;
            _metadata = metadata;
            _slowThresholdMs = slowThresholdMs;
            _stopwatch = Stopwatch.StartNew();
        }

        public long ElapsedMs => _stopwatch.ElapsedMilliseconds;

        public void LogIfSlow(string? checkpoint = null)
        {
            var elapsedMs = _stopwatch.ElapsedMilliseconds;
            var checkpointLabel = checkpoint != null ? $" [{checkpoint}]" : "";

            if (elapsedMs > _slowThresholdMs)
            {
                _logger.LogWarning(
                    "SLOW OPERATION: {Operation}{Checkpoint} took {ElapsedMs}ms (threshold: {ThresholdMs}ms). Metadata: {@Metadata}",
                    _operationName, checkpointLabel, elapsedMs, _slowThresholdMs, _metadata);
            }
            else
            {
                _logger.LogDebug("{Operation}{Checkpoint} completed in {ElapsedMs}ms",
                    _operationName, checkpointLabel, elapsedMs);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _stopwatch.Stop();
            LogIfSlow("FINAL");
        }
    }
}
