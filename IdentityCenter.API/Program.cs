using System.IO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Common.Encryption;
using DataAccessLibrary.ControlPlane;
using DataAccessLibrary.Data;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using DataAccessLibrary.Services;
using IdentityCenter.API.Authentication;
using IdentityCenter.API.Middleware;
using IdentityCenter.API.Services;
using Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Run cleanly as a Windows Service when installed via install-service.ps1 (sc.exe / New-Service).
// This is a NO-OP when launched from the console (dotnet run / .exe in a terminal): the host
// auto-detects whether it was started by the Windows Service Control Manager, so this single call
// is safe for dev, console, and service runs alike — no guard needed. It gives the service proper
// lifetime (clean SCM start/stop, no console window, integrates with auto-restart-on-failure).
// NOTE: this is an intentional, deployment-only divergence from the IdentityCenter-repo copy of
// Program.cs — do not mirror it back upstream.
builder.Host.UseWindowsService();

// ── Listen URL for the PUBLISHED app ─────────────────────────────────────────
// launchSettings.json (which sets the dev URL) is NOT included in a publish build, so a published
// app has no port to bind unless we supply one. ASPNETCORE_URLS (the standard env var) takes priority;
// only when it is absent do we fall back to the "DefaultUrls" key in appsettings.json. This ordering
// is deliberate: a literal "Urls" key in appsettings is authoritative and CANNOT be overridden by
// ASPNETCORE_URLS (verified), which would defeat per-machine network binding. By using a custom
// "DefaultUrls" key applied only as a fallback, ASPNETCORE_URLS=http://0.0.0.0:5062 (set by
// install-service.ps1 / on the server) always wins.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    var defaultUrls = builder.Configuration["DefaultUrls"];
    if (!string.IsNullOrWhiteSpace(defaultUrls))
    {
        builder.WebHost.UseUrls(defaultUrls);
    }
}

// Configure Serilog. A rolling FILE sink is added alongside the console so the app is
// observable when running as a Windows Service (where console output goes nowhere).
// Default location is under ProgramData (persists across publish-folder redeploys, same
// root as the DataProtection keyring); override with the Logging:Directory config key.
var logDirectory = builder.Configuration["Logging:Directory"];
if (string.IsNullOrWhiteSpace(logDirectory))
{
    logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "IdentityCenter", "logs");
}
try { Directory.CreateDirectory(logDirectory); } catch { /* if unwritable, the file sink no-ops; console still works */ }

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    // In-memory ring buffer (last 2000 events) backing the /admin/logs live view. The static
    // Instance is used because Log.Logger is configured before the DI container exists; the
    // same instance is registered in DI below so the UI reads the buffer this logger fills.
    .WriteTo.Sink(InMemoryLogSink.Instance)
    .WriteTo.File(
        Path.Combine(logDirectory, "identitycenter-api-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "IdentityCenter API",
        Version = "v1",
        Description = "API for remote sync agents and job queue management"
    });

    // Add API Key authentication to Swagger
    c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "API Key authentication. Enter your API key in the value field.",
        Name = "X-API-Key",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "ApiKey"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── DataProtection + encryption ─────────────────────────────────────────────
// MUST match ConnectionStringProtector / WebPortal byte-for-byte (application name +
// keyring directory + purpose) so values encrypted by one process decrypt in the other.
// The control-plane registry uses IEncryptionService to protect tenant DB connection
// strings and Conduit tokens at rest.
builder.Services.AddDataProtection()
    .SetApplicationName("IdentityCenter")
    .PersistKeysToFileSystem(new DirectoryInfo(@"C:\ProgramData\IdentityCenter\Keys"));
builder.Services.AddScoped<IEncryptionService, EncryptionService>();

// ── SaaS control plane (tenant registry) ────────────────────────────────────
// DB-per-tenant. The control-plane connection string is read from
// ConnectionStrings:ControlPlane (user-secrets in dev, env/secret store in prod) — never
// hardcoded, never the tenant DefaultConnection. /provision (Day 3) + per-tenant
// resolution (Day 4) build on these.
builder.Services.AddScoped<ITenantRegistryRepository, TenantRegistryRepository>();
builder.Services.AddSingleton<ControlPlaneMigrationService>();

// ── SaaS Day 4: per-tenant request resolution + tenant-scoped keys ───────────
// Control-plane API-key authority (resolves a key → {TenantId, Scope} BEFORE any tenant DB is opened).
builder.Services.AddScoped<ITenantApiKeyRepository, TenantApiKeyRepository>();
// Day 4 enroll (POST /api/agent/enroll): single-use enroll codes + append-only control-plane audit
// + the dedicated per-IP enroll limiter (singleton — one sliding window across all requests).
builder.Services.AddScoped<IEnrollCodeRepository, EnrollCodeRepository>();
builder.Services.AddScoped<IControlPlaneAuditRepository, ControlPlaneAuditRepository>();
builder.Services.AddSingleton<IdentityCenter.API.Services.Enroll.EnrollRateLimiter>();
// Per-node 60s TTL cache backing the tenant-suspension gate in TenantConnectionScopeMiddleware.
builder.Services.AddSingleton<TenantStatusCache>();
// ITenantContext is AsyncLocal-backed; SINGLETON is correct (the AsyncLocal isolates per request flow).
builder.Services.AddSingleton<ITenantContext, AsyncLocalTenantContext>();
// Per-request connection resolver (memoizes per scope). SCOPED so it sees this request's tenant context.
builder.Services.AddScoped<ITenantConnectionResolver, TenantConnectionResolver>();

// ── /provision (Day 3): staged-async tenant provisioning ────────────────────
// POST /api/provision (AdminPolicy only) creates a registry row + enqueues a background job that
// creates the tenant catalog and runs V001..V135 + seeds the admin. The HTTP request never blocks on
// the migration. The one-time admin credential lives ONLY in OneTimeCredentialVault (memory) until the
// first status read. Queue + vault are singletons (shared across requests + the hosted drainer); the
// provisioning service is scoped and resolved per-job inside the drainer's own scope.
builder.Services.AddSingleton<IdentityCenter.API.Services.OneTimeCredentialVault>();
builder.Services.AddSingleton<IdentityCenter.API.Services.ProvisioningQueue>();
builder.Services.AddScoped<IdentityCenter.API.Services.TenantProvisioningService>();
builder.Services.AddHostedService<IdentityCenter.API.Services.ProvisioningHostedService>();

// Register services
builder.Services.AddSingleton<IGlobalLogger, GlobalLogger>();
builder.Services.AddScoped<IJobQueueRepository, JobQueueRepository>();
builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
// Backs ConduitController (/api/conduit/register) — writes the encrypted Conduit
// URL + token into the calling key's tenant Settings (Conduit category). Tenant
// routing is implicit via DapperRepositoryBase + TenantConnectionAccessor.
builder.Services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
builder.Services.AddScoped<IAgentRepository, AgentRepository>();
// IAdminRepository backs the admin login's external-IDP support (reads the SHARED
// IdentityProviders table the WebPortal's configuration page writes). It is the same forked
// DataAccessLibrary repository the portal uses; its ctor needs IChangeHistoryService.
builder.Services.AddScoped<ChangeHistory.Services.IChangeHistoryService, ChangeHistory.Services.ChangeHistoryService>();
builder.Services.AddScoped<IAdminRepository, AdminRepository>();
builder.Services.AddScoped<ISqlLicenseRepository, SqlLicenseRepository>();
builder.Services.AddScoped<ISqlLicenseComplianceEngine, SqlLicenseComplianceEngine>();
// Agent command channel (V138 AgentCommands + V140 targeting) — backs
// AgentCommandsController; the Conduit SQL Discovery poller consumes it via /api/agent/commands.
builder.Services.AddScoped<IAgentCommandRepository, AgentCommandRepository>();
// V140 Agents registry (admin-enrolled installations; per-agent keys carry agent_id).
// Distinct from IAgentRepository, which serves the execution-server RemoteAgents channel.
builder.Services.AddScoped<IAgentRegistryRepository, AgentRegistryRepository>();

// Repositories backing the public API controllers (Prompt 11 Part 2)
builder.Services.AddScoped<IIdentityRepository, IdentityRepository>();
builder.Services.AddScoped<IComplianceQueryRepository, ComplianceQueryRepository>();
builder.Services.AddScoped<ILicenseRepository, LicenseRepository>();
builder.Services.AddScoped<ISyncExecutionRepository, SyncExecutionRepository>();

// ── Phase 2.2: ingest post-processing (Parts A + D) ─────────────────────────
// Sync repository graph needed by IngestPostProcessingService + PersonMatchOrchestrator.
// SyncObjectRepository needs IAuditLogService (registered below). PersonMatchOrchestrator's
// optional IProcessEventPublisher is left unregistered — its ctor defaults it to null,
// so workflow events simply don't fire from an ingest pass (the bulk endpoint already
// owns directory/audit side-effects). This is the minimal closure: NO PersonMatchingService
// / FuzzyMatchingService needed (the orchestrator wrapper doesn't depend on them).
builder.Services.AddScoped<ISyncObjectRepository, SyncObjectRepository>();
builder.Services.AddScoped<ICloudActivityRepository, CloudActivityRepository>();
builder.Services.AddScoped<ISyncRelationshipRepository, SyncRelationshipRepository>();
builder.Services.AddScoped<ISyncScriptRepository, SyncScriptRepository>();
builder.Services.AddScoped<ISyncRepository, SyncRepositoryFacade>();
builder.Services.AddScoped<PersonMatchOrchestrator>();
builder.Services.AddScoped<IngestPostProcessingService>();

// Non-blocking post-process queue + background drainer (Part D). Queue is a
// singleton (shared across requests + the hosted service); the drainer creates
// its own scope per work item.
builder.Services.AddSingleton<IdentityCenter.API.Services.PostProcessQueue>();
builder.Services.AddHostedService<IdentityCenter.API.Services.PostProcessHostedService>();

// Audit logging for admin actions (api-key mint/revoke). SystemAuditService
// depends on IHttpContextAccessor so callers' identity claims can flow through.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogService, SystemAuditService>();

// ── Admin UI (Blazor Server + Razor Pages) ──────────────────────────────────
// Browser-facing admin surface at /admin: cookie login (same ASP.NET Identity binaries and
// database as the IdentityCenter WebPortal, so portal credentials work unchanged), live log
// viewer, live per-host traffic graph. The REST surface is untouched: X-API-Key remains the
// DEFAULT scheme and cookies are only honored on /admin//_blazor paths (middleware below).
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMemoryCache(); // BrandingService cache

// EF Identity store — SAME ApplicationDbContext/ApplicationUser as the WebPortal, pointed at
// DefaultConnection (this box's IC database). Used ONLY by the admin login; all API data access
// remains Dapper through the tenant-aware repositories.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.")));

// Identity options mirror WebPortal/Program.cs exactly (password policy + 5-attempt/30-minute
// lockout) so credential behavior — including lockout state, which lives in the shared
// AspNetUsers table — is identical across both apps.
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/admin/login";
    options.LogoutPath = "/admin/logout";
    options.AccessDeniedPath = "/admin/login";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.Cookie.Name = "IdentityCenter.Api.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    // DELIBERATE divergence from the WebPortal (which uses Always): this service is deployed on
    // plain HTTP :8080 inside the lab LAN; Secure-only cookies would silently never be stored
    // and login would loop. SameAsRequest upgrades to Secure automatically once the service is
    // fronted by HTTPS. Flagged in the deployment notes.
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Admin UI plumbing: live telemetry store + middleware source, log ring buffer + file reader,
// dedicated login throttle (the global rate limiter exempts the admin surface).
builder.Services.AddSingleton<RequestMetricsStore>();
builder.Services.AddSingleton(InMemoryLogSink.Instance);
builder.Services.AddSingleton<LogFileService>();
builder.Services.AddSingleton<LoginAttemptThrottle>();
builder.Services.AddSingleton<IBrandingService, BrandingService>();

// Add authentication.
// ORDER MATTERS: AddIdentity (above) sets the Identity cookie as the default authenticate/
// challenge scheme. This call runs AFTER it and re-asserts X-API-Key as the default for
// EVERYTHING — API endpoints keep exactly their pre-admin-UI behavior (401 JSON challenges,
// key-based identity). The admin UI opts INTO the cookie scheme explicitly: the AdminUi policy
// names IdentityConstants.ApplicationScheme, and the scheme-selection middleware below applies
// the cookie principal on /admin//_blazor paths only.
// NOTE: AddIdentity (above) already set DefaultSignInScheme = Identity.External and this delegate
// deliberately does NOT override it — the dynamically registered external IDP schemes (below)
// sign their callback principal into that external cookie, exactly like the WebPortal.
var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "ApiKey";
        options.DefaultAuthenticateScheme = "ApiKey";
        options.DefaultChallengeScheme = "ApiKey";
    })
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKey", options => { });

// ── External IDP sign-in for the admin login (ported from the WebPortal) ────
// Reads the SHARED IdentityProviders table (written by the portal's configuration page) and
// registers one OIDC/OAuth scheme per enabled provider. STARTUP-TIME, same as the portal: a
// provider configured in IC after this service starts needs a service RESTART to appear on
// /admin/login. Failures here must never stop the API — local password login (and the entire
// X-API-Key surface) work regardless.
using (var tempServiceProvider = builder.Services.BuildServiceProvider(
           new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = false }))
{
    using var startupLoggerFactory = LoggerFactory.Create(lb => lb.AddSerilog(Log.Logger));
    var startupLogger = startupLoggerFactory.CreateLogger("DynamicAuthentication");
    try
    {
        startupLogger.LogInformation("Attempting to register dynamic authentication providers");
        await authBuilder.AddDynamicProvidersAsync(tempServiceProvider, startupLogger);
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(ex,
            "Could not register dynamic authentication providers during startup. " +
            "This is expected if the database is not yet configured.");
    }
}

// TenantData authorization handler (admin/tenant separation for tenant-data endpoints).
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    IdentityCenter.API.Authentication.TenantDataAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AgentPolicy", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("scope", "agent"));

    // AgentChannel*Policy = the V140 per-agent command channel. Requires a per-agent
    // key: the agent_id claim is the ONLY source of agent identity (no endpoint accepts
    // an agentId from the caller), plus the MATCHING agent:* scope minted with the key.
    // The scopes are deliberately NOT interchangeable: command endpoints (claim) need
    // agent:commands and heartbeat needs agent:heartbeat, so a heartbeat-only key can
    // never claim or complete work. Named "Channel" because "AgentPolicy" already
    // belongs to the execution-server RemoteAgents surface above.
    options.AddPolicy("AgentChannelCommandsPolicy", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("agent_id")
              .RequireClaim("scope", "agent:commands"));

    options.AddPolicy("AgentChannelHeartbeatPolicy", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("agent_id")
              .RequireClaim("scope", "agent:heartbeat"));

    // AgentCommandsCompletePolicy = POST /api/agent/commands/{id}/complete, which BOTH
    // key shapes must reach: per-agent keys (KeyType=Agent + agent_id — denied by
    // TenantDataPolicy's Agent-key rule) and legacy shared keys (allowed by the same
    // TenantData rules as before). A key carrying agent_id must ALSO hold agent:commands
    // (a heartbeat-only key cannot complete work). Control-plane admin keys stay denied;
    // Agent-typed keys WITHOUT agent_id stay denied (the 2026-06-09 TenantData decision).
    options.AddPolicy("AgentCommandsCompletePolicy", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
              {
                  var keyType = ctx.User.FindFirst("key_type")?.Value;
                  if (string.Equals(keyType, "ControlPlaneAdmin", StringComparison.Ordinal))
                      return false;
                  if (ctx.User.HasClaim(c => c.Type == "agent_id"))
                      return ctx.User.HasClaim("scope", "agent:commands");
                  if (string.Equals(keyType, "Agent", StringComparison.OrdinalIgnoreCase))
                      return false;
                  return true;
              }));

    // AdminPolicy = control-plane admin scope ONLY (scope=admin). A control-plane TENANT key carries
    // scope=tenant and therefore gets 403 on AdminPolicy endpoints (/provision, /api/admin). This is the
    // first half of the cross-tenant guard. Legacy single-tenant admin keys (scope=admin in the IC
    // ApiKeys store) continue to satisfy it.
    options.AddPolicy("AdminPolicy", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("scope", "admin"));

    // TenantDataPolicy = tenant-data endpoints (Objects, Identities, Compliance, Reports, Sync).
    // Allows control-plane TENANT keys and legacy single-tenant keys; DENIES control-plane ADMIN keys
    // (no ambient admin access to tenant data). The requirement handler encodes that decision.
    options.AddPolicy("TenantDataPolicy", policy =>
        policy.RequireAuthenticatedUser()
              .AddRequirements(new IdentityCenter.API.Authentication.TenantDataRequirement()));

    // AdminUi = the browser-facing admin surface (/admin host page + Blazor pages). COOKIE
    // scheme only — an API key can never open the admin UI, and because no other policy names
    // the cookie scheme, a browser cookie can never satisfy an API policy. Admin role required.
    options.AddPolicy("AdminUi", policy =>
        policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
              .RequireAuthenticatedUser()
              .RequireRole("Admin"));

    // Deny-by-default: any endpoint without explicit authorization metadata requires an
    // authenticated caller. Endpoints intentionally public (health, agent install-script)
    // carry [AllowAnonymous], which overrides the fallback. Swagger is environment-gated
    // middleware, not an MVC endpoint, so the fallback does not apply to it.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// CORS — configurable from appsettings.json
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://localhost:7048", "https://localhost:7001", "http://localhost:5000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebPortal", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ── Control-plane bootstrap (idempotent) ────────────────────────────────────
// If a ControlPlane connection string is configured, ensure the control-plane DB +
// Tenants table exist. Gated on configuration so a dev box that hasn't set the secret
// yet still boots cleanly (Day 3/4 features will require it). This NEVER touches the
// tenant V001..V135 schema.
if (!string.IsNullOrWhiteSpace(
        app.Configuration.GetConnectionString(ControlPlaneMigrationService.ConnectionStringName)))
{
    using var scope = app.Services.CreateScope();
    var controlPlane = scope.ServiceProvider.GetRequiredService<ControlPlaneMigrationService>();
    await controlPlane.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline
// Swagger UI is gated to non-Production environments to avoid disclosing the full API surface
// to unauthenticated clients. Override with EnableSwaggerInProduction=true if a customer needs it
// (and pair that with auth on the swagger route).
if (app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Swagger:EnableInProduction"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "IdentityCenter API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Security response headers. The JSON/API surface keeps a locked-down default-src 'none' CSP:
// nothing may be loaded or framed. Three HTML surfaces carry scoped exemptions:
//   (1) the Swagger UI (dev/opt-in only), which needs its own assets to work;
//   (2) the anonymous branded landing page at "/", which renders inline CSS + an inline SVG;
//   (3) the admin UI (/admin + the Blazor circuit + the copied IC design-system CSS), which
//       needs self-hosted script (blazor.server.js), inline styles, and websockets.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Frame-Options"] = "DENY";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

    var path = context.Request.Path;
    var isSwagger = path.StartsWithSegments("/swagger");
    var isLanding = path == "/" || path.StartsWithSegments("/index.html");
    var isAdminUi = path.StartsWithSegments("/admin")
        || path.StartsWithSegments("/_blazor")
        || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/css")
        || path.StartsWithSegments("/favicon.ico");

    if (isLanding)
    {
        // Page-scoped CSP for the branded landing surface only. 'unsafe-inline' for styles is
        // required because the page ships its CSS inline (no static .css file, and the strict
        // policy would otherwise block it). It does NOT relax script/connect/object.
        headers["Content-Security-Policy"] =
            "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
            "script-src 'none'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    }
    else if (isAdminUi)
    {
        // Admin UI CSP: scripts from self ONLY (blazor.server.js — there is deliberately no
        // inline script anywhere on the admin surface); inline styles allowed (the IC design
        // system and Blazor components use style attributes); websockets for the Blazor
        // circuit. Log content is rendered exclusively through Blazor's auto-encoding, never
        // as MarkupString — synced directory data that lands in logs stays inert text.
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; font-src 'self'; connect-src 'self' ws: wss:; " +
            "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    }
    else if (!isSwagger)
    {
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }
    await next();
});

app.UseHttpsRedirection();

// Static assets for the admin UI (the copied IC design-system CSS + admin.css). Placed after
// the security-header middleware so even static responses carry nosniff/CSP.
app.UseStaticFiles();

app.UseCors("AllowWebPortal");

app.UseAuthentication();

// ── Cookie principal for the admin UI surface ONLY ──────────────────────────
// UseAuthentication above ran the DEFAULT scheme (ApiKey). For the browser-facing admin
// surface we additionally evaluate the Identity application cookie and, when valid, make it
// the request principal. Scoped strictly to /admin + the Blazor hub: /api/* never sees a
// cookie identity (so a lured browser can't ride its admin cookie into API endpoints), and
// API keys never authenticate the admin UI (the AdminUi policy names the cookie scheme only).
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    // /_framework is included because MapBlazorHub serves blazor.server.js as an ENDPOINT with
    // no auth metadata — the deny-by-default FallbackPolicy would 401 it unless the admin
    // cookie principal is honored there too (the unauthenticated login page never loads it).
    if (path.StartsWithSegments("/admin")
        || path.StartsWithSegments("/_blazor")
        || path.StartsWithSegments("/_framework"))
    {
        var cookieAuth = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (cookieAuth.Succeeded && cookieAuth.Principal is not null)
        {
            context.User = cookieAuth.Principal;
        }
    }
    await next();
});

// Request telemetry for the /admin dashboard. After authentication, BEFORE the rate limiter,
// so throttled (429) and unauthenticated (401) responses are graphed along with the rest.
app.UseMiddleware<RequestMetricsMiddleware>();

// Rate limiting. MOVED after UseAuthentication (2026-06-09 review HIGH): the limiter keys off
// the authenticated principal's claims; when it ran before authentication every caller —
// Conduit and agents included — was treated as anonymous (30/min by IP).
app.UseMiddleware<RateLimitingMiddleware>();

// Install the per-request tenant connection routing AFTER authentication (so context.User is populated)
// and BEFORE authorization + the controller (so the ambient resolver is live for the action). This MUST
// sit in a middleware frame that is an ancestor of the controller: an AsyncLocal set inside the awaited
// auth handler does NOT flow forward to the controller (the Day-6 isolation fix). The middleware reads the
// server-set key_type/tenant_id claims (the validated-key trust anchor), installs the resolver, and clears
// it in a finally so nothing bleeds across pooled requests.
app.UseMiddleware<IdentityCenter.API.Middleware.TenantConnectionScopeMiddleware>();

app.UseAuthorization();

// Anonymous branded landing / status page at the root. Explicitly [AllowAnonymous] so it is
// reachable WITHOUT an API key — the deny-by-default FallbackPolicy would otherwise 401 it.
// Returns self-contained HTML (inline CSS + inline SVG) with the live version + environment
// injected; rendered under the page-scoped CSP carved out above. Real API/JSON endpoints are
// untouched and still require a valid key.
app.MapGet("/", (IWebHostEnvironment env) =>
        Results.Content(IdentityCenter.API.LandingPage.Render(env.EnvironmentName), "text/html; charset=utf-8"))
   .AllowAnonymous()
   .ExcludeFromDescription();

app.MapControllers();

// Admin UI endpoints: the login/logout Razor Pages + the Blazor host page (_AdminHost,
// catch-all under /admin, gated by the AdminUi cookie policy) + the Blazor SignalR hub.
app.MapRazorPages();
app.MapBlazorHub();

app.Run();
