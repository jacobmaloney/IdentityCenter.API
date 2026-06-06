using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Background service that periodically checks for expired group memberships
    /// and automatically removes them (soft delete with IsActive = false)
    /// </summary>
    public class MembershipExpirationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MembershipExpirationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15); // Check every 15 minutes

        public MembershipExpirationService(
            IServiceProvider serviceProvider,
            ILogger<MembershipExpirationService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Membership Expiration Service started");

            // Stagger startup — no rush to check expirations in first 2 minutes
            await Task.Delay(TimeSpan.FromSeconds(120), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessExpiredMembershipsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing expired memberships");
                }

                // Wait for the next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Membership Expiration Service stopped");
        }

        private async Task ProcessExpiredMembershipsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var groupService = scope.ServiceProvider.GetRequiredService<IGroupService>();

            _logger.LogDebug("Checking for expired group memberships...");

            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            // Find all active memberships with expiration dates that have passed
            const string sql = @"
                SELECT
                    m.Id,
                    m.GroupId,
                    m.ObjectId,
                    m.IsActive,
                    m.ExpirationDate,
                    m.AddedDate,
                    m.AddedBy,
                    m.RemovedDate,
                    m.RemovedBy,
                    m.RemovalReason,
                    o.Id,
                    o.ObjectGuid,
                    o.DisplayName,
                    o.SamAccountName,
                    o.DistinguishedName,
                    o.ObjectType,
                    o.Enabled,
                    o.LastSyncedAt,
                    g.Id,
                    g.ObjectGuid,
                    g.Name,
                    g.Description,
                    g.DistinguishedName,
                    g.SamAccountName,
                    g.GroupScope,
                    g.GroupCategory,
                    g.IsActive AS GroupIsActive,
                    g.CreatedAt,
                    g.LastSyncedAt AS GroupLastSyncedAt
                FROM ObjectGroupMemberships m
                INNER JOIN Objects o ON m.ObjectId = o.Id
                INNER JOIN Groups g ON m.GroupId = g.Id
                WHERE m.IsActive = 1
                  AND m.ExpirationDate IS NOT NULL
                  AND m.ExpirationDate <= @UtcNow";

            List<ObjectGroupMembership> expiredMemberships;

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                expiredMemberships = (await connection.QueryAsync<ObjectGroupMembership, IdentityObject, Group, ObjectGroupMembership>(
                    sql,
                    (membership, obj, group) =>
                    {
                        membership.Object = obj;
                        membership.Group = group;
                        return membership;
                    },
                    new { UtcNow = DateTime.UtcNow },
                    splitOn: "Id,Id"
                )).ToList();
            }

            if (expiredMemberships.Any())
            {
                _logger.LogInformation("Found {Count} expired memberships to process", expiredMemberships.Count);

                foreach (var membership in expiredMemberships)
                {
                    try
                    {
                        _logger.LogInformation(
                            "Removing expired membership: {ObjectName} from {GroupName} (expired {ExpirationDate})",
                            membership.Object?.DisplayName ?? "Unknown",
                            membership.Group?.Name ?? "Unknown",
                            membership.ExpirationDate);

                        // Use GroupService to properly remove the member (maintains audit trail)
                        var success = await groupService.RemoveMemberAsync(
                            membership.GroupId,
                            membership.ObjectId,
                            reason: $"Membership expired on {membership.ExpirationDate:yyyy-MM-dd HH:mm:ss} UTC",
                            removedBy: "System-AutoExpiration",
                            writeBackToAD: true // 🔥 BADASS - AUTO-EXPIRATION WRITES TO ACTUAL AD!
                        );

                        if (success)
                        {
                            _logger.LogInformation(
                                "Successfully removed expired membership for {ObjectId} from group {GroupId}",
                                membership.ObjectId, membership.GroupId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Failed to remove expired membership for {ObjectId} from group {GroupId}",
                                membership.ObjectId, membership.GroupId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error removing expired membership {MembershipId} for object {ObjectId} from group {GroupId}",
                            membership.Id, membership.ObjectId, membership.GroupId);
                    }
                }

                _logger.LogInformation("Completed processing {Count} expired memberships", expiredMemberships.Count);
            }
            else
            {
                _logger.LogDebug("No expired memberships found");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Membership Expiration Service is stopping");
            await base.StopAsync(cancellationToken);
        }
    }
}
