using System.Security.Claims;
using DataAccessLibrary.ControlPlane;
using IdentityCenter.API.Authentication;
using IdentityCenter.API.Middleware;
using IdentityCenter.API.Services.Enroll;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityCenter.Tests;

/// <summary>
/// Day 4 (Conduit enrollment) fork-parity pins: enroll-code format/normalization/hashing, the
/// atomic consume SQL shape, the per-IP enroll limiter, Agent-key claim stamping, the June-11
/// TenantDataPolicy-denies-Agent invariant, Agent-key tenant routing in the middleware, the
/// HIGH-1 revocation-sweep SQL shape, and the row-shape/SurfacedAgentId invariants.
/// (The CSV-import halves of the upstream Day4 test file are WebPortal-only and have no fork
/// surface, so they are not mirrored here.)
/// </summary>
public class EnrollCodesTests
{
    [Fact]
    public void GenerateCode_IsBase32Grouped()
    {
        var code = EnrollCodes.GenerateCode();
        // 32 bytes → 52 base32 chars → 13 dash-separated groups of 4.
        Assert.Matches("^([A-Z2-7]{4}-){12}[A-Z2-7]{4}$", code);
    }

    [Fact]
    public void GenerateCode_IsUnique()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => EnrollCodes.GenerateCode()).ToHashSet();
        Assert.Equal(50, codes.Count);
    }

    [Fact]
    public void Hash_Is64LowercaseHex_AndDeterministic()
    {
        var code = EnrollCodes.GenerateCode();
        var hash = EnrollCodes.Hash(code);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
        Assert.Equal(hash, EnrollCodes.Hash(code));
    }

    [Fact]
    public void Hash_IgnoresGroupingAndCase()
    {
        // Dashes/whitespace are cosmetic and case is display-only: a pasted code must verify
        // however the user mangled the formatting.
        var code = EnrollCodes.GenerateCode();
        Assert.Equal(EnrollCodes.Hash(code), EnrollCodes.Hash(code.Replace("-", "")));
        Assert.Equal(EnrollCodes.Hash(code), EnrollCodes.Hash(code.ToLowerInvariant()));
        Assert.Equal(EnrollCodes.Hash(code), EnrollCodes.Hash(" " + code.Replace("-", " ") + " "));
    }

    [Fact]
    public void Hash_DiffersPerCode()
    {
        Assert.NotEqual(EnrollCodes.Hash("AAAA-BBBB"), EnrollCodes.Hash("AAAA-BBBC"));
    }
}

public class EnrollCodeConsumeSqlShapeTests
{
    // The repository consume path must be ONE atomic statement whose WHERE carries all three gates
    // (match + unconsumed + unexpired) and whose OUTPUT returns the claimed tenant. Reuse-reject and
    // expiry-reject are enforced by these predicates: a used code fails "UsedAt IS NULL", an expired
    // code fails "ExpiresAt > SYSUTCDATETIME()", and both read as null == the uniform 403.
    [Fact]
    public void ConsumeSql_IsSingleAtomicUpdateWithAllGates()
    {
        var sql = EnrollCodeRepository.ConsumeSql;
        Assert.Contains("UPDATE EnrollCodes", sql);
        Assert.Contains("OUTPUT inserted.TenantId", sql);
        Assert.Contains("UsedAt IS NULL", sql);
        Assert.Contains("ExpiresAt > SYSUTCDATETIME()", sql);
        Assert.Contains("CodeHash = @CodeHash", sql);
        Assert.Contains("UsedByInstanceId = @InstanceId", sql);
        // No read-then-write race window: not a SELECT-first script, and exactly one statement.
        Assert.DoesNotContain("SELECT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(";", sql);
    }
}

public class EnrollCodePurgeSqlShapeTests
{
    // M3: opportunistic purge at mint — sweeps ONLY rows expired more than a day ago (1-day
    // forensics grace), regardless of used/unused, and never touches live or in-grace rows.
    [Fact]
    public void PurgeSql_DeletesOnlyWellExpiredRows()
    {
        var sql = EnrollCodeRepository.PurgeSql;
        Assert.Contains("DELETE FROM EnrollCodes", sql);
        Assert.Contains("ExpiresAt < DATEADD(DAY, -1, SYSUTCDATETIME())", sql);
        // Purge is expiry-driven only: no UsedAt predicate (used rows age out too), no tenant filter.
        Assert.DoesNotContain("UsedAt", sql);
        Assert.DoesNotContain("TenantId", sql);
    }
}

public class EnrollRateLimiterTests
{
    private static EnrollRateLimiter NewLimiter(int? maxPerHour = null)
    {
        var settings = new Dictionary<string, string?>();
        if (maxPerHour is int m) settings["Enroll:MaxPerIpPerHour"] = m.ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new EnrollRateLimiter(config);
    }

    [Fact]
    public void AllowsUpToLimit_ThenDeniesWithRetryAfter()
    {
        var limiter = NewLimiter(maxPerHour: 3);
        for (var i = 0; i < 3; i++)
            Assert.True(limiter.TryAcquire("10.0.0.1", out _));

        Assert.False(limiter.TryAcquire("10.0.0.1", out var retryAfter));
        Assert.True(retryAfter > 0);
    }

    [Fact]
    public void DifferentIpsHaveIndependentWindows()
    {
        var limiter = NewLimiter(maxPerHour: 1);
        Assert.True(limiter.TryAcquire("10.0.0.1", out _));
        Assert.False(limiter.TryAcquire("10.0.0.1", out _));
        Assert.True(limiter.TryAcquire("10.0.0.2", out _));
    }

    [Fact]
    public void DefaultLimit_IsTen()
    {
        var limiter = NewLimiter();
        for (var i = 0; i < EnrollRateLimiter.DefaultMaxPerIpPerHour; i++)
            Assert.True(limiter.TryAcquire("10.0.0.9", out _));
        Assert.False(limiter.TryAcquire("10.0.0.9", out _));
    }

    [Fact]
    public void SlidingWindow_ReleasesAfterOldestAttemptAges()
    {
        // Window math on the underlying counter with injected time.
        var counter = new SlidingWindowCounter();
        var t0 = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var window = TimeSpan.FromHours(1);

        Assert.True(counter.TryAcquire("ip", 2, window, t0, out _));
        Assert.True(counter.TryAcquire("ip", 2, window, t0.AddMinutes(10), out _));
        Assert.False(counter.TryAcquire("ip", 2, window, t0.AddMinutes(30), out var retry));
        Assert.Equal(TimeSpan.FromMinutes(30), retry);
        // The t0 attempt ages out at t0+60 — a third attempt then fits.
        Assert.True(counter.TryAcquire("ip", 2, window, t0.AddMinutes(61), out _));
    }
}

public class ControlPlaneClaimsTests
{
    private static TenantApiKeyValidationResult Valid(TenantApiKeyScope scope, Guid? tenantId = null, Guid? agentId = null) =>
        new() { IsValid = true, KeyId = Guid.NewGuid(), TenantId = tenantId, AgentId = agentId, Scope = scope, Name = "k" };

    [Fact]
    public void AgentKey_GetsAgentClaimShape_AndNothingBroader()
    {
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var claims = ControlPlaneClaims.Build(Valid(TenantApiKeyScope.Agent, tenantId, agentId));

        Assert.Equal("Agent", claims.Single(c => c.Type == "key_type").Value);
        Assert.Equal(agentId.ToString(), claims.Single(c => c.Type == "agent_id").Value);
        Assert.Equal(tenantId.ToString(), claims.Single(c => c.Type == "tenant_id").Value);

        // EXACTLY the scopes the agent channel policies require — heartbeat + commands...
        var scopes = claims.Where(c => c.Type == "scope").Select(c => c.Value).OrderBy(v => v).ToList();
        Assert.Equal(new[] { "agent:commands", "agent:heartbeat" }, scopes);
        // ...and nothing that would satisfy AdminPolicy or read as a tenant-data key.
        Assert.DoesNotContain(claims, c => c.Type == "scope" && (c.Value == "admin" || c.Value == "tenant"));
    }

    [Fact]
    public void TenantKey_GetsTenantClaimShape()
    {
        var tenantId = Guid.NewGuid();
        var claims = ControlPlaneClaims.Build(Valid(TenantApiKeyScope.Tenant, tenantId));

        Assert.Equal("Tenant", claims.Single(c => c.Type == "key_type").Value);
        Assert.Equal("tenant", claims.Single(c => c.Type == "scope").Value);
        Assert.Equal(tenantId.ToString(), claims.Single(c => c.Type == "tenant_id").Value);
        Assert.DoesNotContain(claims, c => c.Type == "agent_id");
    }

    [Fact]
    public void AdminKey_GetsAdminClaimShape_NoTenant()
    {
        var claims = ControlPlaneClaims.Build(Valid(TenantApiKeyScope.Admin));

        Assert.Equal("ControlPlaneAdmin", claims.Single(c => c.Type == "key_type").Value);
        Assert.Equal("admin", claims.Single(c => c.Type == "scope").Value);
        Assert.DoesNotContain(claims, c => c.Type == "tenant_id");
        Assert.DoesNotContain(claims, c => c.Type == "agent_id");
    }

    [Fact]
    public void InvalidResult_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ControlPlaneClaims.Build(TenantApiKeyValidationResult.Fail("nope")));
    }
}

public class TenantDataPolicyAgentDenialTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static async Task<bool> Evaluate(ClaimsPrincipal principal)
    {
        var requirement = new TenantDataRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);
        await new TenantDataAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task ControlPlaneAgentKey_IsDeniedOnTenantData()
    {
        // The June-11 invariant, re-pinned for Day 4: a control-plane-issued Agent key (which now
        // ALSO carries tenant_id) must still be denied on /api/objects/* — sync uses the paired
        // Tenant-scope key. Build the principal from the REAL claim mapping so this test breaks if
        // the mapping and the policy ever drift apart.
        var claims = ControlPlaneClaims.Build(new TenantApiKeyValidationResult
        {
            IsValid = true,
            KeyId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            Scope = TenantApiKeyScope.Agent,
            Name = "conduit-agent"
        });

        Assert.False(await Evaluate(Principal(claims.ToArray())));
    }

    [Fact]
    public async Task LegacyAgentKey_StaysDenied()
    {
        Assert.False(await Evaluate(Principal(new Claim("key_type", "Agent"))));
    }

    [Fact]
    public async Task TenantKey_IsAllowed()
    {
        Assert.True(await Evaluate(Principal(
            new Claim("key_type", "Tenant"),
            new Claim("tenant_id", Guid.NewGuid().ToString()))));
    }

    [Fact]
    public async Task ControlPlaneAdminKey_StaysDenied()
    {
        Assert.False(await Evaluate(Principal(new Claim("key_type", "ControlPlaneAdmin"))));
    }
}

public class TenantConnectionScopeMiddlewareAgentTests
{
    private const string TenantConn = "Server=test;Database=IdentityCenter_acme;";

    private static DefaultHttpContext NewContext(Guid tenantId, TenantStatus status, params Claim[] claims)
    {
        var services = new ServiceCollection();
        var cache = new TenantStatusCache();
        cache.Set(tenantId, status);
        services.AddSingleton(cache);
        services.AddSingleton(Mock.Of<ITenantRegistryRepository>());
        services.AddSingleton<ITenantContext, AsyncLocalTenantContext>();
        services.AddSingleton(Mock.Of<ITenantConnectionResolver>(r => r.Resolve() == TenantConn));

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
            RequestServices = services.BuildServiceProvider()
        };
    }

    [Fact]
    public async Task AgentKeyWithTenantId_InstallsTenantRouting()
    {
        var tenantId = Guid.NewGuid();
        var context = NewContext(tenantId, TenantStatus.Active,
            new Claim("key_type", "Agent"),
            new Claim("agent_id", Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantId.ToString()));

        string? observedConn = null;
        var middleware = new TenantConnectionScopeMiddleware(_ =>
        {
            observedConn = TenantConnectionAccessor.Current?.Resolve();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(TenantConn, observedConn);          // heartbeat/claim writes hit the tenant DB
        Assert.Null(TenantConnectionAccessor.Current);   // cleared after the request unwinds
    }

    [Fact]
    public async Task LegacyAgentKey_NoTenantId_KeepsDefaultConnectionBehavior()
    {
        var context = NewContext(Guid.NewGuid(), TenantStatus.Active,
            new Claim("key_type", "Agent"),
            new Claim("agent_id", Guid.NewGuid().ToString()));

        string? observedConn = "sentinel";
        var middleware = new TenantConnectionScopeMiddleware(_ =>
        {
            observedConn = TenantConnectionAccessor.Current?.Resolve();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Null(observedConn); // no resolver installed → legacy DefaultConnection path
    }

    [Fact]
    public async Task AgentKey_SuspendedTenant_Gets403BeforeRouting()
    {
        var tenantId = Guid.NewGuid();
        var context = NewContext(tenantId, TenantStatus.Suspended,
            new Claim("key_type", "Agent"),
            new Claim("agent_id", Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantId.ToString()));

        var reachedNext = false;
        var middleware = new TenantConnectionScopeMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task TenantKey_StillInstallsTenantRouting()
    {
        var tenantId = Guid.NewGuid();
        var context = NewContext(tenantId, TenantStatus.Active,
            new Claim("key_type", "Tenant"),
            new Claim("tenant_id", tenantId.ToString()));

        string? observedConn = null;
        var middleware = new TenantConnectionScopeMiddleware(_ =>
        {
            observedConn = TenantConnectionAccessor.Current?.Resolve();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(TenantConn, observedConn);
    }
}

// ─── HIGH-1: sync-key binding + revocation sweep pins ────────────────────────

public class RevokeKeysForAgentSqlShapeTests
{
    // LOW-1 + HIGH-1: re-enroll/deactivate retires every prior LIVE credential for exactly one
    // (tenant, agent) pair — the Agent-scope identity key(s) AND the Tenant-scope sync key(s)
    // bound via AgentId. Never Admin keys, never other agents' keys, never already-revoked rows.
    [Fact]
    public void RevokeKeysForAgentSql_ScopesToOneTenantAgentPair_AgentAndBoundTenantKeys_LiveOnly()
    {
        var sql = TenantApiKeyRepository.RevokeKeysForAgentSql;
        Assert.Contains("UPDATE TenantApiKeys", sql);
        Assert.Contains("SET RevokedAt = @Now", sql);
        Assert.Contains("TenantId = @TenantId", sql);
        Assert.Contains("AgentId = @AgentId", sql);
        Assert.Contains("RevokedAt IS NULL", sql);
        // Admin keys can never match (they carry no TenantId, and the Scope guard excludes them).
        Assert.Contains("Scope IN ('Agent', 'Tenant')", sql);
    }

    // The pre-binding backfill: an UNBOUND Tenant key is swept ONLY by exact legacy mint name,
    // only when a name is supplied, and only for Tenant scope with AgentId IS NULL.
    [Fact]
    public void RevokeKeysForAgentSql_LegacyNameSweep_IsNameExact_UnboundTenantOnly_OptIn()
    {
        var sql = TenantApiKeyRepository.RevokeKeysForAgentSql;
        Assert.Contains("@LegacySyncKeyName IS NOT NULL", sql);
        Assert.Contains("Scope = 'Tenant' AND AgentId IS NULL AND Name = @LegacySyncKeyName", sql);
    }
}

public class TenantApiKeyRowShapeTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();

    // HIGH-1: a Tenant-scope key MAY carry an AgentId (sync-key binding) and stays valid…
    [Fact]
    public void TenantKey_WithAgentBinding_IsValidShape() =>
        Assert.Null(TenantApiKeyRepository.RowShapeError(TenantApiKeyScope.Tenant, T, A));

    // …but the binding is NEVER surfaced as agent identity — the claim set must stay unchanged.
    [Fact]
    public void TenantKey_WithAgentBinding_NeverSurfacesAgentId() =>
        Assert.Null(TenantApiKeyRepository.SurfacedAgentId(TenantApiKeyScope.Tenant, A));

    [Fact]
    public void AgentKey_SurfacesAgentId() =>
        Assert.Equal(A, TenantApiKeyRepository.SurfacedAgentId(TenantApiKeyScope.Agent, A));

    [Theory]
    [InlineData(TenantApiKeyScope.Tenant, false, false)] // tenant key without tenant
    [InlineData(TenantApiKeyScope.Admin, true, false)]   // admin key with tenant
    [InlineData(TenantApiKeyScope.Admin, false, true)]   // admin key with agent
    [InlineData(TenantApiKeyScope.Agent, true, false)]   // agent key without agent
    [InlineData(TenantApiKeyScope.Agent, false, true)]   // agent key without tenant
    public void MalformedRows_AreRejected(TenantApiKeyScope scope, bool hasTenant, bool hasAgent) =>
        Assert.NotNull(TenantApiKeyRepository.RowShapeError(scope, hasTenant ? T : null, hasAgent ? A : null));
}

public class EnrollControllerReEnrollTests
{
    private const string TenantConn = "Server=test;Database=IdentityCenter_acme;";

    private sealed class Harness
    {
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid InstanceId = Guid.NewGuid();
        public readonly Mock<IEnrollCodeRepository> Codes = new();
        public readonly Mock<ITenantRegistryRepository> Registry = new();
        public readonly Mock<ITenantApiKeyRepository> Keys = new();
        public readonly Mock<DataAccessLibrary.Repositories.IAgentRegistryRepository> Agents = new();
        public readonly Mock<IControlPlaneAuditRepository> Audit = new();
        public readonly IdentityCenter.API.Controllers.EnrollController Controller;

        public Harness(DataAccessLibrary.Models.Agent? existingAgent)
        {
            Codes.Setup(c => c.TryConsumeAsync(It.IsAny<string>(), InstanceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(TenantId);
            Registry.Setup(r => r.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TenantRecord
                {
                    Id = TenantId,
                    Slug = "acme",
                    DisplayName = "Acme",
                    Status = TenantStatus.Active,
                    IcDbConnectionString = TenantConn
                });
            Agents.Setup(a => a.GetByIdAsync(InstanceId)).ReturnsAsync(existingAgent);
            Agents.Setup(a => a.CreateOrGetWithIdAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
                .ReturnsAsync(new DataAccessLibrary.Models.Agent { Id = InstanceId, IsActive = false });
            Agents.Setup(a => a.SetActiveAsync(It.IsAny<Guid>(), It.IsAny<bool>())).ReturnsAsync(true);
            Keys.Setup(k => k.RevokeKeysForAgentAsync(TenantId, InstanceId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            Keys.Setup(k => k.CreateAgentAsync(TenantId, InstanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid.NewGuid(), "ic_agentkey"));
            Keys.Setup(k => k.CreateTenantKeyForAgentAsync(TenantId, InstanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid.NewGuid(), "ic_synckey"));

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Enroll:PublicBaseUrl"] = "https://api.test"
            }).Build();

            Controller = new IdentityCenter.API.Controllers.EnrollController(
                Codes.Object, Registry.Object, Keys.Object, Agents.Object, Audit.Object,
                new EnrollRateLimiter(config), config, Mock.Of<Logging.IGlobalLogger>())
            {
                ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        }

        public Task<Microsoft.AspNetCore.Mvc.IActionResult> EnrollAsync() =>
            Controller.Enroll(new IdentityCenter.API.Controllers.AgentEnrollRequest
            {
                EnrollCode = "AAAA-BBBB-CCCC-DDDD",
                InstanceId = InstanceId,
                Name = "HQ-DC-01",
                Version = "1.0"
            }, CancellationToken.None);
    }

    private static string? ErrorOf(object? payload) =>
        payload?.GetType().GetProperty("error")?.GetValue(payload) as string;

    [Fact]
    public async Task ActiveInstanceCollision_RejectedWithUniform403_NoMint_NoActivation()
    {
        // M1: a valid code must NOT be able to claim a LIVE agent's identity — that would mint a
        // second live credential and intercept commands (READPAST claim is first-wins).
        var h = new Harness(new DataAccessLibrary.Models.Agent { Id = Guid.NewGuid(), IsActive = true });

        var result = await h.EnrollAsync() as Microsoft.AspNetCore.Mvc.ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(403, result!.StatusCode);
        Assert.Equal("invalid_or_expired_code", ErrorOf(result.Value)); // same body as a bad code — no oracle
        h.Keys.Verify(k => k.CreateAgentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Keys.Verify(k => k.RevokeKeysForAgentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Agents.Verify(a => a.SetActiveAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        h.Audit.Verify(a => a.TryWriteAsync("agent-enroll", "EnrollRejected", h.TenantId, "acme",
            It.IsAny<string?>(), It.Is<string?>(d => d!.Contains("active_instance_collision")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InactiveAgent_ReEnrollAllowed_ReactivatesAndRevokesPriorKeys()
    {
        // The lost-credentials recovery path: admin deactivates the agent, mints a fresh code,
        // re-enrolls. Prior live agent keys are retired BEFORE the new mint (LOW-1).
        var h = new Harness(new DataAccessLibrary.Models.Agent { Id = Guid.NewGuid(), IsActive = false });

        var result = await h.EnrollAsync() as Microsoft.AspNetCore.Mvc.OkObjectResult;

        Assert.NotNull(result);
        h.Agents.Verify(a => a.SetActiveAsync(h.InstanceId, true), Times.Once);
        // HIGH-1: the revocation sweep covers agent keys AND sync keys, and passes the
        // deterministic legacy sync-key name so pre-binding keys are retired too.
        var expectedSyncName = string.Concat("conduit-sync-", h.InstanceId.ToString("N").Substring(0, 8));
        h.Keys.Verify(k => k.RevokeKeysForAgentAsync(h.TenantId, h.InstanceId, expectedSyncName, It.IsAny<CancellationToken>()), Times.Once);
        h.Keys.Verify(k => k.CreateAgentAsync(h.TenantId, h.InstanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        // HIGH-1: the fresh sync key is minted BOUND to the instance (never the unbound overload).
        h.Keys.Verify(k => k.CreateTenantKeyForAgentAsync(h.TenantId, h.InstanceId, expectedSyncName, It.IsAny<CancellationToken>()), Times.Once);
        h.Keys.Verify(k => k.CreateAsync(It.IsAny<TenantApiKeyScope>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var payload = result!.Value!;
        Assert.Equal("ic_agentkey", payload.GetType().GetProperty("agentApiKey")!.GetValue(payload));
        Assert.Equal("acme", payload.GetType().GetProperty("tenantSlug")!.GetValue(payload));
    }

    [Fact]
    public async Task UnknownInstanceId_EnrollSucceeds()
    {
        var h = new Harness(existingAgent: null);

        var result = await h.EnrollAsync() as Microsoft.AspNetCore.Mvc.OkObjectResult;

        Assert.NotNull(result);
        h.Agents.Verify(a => a.CreateOrGetWithIdAsync(h.InstanceId, It.IsAny<string>(), null, null, false), Times.Once);
        h.Agents.Verify(a => a.SetActiveAsync(h.InstanceId, true), Times.Once);
    }

    [Fact]
    public async Task InvalidCode_RejectIsAudited_WithoutTenantAttribution()
    {
        // M2: rejected attempts land in ControlPlaneAuditLog, and an unknown-code reject carries
        // NO tenant/slug (nothing verified to attribute it to).
        var h = new Harness(existingAgent: null);
        h.Codes.Setup(c => c.TryConsumeAsync(It.IsAny<string>(), h.InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await h.EnrollAsync() as Microsoft.AspNetCore.Mvc.ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(403, result!.StatusCode);
        h.Audit.Verify(a => a.TryWriteAsync("agent-enroll", "EnrollRejected", null, null,
            It.IsAny<string?>(), It.Is<string?>(d => d!.Contains("invalid_or_expired_code")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
