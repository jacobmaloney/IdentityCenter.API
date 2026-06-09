using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Common.Encryption;
using DataAccessLibrary.ControlPlane;
using DataAccessLibrary.Repositories;
using DataAccessLibrary.Services;
using IdentityCenter.API.Authentication;
using IdentityCenter.API.Middleware;
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

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
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
builder.Services.AddScoped<ISqlLicenseRepository, SqlLicenseRepository>();
builder.Services.AddScoped<ISqlLicenseComplianceEngine, SqlLicenseComplianceEngine>();

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

// Add authentication
builder.Services.AddAuthentication("ApiKey")
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKey", options => { });

// TenantData authorization handler (admin/tenant separation for tenant-data endpoints).
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    IdentityCenter.API.Authentication.TenantDataAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AgentPolicy", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("scope", "agent"));

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

// Security response headers. This is a REST/JSON API with no HTML UI, so the CSP is strict:
// nothing may be loaded or framed. Two HTML surfaces are exempted from the strict CSP:
//   (1) the Swagger UI (dev/opt-in only), which needs its own assets to work; and
//   (2) the anonymous branded landing page at "/", which renders inline CSS + an inline SVG.
// The landing page gets a deliberately scoped page-CSP (self + inline styles + data: images);
// the JSON/API surface keeps the locked-down default-src 'none'.
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

    if (isLanding)
    {
        // Page-scoped CSP for the branded landing surface only. 'unsafe-inline' for styles is
        // required because the page ships its CSS inline (no static .css file, and the strict
        // policy would otherwise block it). It does NOT relax script/connect/object.
        headers["Content-Security-Policy"] =
            "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
            "script-src 'none'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    }
    else if (!isSwagger)
    {
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }
    await next();
});

app.UseHttpsRedirection();
app.UseCors("AllowWebPortal");

// Add rate limiting middleware
app.UseMiddleware<RateLimitingMiddleware>();

app.UseAuthentication();

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

app.Run();
