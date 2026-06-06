using DataAccessLibrary.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Background service that runs every hour and deactivates delegation assignments
/// whose <c>ExpiresAt</c> timestamp has passed.
/// </summary>
public class DelegationExpiryService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DelegationExpiryService> _logger;

    public DelegationExpiryService(IServiceProvider serviceProvider, ILogger<DelegationExpiryService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DelegationExpiryService started. Checking for expired assignments every {Interval}.", CheckInterval);

        // Run once immediately at startup, then repeat on the interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeactivateExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — do not log as error.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DelegationExpiryService encountered an error while deactivating expired assignments.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("DelegationExpiryService stopped.");
    }

    private async Task DeactivateExpiredAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDelegationRepository>();

        var deactivated = await repo.DeactivateExpiredAssignmentsAsync(ct);

        if (deactivated > 0)
        {
            _logger.LogInformation(
                "DelegationExpiryService deactivated {Count} expired delegation assignment(s).",
                deactivated);
        }
        else
        {
            _logger.LogDebug("DelegationExpiryService: no expired assignments found.");
        }
    }
}
