namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Ambient bridge that lets <c>DapperRepositoryBase</c> (and any raw connection-open site) obtain the
/// CURRENT request's <see cref="ITenantConnectionResolver"/> without threading it through 40+ repository
/// constructors and without DataAccessLibrary taking an ASP.NET dependency.
///
/// HOW IT IS SET: the API installs a request-scoped resolver into this accessor at the start of each
/// request (after authentication has populated <see cref="ITenantContext"/>), then clears it when the
/// request ends. The value is held in an <see cref="AsyncLocal{T}"/>, so it flows with the request's
/// async context and never bleeds across requests.
///
/// FALLBACK / BACKWARD COMPAT: when nothing has been set for the current flow (WebPortal single-tenant,
/// the API before/outside a request, or a host that never wired multi-tenant), <see cref="Current"/> is
/// null and callers fall back to DefaultConnection — i.e. exactly today's single-tenant behavior. The
/// accessor adds NO behavior of its own; absence of a resolver == legacy path.
///
/// This is a deliberate, single, named ambient — not scattered static state. It is the ONE place the
/// shared data layer reaches for per-request tenant routing.
/// </summary>
public static class TenantConnectionAccessor
{
    private static readonly AsyncLocal<ITenantConnectionResolver?> _current = new();

    /// <summary>The resolver for the current async flow, or null when none is installed (legacy path).</summary>
    public static ITenantConnectionResolver? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
