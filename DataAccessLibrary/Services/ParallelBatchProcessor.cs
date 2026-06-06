using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Provides parallel batch processing with progress tracking and fire-and-forget execution.
    /// Splits a large list into configurable batch sizes and processes them concurrently.
    /// </summary>
    public class ParallelBatchProcessor<T>
    {
        private readonly ILogger _logger;

        public ParallelBatchProcessor(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Progress information for batch processing
        /// </summary>
        public class BatchProgress
        {
            public int TotalItems { get; set; }
            public int ProcessedItems { get; set; }
            public int TotalBatches { get; set; }
            public int CompletedBatches { get; set; }
            public int FailedBatches { get; set; }
            public int RunningBatches { get; set; }
            public double PercentComplete => TotalItems > 0 ? (ProcessedItems * 100.0 / TotalItems) : 0;
            public TimeSpan Elapsed { get; set; }
            public TimeSpan? EstimatedTimeRemaining { get; set; }
            public double ItemsPerSecond { get; set; }
        }

        /// <summary>
        /// Result of batch processing
        /// </summary>
        public class BatchResult
        {
            public int BatchNumber { get; set; }
            public int ItemCount { get; set; }
            public bool Success { get; set; }
            public TimeSpan Duration { get; set; }
            public Exception? Error { get; set; }
            public object? Data { get; set; } // Optional result data from processor
        }

        /// <summary>
        /// Process items in parallel batches with progress tracking.
        /// </summary>
        /// <param name="items">Full list of items to process</param>
        /// <param name="batchSize">Number of items per batch (e.g., 500, 1000, 5000)</param>
        /// <param name="maxConcurrentBatches">Max batches to process in parallel (1=sequential, 2-10=parallel)</param>
        /// <param name="batchProcessor">Async function to process each batch (receives batch items, batch number)</param>
        /// <param name="progressCallback">Optional callback invoked after each batch completes with current progress</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of results for each batch</returns>
        public async Task<List<BatchResult>> ProcessBatchesAsync(
            List<T> items,
            int batchSize,
            int maxConcurrentBatches,
            Func<List<T>, int, CancellationToken, Task<object?>> batchProcessor,
            Action<BatchProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (items == null || !items.Any())
            {
                _logger.LogWarning("No items to process");
                return new List<BatchResult>();
            }

            if (batchSize <= 0)
            {
                _logger.LogWarning("Invalid batch size {BatchSize}, defaulting to 500", batchSize);
                batchSize = 500;
            }

            if (maxConcurrentBatches <= 0)
            {
                _logger.LogWarning("Invalid max concurrent batches {MaxConcurrent}, defaulting to 1 (sequential)", maxConcurrentBatches);
                maxConcurrentBatches = 1;
            }

            var startTime = DateTime.UtcNow;
            var totalItems = items.Count;
            var batches = items
                .Select((item, index) => new { item, index })
                .GroupBy(x => x.index / batchSize)
                .Select((g, batchNum) => new
                {
                    BatchNumber = batchNum + 1,
                    Items = g.Select(x => x.item).ToList()
                })
                .ToList();

            var totalBatches = batches.Count;
            var results = new List<BatchResult>();
            var resultsLock = new object();

            var progress = new BatchProgress
            {
                TotalItems = totalItems,
                ProcessedItems = 0,
                TotalBatches = totalBatches,
                CompletedBatches = 0,
                FailedBatches = 0,
                RunningBatches = 0
            };

            // Use local ints for thread-safe Interlocked operations
            int processedItems = 0;
            int completedBatches = 0;
            int failedBatches = 0;
            int runningBatches = 0;

            _logger.LogInformation("⚡ PARALLEL BATCH PROCESSOR: Starting with {TotalItems} items, " +
                                 "{TotalBatches} batches of {BatchSize}, Max Concurrent: {MaxConcurrent}",
                totalItems, totalBatches, batchSize, maxConcurrentBatches);

            // Process batches with controlled parallelism
            var semaphore = new SemaphoreSlim(maxConcurrentBatches, maxConcurrentBatches);
            var tasks = new List<Task>();

            foreach (var batch in batches)
            {
                await semaphore.WaitAsync(cancellationToken);

                var batchTask = Task.Run(async () =>
                {
                    Interlocked.Increment(ref runningBatches);
                    progress.RunningBatches = runningBatches;
                    var batchStart = DateTime.UtcNow;
                    BatchResult result = null!;

                    try
                    {
                        _logger.LogInformation("📦 BATCH {BatchNum}/{Total}: Processing {Count} items (Concurrent: {Running})",
                            batch.BatchNumber, totalBatches, batch.Items.Count, progress.RunningBatches);

                        // Execute the batch processor
                        var data = await batchProcessor(batch.Items, batch.BatchNumber, cancellationToken);

                        var duration = DateTime.UtcNow - batchStart;

                        result = new BatchResult
                        {
                            BatchNumber = batch.BatchNumber,
                            ItemCount = batch.Items.Count,
                            Success = true,
                            Duration = duration,
                            Data = data
                        };

                        Interlocked.Add(ref processedItems, batch.Items.Count);
                        Interlocked.Increment(ref completedBatches);
                        progress.ProcessedItems = processedItems;
                        progress.CompletedBatches = completedBatches;

                        _logger.LogInformation("✅ BATCH {BatchNum}/{Total}: Completed {Count} items in {Duration:F2}s ({ItemsPerSec:F0} items/sec)",
                            batch.BatchNumber, totalBatches, batch.Items.Count, duration.TotalSeconds, batch.Items.Count / duration.TotalSeconds);
                    }
                    catch (Exception ex)
                    {
                        var duration = DateTime.UtcNow - batchStart;

                        result = new BatchResult
                        {
                            BatchNumber = batch.BatchNumber,
                            ItemCount = batch.Items.Count,
                            Success = false,
                            Duration = duration,
                            Error = ex
                        };

                        Interlocked.Add(ref processedItems, batch.Items.Count); // Count as processed even if failed
                        Interlocked.Increment(ref failedBatches);
                        progress.ProcessedItems = processedItems;
                        progress.FailedBatches = failedBatches;

                        _logger.LogError(ex, "❌ BATCH {BatchNum}/{Total}: Failed after {Duration:F2}s - {Error}",
                            batch.BatchNumber, totalBatches, duration.TotalSeconds, ex.Message);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref runningBatches);
                        progress.RunningBatches = runningBatches;
                        semaphore.Release();

                        lock (resultsLock)
                        {
                            results.Add(result);
                        }

                        // Update progress
                        var elapsed = DateTime.UtcNow - startTime;
                        progress.Elapsed = elapsed;
                        progress.ItemsPerSecond = progress.ProcessedItems / elapsed.TotalSeconds;

                        if (progress.ProcessedItems > 0 && progress.ProcessedItems < totalItems)
                        {
                            var itemsRemaining = totalItems - progress.ProcessedItems;
                            var secondsRemaining = itemsRemaining / progress.ItemsPerSecond;
                            progress.EstimatedTimeRemaining = TimeSpan.FromSeconds(secondsRemaining);
                        }
                        else
                        {
                            progress.EstimatedTimeRemaining = TimeSpan.Zero;
                        }

                        // Invoke progress callback
                        progressCallback?.Invoke(progress);
                    }
                }, cancellationToken);

                tasks.Add(batchTask);
            }

            // Wait for all batches to complete
            await Task.WhenAll(tasks);

            var totalDuration = DateTime.UtcNow - startTime;
            var successfulBatches = results.Count(r => r.Success);
            var totalFailedBatches = results.Count(r => !r.Success);

            _logger.LogInformation("🎯 PARALLEL BATCH PROCESSOR COMPLETE: " +
                                 "Total: {TotalItems} items, {TotalBatches} batches | " +
                                 "Success: {SuccessBatches}, Failed: {FailedBatches} | " +
                                 "Duration: {Duration:F2}s ({ItemsPerSec:F0} items/sec)",
                totalItems, totalBatches, successfulBatches, totalFailedBatches,
                totalDuration.TotalSeconds, totalItems / totalDuration.TotalSeconds);

            return results.OrderBy(r => r.BatchNumber).ToList();
        }

        /// <summary>
        /// Simple batch processing without return data - just success/failure tracking.
        /// </summary>
        public async Task<List<BatchResult>> ProcessBatchesAsync(
            List<T> items,
            int batchSize,
            int maxConcurrentBatches,
            Func<List<T>, int, CancellationToken, Task> batchProcessor,
            Action<BatchProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            return await ProcessBatchesAsync(
                items,
                batchSize,
                maxConcurrentBatches,
                async (batch, batchNum, ct) =>
                {
                    await batchProcessor(batch, batchNum, ct);
                    return null; // No return data
                },
                progressCallback,
                cancellationToken);
        }
    }
}
