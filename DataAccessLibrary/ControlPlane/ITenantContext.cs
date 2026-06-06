namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// The resolved tenant identity for the CURRENT request, derived ONLY from a validated control-plane
/// API key. Mirrors Conduit's TenantContext.
///
/// SECURITY: <see cref="TenantId"/> is the trust anchor for per-request connection resolution. It is set
/// exclusively by the authentication handler from the validated key row — never from a client-supplied
/// header, route value, or query param. Nothing in the request body can influence which tenant DB is
/// opened. This is the single defense against cross-tenant IDOR.
///
/// When no control-plane key matched (legacy single-tenant request, or no control plane configured),
/// the context is NEVER populated and the connection resolver falls back to DefaultConnection — the
/// existing single-tenant behavior, untouched.
/// </summary>
public interface ITenantContext
{
    /// <summary>True if a control-plane key resolved this request to a tenant or admin scope.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// The tenant whose DB this request may touch. Null for an admin/control-plane key OR an unresolved
    /// (legacy single-tenant) request. A tenant-scoped request always has this set.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>The validated key's scope, when resolved.</summary>
    TenantApiKeyScope? Scope { get; }

    /// <summary>Sets the resolved context for the current async flow. Called only by the auth handler.</summary>
    void Set(Guid? tenantId, TenantApiKeyScope scope);
}

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed tenant context. Registered as a SINGLETON: AsyncLocal already
/// isolates the value per async flow (i.e. per request), so a singleton holder is correct and avoids
/// any reliance on DI scope plumbing reaching deep into DapperRepositoryBase.
///
/// Why AsyncLocal and not just HttpContext.Items: the connection seam lives in DataAccessLibrary, which
/// must not take an ASP.NET dependency, and the value must flow into background continuations spawned
/// from the request. AsyncLocal flows with the logical call context exactly like the framework's own
/// ambient request state. WebPortal never calls Set(), so it always reads the unresolved default.
/// </summary>
public sealed class AsyncLocalTenantContext : ITenantContext
{
    private static readonly AsyncLocal<TenantScope?> _current = new();

    private sealed record TenantScope(Guid? TenantId, TenantApiKeyScope Scope);

    public bool IsResolved => _current.Value is not null;

    public Guid? TenantId => _current.Value?.TenantId;

    public TenantApiKeyScope? Scope => _current.Value?.Scope;

    public void Set(Guid? tenantId, TenantApiKeyScope scope) =>
        _current.Value = new TenantScope(tenantId, scope);
}
