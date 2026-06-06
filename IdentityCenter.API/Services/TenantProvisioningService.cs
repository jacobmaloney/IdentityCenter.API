using Common;
using DataAccessLibrary.ControlPlane;
using DataAccessLibrary.Data;
using DataAccessLibrary.Models;
using DataAccessLibrary.Services;
using IdentityCenter.API.Models;
using Logging;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdentityCenter.API.Services;

/// <summary>
/// Executes the DB-per-tenant provisioning flow for a single tenant. Invoked by the
/// <see cref="ProvisioningHostedService"/> drainer on a background thread (NOT on the HTTP request),
/// because creating a catalog and running V001..V135 takes far longer than an HTTP request should hold.
///
/// SECURITY (this is the most sensitive operation in the product — it creates databases):
///   - The ONLY caller-supplied value that reaches a database NAME is the slug, which is normalized +
///     whitelist-validated by <see cref="TenantSlug"/> before this method runs and re-validated in
///     <see cref="TenantSlug.ToDatabaseName"/>. Everything else is parameterized.
///   - The tenant connection string is DERIVED from the control-plane connection (server + creds from
///     config), swapping ONLY Initial Catalog. No caller input contributes server/host/credentials.
///   - The generated admin password is surfaced exactly once via <see cref="OneTimeCredentialVault"/>
///     and is never persisted or logged.
///   - On any failure the tenant is marked <see cref="TenantStatus.Failed"/> with a sanitized error;
///     the partial DB is left in place for diagnosis (NOT silently dropped, NOT reported as success).
/// </summary>
public sealed class TenantProvisioningService
{
    private readonly IConfiguration _configuration;
    private readonly ITenantRegistryRepository _registry;
    private readonly ITenantApiKeyRepository _apiKeys;
    private readonly OneTimeCredentialVault _vault;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IGlobalLogger _logger;

    public TenantProvisioningService(
        IConfiguration configuration,
        ITenantRegistryRepository registry,
        ITenantApiKeyRepository apiKeys,
        OneTimeCredentialVault vault,
        ILoggerFactory loggerFactory,
        IGlobalLogger logger)
    {
        _configuration = configuration;
        _registry = registry;
        _apiKeys = apiKeys;
        _vault = vault;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Derives the per-tenant connection string from the control-plane connection: same server, same
    /// credentials, same options — only Initial Catalog changes to the tenant DB name. The DB name is
    /// composed via <see cref="TenantSlug.ToDatabaseName"/> from an already-validated slug.
    /// </summary>
    public string BuildTenantConnectionString(string slug)
    {
        var controlPlane = _configuration.GetConnectionString(ControlPlaneMigrationService.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Control-plane connection string '{ControlPlaneMigrationService.ConnectionStringName}' not configured.");

        // ToDatabaseName re-validates the slug (defense in depth) and returns "IdentityCenter_{slug}".
        var dbName = TenantSlug.ToDatabaseName(slug);

        var builder = new SqlConnectionStringBuilder(controlPlane)
        {
            InitialCatalog = dbName
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Runs the full staged flow for a tenant that already exists in the registry (Status=Provisioning).
    /// Idempotent enough to be safe on retry of a Failed row. Marks Active on success, Failed on error.
    /// </summary>
    public async Task ProvisionAsync(Guid tenantId, string? adminEmail, CancellationToken cancellationToken)
    {
        var tenant = await _registry.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            _logger.LogWarning("Provisioning: tenant {TenantId} not found in registry — skipping", tenantId);
            return;
        }

        var slug = tenant.Slug; // already normalized at CreateAsync
        try
        {
            _logger.LogInformation("Provisioning tenant {Slug} ({TenantId}) — starting", slug, tenantId);

            // (c) Build + encrypt + store the tenant connection string.
            var tenantConn = BuildTenantConnectionString(slug);
            await _registry.SetConnectionStringAsync(tenantId, tenantConn, cancellationToken).ConfigureAwait(false);

            // (d) Create the catalog + run V001..V135 (seeds via V005). Reuse the proven migrator
            //     primitive, repointed at the tenant DB.
            var migrator = new DatabaseMigrationService(
                _configuration, _loggerFactory.CreateLogger<DatabaseMigrationService>());
            migrator.SetConnectionString(tenantConn);
            await migrator.EnsureDatabaseExistsAsync().ConfigureAwait(false);
            await migrator.EnsureUpToDateAsync(cancellationToken).ConfigureAwait(false);

            var schemaVersion = await migrator.GetCurrentVersionAsync().ConfigureAwait(false);
            _logger.LogInformation("Provisioning tenant {Slug}: schema applied to V{Version}", slug, schemaVersion);

            // (e) Seed the tenant admin via the SAME EF Identity path as CreateAdminUser, in an
            //     isolated service graph pointed at the tenant DB. Capture the generated password ONCE.
            var resolvedEmail = ResolveAdminEmail(adminEmail, slug);
            var password = BootstrapPasswordGenerator.Generate();
            await SeedTenantAdminAsync(tenantConn, resolvedEmail, password, cancellationToken).ConfigureAwait(false);

            // (f) Mint this tenant's control-plane API key (scope=tenant). The HASH is stored in the
            //     control-plane TenantApiKeys table; the RAW key is captured ONCE here and never persisted.
            //     This is what the tenant's integrations present to call the tenant-data API — every such
            //     request is bound to THIS tenant's DB by the validated key (no client-supplied tenant id).
            //     If the mint fails, the whole provision fails (we do NOT report Active without a usable key).
            var (_, rawTenantKey) = await _apiKeys.CreateAsync(
                TenantApiKeyScope.Tenant, tenantId, $"{slug}-tenant-key", cancellationToken).ConfigureAwait(false);

            // Stash the one-time bundle (memory only; never persisted, never logged).
            _vault.Stash(tenantId, new TenantAccessBundle
            {
                AdminEmail = resolvedEmail,
                AdminPassword = password,
                TenantApiKey = rawTenantKey
            });

            // (g) Mark Active.
            await _registry.SetStatusAsync(tenantId, TenantStatus.Active, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Provisioning tenant {Slug} ({TenantId}) — COMPLETE (Active, V{Version})",
                slug, tenantId, schemaVersion);
        }
        catch (Exception ex)
        {
            // Mark Failed with a sanitized reason. Leave the partial DB for diagnosis. NEVER log secrets:
            // we log only the exception TYPE and a credential-redacted message, not the raw exception
            // object — a connection-layer exception's message can echo the connection string, which
            // carries the master credential. Redaction is defense in depth on top of that.
            _logger.LogError("Provisioning tenant {Slug} ({TenantId}) FAILED: {ExType}: {Reason}",
                slug, tenantId, ex.GetType().Name, Redact(ex.Message));
            _vault.Discard(tenantId);
            try
            {
                await _registry.SetStatusAsync(tenantId, TenantStatus.Failed, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception markEx)
            {
                _logger.LogError("Provisioning tenant {Slug}: could not even mark Failed: {ExType}: {Reason}",
                    slug, markEx.GetType().Name, Redact(markEx.Message));
            }
        }
    }

    /// <summary>
    /// Strips connection-string credential tokens (Password=…;, User ID=…;, Pwd=…;) from a string before
    /// it is logged. A SQL/connection exception message can occasionally echo the connection string,
    /// which carries the master credential — this guarantees that credential never reaches a log sink.
    /// </summary>
    private static string Redact(string? message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(
            message,
            @"(?i)\b(password|pwd|user id|uid)\s*=\s*[^;]*",
            "$1=***",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string ResolveAdminEmail(string? requested, string slug)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested.Trim();
        return $"admin@{slug}.identitycenter.local";
    }

    /// <summary>
    /// Seeds (or resets) the sole tenant admin against the tenant DB using a self-contained EF Identity
    /// service graph — the same UserManager/RoleManager path used by the CreateAdminUser CLI. This keeps
    /// the seed OFF the API's own request pipeline (which uses ApiKey auth, not ASP.NET Identity) and
    /// pointed strictly at the freshly-migrated tenant catalog.
    /// </summary>
    private async Task SeedTenantAdminAsync(
        string tenantConnectionString, string adminEmail, string password, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(tenantConnectionString, sql =>
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: new[] { -1, -2, 19, 64, 233, 10053, 10054, 10060, 40197, 40501, 40613 })));

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 1;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        if (!await roleManager.RoleExistsAsync("Admin").ConfigureAwait(false))
        {
            var roleResult = await roleManager.CreateAsync(new ApplicationRole
            {
                Name = "Admin",
                Description = "System Administrator",
                CreatedAt = DateTime.UtcNow
            }).ConfigureAwait(false);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    "Failed to create Admin role: " + DescribeIdentityErrors(roleResult));
        }

        var existing = await userManager.FindByEmailAsync(adminEmail).ConfigureAwait(false);
        if (existing is not null)
        {
            // Idempotent retry path: reset the password to the freshly-generated one.
            var token = await userManager.GeneratePasswordResetTokenAsync(existing).ConfigureAwait(false);
            var reset = await userManager.ResetPasswordAsync(existing, token, password).ConfigureAwait(false);
            if (!reset.Succeeded)
                throw new InvalidOperationException("Failed to reset admin password: " + DescribeIdentityErrors(reset));
            if (!await userManager.IsInRoleAsync(existing, "Admin").ConfigureAwait(false))
                await userManager.AddToRoleAsync(existing, "Admin").ConfigureAwait(false);
            return;
        }

        var adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            DisplayName = "System Administrator",
            FirstName = "Admin",
            LastName = "User",
            Department = "IT",
            Title = "System Administrator",
            IsActive = true,
            IsSystem = true,
            CreatedAt = DateTime.UtcNow
        };

        var create = await userManager.CreateAsync(adminUser, password).ConfigureAwait(false);
        if (!create.Succeeded)
            throw new InvalidOperationException("Failed to create admin user: " + DescribeIdentityErrors(create));

        var addToRole = await userManager.AddToRoleAsync(adminUser, "Admin").ConfigureAwait(false);
        if (!addToRole.Succeeded)
            throw new InvalidOperationException("Failed to add admin to role: " + DescribeIdentityErrors(addToRole));
    }

    private static string DescribeIdentityErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
