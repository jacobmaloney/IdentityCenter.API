using System.Threading.Channels;
using Logging;

namespace IdentityCenter.API.Services;

/// <summary>
/// In-process, non-blocking queue for tenant provisioning jobs. POST /api/provision creates the
/// registry row (Status=Provisioning), enqueues the tenant id here, and returns 202 immediately — the
/// HTTP request is NEVER blocked while the catalog is created and V001..V135 run (which can take many
/// seconds to minutes). A background <see cref="ProvisioningHostedService"/> drains the channel and
/// runs <see cref="TenantProvisioningService"/> in its own DI scope.
///
/// De-duplicating by tenant id: a duplicate enqueue for a tenant already pending is coalesced, so a
/// double-submit cannot launch two concurrent provisions of the same catalog.
/// </summary>
public sealed class ProvisioningQueue
{
    private readonly Channel<ProvisioningWorkItem> _channel =
        Channel.CreateUnbounded<ProvisioningWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly HashSet<Guid> _pending = new();
    private readonly object _gate = new();
    private readonly IGlobalLogger _logger;

    /// <summary>
    /// Hard cap on simultaneously-pending provisions. Each provision is an EXPENSIVE operation
    /// (CREATE DATABASE + the full V001..V135 migration stream), so an attacker holding a leaked admin
    /// key could otherwise flood POST /api/provision and exhaust disk/CPU with hundreds of concurrent
    /// catalog builds. Beyond this many in-flight, Enqueue refuses (the controller surfaces 429) until the
    /// background drainer works the backlog down. Configurable via SaaS:MaxPendingProvisions; default 25.
    /// This is a coarse safety valve, NOT a billing quota (per-account caps are a tracked follow-up).
    /// </summary>
    private readonly int _maxPending;

    public ProvisioningQueue(IGlobalLogger logger, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _logger = logger;
        var configured = configuration.GetValue<int?>("SaaS:MaxPendingProvisions") ?? 25;
        _maxPending = configured > 0 ? configured : 25;
    }

    /// <summary>
    /// Result of an enqueue attempt: accepted, coalesced (already pending), or rejected because the
    /// in-flight provisioning cap is reached.
    /// </summary>
    public enum EnqueueResult { Accepted, Duplicate, CapacityExceeded }

    /// <summary>
    /// Enqueue a tenant for provisioning. Returns false (without enqueuing a duplicate) if the tenant
    /// already has a pending provisioning pass OR the in-flight cap is reached. Use
    /// <see cref="TryEnqueue"/> when the caller needs to distinguish those cases (e.g. 409 vs 429).
    /// </summary>
    public bool Enqueue(Guid tenantId, string? adminEmail) =>
        TryEnqueue(tenantId, adminEmail) == EnqueueResult.Accepted;

    /// <summary>
    /// Enqueue a tenant for provisioning, distinguishing the outcome. Enforces the in-flight cap and the
    /// per-tenant coalesce.
    /// </summary>
    public EnqueueResult TryEnqueue(Guid tenantId, string? adminEmail)
    {
        lock (_gate)
        {
            if (_pending.Contains(tenantId))
            {
                _logger.LogDebug("ProvisioningQueue: tenant {TenantId} already pending — coalesced", tenantId);
                return EnqueueResult.Duplicate;
            }

            if (_pending.Count >= _maxPending)
            {
                _logger.LogWarning(
                    "ProvisioningQueue: at capacity ({Count}/{Max}) — refusing tenant {TenantId}. Possible flood / leaked admin key.",
                    _pending.Count, _maxPending, tenantId);
                return EnqueueResult.CapacityExceeded;
            }

            _pending.Add(tenantId);
        }

        if (!_channel.Writer.TryWrite(new ProvisioningWorkItem(tenantId, adminEmail)))
        {
            lock (_gate) { _pending.Remove(tenantId); }
            _logger.LogWarning("ProvisioningQueue: failed to enqueue tenant {TenantId}", tenantId);
            return EnqueueResult.CapacityExceeded;
        }
        return EnqueueResult.Accepted;
    }

    internal ChannelReader<ProvisioningWorkItem> Reader => _channel.Reader;

    internal void MarkDequeued(Guid tenantId)
    {
        lock (_gate) { _pending.Remove(tenantId); }
    }
}

public readonly record struct ProvisioningWorkItem(Guid TenantId, string? AdminEmail);

/// <summary>
/// Background drainer for <see cref="ProvisioningQueue"/>. Creates a fresh DI scope per work item so
/// the scoped registry repository + <see cref="TenantProvisioningService"/> resolve correctly outside
/// any HTTP request scope.
/// </summary>
public sealed class ProvisioningHostedService : BackgroundService
{
    private readonly ProvisioningQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGlobalLogger _logger;

    public ProvisioningHostedService(
        ProvisioningQueue queue,
        IServiceScopeFactory scopeFactory,
        IGlobalLogger logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProvisioningHostedService started");
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _queue.MarkDequeued(item.TenantId);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();
                await svc.ProvisionAsync(item.TenantId, item.AdminEmail, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // ProvisionAsync already marks Failed internally; this is the last-resort net.
                _logger.LogError(ex, "ProvisioningHostedService: tenant {TenantId} threw outside ProvisionAsync", item.TenantId);
            }
        }
        _logger.LogInformation("ProvisioningHostedService stopping");
    }
}
