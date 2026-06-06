using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

public interface IReportSeedService
{
    Task SeedBuiltInReportsAsync();
    Task SeedBuiltInSchedulesAsync();
}

public class ReportSeedService : IReportSeedService
{
    private readonly IReportRepository _reportRepo;
    private readonly ILogger<ReportSeedService> _logger;

    public ReportSeedService(IReportRepository reportRepo, ILogger<ReportSeedService> logger)
    {
        _reportRepo = reportRepo;
        _logger = logger;
    }

    public async Task SeedBuiltInReportsAsync()
    {
        var existingReports = (await _reportRepo.GetAllReportsAsync()).ToList();
        // Handle potential duplicates in database by using first occurrence
        var existingByName = existingReports
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var builtInReports = GetBuiltInReports();
        int created = 0, updated = 0, skipped = 0;

        foreach (var report in builtInReports)
        {
            if (existingByName.TryGetValue(report.Name, out var existing))
            {
                // Update existing built-in reports if the query has changed
                if (existing.IsBuiltIn && existing.QueryDefinition != report.QueryDefinition)
                {
                    try
                    {
                        existing.QueryDefinition = report.QueryDefinition;
                        existing.Description = report.Description;
                        existing.DisplayName = report.DisplayName;
                        existing.Category = report.Category;
                        existing.SubCategory = report.SubCategory;
                        existing.Icon = report.Icon;
                        existing.Tags = report.Tags;
                        await _reportRepo.UpdateReportAsync(existing);
                        updated++;
                        _logger.LogInformation("Updated built-in report: {ReportName}", report.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update report {ReportName}", report.Name);
                    }
                }
                else
                {
                    skipped++;
                }
                continue;
            }

            try
            {
                await _reportRepo.CreateReportAsync(report);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed report {ReportName}", report.Name);
            }
        }

        _logger.LogInformation("Report seeding complete: {Created} created, {Updated} updated, {Skipped} unchanged",
            created, updated, skipped);
    }

    public async Task SeedBuiltInSchedulesAsync()
    {
        var existingSchedules = (await _reportRepo.GetAllSchedulesAsync()).ToList();
        var allReports = (await _reportRepo.GetAllReportsAsync()).ToList();
        // Handle potential duplicates in database by using first occurrence
        var reportsByName = allReports
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var defaultSchedules = GetBuiltInSchedules();
        int created = 0, skipped = 0;

        foreach (var scheduleDef in defaultSchedules)
        {
            if (!reportsByName.TryGetValue(scheduleDef.ReportName, out var report))
            {
                _logger.LogDebug("Skipping schedule for '{ReportName}' - report not found", scheduleDef.ReportName);
                continue;
            }

            // Check if this report already has a schedule
            if (existingSchedules.Any(s => s.ReportId == report.Id))
            {
                skipped++;
                continue;
            }

            try
            {
                var schedule = new ReportSchedule
                {
                    Id = Guid.NewGuid(),
                    ReportId = report.Id,
                    Name = scheduleDef.Name,
                    Frequency = scheduleDef.Frequency,
                    CronExpression = scheduleDef.CronExpression,
                    ExecutionTime = scheduleDef.ExecutionTime,
                    DayOfWeek = scheduleDef.DayOfWeek,
                    DayOfMonth = scheduleDef.DayOfMonth,
                    OutputFormat = scheduleDef.OutputFormat,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System"
                };

                await _reportRepo.CreateScheduleAsync(schedule);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed schedule for report '{ReportName}'", scheduleDef.ReportName);
            }
        }

        _logger.LogInformation("Report schedule seeding complete: {Created} created, {Skipped} already exist",
            created, skipped);
    }

    private List<BuiltInScheduleDefinition> GetBuiltInSchedules()
    {
        return new List<BuiltInScheduleDefinition>
        {
            // Daily reports - run at 6 AM UTC
            new BuiltInScheduleDefinition
            {
                ReportName = "active_violations",
                Name = "Daily Active Violations",
                Frequency = "Daily",
                CronExpression = "0 0 6 * * ?",
                ExecutionTime = "06:00",
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "new_identities",
                Name = "Daily New Identities",
                Frequency = "Daily",
                CronExpression = "0 0 6 * * ?",
                ExecutionTime = "06:00",
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "sync_errors",
                Name = "Daily Sync Errors",
                Frequency = "Daily",
                CronExpression = "0 0 6 * * ?",
                ExecutionTime = "06:00",
                OutputFormat = "Excel"
            },

            // Weekly reports - run Monday at 7 AM UTC
            new BuiltInScheduleDefinition
            {
                ReportName = "violations_by_severity",
                Name = "Weekly Violations by Severity",
                Frequency = "Weekly",
                CronExpression = "0 0 7 ? * MON",
                ExecutionTime = "07:00",
                DayOfWeek = 1,
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "privileged_accounts",
                Name = "Weekly Privileged Accounts",
                Frequency = "Weekly",
                CronExpression = "0 0 7 ? * MON",
                ExecutionTime = "07:00",
                DayOfWeek = 1,
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "stale_accounts",
                Name = "Weekly Stale Accounts",
                Frequency = "Weekly",
                CronExpression = "0 0 7 ? * MON",
                ExecutionTime = "07:00",
                DayOfWeek = 1,
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "identities_without_manager",
                Name = "Weekly Identities Without Manager",
                Frequency = "Weekly",
                CronExpression = "0 0 7 ? * MON",
                ExecutionTime = "07:00",
                DayOfWeek = 1,
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "groups_without_owner",
                Name = "Weekly Groups Without Owner",
                Frequency = "Weekly",
                CronExpression = "0 0 7 ? * MON",
                ExecutionTime = "07:00",
                DayOfWeek = 1,
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "overdue_reviews",
                Name = "Weekly Overdue Reviews",
                Frequency = "Weekly",
                CronExpression = "0 0 7 ? * MON",
                ExecutionTime = "07:00",
                DayOfWeek = 1,
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "policy_effectiveness",
                Name = "Weekly Policy Effectiveness",
                Frequency = "Weekly",
                CronExpression = "0 0 7 ? * MON",
                ExecutionTime = "07:00",
                DayOfWeek = 1,
                OutputFormat = "Excel"
            },

            // Monthly reports - run 1st of month at 8 AM UTC
            new BuiltInScheduleDefinition
            {
                ReportName = "sox_compliance_summary",
                Name = "Monthly SOX Compliance",
                Frequency = "Monthly",
                CronExpression = "0 0 8 1 * ?",
                ExecutionTime = "08:00",
                DayOfMonth = 1,
                OutputFormat = "PDF"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "hipaa_compliance_summary",
                Name = "Monthly HIPAA Compliance",
                Frequency = "Monthly",
                CronExpression = "0 0 8 1 * ?",
                ExecutionTime = "08:00",
                DayOfMonth = 1,
                OutputFormat = "PDF"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "identities_by_department",
                Name = "Monthly Department Report",
                Frequency = "Monthly",
                CronExpression = "0 0 8 1 * ?",
                ExecutionTime = "08:00",
                DayOfMonth = 1,
                OutputFormat = "Excel"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "review_completion_rates",
                Name = "Monthly Review Completion Rates",
                Frequency = "Monthly",
                CronExpression = "0 0 8 1 * ?",
                ExecutionTime = "08:00",
                DayOfMonth = 1,
                OutputFormat = "PDF"
            },
            new BuiltInScheduleDefinition
            {
                ReportName = "compliance_trend",
                Name = "Monthly Compliance Trend",
                Frequency = "Monthly",
                CronExpression = "0 0 8 1 * ?",
                ExecutionTime = "08:00",
                DayOfMonth = 1,
                OutputFormat = "PDF"
            }
        };
    }

    private class BuiltInScheduleDefinition
    {
        public string ReportName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Frequency { get; set; } = "Weekly";
        public string CronExpression { get; set; } = string.Empty;
        public string ExecutionTime { get; set; } = "07:00";
        public int? DayOfWeek { get; set; }
        public int? DayOfMonth { get; set; }
        public string OutputFormat { get; set; } = "Excel";
    }

    private List<Report> GetBuiltInReports()
    {
        return new List<Report>
        {
            // =====================================
            // IDENTITY REPORTS (10)
            // =====================================
            new Report
            {
                Name = "all_identities",
                DisplayName = "All Identities",
                Description = "Complete list of all identities synced from directory sources",
                Category = "Identity",
                SubCategory = "Inventory",
                Icon = "fa-users",
                QueryDefinition = "SELECT * FROM Identities",
                IsBuiltIn = true,
                SortOrder = 1,
                Tags = "identity,users,inventory"
            },
            new Report
            {
                Name = "identities_without_manager",
                DisplayName = "Identities Without Manager",
                Description = "Identities that have no manager assigned - potential orphan accounts",
                Category = "Identity",
                SubCategory = "Compliance",
                Icon = "fa-user-slash",
                QueryDefinition = "SELECT * FROM Identities WHERE ManagerIdentityId IS NULL",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "identity,manager,compliance,orphan"
            },
            new Report
            {
                Name = "identities_without_department",
                DisplayName = "Identities Without Department",
                Description = "Identities that have no department assigned - needs data cleanup",
                Category = "Identity",
                SubCategory = "Compliance",
                Icon = "fa-building",
                QueryDefinition = "SELECT * FROM Identities WHERE Department IS NULL OR Department = ''",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "identity,department,compliance,cleanup"
            },
            new Report
            {
                Name = "identities_without_email",
                DisplayName = "Identities Without Email",
                Description = "Identities that have no email address assigned",
                Category = "Identity",
                SubCategory = "Compliance",
                Icon = "fa-envelope",
                QueryDefinition = "SELECT * FROM Identities WHERE PrimaryEmail IS NULL OR PrimaryEmail = ''",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "identity,email,compliance,cleanup"
            },
            new Report
            {
                Name = "identities_without_title",
                DisplayName = "Identities Without Job Title",
                Description = "Identities that have no job title assigned",
                Category = "Identity",
                SubCategory = "Compliance",
                Icon = "fa-briefcase",
                QueryDefinition = "SELECT * FROM Identities WHERE JobTitle IS NULL OR JobTitle = ''",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "identity,title,compliance,cleanup"
            },
            new Report
            {
                Name = "disabled_identities",
                DisplayName = "Disabled Identities",
                Description = "All disabled user identities that may need cleanup",
                Category = "Identity",
                SubCategory = "Cleanup",
                Icon = "fa-user-times",
                QueryDefinition = "SELECT * FROM Identities WHERE IsActive = 0",
                IsBuiltIn = true,
                SortOrder = 3,
                Tags = "identity,disabled,cleanup"
            },
            new Report
            {
                Name = "identities_by_department",
                DisplayName = "Identities by Department",
                Description = "Identity counts grouped by department for workforce analysis",
                Category = "Identity",
                SubCategory = "Analytics",
                Icon = "fa-building",
                QueryDefinition = "SELECT COALESCE(Department, '(No Department)') as Department, COUNT(*) as Count FROM Identities GROUP BY Department ORDER BY Count DESC",
                IsBuiltIn = true,
                SortOrder = 4,
                Tags = "identity,department,analytics"
            },
            new Report
            {
                Name = "identities_by_job_title",
                DisplayName = "Identities by Job Title",
                Description = "Identity counts grouped by job title",
                Category = "Identity",
                SubCategory = "Analytics",
                Icon = "fa-briefcase",
                QueryDefinition = "SELECT COALESCE(JobTitle, '(No Title)') as JobTitle, COUNT(*) as Count FROM Identities GROUP BY JobTitle ORDER BY Count DESC",
                IsBuiltIn = true,
                SortOrder = 5,
                Tags = "identity,jobtitle,analytics"
            },
            new Report
            {
                Name = "recent_identity_changes",
                DisplayName = "Recent Identity Changes",
                Description = "Identities modified in the last 30 days",
                Category = "Identity",
                SubCategory = "Audit",
                Icon = "fa-history",
                QueryDefinition = "SELECT * FROM Identities WHERE ModifiedAt >= DATEADD(DAY, -30, GETDATE())",
                IsBuiltIn = true,
                SortOrder = 6,
                Tags = "identity,audit,changes"
            },
            new Report
            {
                Name = "new_identities",
                DisplayName = "New Identities",
                Description = "Identities created in the last 30 days",
                Category = "Identity",
                SubCategory = "Onboarding",
                Icon = "fa-user-plus",
                QueryDefinition = "SELECT * FROM Identities WHERE CreatedAt >= DATEADD(DAY, -30, GETDATE())",
                IsBuiltIn = true,
                SortOrder = 7,
                Tags = "identity,new,onboarding"
            },
            new Report
            {
                Name = "identities_multiple_objects",
                DisplayName = "Identities with Multiple Objects",
                Description = "Identities linked to more than one directory object",
                Category = "Identity",
                SubCategory = "Analysis",
                Icon = "fa-clone",
                QueryDefinition = @"SELECT i.Id, i.DisplayName, i.FirstName, i.LastName, i.PrimaryEmail,
                    i.Department, i.JobTitle, i.IsActive, COUNT(o.Id) as ObjectCount
                    FROM Identities i
                    INNER JOIN Objects o ON i.Id = o.IdentityId
                    GROUP BY i.Id, i.DisplayName, i.FirstName, i.LastName, i.PrimaryEmail,
                        i.Department, i.JobTitle, i.IsActive
                    HAVING COUNT(o.Id) > 1
                    ORDER BY ObjectCount DESC",
                IsBuiltIn = true,
                SortOrder = 8,
                Tags = "identity,objects,multiple"
            },
            new Report
            {
                Name = "all_user_objects",
                DisplayName = "All User Objects",
                Description = "All user accounts from directory sources",
                Category = "Identity",
                SubCategory = "Inventory",
                Icon = "fa-users-cog",
                QueryDefinition = @"SELECT Id, DisplayName, Username, Email, Department, JobTitle, DN, IsActive, FirstSyncedAt, LastSyncedAt
                    FROM Objects WHERE ObjectClass = 'user' ORDER BY DisplayName",
                IsBuiltIn = true,
                SortOrder = 9,
                Tags = "identity,users,objects,inventory"
            },
            new Report
            {
                Name = "unlinked_identities",
                DisplayName = "Unlinked Identities",
                Description = "Identities without any linked directory objects",
                Category = "Identity",
                SubCategory = "Cleanup",
                Icon = "fa-user-tie",
                QueryDefinition = @"SELECT i.* FROM Identities i
                    LEFT JOIN Objects o ON i.Id = o.IdentityId
                    WHERE o.Id IS NULL",
                IsBuiltIn = true,
                SortOrder = 10,
                Tags = "identity,unlinked,cleanup"
            },
            new Report
            {
                Name = "identities_by_location",
                DisplayName = "Identities by Location",
                Description = "Identity counts grouped by office location",
                Category = "Identity",
                SubCategory = "Analytics",
                Icon = "fa-map-marker-alt",
                QueryDefinition = "SELECT COALESCE(Office, '(No Location)') as Location, COUNT(*) as Count FROM Identities GROUP BY Office ORDER BY Count DESC",
                IsBuiltIn = true,
                SortOrder = 11,
                Tags = "identity,location,analytics"
            },
            new Report
            {
                Name = "contractor_identities",
                DisplayName = "Contractor Identities",
                Description = "Identities classified as contractors or external users",
                Category = "Identity",
                SubCategory = "Compliance",
                Icon = "fa-id-badge",
                QueryDefinition = @"SELECT * FROM Identities
                    WHERE IdentityType IN ('Contractor', 'External', 'Vendor', 'Consultant')
                    OR UserType IN ('Contractor', 'External', 'Guest')
                    ORDER BY DisplayName",
                IsBuiltIn = true,
                SortOrder = 12,
                Tags = "identity,contractor,external,compliance"
            },

            // =====================================
            // GROUP REPORTS (11) - Query from Objects table where ObjectClass = 'group'
            // =====================================
            new Report
            {
                Name = "all_groups",
                DisplayName = "All Groups",
                Description = "Complete inventory of all directory groups",
                Category = "Access",
                SubCategory = "Groups",
                Icon = "fa-layer-group",
                QueryDefinition = @"SELECT Id,
                    COALESCE(DisplayName, CN, SUBSTRING(DN, 4, CHARINDEX(',', DN) - 4)) as GroupName,
                    DN, Email, OwnerObjectId, IsActive, FirstSyncedAt, LastSyncedAt
                    FROM Objects WHERE ObjectClass = 'group' ORDER BY GroupName",
                IsBuiltIn = true,
                SortOrder = 1,
                Tags = "group,inventory,access"
            },
            new Report
            {
                Name = "empty_groups",
                DisplayName = "Empty Groups",
                Description = "Groups with no members - candidates for cleanup",
                Category = "Access",
                SubCategory = "Cleanup",
                Icon = "fa-users-slash",
                QueryDefinition = @"SELECT g.Id,
                    COALESCE(g.DisplayName, g.CN, SUBSTRING(g.DN, 4, CHARINDEX(',', g.DN) - 4)) as GroupName,
                    g.DN, g.IsActive, g.FirstSyncedAt
                    FROM Objects g
                    LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
                    WHERE g.ObjectClass = 'group' AND ogm.Id IS NULL
                    ORDER BY GroupName",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "group,empty,cleanup"
            },
            new Report
            {
                Name = "large_groups",
                DisplayName = "Large Groups",
                Description = "Groups with more than 100 members",
                Category = "Access",
                SubCategory = "Analysis",
                Icon = "fa-users",
                QueryDefinition = @"SELECT g.Id,
                    COALESCE(g.DisplayName, g.CN) as GroupName,
                    g.DN, COUNT(ogm.Id) as MemberCount
                    FROM Objects g
                    INNER JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
                    WHERE g.ObjectClass = 'group'
                    GROUP BY g.Id, g.DisplayName, g.CN, g.DN
                    HAVING COUNT(ogm.Id) > 100
                    ORDER BY MemberCount DESC",
                IsBuiltIn = true,
                SortOrder = 3,
                Tags = "group,large,analysis"
            },
            new Report
            {
                Name = "groups_without_owner",
                DisplayName = "Groups Without Owner",
                Description = "Groups missing a designated owner - governance risk",
                Category = "Access",
                SubCategory = "Governance",
                Icon = "fa-exclamation-triangle",
                QueryDefinition = @"SELECT Id,
                    COALESCE(DisplayName, CN) as GroupName,
                    DN, IsActive, FirstSyncedAt
                    FROM Objects
                    WHERE ObjectClass = 'group' AND OwnerObjectId IS NULL
                    ORDER BY GroupName",
                IsBuiltIn = true,
                SortOrder = 4,
                Tags = "group,owner,governance,risk"
            },
            new Report
            {
                Name = "groups_without_email",
                DisplayName = "Security Groups (No Email)",
                Description = "Groups without email - typically security groups",
                Category = "Access",
                SubCategory = "Security",
                Icon = "fa-shield-alt",
                QueryDefinition = @"SELECT Id,
                    COALESCE(DisplayName, CN) as GroupName,
                    DN, IsActive, FirstSyncedAt
                    FROM Objects
                    WHERE ObjectClass = 'group' AND (Email IS NULL OR Email = '')
                    ORDER BY GroupName",
                IsBuiltIn = true,
                SortOrder = 6,
                Tags = "group,security,access"
            },
            new Report
            {
                Name = "nested_group_membership",
                DisplayName = "Nested Group Membership",
                Description = "Groups that are members of other groups",
                Category = "Access",
                SubCategory = "Analysis",
                Icon = "fa-sitemap",
                QueryDefinition = @"SELECT child.Id as ChildGroupId,
                    COALESCE(child.DisplayName, child.CN) as ChildGroupName,
                    parent.Id as ParentGroupId,
                    COALESCE(parent.DisplayName, parent.CN) as ParentGroupName
                    FROM Objects child
                    INNER JOIN ObjectGroupMemberships ogm ON child.Id = ogm.ObjectId
                    INNER JOIN Objects parent ON ogm.GroupId = parent.Id
                    WHERE child.ObjectClass = 'group' AND parent.ObjectClass = 'group'
                    ORDER BY ParentGroupName, ChildGroupName",
                IsBuiltIn = true,
                SortOrder = 7,
                Tags = "group,nested,membership"
            },
            new Report
            {
                Name = "group_member_counts",
                DisplayName = "Group Member Counts",
                Description = "All groups with their member counts",
                Category = "Access",
                SubCategory = "Analytics",
                Icon = "fa-chart-pie",
                QueryDefinition = @"SELECT g.Id,
                    COALESCE(g.DisplayName, g.CN) as GroupName,
                    COUNT(ogm.Id) as MemberCount
                    FROM Objects g
                    LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
                    WHERE g.ObjectClass = 'group'
                    GROUP BY g.Id, g.DisplayName, g.CN
                    ORDER BY MemberCount DESC",
                IsBuiltIn = true,
                SortOrder = 8,
                Tags = "group,count,analytics"
            },
            new Report
            {
                Name = "groups_with_email",
                DisplayName = "Groups With Email",
                Description = "Distribution groups and mail-enabled security groups",
                Category = "Access",
                SubCategory = "Email",
                Icon = "fa-envelope",
                QueryDefinition = @"SELECT Id,
                    COALESCE(DisplayName, CN) as GroupName,
                    DN, Email, IsActive, FirstSyncedAt
                    FROM Objects
                    WHERE ObjectClass = 'group' AND Email IS NOT NULL AND Email != ''
                    ORDER BY DisplayName",
                IsBuiltIn = true,
                SortOrder = 9,
                Tags = "group,email,distribution"
            },
            new Report
            {
                Name = "security_groups",
                DisplayName = "Security Groups",
                Description = "Security groups used for access control",
                Category = "Access",
                SubCategory = "Security",
                Icon = "fa-shield-alt",
                QueryDefinition = @"SELECT Id,
                    COALESCE(DisplayName, CN) as GroupName,
                    DN, IsActive, FirstSyncedAt
                    FROM Objects
                    WHERE ObjectClass = 'group' AND (Email IS NULL OR Email = '')
                    ORDER BY DisplayName",
                IsBuiltIn = true,
                SortOrder = 10,
                Tags = "group,security,access"
            },
            new Report
            {
                Name = "distribution_groups",
                DisplayName = "Distribution Groups",
                Description = "Mail-enabled distribution groups",
                Category = "Access",
                SubCategory = "Email",
                Icon = "fa-mail-bulk",
                QueryDefinition = @"SELECT Id,
                    COALESCE(DisplayName, CN) as GroupName,
                    DN, Email, IsActive, FirstSyncedAt
                    FROM Objects
                    WHERE ObjectClass = 'group' AND Email IS NOT NULL AND Email != ''
                    ORDER BY DisplayName",
                IsBuiltIn = true,
                SortOrder = 11,
                Tags = "group,distribution,email"
            },
            new Report
            {
                Name = "groups_by_type",
                DisplayName = "Groups by Type",
                Description = "Group counts categorized by type (security vs distribution)",
                Category = "Access",
                SubCategory = "Analytics",
                Icon = "fa-chart-bar",
                QueryDefinition = @"SELECT
                    CASE WHEN Email IS NOT NULL AND Email != '' THEN 'Distribution/Mail-Enabled' ELSE 'Security' END as GroupType,
                    COUNT(*) as Count
                    FROM Objects
                    WHERE ObjectClass = 'group'
                    GROUP BY CASE WHEN Email IS NOT NULL AND Email != '' THEN 'Distribution/Mail-Enabled' ELSE 'Security' END",
                IsBuiltIn = true,
                SortOrder = 12,
                Tags = "group,type,analytics"
            },

            // =====================================
            // COMPLIANCE REPORTS (8)
            // =====================================
            new Report
            {
                Name = "active_violations",
                DisplayName = "Active Policy Violations",
                Description = "All unresolved compliance policy violations requiring attention",
                Category = "Compliance",
                SubCategory = "Violations",
                Icon = "fa-exclamation-circle",
                QueryDefinition = @"SELECT v.Id, v.EntityId, v.EntityType, v.EntityDisplayName, v.Severity,
                    v.Status, v.ViolationScore, v.Description, v.DetectedAt, v.AcknowledgedAt,
                    v.RemediatedAt, v.ClosedAt, p.Name as PolicyName, p.DisplayName as PolicyDisplayName,
                    p.ComplianceFramework as FrameworkName
                    FROM CompliancePolicyViolations v
                    INNER JOIN CompliancePolicies p ON v.CompliancePolicyId = p.Id
                    WHERE v.Status = 'Open' OR v.Status = 'Active'",
                IsBuiltIn = true,
                SortOrder = 1,
                Tags = "compliance,violation,active,policy"
            },
            new Report
            {
                Name = "violations_by_severity",
                DisplayName = "Violations by Severity",
                Description = "Policy violations grouped by severity level",
                Category = "Compliance",
                SubCategory = "Analytics",
                Icon = "fa-chart-bar",
                QueryDefinition = @"SELECT v.Severity, COUNT(*) as Count
                    FROM CompliancePolicyViolations v
                    WHERE v.Status = 'Active'
                    GROUP BY v.Severity",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "compliance,violation,severity,analytics"
            },
            new Report
            {
                Name = "violations_by_framework",
                DisplayName = "Violations by Framework",
                Description = "Policy violations grouped by compliance framework",
                Category = "Compliance",
                SubCategory = "Analytics",
                Icon = "fa-balance-scale",
                QueryDefinition = @"SELECT p.ComplianceFramework as Framework, COUNT(v.Id) as ViolationCount
                    FROM CompliancePolicies p
                    LEFT JOIN CompliancePolicyViolations v ON p.Id = v.CompliancePolicyId
                        AND (v.Status = 'Open' OR v.Status = 'Active')
                    WHERE p.ComplianceFramework IS NOT NULL AND p.ComplianceFramework != ''
                    GROUP BY p.ComplianceFramework",
                IsBuiltIn = true,
                SortOrder = 3,
                Tags = "compliance,violation,framework,analytics"
            },
            new Report
            {
                Name = "sox_compliance_summary",
                DisplayName = "SOX Compliance Summary",
                Description = "Sarbanes-Oxley compliance status and violations",
                Category = "Compliance",
                SubCategory = "SOX",
                Icon = "fa-gavel",
                QueryDefinition = @"SELECT v.Id, v.EntityId, v.EntityType, v.EntityDisplayName, v.Severity,
                    v.Status, v.ViolationScore, v.Description, v.DetectedAt,
                    p.Name as PolicyName, p.DisplayName as PolicyDisplayName
                    FROM CompliancePolicyViolations v
                    INNER JOIN CompliancePolicies p ON v.CompliancePolicyId = p.Id
                    WHERE p.ComplianceFramework LIKE '%SOX%' AND (v.Status = 'Open' OR v.Status = 'Active')",
                IsBuiltIn = true,
                SortOrder = 4,
                Tags = "compliance,sox,audit"
            },
            new Report
            {
                Name = "hipaa_compliance_summary",
                DisplayName = "HIPAA Compliance Summary",
                Description = "HIPAA compliance status and violations",
                Category = "Compliance",
                SubCategory = "HIPAA",
                Icon = "fa-heartbeat",
                QueryDefinition = @"SELECT v.Id, v.EntityId, v.EntityType, v.EntityDisplayName, v.Severity,
                    v.Status, v.ViolationScore, v.Description, v.DetectedAt,
                    p.Name as PolicyName, p.DisplayName as PolicyDisplayName
                    FROM CompliancePolicyViolations v
                    INNER JOIN CompliancePolicies p ON v.CompliancePolicyId = p.Id
                    WHERE p.ComplianceFramework LIKE '%HIPAA%' AND (v.Status = 'Open' OR v.Status = 'Active')",
                IsBuiltIn = true,
                SortOrder = 5,
                Tags = "compliance,hipaa,healthcare"
            },
            new Report
            {
                Name = "remediated_violations",
                DisplayName = "Remediated Violations",
                Description = "Recently remediated policy violations",
                Category = "Compliance",
                SubCategory = "History",
                Icon = "fa-check-circle",
                QueryDefinition = @"SELECT * FROM CompliancePolicyViolations
                    WHERE Status = 'Remediated' AND RemediatedAt >= DATEADD(DAY, -30, GETDATE())",
                IsBuiltIn = true,
                SortOrder = 6,
                Tags = "compliance,remediated,history"
            },
            new Report
            {
                Name = "policy_effectiveness",
                DisplayName = "Policy Effectiveness",
                Description = "Compliance policy effectiveness metrics",
                Category = "Compliance",
                SubCategory = "Metrics",
                Icon = "fa-tachometer-alt",
                QueryDefinition = @"SELECT p.Name, p.DisplayName, p.ComplianceFramework,
                    COUNT(CASE WHEN v.Status = 'Open' OR v.Status = 'Active' THEN 1 END) as ActiveViolations,
                    COUNT(CASE WHEN v.Status = 'Remediated' THEN 1 END) as Remediated,
                    COUNT(CASE WHEN v.Status = 'Closed' THEN 1 END) as Closed,
                    COUNT(v.Id) as TotalDetected
                    FROM CompliancePolicies p
                    LEFT JOIN CompliancePolicyViolations v ON p.Id = v.CompliancePolicyId
                    GROUP BY p.Id, p.Name, p.DisplayName, p.ComplianceFramework",
                IsBuiltIn = true,
                SortOrder = 7,
                Tags = "compliance,policy,effectiveness,metrics"
            },
            new Report
            {
                Name = "compliance_trend",
                DisplayName = "Compliance Trend",
                Description = "Compliance violation trends over time",
                Category = "Compliance",
                SubCategory = "Trends",
                Icon = "fa-chart-line",
                QueryDefinition = @"SELECT CAST(DetectedAt as DATE) as Date, COUNT(*) as ViolationCount
                    FROM CompliancePolicyViolations
                    WHERE DetectedAt >= DATEADD(MONTH, -3, GETDATE())
                    GROUP BY CAST(DetectedAt as DATE)
                    ORDER BY Date",
                IsBuiltIn = true,
                SortOrder = 8,
                Tags = "compliance,trend,analytics"
            },

            // =====================================
            // ACCESS REVIEW REPORTS (6)
            // =====================================
            new Report
            {
                Name = "active_access_reviews",
                DisplayName = "Active Access Reviews",
                Description = "Currently active access review campaigns",
                Category = "Access Review",
                SubCategory = "Active",
                Icon = "fa-clipboard-check",
                QueryDefinition = @"SELECT * FROM Campaigns WHERE Status = 'Active'",
                IsBuiltIn = true,
                SortOrder = 1,
                Tags = "accessreview,active,campaign"
            },
            new Report
            {
                Name = "pending_review_decisions",
                DisplayName = "Pending Review Decisions",
                Description = "Access review assignments awaiting decision",
                Category = "Access Review",
                SubCategory = "Pending",
                Icon = "fa-clock",
                QueryDefinition = @"SELECT a.*, c.Name as CampaignName
                    FROM AccessReviewAssignments a
                    INNER JOIN Campaigns c ON a.CampaignId = c.Id
                    WHERE a.Decision IS NULL AND c.Status = 'Active'",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "accessreview,pending,decision"
            },
            new Report
            {
                Name = "overdue_reviews",
                DisplayName = "Overdue Access Reviews",
                Description = "Access reviews past their due date",
                Category = "Access Review",
                SubCategory = "Overdue",
                Icon = "fa-exclamation-triangle",
                QueryDefinition = @"SELECT c.* FROM Campaigns c
                    WHERE c.Status = 'Active' AND c.DueDate < GETDATE()",
                IsBuiltIn = true,
                SortOrder = 3,
                Tags = "accessreview,overdue,risk"
            },
            new Report
            {
                Name = "review_completion_rates",
                DisplayName = "Review Completion Rates",
                Description = "Access review completion statistics by campaign",
                Category = "Access Review",
                SubCategory = "Analytics",
                Icon = "fa-percentage",
                QueryDefinition = @"SELECT c.Name, c.TotalAssignments, c.CompletedAssignments,
                    CASE WHEN c.TotalAssignments > 0
                        THEN CAST(c.CompletedAssignments as FLOAT) / c.TotalAssignments * 100
                        ELSE 0 END as CompletionRate
                    FROM Campaigns c
                    ORDER BY c.CreatedAt DESC",
                IsBuiltIn = true,
                SortOrder = 4,
                Tags = "accessreview,completion,analytics"
            },
            new Report
            {
                Name = "revoked_access",
                DisplayName = "Revoked Access",
                Description = "Access that was revoked through access reviews",
                Category = "Access Review",
                SubCategory = "Actions",
                Icon = "fa-user-minus",
                QueryDefinition = @"SELECT a.*, c.Name as CampaignName
                    FROM AccessReviewAssignments a
                    INNER JOIN Campaigns c ON a.CampaignId = c.Id
                    WHERE a.Decision = 'Revoke'",
                IsBuiltIn = true,
                SortOrder = 5,
                Tags = "accessreview,revoked,action"
            },
            new Report
            {
                Name = "review_decision_history",
                DisplayName = "Review Decision History",
                Description = "Historical record of all access review decisions",
                Category = "Access Review",
                SubCategory = "Audit",
                Icon = "fa-history",
                QueryDefinition = @"SELECT * FROM ReviewDecisionHistories ORDER BY DecisionDate DESC",
                IsBuiltIn = true,
                SortOrder = 6,
                Tags = "accessreview,decision,audit,history"
            },

            // =====================================
            // SECURITY REPORTS (6)
            // =====================================
            new Report
            {
                Name = "privileged_accounts",
                DisplayName = "Privileged Accounts",
                Description = "Accounts in groups containing Admin or Privileged in the name",
                Category = "Security",
                SubCategory = "Privileged Access",
                Icon = "fa-user-shield",
                QueryDefinition = @"SELECT DISTINCT o.Id, o.DisplayName, o.Username, o.Email, o.ObjectClass, o.IsActive,
                    g.DisplayName as GroupName, o.FirstSyncedAt, o.LastSyncedAt
                    FROM Objects o
                    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    INNER JOIN Objects g ON ogm.GroupId = g.Id
                    WHERE g.ObjectClass = 'group'
                    AND (g.DisplayName LIKE '%Admin%' OR g.CN LIKE '%Admin%'
                         OR g.DisplayName LIKE '%Privileged%' OR g.CN LIKE '%Privileged%')
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 1,
                Tags = "security,privileged,admin,access"
            },
            new Report
            {
                Name = "service_accounts",
                DisplayName = "Service Accounts",
                Description = "Accounts with usernames starting with svc or service",
                Category = "Security",
                SubCategory = "Service Accounts",
                Icon = "fa-robot",
                QueryDefinition = @"SELECT Id, DisplayName, Username, Email, ObjectClass, IsActive, FirstSyncedAt, LastSyncedAt
                    FROM Objects
                    WHERE ObjectClass = 'user' AND (
                        Username LIKE 'svc%' OR
                        Username LIKE 'service%'
                    )
                    ORDER BY Username",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "security,service,account,nonhuman"
            },
            new Report
            {
                Name = "stale_accounts",
                DisplayName = "Stale Accounts",
                Description = "User accounts not synced in 90+ days - may need cleanup",
                Category = "Security",
                SubCategory = "Stale",
                Icon = "fa-user-clock",
                QueryDefinition = @"SELECT Id, DisplayName, Username, Email, ObjectClass, IsActive, FirstSyncedAt, LastSyncedAt
                    FROM Objects
                    WHERE ObjectClass = 'user' AND LastSyncedAt < DATEADD(DAY, -90, GETDATE())
                    ORDER BY LastSyncedAt ASC",
                IsBuiltIn = true,
                SortOrder = 3,
                Tags = "security,stale,inactive,risk"
            },
            new Report
            {
                Name = "accounts_password_never_expires",
                DisplayName = "Accounts with Non-Expiring Passwords",
                Description = "Accounts configured with password never expires - compliance risk",
                Category = "Security",
                SubCategory = "Password",
                Icon = "fa-key",
                QueryDefinition = @"SELECT Id, DisplayName, Username, Email, DN, IsActive, PasswordLastSet, FirstSyncedAt
                    FROM Objects
                    WHERE ObjectClass = 'user' AND PasswordNeverExpires = 1
                    ORDER BY DisplayName",
                IsBuiltIn = true,
                SortOrder = 4,
                Tags = "security,password,neverexpires,risk"
            },
            new Report
            {
                Name = "computer_accounts",
                DisplayName = "Computer Accounts",
                Description = "All computer and device accounts in the directory",
                Category = "Security",
                SubCategory = "Devices",
                Icon = "fa-desktop",
                QueryDefinition = @"SELECT Id, DisplayName, CN, DN, IsActive, FirstSyncedAt, LastSyncedAt
                    FROM Objects WHERE ObjectClass = 'computer'
                    ORDER BY DisplayName",
                IsBuiltIn = true,
                SortOrder = 5,
                Tags = "security,computer,device"
            },
            new Report
            {
                Name = "disabled_accounts_with_access",
                DisplayName = "Disabled Accounts with Group Membership",
                Description = "Disabled accounts that still have group memberships - security risk",
                Category = "Security",
                SubCategory = "Risk",
                Icon = "fa-radiation",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Email, o.IsActive,
                    COUNT(ogm.Id) as GroupCount
                    FROM Objects o
                    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    WHERE o.ObjectClass = 'user' AND o.IsActive = 0
                    GROUP BY o.Id, o.DisplayName, o.Username, o.Email, o.IsActive
                    HAVING COUNT(ogm.Id) > 0
                    ORDER BY GroupCount DESC",
                IsBuiltIn = true,
                SortOrder = 6,
                Tags = "security,risk,disabled,access"
            },
            new Report
            {
                Name = "password_age_report",
                DisplayName = "Password Age Report",
                Description = "Accounts with old passwords that may need rotation",
                Category = "Security",
                SubCategory = "Passwords",
                Icon = "fa-clock",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Email,
                    o.PasswordLastSet, o.IsActive,
                    DATEDIFF(DAY, o.PasswordLastSet, GETDATE()) as PasswordAgeDays
                    FROM Objects o
                    WHERE o.ObjectClass = 'user' AND o.IsActive = 1 AND o.PasswordLastSet IS NOT NULL
                    ORDER BY o.PasswordLastSet ASC",
                IsBuiltIn = true,
                SortOrder = 7,
                Tags = "security,password,age,rotation"
            },
            new Report
            {
                Name = "high_risk_accounts",
                DisplayName = "High Risk Accounts",
                Description = "Accounts with multiple security risk factors",
                Category = "Security",
                SubCategory = "Risk",
                Icon = "fa-exclamation-circle",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Email, o.IsActive,
                    o.PasswordNeverExpires, o.PasswordLastSet, o.LastLogonTimestamp,
                    (SELECT COUNT(*) FROM ObjectGroupMemberships ogm WHERE ogm.ObjectId = o.Id) as GroupCount
                    FROM Objects o
                    WHERE o.ObjectClass = 'user' AND o.IsActive = 1
                    AND (o.PasswordNeverExpires = 1
                        OR o.PasswordLastSet < DATEADD(YEAR, -1, GETDATE())
                        OR o.LastLogonTimestamp < DATEADD(DAY, -90, GETDATE()))
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 8,
                Tags = "security,risk,high,accounts"
            },

            // =====================================
            // SYNC REPORTS (4)
            // =====================================
            new Report
            {
                Name = "sync_project_status",
                DisplayName = "Sync Project Status",
                Description = "Status of all directory sync projects",
                Category = "Sync",
                SubCategory = "Status",
                Icon = "fa-sync",
                QueryDefinition = @"SELECT * FROM SyncProjects ORDER BY LastRunAt DESC",
                IsBuiltIn = true,
                SortOrder = 1,
                Tags = "sync,project,status"
            },
            new Report
            {
                Name = "sync_errors",
                DisplayName = "Sync Errors",
                Description = "Recent synchronization errors and failures",
                Category = "Sync",
                SubCategory = "Errors",
                Icon = "fa-exclamation-triangle",
                QueryDefinition = @"SELECT sal.Id, sal.SyncStepRunId, sal.ObjectId, sal.OperationType,
                    sal.ObjectDisplayName, sal.SourceUniqueId, sal.Email, sal.Username,
                    sal.ErrorMessage, sal.Timestamp, sal.ChangeCount
                    FROM SyncAuditLogs sal
                    WHERE sal.OperationType = 'Error' AND sal.Timestamp >= DATEADD(DAY, -7, GETDATE())
                    ORDER BY sal.Timestamp DESC",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "sync,error,failure"
            },
            new Report
            {
                Name = "sync_statistics",
                DisplayName = "Sync Statistics",
                Description = "Synchronization performance and record counts",
                Category = "Sync",
                SubCategory = "Analytics",
                Icon = "fa-chart-area",
                QueryDefinition = @"SELECT sp.Name, sp.LastRunAt, sp.RecordsProcessed, sp.RecordsCreated,
                    sp.RecordsUpdated, sp.RecordsDeleted, sp.Status
                    FROM SyncProjects sp
                    ORDER BY sp.LastRunAt DESC",
                IsBuiltIn = true,
                SortOrder = 3,
                Tags = "sync,statistics,performance"
            },
            new Report
            {
                Name = "directory_connections",
                DisplayName = "Directory Connections",
                Description = "Status of all directory connections",
                Category = "Sync",
                SubCategory = "Connections",
                Icon = "fa-plug",
                QueryDefinition = @"SELECT * FROM DirectoryConnections ORDER BY Name",
                IsBuiltIn = true,
                SortOrder = 4,
                Tags = "sync,connection,directory"
            },

            // =====================================
            // AUDIT REPORTS (4)
            // =====================================
            new Report
            {
                Name = "user_activity_log",
                DisplayName = "User Activity Log",
                Description = "Recent user activity and system events",
                Category = "Audit",
                SubCategory = "Activity",
                Icon = "fa-user-clock",
                QueryDefinition = @"SELECT * FROM AuditLogs
                    WHERE Timestamp >= DATEADD(DAY, -7, GETDATE())
                    ORDER BY Timestamp DESC",
                IsBuiltIn = true,
                SortOrder = 1,
                Tags = "audit,activity,user,log"
            },
            new Report
            {
                Name = "admin_activity",
                DisplayName = "Administrative Activity",
                Description = "Actions performed by administrators",
                Category = "Audit",
                SubCategory = "Admin",
                Icon = "fa-user-cog",
                QueryDefinition = @"SELECT * FROM AuditLogs
                    WHERE Action LIKE 'Admin%' OR Category = 'Administration'
                    ORDER BY Timestamp DESC",
                IsBuiltIn = true,
                SortOrder = 2,
                Tags = "audit,admin,activity"
            },
            new Report
            {
                Name = "login_history",
                DisplayName = "Login History",
                Description = "User login attempts and authentication events",
                Category = "Audit",
                SubCategory = "Authentication",
                Icon = "fa-sign-in-alt",
                QueryDefinition = @"SELECT * FROM AuditLogs
                    WHERE Category = 'Authentication' OR Action LIKE '%Login%'
                    ORDER BY Timestamp DESC",
                IsBuiltIn = true,
                SortOrder = 3,
                Tags = "audit,login,authentication"
            },
            new Report
            {
                Name = "data_changes",
                DisplayName = "Data Change Log",
                Description = "All data modifications and changes",
                Category = "Audit",
                SubCategory = "Changes",
                Icon = "fa-edit",
                QueryDefinition = @"SELECT * FROM AuditLogs
                    WHERE Action IN ('Create', 'Update', 'Delete')
                    ORDER BY Timestamp DESC",
                IsBuiltIn = true,
                SortOrder = 4,
                Tags = "audit,changes,data,modification"
            },

            // =====================================
            // PCI DSS COMPLIANCE REPORTS (3)
            // =====================================
            new Report
            {
                Name = "pci_admin_account_inventory",
                DisplayName = "PCI DSS: Admin Account Inventory",
                Description = "All administrative accounts for PCI DSS Requirement 8 compliance",
                Category = "Compliance",
                SubCategory = "PCI DSS",
                Icon = "fa-credit-card",
                QueryDefinition = @"SELECT DISTINCT o.Id, o.DisplayName, o.Username, o.Email, o.IsActive,
                    g.DisplayName as PrivilegedGroup, o.PasswordLastSet, o.LastLoginAt
                    FROM Objects o
                    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    INNER JOIN Objects g ON ogm.GroupId = g.Id
                    WHERE o.ObjectClass = 'user'
                    AND (g.DN LIKE '%Admin%' OR g.DN LIKE '%Privileged%' OR g.DN LIKE '%Operator%')
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 10,
                Tags = "compliance,pcidss,admin,inventory,requirement8"
            },
            new Report
            {
                Name = "pci_password_policy_compliance",
                DisplayName = "PCI DSS: Password Policy Compliance",
                Description = "Users with non-compliant password settings per PCI DSS Requirement 8.2",
                Category = "Compliance",
                SubCategory = "PCI DSS",
                Icon = "fa-key",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.IsActive,
                    o.PasswordNeverExpires, o.PasswordLastSet,
                    DATEDIFF(DAY, o.PasswordLastSet, GETUTCDATE()) as PasswordAgeDays
                    FROM Objects o
                    WHERE o.ObjectClass = 'user' AND o.IsActive = 1
                    AND (o.PasswordNeverExpires = 1 OR o.PasswordLastSet < DATEADD(DAY, -90, GETUTCDATE()))
                    ORDER BY o.PasswordLastSet ASC",
                IsBuiltIn = true,
                SortOrder = 11,
                Tags = "compliance,pcidss,password,requirement8"
            },
            new Report
            {
                Name = "pci_access_control_review",
                DisplayName = "PCI DSS: Access Control Review",
                Description = "User access rights review for PCI DSS Requirement 7",
                Category = "Compliance",
                SubCategory = "PCI DSS",
                Icon = "fa-lock",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Department,
                    COUNT(ogm.Id) as GroupCount,
                    STRING_AGG(COALESCE(g.DisplayName, g.CN), ', ') as Groups
                    FROM Objects o
                    LEFT JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    LEFT JOIN Objects g ON ogm.GroupId = g.Id AND g.ObjectClass = 'group'
                    WHERE o.ObjectClass = 'user' AND o.IsActive = 1
                    GROUP BY o.Id, o.DisplayName, o.Username, o.Department
                    ORDER BY GroupCount DESC",
                IsBuiltIn = true,
                SortOrder = 12,
                Tags = "compliance,pcidss,access,review,requirement7"
            },

            // =====================================
            // ISO 27001 COMPLIANCE REPORTS (3)
            // =====================================
            new Report
            {
                Name = "iso_user_access_rights",
                DisplayName = "ISO 27001: User Access Rights",
                Description = "Complete user access rights inventory per ISO 27001 A.9.2",
                Category = "Compliance",
                SubCategory = "ISO 27001",
                Icon = "fa-certificate",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Email, o.Department, o.JobTitle,
                    o.IsActive, o.LastLoginAt, COUNT(ogm.Id) as GroupMemberships
                    FROM Objects o
                    LEFT JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    WHERE o.ObjectClass = 'user'
                    GROUP BY o.Id, o.DisplayName, o.Username, o.Email, o.Department, o.JobTitle, o.IsActive, o.LastLoginAt
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 13,
                Tags = "compliance,iso27001,access,rights,A9"
            },
            new Report
            {
                Name = "iso_privileged_access",
                DisplayName = "ISO 27001: Privileged Access Report",
                Description = "Privileged access review per ISO 27001 A.9.2.3",
                Category = "Compliance",
                SubCategory = "ISO 27001",
                Icon = "fa-user-shield",
                QueryDefinition = @"SELECT DISTINCT o.Id, o.DisplayName, o.Username, o.Department,
                    o.IsActive, o.LastLoginAt,
                    COALESCE(g.DisplayName, g.CN) as PrivilegedGroup
                    FROM Objects o
                    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    INNER JOIN Objects g ON ogm.GroupId = g.Id
                    WHERE o.ObjectClass = 'user'
                    AND (g.DN LIKE '%Admin%' OR g.DN LIKE '%Enterprise%' OR g.DN LIKE '%Schema%'
                         OR g.DN LIKE '%Domain Controllers%')
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 14,
                Tags = "compliance,iso27001,privileged,A9"
            },
            new Report
            {
                Name = "iso_access_recertification",
                DisplayName = "ISO 27001: Access Recertification Status",
                Description = "Access review recertification status per ISO 27001 A.9.2.5",
                Category = "Compliance",
                SubCategory = "ISO 27001",
                Icon = "fa-clipboard-check",
                QueryDefinition = @"SELECT c.Id, c.Name as CampaignName, c.Status, c.StartDate, c.DueDate,
                    c.TotalAssignments, c.CompletedAssignments,
                    CASE WHEN c.TotalAssignments > 0
                        THEN CAST(c.CompletedAssignments AS FLOAT) / c.TotalAssignments * 100
                        ELSE 0 END as CompletionPct
                    FROM Campaigns c
                    ORDER BY c.DueDate DESC",
                IsBuiltIn = true,
                SortOrder = 15,
                Tags = "compliance,iso27001,recertification,A9"
            },

            // =====================================
            // ADDITIONAL SOX REPORTS (2)
            // =====================================
            new Report
            {
                Name = "sox_access_change_log",
                DisplayName = "SOX: Access Change Log",
                Description = "All access changes for SOX Section 404 audit trail",
                Category = "Compliance",
                SubCategory = "SOX",
                Icon = "fa-exchange-alt",
                QueryDefinition = @"SELECT * FROM ChangeAuditLogs
                    WHERE Timestamp >= DATEADD(MONTH, -3, GETUTCDATE())
                    ORDER BY Timestamp DESC",
                IsBuiltIn = true,
                SortOrder = 16,
                Tags = "compliance,sox,changes,audit,section404"
            },
            new Report
            {
                Name = "sox_segregation_of_duties",
                DisplayName = "SOX: Segregation of Duties",
                Description = "Users in multiple conflicting privileged groups (potential SoD violation)",
                Category = "Compliance",
                SubCategory = "SOX",
                Icon = "fa-balance-scale",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Department,
                    COUNT(DISTINCT g.Id) as PrivilegedGroupCount,
                    STRING_AGG(COALESCE(g.DisplayName, g.CN), ', ') as PrivilegedGroups
                    FROM Objects o
                    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    INNER JOIN Objects g ON ogm.GroupId = g.Id
                    WHERE o.ObjectClass = 'user' AND o.IsActive = 1
                    AND (g.DN LIKE '%Admin%' OR g.DN LIKE '%Operator%' OR g.DN LIKE '%Manager%')
                    GROUP BY o.Id, o.DisplayName, o.Username, o.Department
                    HAVING COUNT(DISTINCT g.Id) >= 2
                    ORDER BY PrivilegedGroupCount DESC",
                IsBuiltIn = true,
                SortOrder = 17,
                Tags = "compliance,sox,sod,segregation,risk"
            },

            // =====================================
            // ADDITIONAL HIPAA REPORTS (2)
            // =====================================
            new Report
            {
                Name = "hipaa_phi_access_audit",
                DisplayName = "HIPAA: PHI Access Audit",
                Description = "Users with access to groups that may contain PHI data",
                Category = "Compliance",
                SubCategory = "HIPAA",
                Icon = "fa-file-medical",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Department,
                    COALESCE(g.DisplayName, g.CN) as GroupName, g.DN as GroupDN
                    FROM Objects o
                    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    INNER JOIN Objects g ON ogm.GroupId = g.Id
                    WHERE o.ObjectClass = 'user' AND o.IsActive = 1
                    AND (g.DN LIKE '%Health%' OR g.DN LIKE '%Medical%' OR g.DN LIKE '%PHI%'
                         OR g.DN LIKE '%Patient%' OR g.DN LIKE '%Clinical%')
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 18,
                Tags = "compliance,hipaa,phi,access,healthcare"
            },
            new Report
            {
                Name = "hipaa_terminated_employee_access",
                DisplayName = "HIPAA: Terminated Employee Access",
                Description = "Disabled accounts that still have group memberships - HIPAA violation risk",
                Category = "Compliance",
                SubCategory = "HIPAA",
                Icon = "fa-user-times",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Email,
                    COUNT(ogm.Id) as RemainingGroups, o.LastSyncedAt
                    FROM Objects o
                    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    WHERE o.ObjectClass = 'user' AND o.IsActive = 0
                    GROUP BY o.Id, o.DisplayName, o.Username, o.Email, o.LastSyncedAt
                    HAVING COUNT(ogm.Id) > 0
                    ORDER BY RemainingGroups DESC",
                IsBuiltIn = true,
                SortOrder = 19,
                Tags = "compliance,hipaa,terminated,access,risk"
            },

            // =====================================
            // GENERAL GOVERNANCE REPORTS (3)
            // =====================================
            new Report
            {
                Name = "orphaned_objects",
                DisplayName = "Orphaned Objects",
                Description = "Directory objects not linked to any identity record",
                Category = "Identity",
                SubCategory = "Cleanup",
                Icon = "fa-unlink",
                QueryDefinition = @"SELECT o.Id, o.DisplayName, o.Username, o.Email, o.ObjectClass,
                    o.DN, o.IsActive, o.FirstSyncedAt
                    FROM Objects o
                    WHERE o.IdentityId IS NULL AND o.ObjectClass IN ('user', 'contact')
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 20,
                Tags = "identity,orphaned,cleanup,governance"
            },
            new Report
            {
                Name = "group_membership_matrix",
                DisplayName = "Group Membership Matrix",
                Description = "Cross-reference of users and their group memberships",
                Category = "Access",
                SubCategory = "Analysis",
                Icon = "fa-th",
                QueryDefinition = @"SELECT o.DisplayName as UserName, o.Department,
                    COALESCE(g.DisplayName, g.CN) as GroupName, g.DN as GroupDN
                    FROM Objects o
                    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
                    INNER JOIN Objects g ON ogm.GroupId = g.Id
                    WHERE o.ObjectClass = 'user' AND g.ObjectClass = 'group' AND o.IsActive = 1
                    ORDER BY o.DisplayName, GroupName",
                IsBuiltIn = true,
                SortOrder = 13,
                Tags = "access,matrix,membership,analysis"
            },
            new Report
            {
                Name = "license_usage_summary",
                DisplayName = "License Usage Summary",
                Description = "Object counts by type for license and capacity planning",
                Category = "Identity",
                SubCategory = "Analytics",
                Icon = "fa-chart-pie",
                QueryDefinition = @"SELECT ObjectClass, IsActive,
                    COUNT(*) as ObjectCount,
                    MIN(FirstSyncedAt) as EarliestSync,
                    MAX(LastSyncedAt) as LatestSync
                    FROM Objects
                    GROUP BY ObjectClass, IsActive
                    ORDER BY ObjectClass, IsActive DESC",
                IsBuiltIn = true,
                SortOrder = 21,
                Tags = "identity,license,capacity,analytics"
            },

            // =====================================
            // ENTERPRISE REPORTER REPLACEMENT REPORTS
            // =====================================
            new Report
            {
                Name = "stale_computers",
                DisplayName = "Stale Computers (90+ Days)",
                Description = "Computers not synced in 90+ days - may be decommissioned or offline",
                Category = "Security",
                SubCategory = "Computers",
                Icon = "fa-desktop",
                QueryDefinition = @"SELECT o.DisplayName, o.CN, o.DN, o.IsActive,
                    FORMAT(o.LastSyncedAt, 'yyyy-MM-dd') as LastSynced,
                    DATEDIFF(DAY, o.LastSyncedAt, GETUTCDATE()) as DaysInactive,
                    dc.Name as Connection
                    FROM Objects o
                    LEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId
                    WHERE o.ObjectClass = 'computer'
                      AND o.DeletedAt IS NULL
                      AND DATEDIFF(DAY, o.LastSyncedAt, GETUTCDATE()) > 90
                    ORDER BY DaysInactive DESC",
                IsBuiltIn = true,
                SortOrder = 22,
                Tags = "security,computer,stale,cleanup"
            },
            new Report
            {
                Name = "accounts_never_logged_in",
                DisplayName = "Accounts Never Logged In",
                Description = "Active user accounts that have never had a recorded login",
                Category = "Security",
                SubCategory = "Hygiene",
                Icon = "fa-user-clock",
                QueryDefinition = @"SELECT o.DisplayName, o.Username, o.UserPrincipalName,
                    o.Department, o.JobTitle,
                    FORMAT(o.FirstSyncedAt, 'yyyy-MM-dd') as FirstSynced,
                    dc.Name as Connection
                    FROM Objects o
                    LEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId
                    LEFT JOIN ObjectAttributes oa ON oa.ObjectId = o.Id AND oa.AttributeName = 'lastLogonTimestamp'
                    WHERE o.ObjectClass = 'user'
                      AND o.IsActive = 1
                      AND o.DeletedAt IS NULL
                      AND (oa.AttributeValue IS NULL OR oa.AttributeValue = '' OR oa.AttributeValue = '0')
                    ORDER BY o.FirstSyncedAt",
                IsBuiltIn = true,
                SortOrder = 23,
                Tags = "security,login,never,hygiene"
            },
            new Report
            {
                Name = "password_never_expires",
                DisplayName = "Password Never Expires",
                Description = "Active user accounts with password set to never expire - security risk",
                Category = "Security",
                SubCategory = "Password",
                Icon = "fa-key",
                QueryDefinition = @"SELECT o.DisplayName, o.Username, o.UserPrincipalName,
                    o.Department, o.JobTitle,
                    FORMAT(o.PasswordLastSet, 'yyyy-MM-dd') as PasswordLastSet,
                    CASE WHEN o.IsAdminSDHolder = 1 THEN 'Yes' ELSE 'No' END as AdminSDHolder,
                    dc.Name as Connection
                    FROM Objects o
                    LEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId
                    WHERE o.ObjectClass = 'user'
                      AND o.IsActive = 1
                      AND o.DeletedAt IS NULL
                      AND o.PasswordNeverExpires = 1
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 24,
                Tags = "security,password,compliance"
            },
            new Report
            {
                Name = "users_without_manager_objects",
                DisplayName = "Users Without Manager (Objects)",
                Description = "Active user objects with no manager assigned - from Objects table",
                Category = "Identity",
                SubCategory = "Compliance",
                Icon = "fa-user-slash",
                QueryDefinition = @"SELECT o.DisplayName, o.Username, o.UserPrincipalName,
                    o.Department, o.JobTitle, o.Company,
                    dc.Name as Connection
                    FROM Objects o
                    LEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId
                    WHERE o.ObjectClass = 'user'
                      AND o.IsActive = 1
                      AND o.DeletedAt IS NULL
                      AND o.ManagerObjectId IS NULL
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 25,
                Tags = "identity,manager,compliance,orphan"
            },
            new Report
            {
                Name = "all_computers",
                DisplayName = "All Computers",
                Description = "Complete inventory of all computer objects with OS details",
                Category = "Security",
                SubCategory = "Inventory",
                Icon = "fa-server",
                QueryDefinition = @"SELECT o.DisplayName, o.CN, o.DN,
                    CASE WHEN o.IsActive = 1 THEN 'Active' ELSE 'Inactive' END as Status,
                    oa_os.AttributeValue as OperatingSystem,
                    oa_osv.AttributeValue as OSVersion,
                    oa_dns.AttributeValue as DNSHostName,
                    FORMAT(o.LastSyncedAt, 'yyyy-MM-dd') as LastSynced,
                    dc.Name as Connection
                    FROM Objects o
                    LEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId
                    LEFT JOIN ObjectAttributes oa_os ON oa_os.ObjectId = o.Id AND oa_os.AttributeName = 'operatingSystem'
                    LEFT JOIN ObjectAttributes oa_osv ON oa_osv.ObjectId = o.Id AND oa_osv.AttributeName = 'operatingSystemVersion'
                    LEFT JOIN ObjectAttributes oa_dns ON oa_dns.ObjectId = o.Id AND oa_dns.AttributeName = 'dNSHostName'
                    WHERE o.ObjectClass = 'computer'
                      AND o.DeletedAt IS NULL
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 26,
                Tags = "security,computer,inventory,os"
            },
            new Report
            {
                Name = "recently_created_accounts",
                DisplayName = "Recently Created Accounts (30 Days)",
                Description = "All objects first synced within the last 30 days",
                Category = "Identity",
                SubCategory = "Onboarding",
                Icon = "fa-user-plus",
                QueryDefinition = @"SELECT o.DisplayName, o.Username, o.ObjectClass,
                    FORMAT(o.FirstSyncedAt, 'yyyy-MM-dd HH:mm') as FirstSynced,
                    CASE WHEN o.IsActive = 1 THEN 'Active' ELSE 'Inactive' END as Status,
                    dc.Name as Connection
                    FROM Objects o
                    LEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId
                    WHERE o.DeletedAt IS NULL
                      AND o.FirstSyncedAt >= DATEADD(DAY, -30, GETUTCDATE())
                    ORDER BY o.FirstSyncedAt DESC",
                IsBuiltIn = true,
                SortOrder = 27,
                Tags = "identity,new,onboarding,recent"
            },
            new Report
            {
                Name = "sql_servers_from_spn",
                DisplayName = "SQL Servers (SPN Detection)",
                Description = "Computers with MSSQLSvc service principal names - SQL Server instances",
                Category = "Security",
                SubCategory = "SQL Servers",
                Icon = "fa-database",
                QueryDefinition = @"SELECT o.DisplayName, o.CN,
                    oa_dns.AttributeValue as DNSHostName,
                    oa_os.AttributeValue as OperatingSystem,
                    oa_spn.AttributeValue as ServicePrincipalName,
                    dc.Name as Connection
                    FROM Objects o
                    LEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId
                    LEFT JOIN ObjectAttributes oa_dns ON oa_dns.ObjectId = o.Id AND oa_dns.AttributeName = 'dNSHostName'
                    LEFT JOIN ObjectAttributes oa_os ON oa_os.ObjectId = o.Id AND oa_os.AttributeName = 'operatingSystem'
                    LEFT JOIN ObjectAttributes oa_spn ON oa_spn.ObjectId = o.Id AND oa_spn.AttributeName = 'servicePrincipalName'
                    WHERE o.ObjectClass = 'computer'
                      AND o.DeletedAt IS NULL
                      AND oa_spn.AttributeValue LIKE '%MSSQLSvc%'
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 28,
                Tags = "security,sql,server,database,spn"
            },
            new Report
            {
                Name = "users_without_email_objects",
                DisplayName = "Users Without Email (Objects)",
                Description = "Active user objects with no email address",
                Category = "Identity",
                SubCategory = "Compliance",
                Icon = "fa-envelope-open",
                QueryDefinition = @"SELECT o.DisplayName, o.Username, o.UserPrincipalName,
                    o.Department, o.JobTitle, o.Company,
                    dc.Name as Connection
                    FROM Objects o
                    LEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId
                    WHERE o.ObjectClass = 'user'
                      AND o.IsActive = 1
                      AND o.DeletedAt IS NULL
                      AND (o.Email IS NULL OR o.Email = '')
                    ORDER BY o.DisplayName",
                IsBuiltIn = true,
                SortOrder = 29,
                Tags = "identity,email,compliance,cleanup"
            }
        };
    }
}
