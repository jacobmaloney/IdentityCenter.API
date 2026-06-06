namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// An <see cref="ITenantConnectionResolver"/> that always returns a fixed, pre-resolved connection
/// string. Used by BACKGROUND drainers (post-process / provisioning continuations) that run OUTSIDE the
/// HTTP request, where the request's AsyncLocal tenant context does NOT flow.
///
/// The pattern: the request thread (where the real resolver is live) resolves the tenant connection
/// string ONCE and hands it to the background work item. The drainer installs a FixedConnectionResolver
/// carrying exactly that string into <see cref="TenantConnectionAccessor"/> for the lifetime of that one
/// work item, so the shared data layer routes to the correct tenant DB even off-request — then clears it.
///
/// This keeps the tenant decision anchored to the request that enqueued the work (the validated key),
/// never to anything the background thread could infer on its own.
/// </summary>
public sealed class FixedConnectionResolver : ITenantConnectionResolver
{
    private readonly string _connectionString;

    public FixedConnectionResolver(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public string Resolve() => _connectionString;
}
