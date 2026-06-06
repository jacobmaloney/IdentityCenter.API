using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Default <see cref="ITenantConnectionResolver"/>.
///
/// Registered SCOPED (per request). It memoizes the resolved connection string for the lifetime of the
/// scope so the control-plane registry is hit at most once per request even though
/// <c>DapperRepositoryBase</c> asks for the string on every DB call.
///
/// ISOLATION CONTRACT (the whole point of DB-per-tenant):
///   - Tenant-scoped context  ⇒ returns ONLY that tenant's decrypted connection string. If the tenant
///     row is missing or has no connection string, it THROWS. It never degrades to DefaultConnection for
///     a tenant request — silently using Default would point the tenant at the shared/wrong DB, which is
///     the exact cross-tenant leak this design exists to prevent.
///   - Admin or unresolved context ⇒ DefaultConnection (control-plane ops + existing single-tenant).
///
/// The tenant id is read from <see cref="ITenantContext"/> only; no client input reaches this class.
/// </summary>
public sealed class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantRegistryRepository _registry;
    private readonly IConfiguration _configuration;
    private readonly IGlobalLogger _logger;

    // Per-scope memo. Keyed defensively by the tenant id the value was resolved FOR, so a context that
    // somehow changes mid-scope cannot serve a stale other-tenant string.
    private string? _memoized;
    private Guid? _memoizedFor;
    private bool _memoizedDefault;

    public TenantConnectionResolver(
        ITenantContext tenantContext,
        ITenantRegistryRepository registry,
        IConfiguration configuration,
        IGlobalLogger logger)
    {
        _tenantContext = tenantContext;
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
    }

    public string Resolve()
    {
        // Tenant-scoped request: resolve that tenant's DB, and ONLY that tenant's DB.
        if (_tenantContext.IsResolved
            && _tenantContext.Scope == TenantApiKeyScope.Tenant
            && _tenantContext.TenantId is Guid tenantId)
        {
            // Serve the memo only if it was resolved for THIS exact tenant.
            if (_memoized is not null && _memoizedFor == tenantId)
                return _memoized;

            // GetConnectionStringAsync decrypts the tenant's stored IcDbConnectionString. Resolver's
            // contract is synchronous (DapperRepositoryBase reads a string field), and the registry call
            // is a single indexed read; block on it once per scope. This is intentional and bounded.
            var connStr = _registry.GetConnectionStringAsync(tenantId).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(connStr))
            {
                // HARD FAIL — never fall back to DefaultConnection for a tenant request.
                _logger.LogError(
                    "TenantConnectionResolver: no connection string for tenant {TenantId}; refusing to fall back to DefaultConnection",
                    tenantId);
                throw new InvalidOperationException(
                    $"No connection string is configured for tenant {tenantId}. The request cannot be served against another database.");
            }

            _memoized = connStr;
            _memoizedFor = tenantId;
            return connStr;
        }

        // Admin scope OR unresolved (legacy single-tenant): DefaultConnection.
        if (_memoizedDefault && _memoized is not null)
            return _memoized;

        var defaultConn = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _memoized = defaultConn;
        _memoizedFor = null;
        _memoizedDefault = true;
        return defaultConn;
    }
}
