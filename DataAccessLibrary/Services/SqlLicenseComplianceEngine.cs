using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Services;

public class SqlLicenseComplianceEngine : ISqlLicenseComplianceEngine
{
    private readonly ISqlLicenseRepository _repo;
    private readonly IGlobalLogger _logger;
    private readonly string _connectionString;

    public SqlLicenseComplianceEngine(ISqlLicenseRepository repo, IGlobalLogger logger, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _repo = repo;
        _logger = logger;
        _connectionString = config.GetConnectionString("DefaultConnection") ?? "";
    }

    private static readonly HashSet<string> DemoConnectionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "D0000000-0000-0000-0000-000000000001",
        "D0000000-0000-0000-0000-000000000002",
        "D0000000-0000-0000-0000-000000000003",
        "D0000000-0000-0000-0000-000000000004"
    };

    public async Task EvaluateAllServersAsync(CancellationToken ct = default, bool excludeDemo = false)
    {
        _logger.LogInformation("SqlLicenseComplianceEngine.EvaluateAllServersAsync: Starting compliance evaluation (excludeDemo={ExcludeDemo})", excludeDemo);

        var allServers = await _repo.GetInventoryAsync();

        List<SqlServerInventory> servers;
        if (excludeDemo)
        {
            // Get demo Object IDs to exclude
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            var demoObjectIds = (await conn.QueryAsync<string>(
                @"SELECT CAST(Id AS NVARCHAR(100)) FROM Objects
                  WHERE SourceConnectionId IN ('D0000000-0000-0000-0000-000000000001','D0000000-0000-0000-0000-000000000002','D0000000-0000-0000-0000-000000000003','D0000000-0000-0000-0000-000000000004')
                    AND DeletedAt IS NULL")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            servers = allServers.Where(s => string.IsNullOrEmpty(s.ObjectId) || !demoObjectIds.Contains(s.ObjectId)).ToList();
            _logger.LogInformation("SqlLicenseComplianceEngine: filtered {Excluded} demo servers, evaluating {Count}",
                allServers.Count - servers.Count, servers.Count);
        }
        else
        {
            servers = allServers;
        }
        var entitlements = await _repo.GetEntitlementsAsync();

        // Calculate total owned cores by edition (use Contains for full edition strings like "Enterprise Edition (64-bit)")
        int ownedEnterpriseCores = entitlements
            .Where(e => e.IsActive && e.Edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) && e.QuantityUnit == "Cores")
            .Sum(e => e.Quantity);
        int ownedStandardCores = entitlements
            .Where(e => e.IsActive && e.Edition.Contains("Standard", StringComparison.OrdinalIgnoreCase) && e.QuantityUnit == "Cores")
            .Sum(e => e.Quantity);

        // Cores assigned to servers
        var assignments = await _repo.GetAssignmentsAsync();
        int assignedEnterpriseCores = assignments
            .Where(a => a.IsActive && (a.Edition ?? "").Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
            .Sum(a => a.AssignedCores ?? 0);
        int assignedStandardCores = assignments
            .Where(a => a.IsActive && (a.Edition ?? "").Contains("Standard", StringComparison.OrdinalIgnoreCase))
            .Sum(a => a.AssignedCores ?? 0);

        foreach (var server in servers)
        {
            if (ct.IsCancellationRequested) break;
            await EvaluateServerInternalAsync(server, ownedEnterpriseCores, ownedStandardCores,
                assignedEnterpriseCores, assignedStandardCores);
        }

        // Check entitlement expirations
        var existing = await _repo.GetViolationsAsync(unresolvedOnly: true, sourceType: "SQL");
        foreach (var ent in entitlements.Where(e => e.IsActive))
        {
            if (ent.ExpiresWithin90Days)
            {
                var daysLeft = ent.ExpiryDate.HasValue ? (int)(ent.ExpiryDate.Value - DateTime.Today).TotalDays : 0;
                var severity = daysLeft <= 30 ? "Critical" : "Warning";
                var alreadyExists = existing.Any(e =>
                    e.ViolationType == "ExpiringEntitlement" && e.Detail != null && e.Detail.Contains(ent.Id.ToString()));
                if (!alreadyExists)
                {
                    await _repo.CreateViolationAsync(new LicenseComplianceViolation
                    {
                        SourceType = "SQL",
                        ViolationType = "ExpiringEntitlement",
                        Severity = severity,
                        Title = string.Concat(ent.Edition, " license expires in ", daysLeft, " days"),
                        Detail = string.Concat("Entitlement ", ent.Id, ": ", ent.Edition, " ", ent.LicenseType,
                            " (", ent.Quantity, " ", ent.QuantityUnit, ") expires ",
                            ent.ExpiryDate?.ToString("MMM dd, yyyy"), ". Renew or replace before expiration."),
                        DetectedAt = DateTime.UtcNow
                    });
                }
            }

            if (ent.IsExpired)
            {
                var alreadyExists = existing.Any(e =>
                    e.ViolationType == "ExpiredEntitlement" && e.Detail != null && e.Detail.Contains(ent.Id.ToString()));
                if (!alreadyExists)
                {
                    await _repo.CreateViolationAsync(new LicenseComplianceViolation
                    {
                        SourceType = "SQL",
                        ViolationType = "ExpiredEntitlement",
                        Severity = "Critical",
                        Title = string.Concat(ent.Edition, " license has EXPIRED"),
                        Detail = string.Concat("Entitlement ", ent.Id, ": ", ent.Edition, " ", ent.LicenseType,
                            " expired on ", ent.ExpiryDate?.ToString("MMM dd, yyyy"),
                            ". This license is no longer valid. Renew immediately."),
                        DetectedAt = DateTime.UtcNow
                    });
                }
            }
        }

        _logger.LogInformation("SqlLicenseComplianceEngine.EvaluateAllServersAsync: Evaluated {Count} servers", servers.Count);
    }

    private async Task EvaluateServerInternalAsync(
        SqlServerInventory server,
        int ownedEnterpriseCores, int ownedStandardCores,
        int assignedEnterpriseCores, int assignedStandardCores)
    {
        var violations = new List<LicenseComplianceViolation>();

        // Rule 1: Developer Edition in production (regardless of online status — the license issue exists whether the server is reachable or not)
        if (server.IsDeveloperEdition)
        {
            violations.Add(new LicenseComplianceViolation
            {
                SqlServerInventoryId = server.Id,
                ObjectId = server.ObjectId,
                ViolationType = "DeveloperInProd",
                Severity = "Critical",
                Title = server.ServerName + ": Developer Edition is not licensed for production use",
                Detail = "SQL Server Developer Edition is licensed only for development and testing. " +
                         "This server is active and appears to be in production use. " +
                         "Obtain an Enterprise or Standard license immediately."
            });
        }

        // Rule 2: End of Life version
        if (server.IsEndOfLife && server.IsOnline)
        {
            violations.Add(new LicenseComplianceViolation
            {
                SqlServerInventoryId = server.Id,
                ObjectId = server.ObjectId,
                ViolationType = "EndOfLife",
                Severity = "Warning",
                Title = server.ServerName + ": SQL Server " + server.SqlVersion + " is End of Life",
                Detail = "SQL Server " + server.SqlVersion + " has reached end of extended support. " +
                         "No security patches are available. Plan an upgrade or purchase Extended Security Updates (ESU)."
            });
        }

        // Rule 3: No owner assigned
        if (server.OwnerId == null)
        {
            violations.Add(new LicenseComplianceViolation
            {
                SqlServerInventoryId = server.Id,
                ObjectId = server.ObjectId,
                ViolationType = "NoOwner",
                Severity = "Warning",
                Title = server.ServerName + ": No owner assigned",
                Detail = "All SQL Servers must have an assigned owner responsible for license compliance and security."
            });
        }

        // Rule 4: No license assignment for paid editions
        var isPaidEdition = server.SqlEdition != null &&
            (server.SqlEdition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) ||
             server.SqlEdition.Contains("Standard", StringComparison.OrdinalIgnoreCase));
        var hasAssignment = server.LicenseAssignment != null && server.LicenseAssignment.IsActive;

        if (isPaidEdition && !hasAssignment)
        {
            violations.Add(new LicenseComplianceViolation
            {
                SqlServerInventoryId = server.Id,
                ObjectId = server.ObjectId,
                ViolationType = "Unlicensed",
                Severity = "Critical",
                Title = server.ServerName + ": " + server.SqlEdition + " Edition with no license assigned",
                Detail = "This server is running a paid SQL Server edition but has no license entitlement assigned. " +
                         "Assign a license from the entitlements pool or add a new entitlement."
            });
        }

        // Determine overall status
        string newStatus = "Unknown";
        if (violations.Any(v => v.ViolationType is "Unlicensed" or "DeveloperInProd"))
            newStatus = "Violation";
        else if (violations.Any(v => v.Severity == "Critical"))
            newStatus = "Violation";
        else if (violations.Any())
            newStatus = "Licensed"; // has some warnings but is covered
        else if (hasAssignment || (server.SqlEdition != null && server.SqlEdition.Contains("Express", StringComparison.OrdinalIgnoreCase)))
            newStatus = "Licensed"; // Express is free; Developer is handled by DeveloperInProd violation above
        else
            newStatus = "Unknown";

        await _repo.UpdateServerComplianceStatusAsync(server.Id, newStatus);

        // Persist any new violations (avoid duplicates)
        var existing = await _repo.GetViolationsAsync(unresolvedOnly: true, sourceType: "SQL");
        foreach (var v in violations)
        {
            bool alreadyExists = existing.Any(e =>
                e.SqlServerInventoryId == v.SqlServerInventoryId &&
                e.ViolationType == v.ViolationType &&
                !e.IsResolved);
            if (!alreadyExists)
            {
                v.DetectedAt = DateTime.UtcNow;
                v.SourceType = "SQL";
                await _repo.CreateViolationAsync(v);
            }
        }
    }

    public async Task EvaluateServerAsync(Guid serverId, CancellationToken ct = default)
    {
        var server = await _repo.GetServerAsync(serverId);
        if (server == null) return;
        await EvaluateServerInternalAsync(server, 0, 0, 0, 0);
    }

    public async Task<List<LicenseComplianceViolation>> GetPendingViolationsAsync()
        => await _repo.GetViolationsAsync(unresolvedOnly: true, sourceType: "SQL");
}
