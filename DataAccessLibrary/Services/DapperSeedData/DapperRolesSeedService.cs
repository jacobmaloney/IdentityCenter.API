using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based role seeding for ASP.NET Identity roles.
/// Seeds default application roles with AD/EntraID group mappings.
/// </summary>
public class DapperRolesSeedService : DapperSeedServiceBase
{
    public DapperRolesSeedService(
        IConfiguration configuration,
        ILogger<DapperRolesSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if roles already exist
        var existingCount = await GetCountAsync(connection, transaction, "AspNetRoles");
        if (existingCount >= 8)
        {
            _logger.LogDebug("Roles already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var roles = GetDefaultRoles();
        int created = 0;
        int skipped = 0;

        const string insertSql = @"
            INSERT INTO AspNetRoles (Id, Name, NormalizedName, Description, Permissions, AdGroupMappings, EntraIdGroupMappings, ConcurrencyStamp)
            SELECT @Id, @Name, @NormalizedName, @Description, @Permissions, @AdGroupMappings, @EntraIdGroupMappings, @ConcurrencyStamp
            WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = @NormalizedName)";

        foreach (var role in roles)
        {
            var rowsAffected = await InsertAsync(connection, transaction, insertSql, role);
            if (rowsAffected > 0)
                created++;
            else
                skipped++;
        }

        sw.Stop();
        LogSeedComplete("Roles", created, skipped, sw.Elapsed);
    }

    private static List<object> GetDefaultRoles()
    {
        return new List<object>
        {
            new
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Admin",
                NormalizedName = "ADMIN",
                Description = "System administrators with full control over Certification Center",
                Permissions = "Full system access, user management, configuration, audit logs",
                AdGroupMappings = "Domain Admins;Certification Center Admins;IT Administrators;Enterprise Admins",
                EntraIdGroupMappings = "Certification Center Administrators;Global Administrators;Privileged Role Administrators",
                ConcurrencyStamp = Guid.NewGuid().ToString()
            },
            new
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Manager",
                NormalizedName = "MANAGER",
                Description = "Department managers who approve access reviews and user requests",
                Permissions = "Approve access reviews, view team members, manage direct reports",
                AdGroupMappings = "Managers;Department Heads;Team Leads",
                EntraIdGroupMappings = "Managers;Department Managers;People Managers",
                ConcurrencyStamp = Guid.NewGuid().ToString()
            },
            new
            {
                Id = Guid.NewGuid().ToString(),
                Name = "User",
                NormalizedName = "USER",
                Description = "Standard users who can view their own information and submit requests",
                Permissions = "View own profile, submit access requests, view personal access history",
                AdGroupMappings = "Domain Users;Employees;All Users",
                EntraIdGroupMappings = "All Users;Employees;Company",
                ConcurrencyStamp = Guid.NewGuid().ToString()
            },
            new
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Auditor",
                NormalizedName = "AUDITOR",
                Description = "Compliance and security auditors with read-only access to everything",
                Permissions = "Read-only access to all data, export audit logs, view all reviews",
                AdGroupMappings = "Auditors;Compliance Team;Security Auditors;Internal Audit",
                EntraIdGroupMappings = "Compliance Officers;Auditors;Security Auditors",
                ConcurrencyStamp = Guid.NewGuid().ToString()
            },
            new
            {
                Id = Guid.NewGuid().ToString(),
                Name = "ComplianceOfficer",
                NormalizedName = "COMPLIANCEOFFICER",
                Description = "Compliance officers who manage frameworks, policies, and review campaigns",
                Permissions = "Manage compliance frameworks, create review campaigns, configure policies",
                AdGroupMappings = "Compliance Officers;Risk Management;GRC Team",
                EntraIdGroupMappings = "Compliance Officers;Risk Management;Governance Team",
                ConcurrencyStamp = Guid.NewGuid().ToString()
            },
            new
            {
                Id = Guid.NewGuid().ToString(),
                Name = "HelpDesk",
                NormalizedName = "HELPDESK",
                Description = "IT help desk staff who assist users with password resets and basic support",
                Permissions = "Reset passwords, unlock accounts, view user information, assist with access requests",
                AdGroupMappings = "Help Desk;IT Support;Service Desk",
                EntraIdGroupMappings = "Help Desk;IT Support Team;Service Desk",
                ConcurrencyStamp = Guid.NewGuid().ToString()
            },
            new
            {
                Id = Guid.NewGuid().ToString(),
                Name = "SecurityOfficer",
                NormalizedName = "SECURITYOFFICER",
                Description = "Security team members who monitor access, investigate anomalies, and manage security policies",
                Permissions = "Monitor access patterns, investigate security events, manage security policies, review audit logs",
                AdGroupMappings = "Security Team;InfoSec;Security Operations;SOC Team",
                EntraIdGroupMappings = "Security Team;Information Security;SOC Analysts",
                ConcurrencyStamp = Guid.NewGuid().ToString()
            },
            new
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Reviewer",
                NormalizedName = "REVIEWER",
                Description = "Users designated to review and certify access for specific groups or applications",
                Permissions = "Review assigned access certifications, approve/revoke access, provide review comments",
                AdGroupMappings = "Access Reviewers;Certification Team",
                EntraIdGroupMappings = "Access Reviewers;Certification Reviewers",
                ConcurrencyStamp = Guid.NewGuid().ToString()
            }
        };
    }
}
