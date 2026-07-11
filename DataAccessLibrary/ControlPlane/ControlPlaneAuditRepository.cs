using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Append-only writer for the control-plane <c>ControlPlaneAuditLog</c> table.
/// <see cref="TryWriteAsync"/> NEVER throws to the caller — an audit-write failure must not fail
/// the business operation it describes (it is logged instead). Detail must never contain secrets.
/// </summary>
public interface IControlPlaneAuditRepository
{
    Task TryWriteAsync(
        string actor, string action, Guid? tenantId = null, string? slug = null,
        string? clientIp = null, string? detail = null, CancellationToken cancellationToken = default);
}

public sealed class ControlPlaneAuditRepository : IControlPlaneAuditRepository
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public ControlPlaneAuditRepository(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString(ControlPlaneMigrationService.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Control-plane connection string '{ControlPlaneMigrationService.ConnectionStringName}' not found. " +
                "Configure it via user-secrets (dev) or an environment variable / secret store (prod).");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task TryWriteAsync(
        string actor, string action, Guid? tenantId = null, string? slug = null,
        string? clientIp = null, string? detail = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await conn.ExecuteAsync(@"
INSERT INTO ControlPlaneAuditLog (Actor, Action, TenantId, Slug, ClientIp, Detail)
VALUES (@Actor, @Action, @TenantId, @Slug, @ClientIp, @Detail)",
                new
                {
                    Actor = string.IsNullOrWhiteSpace(actor) ? "unknown" : Truncate(actor, 256),
                    Action = Truncate(action, 64),
                    TenantId = tenantId,
                    Slug = slug is null ? null : Truncate(slug, 40),
                    ClientIp = clientIp is null ? null : Truncate(clientIp, 64),
                    Detail = detail
                }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fire-and-forget-safe by contract: log and swallow. The business operation proceeds.
            _logger.LogWarning("Control-plane audit write failed for action {Action}: {ExType}: {Message}",
                action, ex.GetType().Name, ex.Message);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);
}
