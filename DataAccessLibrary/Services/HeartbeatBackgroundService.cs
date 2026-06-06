using DataAccessLibrary.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataAccessLibrary.Services;

/// <summary>
/// BackgroundService that runs on every execution server (primary and workers).
///
/// Responsibilities:
///   1. Waits for IExecutionServerContext.IsReady (set by ExecutionServerStartupService).
///   2. Collects system telemetry (CPU, memory, disk, GC, thread-pool) on each heartbeat cycle.
///   3. Records the heartbeat via IExecutionServerRegistry.RecordHeartbeatAsync.
///   4. On the primary server only: runs orphan detection every OrphanDetectionInterval to
///      reassign jobs whose claiming server has missed heartbeats.
///
/// The service is resilient — any exception inside the loop is logged and swallowed so
/// that a single transient failure (e.g. DB unavailable) does not crash the process.
/// </summary>
public class HeartbeatBackgroundService : BackgroundService
{
    private readonly IExecutionServerContext _executionServerContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HeartbeatBackgroundService> _logger;
    private readonly ExecutionServerOptions _options;

    // Tracks when we last ran orphan detection so we can space it out independently
    // of the heartbeat interval.
    private DateTime _lastOrphanDetectionUtc = DateTime.MinValue;

    // Shared Process object for CPU measurement — created once, reused on each cycle.
    private readonly System.Diagnostics.Process _currentProcess =
        System.Diagnostics.Process.GetCurrentProcess();

    // Tracks previous CPU sample for delta calculation.
    private TimeSpan _previousTotalProcessorTime = TimeSpan.Zero;
    private DateTime _previousCpuSampleUtc = DateTime.MinValue;

    public HeartbeatBackgroundService(
        IExecutionServerContext executionServerContext,
        IServiceProvider serviceProvider,
        ILogger<HeartbeatBackgroundService> logger,
        IOptions<ExecutionServerOptions> options)
    {
        _executionServerContext = executionServerContext ?? throw new ArgumentNullException(nameof(executionServerContext));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("HeartbeatBackgroundService: starting");

        // ----------------------------------------------------------------
        // Phase 1 — wait until the execution server context is ready.
        // IExecutionServerContext.IsReady is set to true by
        // ExecutionServerStartupService after InitializeAsync completes.
        // ----------------------------------------------------------------
        while (!_executionServerContext.IsReady && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("HeartbeatBackgroundService: cancelled before server was ready — exiting");
            return;
        }

        _logger.LogInformation(
            "HeartbeatBackgroundService: execution server is ready — starting heartbeat loop (interval: {Interval}s)",
            _options.HeartbeatInterval.TotalSeconds);

        // ----------------------------------------------------------------
        // Phase 2 — heartbeat loop.
        // ----------------------------------------------------------------
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HeartbeatBackgroundService: heartbeat cycle failed — will retry after interval");
            }

            try
            {
                await Task.Delay(_options.HeartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("HeartbeatBackgroundService: exiting heartbeat loop");
    }

    // ====================================================================
    // Private helpers
    // ====================================================================

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var telemetry = CollectTelemetry();

        // Use a scoped service provider so that IExecutionServerRegistry (Scoped)
        // gets its own DB connection per heartbeat cycle.
        await using var scope = _serviceProvider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IExecutionServerRegistry>();

        await registry.RecordHeartbeatAsync(telemetry, cancellationToken);

        _logger.LogDebug(
            "HeartbeatBackgroundService: heartbeat recorded (CPU: {Cpu:F1}%, Mem: {Mem}MB, Disk: {Disk:F1}GB free)",
            telemetry.CpuPercent, telemetry.MemoryUsedMb, telemetry.DiskFreeGb);

        // Primary-only: orphan detection
        if (_executionServerContext.IsPrimary)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastOrphanDetectionUtc) >= _options.OrphanDetectionInterval)
            {
                _lastOrphanDetectionUtc = now;
                try
                {
                    var reassigned = await registry.DetectAndRecoverOrphansAsync(
                        _options.HeartbeatTimeoutMinutes, cancellationToken);

                    if (reassigned > 0)
                    {
                        _logger.LogInformation(
                            "HeartbeatBackgroundService: orphan detection reassigned {Count} job(s) to Pending",
                            reassigned);
                    }
                    else
                    {
                        _logger.LogDebug("HeartbeatBackgroundService: orphan detection — no orphaned jobs found");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "HeartbeatBackgroundService: orphan detection failed — will retry next cycle");
                }
            }
        }
    }

    private ServerHeartbeatData CollectTelemetry()
    {
        // --- CPU ---
        double cpuPercent = 0;
        try
        {
            _currentProcess.Refresh();
            var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;
            var now = DateTime.UtcNow;

            if (_previousCpuSampleUtc != DateTime.MinValue)
            {
                var cpuUsed = (currentTotalProcessorTime - _previousTotalProcessorTime).TotalMilliseconds;
                var elapsed = (now - _previousCpuSampleUtc).TotalMilliseconds;
                var processorCount = Environment.ProcessorCount;

                if (elapsed > 0 && processorCount > 0)
                {
                    cpuPercent = Math.Round(cpuUsed / (elapsed * processorCount) * 100.0, 1);
                    cpuPercent = Math.Clamp(cpuPercent, 0, 100);
                }
            }

            _previousTotalProcessorTime = currentTotalProcessorTime;
            _previousCpuSampleUtc = now;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HeartbeatBackgroundService: could not read CPU usage");
        }

        // --- Memory ---
        long memoryUsedMb = 0;
        double memoryPercent = 0;
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            memoryUsedMb = _currentProcess.WorkingSet64 / (1024 * 1024);

            if (gcInfo.TotalAvailableMemoryBytes > 0)
            {
                memoryPercent = Math.Round(
                    (double)_currentProcess.WorkingSet64 / gcInfo.TotalAvailableMemoryBytes * 100.0, 1);
                memoryPercent = Math.Clamp(memoryPercent, 0, 100);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HeartbeatBackgroundService: could not read memory usage");
        }

        // --- Disk ---
        double diskFreeGb = 0;
        try
        {
            // Use the drive that hosts the application's base directory.
            var appDrive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\");
            diskFreeGb = Math.Round(appDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 2);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HeartbeatBackgroundService: could not read disk free space");
        }

        // --- GC ---
        long gcGen0 = 0;
        long gcGen2 = 0;
        double heapSizeMb = 0;
        try
        {
            gcGen0 = GC.CollectionCount(0);
            gcGen2 = GC.CollectionCount(2);
            heapSizeMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HeartbeatBackgroundService: could not read GC metrics");
        }

        // --- Thread pool ---
        int threadPoolActive = 0;
        int threadPoolQueued = 0;
        try
        {
            ThreadPool.GetAvailableThreads(out int availableWorkers, out _);
            ThreadPool.GetMaxThreads(out int maxWorkers, out _);
            ThreadPool.GetMinThreads(out _, out _);

            threadPoolActive = maxWorkers - availableWorkers;
            // PendingWorkItemCount is .NET 6+
            threadPoolQueued = (int)Math.Min(ThreadPool.PendingWorkItemCount, int.MaxValue);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HeartbeatBackgroundService: could not read thread-pool metrics");
        }

        return new ServerHeartbeatData
        {
            ServerId = _executionServerContext.ServerId,
            Timestamp = DateTime.UtcNow,
            CpuPercent = cpuPercent,
            MemoryPercent = memoryPercent,
            MemoryUsedMb = memoryUsedMb,
            DiskFreeGb = diskFreeGb,
            ActiveJobCount = 0,   // Will be wired to IExecutionServerJobRunner in Phase 3
            ThreadPoolActive = threadPoolActive,
            ThreadPoolQueued = threadPoolQueued,
            GcGen0Count = gcGen0,
            GcGen2Count = gcGen2,
            HeapSizeMb = heapSizeMb,
            IsHealthy = true
        };
    }
}
