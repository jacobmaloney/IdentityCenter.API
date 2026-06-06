using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default roles with AD/EntraID group mappings for automatic role assignment
/// This is the rockstar role seeding service that makes setup absolutely painless
/// </summary>
public class DefaultRolesSeedService
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DefaultRolesSeedService> _logger;

    public DefaultRolesSeedService(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ILogger<DefaultRolesSeedService> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Seeds all default roles with descriptions and AD/EntraID group mappings
    /// This makes Certification Center production-ready out of the box!
    /// </summary>
    public async Task SeedDefaultRolesAsync()
    {
        _logger.LogInformation("🎯 Starting default roles seeding with AD/EntraID group mapping support");

        var defaultRoles = new[]
        {
            new RoleDefinition
            {
                Name = "Admin",
                IsSystem = true,
                Description = "System administrators with full control over Certification Center",
                AdGroupNames = new[]
                {
                    "Domain Admins",
                    "Certification Center Admins",
                    "IT Administrators",
                    "Enterprise Admins"
                },
                EntraIdGroupNames = new[]
                {
                    "Certification Center Administrators",
                    "Global Administrators",
                    "Privileged Role Administrators"
                },
                Permissions = "Full system access, user management, configuration, audit logs"
            },
            new RoleDefinition
            {
                Name = "Manager",
                Description = "Department managers who approve access reviews and user requests",
                AdGroupNames = new[]
                {
                    "Managers",
                    "Department Heads",
                    "Team Leads"
                },
                EntraIdGroupNames = new[]
                {
                    "Managers",
                    "Department Managers",
                    "People Managers"
                },
                Permissions = "Approve access reviews, view team members, manage direct reports"
            },
            new RoleDefinition
            {
                Name = "User",
                Description = "Standard users who can view their own information and submit requests",
                AdGroupNames = new[]
                {
                    "Domain Users",
                    "Employees",
                    "All Users"
                },
                EntraIdGroupNames = new[]
                {
                    "All Users",
                    "Employees",
                    "Company"
                },
                Permissions = "View own profile, submit access requests, view personal access history"
            },
            new RoleDefinition
            {
                Name = "Auditor",
                Description = "Compliance and security auditors with read-only access to everything",
                AdGroupNames = new[]
                {
                    "Auditors",
                    "Compliance Team",
                    "Security Auditors",
                    "Internal Audit"
                },
                EntraIdGroupNames = new[]
                {
                    "Compliance Officers",
                    "Auditors",
                    "Security Auditors"
                },
                Permissions = "Read-only access to all data, export audit logs, view all reviews"
            },
            new RoleDefinition
            {
                Name = "ComplianceOfficer",
                Description = "Compliance officers who manage frameworks, policies, and review campaigns",
                AdGroupNames = new[]
                {
                    "Compliance Officers",
                    "Risk Management",
                    "GRC Team"
                },
                EntraIdGroupNames = new[]
                {
                    "Compliance Officers",
                    "Risk Management",
                    "Governance Team"
                },
                Permissions = "Manage compliance frameworks, create review campaigns, configure policies"
            },
            new RoleDefinition
            {
                Name = "HelpDesk",
                Description = "IT help desk staff who assist users with password resets and basic support",
                AdGroupNames = new[]
                {
                    "Help Desk",
                    "IT Support",
                    "Service Desk"
                },
                EntraIdGroupNames = new[]
                {
                    "Help Desk",
                    "IT Support Team",
                    "Service Desk"
                },
                Permissions = "Reset passwords, unlock accounts, view user information, assist with access requests"
            },
            new RoleDefinition
            {
                Name = "SecurityOfficer",
                Description = "Security team members who monitor access, investigate anomalies, and manage security policies",
                AdGroupNames = new[]
                {
                    "Security Team",
                    "InfoSec",
                    "Security Operations",
                    "SOC Team"
                },
                EntraIdGroupNames = new[]
                {
                    "Security Team",
                    "Information Security",
                    "SOC Analysts"
                },
                Permissions = "Monitor access patterns, investigate security events, manage security policies, review audit logs"
            },
            new RoleDefinition
            {
                Name = "Reviewer",
                Description = "Users designated to review and certify access for specific groups or applications",
                AdGroupNames = new[]
                {
                    "Access Reviewers",
                    "Certification Team"
                },
                EntraIdGroupNames = new[]
                {
                    "Access Reviewers",
                    "Certification Reviewers"
                },
                Permissions = "Review assigned access certifications, approve/revoke access, provide review comments"
            },
            new RoleDefinition
            {
                Name = "FallbackReviewer",
                Description = "Designated fallback reviewers for access reviews when primary reviewers are unavailable",
                AdGroupNames = new[]
                {
                    "Fallback Reviewers",
                    "Backup Approvers",
                    "Emergency Access Team"
                },
                EntraIdGroupNames = new[]
                {
                    "Fallback Reviewers",
                    "Backup Approvers"
                },
                Permissions = "Review access certifications as backup, approve/revoke access when primary reviewer unavailable"
            },
            new RoleDefinition
            {
                Name = "UserManager",
                Description = "Can manage users and groups",
                AdGroupNames = new[]
                {
                    "User Managers",
                    "Account Managers",
                    "Identity Admins"
                },
                EntraIdGroupNames = new[]
                {
                    "User Account Administrators",
                    "User Managers"
                },
                Permissions = "Create users, modify user attributes, manage group memberships"
            },
            new RoleDefinition
            {
                Name = "AuditViewer",
                Description = "Can view audit logs and reports",
                AdGroupNames = new[]
                {
                    "Audit Viewers",
                    "Report Viewers",
                    "Log Analysts"
                },
                EntraIdGroupNames = new[]
                {
                    "Audit Viewers",
                    "Reports Reader"
                },
                Permissions = "View audit logs, run reports, export audit data"
            }
        };

        int created = 0;
        int skipped = 0;

        foreach (var roleDefinition in defaultRoles)
        {
            if (await _roleManager.RoleExistsAsync(roleDefinition.Name))
            {
                _logger.LogDebug("⏭️  Role '{RoleName}' already exists, skipping", roleDefinition.Name);
                skipped++;
                continue;
            }

            var role = new ApplicationRole
            {
                Name = roleDefinition.Name,
                NormalizedName = roleDefinition.Name.ToUpperInvariant(),
                Description = roleDefinition.Description,
                Permissions = roleDefinition.Permissions,
                IsSystem = roleDefinition.IsSystem,
                AdGroupMappings = string.Join(";", roleDefinition.AdGroupNames),
                EntraIdGroupMappings = string.Join(";", roleDefinition.EntraIdGroupNames)
            };

            var result = await _roleManager.CreateAsync(role);

            if (result.Succeeded)
            {
                _logger.LogInformation("✅ Created role '{RoleName}' with {AdGroups} AD groups and {EntraGroups} EntraID groups mapped",
                    roleDefinition.Name,
                    roleDefinition.AdGroupNames.Length,
                    roleDefinition.EntraIdGroupNames.Length);
                created++;
            }
            else
            {
                _logger.LogError("❌ Failed to create role '{RoleName}': {Errors}",
                    roleDefinition.Name,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        _logger.LogInformation("🎉 Default roles seeding complete! Created: {Created}, Skipped: {Skipped}", created, skipped);
        _logger.LogInformation("💡 Roles now support automatic assignment based on AD/EntraID group membership!");
    }

    /// <summary>
    /// Ensures all system users (IsSystem = true) are assigned to the Admin role.
    /// Called after seeding roles to restore admin access after a wipe.
    /// </summary>
    public async Task EnsureSystemUsersHaveAdminRoleAsync()
    {
        if (!await _roleManager.RoleExistsAsync("Admin"))
            return;

        var systemUsers = _userManager.Users.Where(u => u.IsSystem).ToList();
        foreach (var user in systemUsers)
        {
            if (!await _userManager.IsInRoleAsync(user, "Admin"))
            {
                await _userManager.AddToRoleAsync(user, "Admin");
                _logger.LogInformation("Reassigned system user '{UserName}' to Admin role", user.UserName);
            }
        }
    }

    private class RoleDefinition
    {
        public string Name { get; set; } = "";
        public bool IsSystem { get; set; }
        public string Description { get; set; } = "";
        public string[] AdGroupNames { get; set; } = Array.Empty<string>();
        public string[] EntraIdGroupNames { get; set; } = Array.Empty<string>();
        public string Permissions { get; set; } = "";
    }
}
