using System.Security.Claims;
using DataAccessLibrary.ControlPlane;

namespace IdentityCenter.API.Middleware;

/// <summary>
/// Installs (and, in its finally, clears) the ambient tenant routing for the CURRENT request.
///
/// WHY THIS LIVES IN MIDDLEWARE AND NOT THE AUTH HANDLER (the Day-6 fix):
/// <see cref="System.Threading.AsyncLocal{T}"/> values set INSIDE an awaited callee do NOT flow back out
/// to the caller after the await completes — they flow DOWN into deeper async frames, never UP. The
/// ASP.NET auth handler runs as an awaited subtree of the authorization middleware; anything it set on an
/// AsyncLocal was therefore GONE by the time the controller action ran, and every tenant request silently
/// fell back to DefaultConnection (a total isolation break). The fix: set the AsyncLocal HERE, in a
/// middleware frame that is an ANCESTOR of the controller, so <c>await _next(context)</c> runs deeper and
/// the value is visible the whole way down to <c>DapperRepositoryBase</c>.
///
/// TRUST ANCHOR: the tenant id is read from the AUTHENTICATED principal's claims — claims that the auth
/// handler set SERVER-SIDE from the validated control-plane key row. A client cannot inject a claim into a
/// server-constructed <see cref="ClaimsPrincipal"/> without forging the whole authentication ticket (which
/// requires the raw key), so this is equivalent to reading the validated key row. We honor a tenant id
/// ONLY when key_type == "Tenant" (also server-set), so an admin/legacy principal can never be steered at
/// a tenant DB. Nothing from the request body, route, query, or a raw header reaches this decision.
///
/// Registered AFTER <c>UseAuthentication()</c> (so <c>context.User</c> is populated) and BEFORE
/// <c>UseAuthorization()</c>/<c>MapControllers()</c> (so the resolver is live for the action).
/// </summary>
public sealed class TenantConnectionScopeMiddleware
{
    private readonly RequestDelegate _next;

    public TenantConnectionScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            var user = context.User;
            if (user?.Identity?.IsAuthenticated == true &&
                string.Equals(user.FindFirst("key_type")?.Value, "Tenant", StringComparison.Ordinal) &&
                Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId) &&
                tenantId != Guid.Empty)
            {
                // Control-plane TENANT key: pin this request to exactly that tenant's DB. Resolve the
                // scoped services from the request container so they share this request's DI scope.
                var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
                var resolver = context.RequestServices.GetRequiredService<ITenantConnectionResolver>();

                tenantContext.Set(tenantId, TenantApiKeyScope.Tenant);
                TenantConnectionAccessor.Current = resolver;
            }
            // Admin / legacy / unauthenticated: leave the accessor null → DefaultConnection (unchanged).

            await _next(context);
        }
        finally
        {
            // Guarantee the slot is null again once the request unwinds, so a pooled thread can never
            // observe a stale resolver and route to the wrong tenant's DB.
            TenantConnectionAccessor.Current = null;
        }
    }
}
