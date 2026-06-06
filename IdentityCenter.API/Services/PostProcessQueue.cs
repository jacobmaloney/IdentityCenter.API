using System.Threading.Channels;
using DataAccessLibrary.Services;
using Logging;

namespace IdentityCenter.API.Services;

/// <summary>
/// Phase 2.2 Part D. In-process, non-blocking queue for ingest-triggered
/// post-processing. The bulk/post-process endpoints enqueue a connection id and
/// return immediately — the HTTP request is NEVER blocked while person-match +
/// manager resolution run over a large batch. A background <see cref="PostProcessHostedService"/>
/// drains the channel and runs <see cref="IngestPostProcessingService"/> in its
/// own DI scope.
///
/// Bounded + de-duplicating: many bulk batches for the same connection collapse
/// to a single pending post-process (the channel coalesces by connection id), so
/// a burst of 50 batches doesn't enqueue 50 redundant full-connection passes.
/// </summary>
public sealed class PostProcessQueue
{
    private readonly Channel<PostProcessWorkItem> _channel =
        Channel.CreateUnbounded<PostProcessWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    // Tracks connection ids already pending so repeated enqueues coalesce.
    private readonly HashSet<Guid> _pending = new();
    private readonly object _gate = new();
    private readonly IGlobalLogger _logger;

    public PostProcessQueue(IGlobalLogger logger) => _logger = logger;

    /// <summary>
    /// Enqueue a connection for post-processing. Returns false (without enqueuing
    /// a duplicate) if the connection already has a pending pass.
    ///
    /// <paramref name="tenantConnectionString"/> is the tenant DB connection RESOLVED ON THE REQUEST
    /// THREAD (where the tenant context is live). It is carried through to the background drainer so the
    /// post-process pass runs against the SAME tenant DB the bulk write landed in — not DefaultConnection.
    /// Null ⇒ legacy/admin/single-tenant request: the drainer runs against DefaultConnection (unchanged).
    /// </summary>
    public bool Enqueue(Guid connectionId, bool runPersonMatch, bool runManagerResolution, string? tenantConnectionString = null)
    {
        lock (_gate)
        {
            if (!_pending.Add(connectionId))
            {
                _logger.LogDebug("PostProcessQueue: connection {ConnectionId} already pending — coalesced", connectionId);
                return false;
            }
        }

        var item = new PostProcessWorkItem(connectionId, runPersonMatch, runManagerResolution, tenantConnectionString);
        if (!_channel.Writer.TryWrite(item))
        {
            // Unbounded writer never fails, but be honest if it ever does.
            lock (_gate) { _pending.Remove(connectionId); }
            _logger.LogWarning("PostProcessQueue: failed to enqueue connection {ConnectionId}", connectionId);
            return false;
        }
        return true;
    }

    internal ChannelReader<PostProcessWorkItem> Reader => _channel.Reader;

    /// <summary>Called by the host service once it picks an item up, so a new
    /// enqueue for the same connection (arriving after the pass started) is allowed.</summary>
    internal void MarkDequeued(Guid connectionId)
    {
        lock (_gate) { _pending.Remove(connectionId); }
    }
}

public readonly record struct PostProcessWorkItem(
    Guid ConnectionId, bool RunPersonMatch, bool RunManagerResolution, string? TenantConnectionString);

/// <summary>
/// Background drainer for <see cref="PostProcessQueue"/>. Creates a fresh DI
/// scope per work item so the scoped repositories + IngestPostProcessingService
/// resolve correctly outside any HTTP request scope.
/// </summary>
public sealed class PostProcessHostedService : BackgroundService
{
    private readonly PostProcessQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGlobalLogger _logger;

    public PostProcessHostedService(
        PostProcessQueue queue,
        IServiceScopeFactory scopeFactory,
        IGlobalLogger logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PostProcessHostedService started");
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _queue.MarkDequeued(item.ConnectionId);
            try
            {
                // Re-establish tenant routing off-request: install a fixed resolver carrying the tenant
                // connection string captured at enqueue time, so the post-process pass hits the SAME
                // tenant DB the bulk write landed in. Null ⇒ legacy/single-tenant → DefaultConnection.
                if (!string.IsNullOrWhiteSpace(item.TenantConnectionString))
                    DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current =
                        new DataAccessLibrary.ControlPlane.FixedConnectionResolver(item.TenantConnectionString);

                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IngestPostProcessingService>();
                var result = await svc.RunForConnectionAsync(
                    item.ConnectionId, item.RunPersonMatch, item.RunManagerResolution, stoppingToken);

                if (result.HadError)
                {
                    _logger.LogWarning(
                        "PostProcess for connection {ConnectionId} completed WITH ERRORS (personMatch='{PM}', manager='{MR}')",
                        item.ConnectionId, result.PersonMatchError, result.ManagerResolutionError);
                }
                else
                {
                    _logger.LogInformation(
                        "PostProcess for connection {ConnectionId} OK — matched={Matched}, created={Created}, managers={Managers}, {Ms}ms",
                        item.ConnectionId, result.PersonsMatched, result.PersonsCreated, result.ManagersResolved, result.DurationMs);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostProcess for connection {ConnectionId} threw", item.ConnectionId);
            }
            finally
            {
                // Clear tenant routing before the next item: the drainer reuses its thread, so a stale
                // resolver must never carry one tenant's connection into the next item's pass.
                DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current = null;
            }
        }
        _logger.LogInformation("PostProcessHostedService stopping");
    }
}
