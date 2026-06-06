using Microsoft.AspNetCore.Authorization;

namespace IdentityCenter.API.Authentication;

/// <summary>
/// Authorization requirement for TENANT-DATA endpoints (Objects, Identities, …) in the multi-tenant API.
///
/// Policy intent (the admin/tenant separation half of the cross-tenant guard):
///   - A control-plane TENANT key (scope=tenant) is ALLOWED — its request is bound to exactly one
///     tenant DB by ITenantContext.
///   - A control-plane ADMIN key (scope=admin) is DENIED here. Admin keys are control-plane-only; there
///     is NO ambient admin access to tenant data. An admin who wants tenant data must use that tenant's
///     key. This is the explicit decision from the Day-4 brief.
///   - A LEGACY single-tenant key (no control-plane match; key_type is a legacy type such as Agent/Admin
///     issued by today's IC ApiKeys store) is ALLOWED — it operates against DefaultConnection, exactly as
///     before SaaS. Backward compatibility.
///
/// The distinguishing signal: control-plane keys carry key_type "ControlPlaneAdmin" or "Tenant" (set by
/// the auth handler). Anything else is a legacy key.
/// </summary>
public sealed class TenantDataRequirement : IAuthorizationRequirement
{
}

public sealed class TenantDataAuthorizationHandler : AuthorizationHandler<TenantDataRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantDataRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
            return Task.CompletedTask; // unauthenticated → fail (no Succeed call)

        var keyType = context.User.FindFirst("key_type")?.Value;

        // Control-plane ADMIN key → explicitly NOT allowed on tenant data.
        if (string.Equals(keyType, "ControlPlaneAdmin", StringComparison.Ordinal))
            return Task.CompletedTask; // do not Succeed → 403

        // Control-plane TENANT key → allowed (bound to one tenant DB by ITenantContext).
        if (string.Equals(keyType, "Tenant", StringComparison.Ordinal))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Legacy single-tenant key (any other authenticated key_type) → allowed for back-compat;
        // it runs against DefaultConnection because no tenant context was set.
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
