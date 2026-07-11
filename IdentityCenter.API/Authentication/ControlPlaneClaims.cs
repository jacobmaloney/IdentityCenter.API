using System.Security.Claims;
using DataAccessLibrary.ControlPlane;

namespace IdentityCenter.API.Authentication;

/// <summary>
/// Builds the claim set for a validated CONTROL-PLANE key. Extracted from the auth handler so the
/// mapping — the contract between authentication, the authorization policies, and
/// TenantConnectionScopeMiddleware — is a pure, unit-testable function.
///
/// Per scope:
///   - Admin  → key_type=ControlPlaneAdmin, scope=admin (AdminPolicy; denied on tenant data).
///   - Tenant → key_type=Tenant, scope=tenant, tenant_id (TenantDataPolicy + tenant DB routing).
///   - Agent  → key_type=Agent, agent_id, scope=agent:commands + scope=agent:heartbeat, tenant_id.
///     EXACTLY the claims AgentChannelHeartbeatPolicy / AgentChannelCommandsPolicy /
///     AgentCommandsCompletePolicy require — and key_type=Agent keeps
///     TenantDataAuthorizationHandler DENYING this key on /api/objects/* (the June-11 invariant:
///     an agent key never reads or pushes tenant data; the paired Tenant-scope sync key does).
///     Deliberately NO scope=tenant and NO scope=admin on an agent key.
///
/// tenant_id is the trust anchor TenantConnectionScopeMiddleware reads to install per-tenant DB
/// routing; it is server-set here from the validated key row, never client-supplied.
/// </summary>
public static class ControlPlaneClaims
{
    public static List<Claim> Build(TenantApiKeyValidationResult cp)
    {
        if (!cp.IsValid)
            throw new ArgumentException("Cannot build claims for an invalid key.", nameof(cp));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, cp.KeyId.ToString()),
            new(ClaimTypes.Name, cp.Name ?? "Control-plane key"),
        };

        switch (cp.Scope)
        {
            case TenantApiKeyScope.Admin:
                claims.Add(new Claim("key_type", "ControlPlaneAdmin"));
                claims.Add(new Claim("scope", "admin"));
                break;

            case TenantApiKeyScope.Tenant:
                claims.Add(new Claim("key_type", "Tenant"));
                claims.Add(new Claim("scope", "tenant"));
                break;

            case TenantApiKeyScope.Agent:
                claims.Add(new Claim("key_type", "Agent"));
                claims.Add(new Claim("agent_id", cp.AgentId!.Value.ToString()));
                claims.Add(new Claim("scope", "agent:commands"));
                claims.Add(new Claim("scope", "agent:heartbeat"));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(cp), cp.Scope, "Unknown key scope.");
        }

        if (cp.TenantId is Guid tid)
            claims.Add(new Claim("tenant_id", tid.ToString()));

        return claims;
    }
}
