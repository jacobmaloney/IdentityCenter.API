using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based compliance policy seeding.
/// Seeds 27 production-ready compliance policies with rules, actions, and framework mappings.
/// Supports tiered dormancy escalation, governance, security, and lifecycle policies.
/// </summary>
public class DapperPolicySeedService : DapperSeedServiceBase
{
    // Fixed GUIDs for policies - allows consistent referencing and framework mapping
    public static readonly Guid DormantAccount45DayPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222201");
    public static readonly Guid DormantAccount90DayPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222215");
    public static readonly Guid DormantAccount180DayPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222216");
    public static readonly Guid DormantAccount365DayPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222217");
    public static readonly Guid OrphanedAccountPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222202");
    public static readonly Guid ExcessivePermissionsPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222203");
    public static readonly Guid PrivilegedAccessReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222204");
    public static readonly Guid NewHireAccessReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222205");
    public static readonly Guid TerminationProcessingPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222206");
    public static readonly Guid PasswordExpirationPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222207");
    public static readonly Guid HighRiskUserMonitoringPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222208");
    public static readonly Guid ManagerRequiredPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222209");
    public static readonly Guid FailedLoginMonitoringPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222210");
    public static readonly Guid ServiceAccountReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222211");
    public static readonly Guid ContractorAccessExpirationPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222212");
    public static readonly Guid MfaEnforcementPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222213");
    public static readonly Guid SeparationOfDutiesPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222214");
    public static readonly Guid StaleGroupDetectionPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222218");
    public static readonly Guid EmptyGroupCleanupPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222219");
    public static readonly Guid NestedGroupReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222220");
    public static readonly Guid LargeGroupReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222221");
    public static readonly Guid ExternalUserReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid GuestAccountExpirationPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222223");
    public static readonly Guid SharedAccountDetectionPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222224");
    public static readonly Guid PasswordNeverExpiresPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222225");
    public static readonly Guid AdminAccountCreepPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222226");
    public static readonly Guid StalePasswordPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222227");

    // Fixed GUIDs for per-policy standing campaigns
    public static readonly Guid ExcessivePermissionsCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333301");
    public static readonly Guid PrivilegedAccessReviewCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333302");
    public static readonly Guid NewHireAccessReviewCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333303");
    public static readonly Guid ServiceAccountReviewCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333304");
    public static readonly Guid SeparationOfDutiesCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333305");
    public static readonly Guid NestedGroupReviewCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333306");
    public static readonly Guid LargeGroupReviewCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333307");
    public static readonly Guid ExternalUserReviewCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333308");
    public static readonly Guid AdminAccountCreepCampaignId = Guid.Parse("33333333-3333-3333-3333-333333333309");

    public DapperPolicySeedService(
        IConfiguration configuration,
        ILogger<DapperPolicySeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if policies already exist
        var existingCount = await GetCountAsync(connection, transaction, "CompliancePolicies", "IsBuiltIn = 1");
        if (existingCount >= 27)
        {
            _logger.LogDebug("Policies already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var policies = GetDefaultPolicies();
        int policyCount = 0;
        int ruleCount = 0;
        int actionCount = 0;

        const string policyInsertSql = @"
            INSERT INTO CompliancePolicies (
                Id, Name, Description, Category, Severity, Priority, IsActive, IsBuiltIn,
                EvaluationFrequencyHours, ComplianceFramework, CreatedBy, CreatedAt
            )
            SELECT @Id, @Name, @Description, @Category, @Severity, @Priority, @IsActive, @IsBuiltIn,
                   @EvaluationFrequencyHours, @ComplianceFramework, @CreatedBy, @CreatedAt
            WHERE NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = @Id)";

        const string ruleInsertSql = @"
            INSERT INTO CompliancePolicyRule (
                Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator,
                ComparisonValue, DaysOffset, Weight, SortOrder, IsActive, CreatedAt
            )
            VALUES (
                @Id, @CompliancePolicyId, @Name, @Description, @RuleType, @FieldName, @Operator,
                @ComparisonValue, @DaysOffset, @Weight, @SortOrder, @IsActive, @CreatedAt
            )";

        const string actionInsertSql = @"
            INSERT INTO CompliancePolicyAction (
                Id, CompliancePolicyId, Name, Description, ActionType, ExecutionTiming,
                Priority, RequiresApproval, Configuration, IsActive, CreatedAt
            )
            VALUES (
                @Id, @CompliancePolicyId, @Name, @Description, @ActionType, @ExecutionTiming,
                @Priority, @RequiresApproval, @Configuration, @IsActive, @CreatedAt
            )";

        foreach (var policy in policies)
        {
            var rowsAffected = await InsertAsync(connection, transaction, policyInsertSql, policy.Policy);
            if (rowsAffected > 0)
            {
                policyCount++;

                // Insert rules
                foreach (var rule in policy.Rules)
                {
                    await InsertAsync(connection, transaction, ruleInsertSql, rule);
                    ruleCount++;
                }

                // Insert actions
                foreach (var action in policy.Actions)
                {
                    await InsertAsync(connection, transaction, actionInsertSql, action);
                    actionCount++;
                }
            }
        }

        // Seed framework-policy mappings
        await SeedFrameworkPolicyMappingsAsync(connection, transaction);

        // Seed per-policy standing campaigns
        await SeedPolicyCampaignsAsync(connection, transaction);

        sw.Stop();
        _logger.LogInformation(
            "CompliancePolicies seed complete: {PolicyCount} policies, {RuleCount} rules, {ActionCount} actions in {Duration:0.00}ms",
            policyCount, ruleCount, actionCount, sw.Elapsed.TotalMilliseconds);
    }

    private async Task SeedFrameworkPolicyMappingsAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var mappings = GetFrameworkPolicyMappings();

        const string insertSql = @"
            INSERT INTO ComplianceFrameworkPolicyMappings (Id, FrameworkId, CompliancePolicyId, CreatedAt)
            SELECT @Id, @FrameworkId, @CompliancePolicyId, @CreatedAt
            WHERE NOT EXISTS (
                SELECT 1 FROM ComplianceFrameworkPolicyMappings
                WHERE FrameworkId = @FrameworkId AND CompliancePolicyId = @CompliancePolicyId
            )";

        int created = 0;
        foreach (var mapping in mappings)
        {
            var rowsAffected = await InsertAsync(connection, transaction, insertSql, mapping);
            if (rowsAffected > 0) created++;
        }

        _logger.LogInformation("Framework-policy mappings: Created {Created}", created);
    }

    private async Task SeedPolicyCampaignsAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var now = DateTime.UtcNow;

        // Policies that have CreateAccessReview actions need standing campaigns
        var campaignMappings = new (Guid CampaignId, Guid PolicyId, string PolicyName)[]
        {
            (ExcessivePermissionsCampaignId, ExcessivePermissionsPolicyId, "Excessive Permissions Detection"),
            (PrivilegedAccessReviewCampaignId, PrivilegedAccessReviewPolicyId, "Privileged Access Review"),
            (NewHireAccessReviewCampaignId, NewHireAccessReviewPolicyId, "New Hire Access Review"),
            (ServiceAccountReviewCampaignId, ServiceAccountReviewPolicyId, "Service Account Review"),
            (SeparationOfDutiesCampaignId, SeparationOfDutiesPolicyId, "Separation of Duties"),
            (NestedGroupReviewCampaignId, NestedGroupReviewPolicyId, "Nested Group Review"),
            (LargeGroupReviewCampaignId, LargeGroupReviewPolicyId, "Large Group Review"),
            (ExternalUserReviewCampaignId, ExternalUserReviewPolicyId, "External User Review"),
            (AdminAccountCreepCampaignId, AdminAccountCreepPolicyId, "Admin Account Creep"),
        };

        const string insertSql = @"
            INSERT INTO Campaigns (
                Id, Name, Description, CampaignType, ReviewType, Status,
                StartDate, EndDate, DueDate, ReviewPeriodDays,
                CompletionPercentage, TotalAssignments, CompletedAssignments,
                AutoGenerated, IsRecurring, RecurrencePattern,
                EnableNotifications, ReminderDaysBefore,
                SourcePolicyId,
                OnDenialAction, AutoRemediateOnDenial, OnIncompleteAction, OnApprovalAction, ExtensionDays,
                CompletionActionsProcessed, CreatedBy, CreatedAt,
                PolicyViolationFilter, IncludeNestedMemberships, MaxNestedDepth
            )
            SELECT @Id, @Name, @Description, 'ComplianceReview', 'UserAccess', 'Active',
                @StartDate, @EndDate, @DueDate, 14,
                0, 0, 0,
                1, 1, 'Continuous',
                1, 3,
                @SourcePolicyId,
                'RemoveFromGroup', 1, 'None', 'Certify', 7,
                0, 'System', @CreatedAt,
                0, 0, 10
            WHERE NOT EXISTS (SELECT 1 FROM Campaigns WHERE Id = @Id)
              AND NOT EXISTS (SELECT 1 FROM Campaigns WHERE SourcePolicyId = @SourcePolicyId AND Status NOT IN ('Deleted', 'Archived'))";

        int created = 0;
        foreach (var (campaignId, policyId, policyName) in campaignMappings)
        {
            var campaignName = string.Concat(policyName, " - Standing Review");
            var rows = await InsertAsync(connection, transaction, insertSql, new
            {
                Id = campaignId,
                Name = campaignName,
                Description = string.Concat("Continuous campaign for ", policyName, " violation reviews. Cases are added automatically when violations are detected."),
                StartDate = now,
                EndDate = now.AddYears(1),
                DueDate = now.AddYears(1),
                SourcePolicyId = policyId,
                CreatedAt = now
            });
            if (rows > 0) created++;
        }

        _logger.LogInformation("Per-policy standing campaigns: Created {Created} of {Total}", created, campaignMappings.Length);
    }

    private static List<PolicyDefinition> GetDefaultPolicies()
    {
        var now = DateTime.UtcNow;
        var policies = new List<PolicyDefinition>();

        // Tiered Dormancy Escalation
        policies.Add(CreateDormantAccountPolicy(DormantAccount45DayPolicyId, "Dormant Account - 45 Day Warning",
            "Early warning for accounts inactive for 45+ days. Notifies user and manager.", 4, 8, 24, 45, 89, "SOX,ISO27001"));
        policies.Add(CreateDormantAccountPolicy(DormantAccount90DayPolicyId, "Dormant Account - 90 Day Review Required",
            "Accounts inactive for 90+ days require manager review.", 3, 10, 24, 90, 179, "SOX,HIPAA,ISO27001"));
        policies.Add(CreateDormantAccountPolicy(DormantAccount180DayPolicyId, "Dormant Account - 180 Day Auto-Disable",
            "Accounts inactive for 180+ days are automatically disabled.", 2, 12, 24, 180, 364, "SOX,HIPAA,PCI-DSS,ISO27001"));
        policies.Add(CreateDormantAccountPolicy(DormantAccount365DayPolicyId, "Dormant Account - 365 Day Archive/Delete",
            "Accounts inactive for 1+ year are scheduled for deletion.", 1, 14, 168, 365, null, "SOX,HIPAA,PCI-DSS,GDPR,ISO27001"));

        // Core Governance Policies
        policies.Add(CreatePolicy(OrphanedAccountPolicyId, "Orphaned Account Detection",
            "Identifies accounts without valid managers.", "Governance", 2, 15, 24, "SOX,HIPAA,GDPR",
            new RuleDef[] { new("No Manager Assigned", "ManagerHierarchy", "ManagerId", "IsNull", null, null, 1.0m, 1) },
            new ActionDef[] { new("Escalate to HR", "EscalateToManager", "{\"escalateTo\": \"HR\", \"urgency\": \"High\"}", 1) }));

        policies.Add(CreatePolicy(ExcessivePermissionsPolicyId, "Excessive Permissions Detection",
            "Identifies users with excessive permissions or high risk scores.", "Risk", 2, 20, 168, "SOX,PCI-DSS,ISO27001",
            new RuleDef[] {
                new("High Risk Score", "RiskThreshold", "RiskScore", "GreaterThan", "0.7", null, 1.0m, 1),
                new("Excessive Groups", "PermissionCount", "GroupMembershipCount", "GreaterThan", "20", null, 0.8m, 2)
            },
            new ActionDef[] { new("Create Access Review", "CreateAccessReview", "{\"reviewType\": \"PermissionReview\"}", 1) }));

        policies.Add(CreatePolicy(PrivilegedAccessReviewPolicyId, "Privileged Access Review",
            "Quarterly review of all privileged/admin accounts.", "Compliance", 1, 25, 2160, "SOX,PCI-DSS,NIST80053",
            new RuleDef[] { new("Admin Group Membership", "GroupMembership", "GroupMemberships", "Contains", "Admin,Administrators,Domain Admins", null, 1.0m, 1) },
            new ActionDef[] { new("Create Privileged Access Review", "CreateAccessReview", "{\"reviewType\": \"PrivilegedAccessReview\"}", 1) }));

        policies.Add(CreatePolicy(NewHireAccessReviewPolicyId, "New Hire Access Review",
            "30-day access review for newly provisioned accounts.", "Lifecycle", 3, 5, 24, "SOX,HIPAA,ISO27001",
            new RuleDef[] { new("Account Age 30 Days", "AccountAge", "CreatedAt", "Between", "29,31", 30, 1.0m, 1) },
            new ActionDef[] { new("Create New Hire Review", "CreateAccessReview", "{\"reviewType\": \"NewHireReview\"}", 1) }));

        policies.Add(CreatePolicy(TerminationProcessingPolicyId, "Termination Processing",
            "Automatically disables accounts flagged for termination.", "Lifecycle", 1, 30, 1, "SOX,HIPAA,PCI-DSS,GDPR,ISO27001,NIST80053",
            new RuleDef[] { new("Termination Flag", "AccountStatus", "IsTerminated", "Equals", "true", null, 1.0m, 1) },
            new ActionDef[] { new("Disable Account", "DisableAccount", null, 1), new("Revoke Access", "RemovePermissions", "{\"scope\": \"all\"}", 2) }));

        policies.Add(CreatePolicy(PasswordExpirationPolicyId, "Password Expiration Policy",
            "Monitors and enforces password expiration policies.", "Security", 3, 10, 24, "PCI-DSS,ISO27001,NIST80053",
            new RuleDef[] { new("Password Expired", "PasswordAge", "PasswordLastSet", "OlderThan", null, 90, 1.0m, 1) },
            new ActionDef[] { new("Send Expiration Notice", "SendNotification", "{\"template\": \"PasswordExpiration\"}", 1) }));

        policies.Add(CreatePolicy(HighRiskUserMonitoringPolicyId, "High Risk User Monitoring",
            "Continuous monitoring of users flagged as high-risk.", "Risk", 1, 28, 4, "SOX,PCI-DSS,ISO27001",
            new RuleDef[] { new("High Risk Score", "RiskThreshold", "RiskScore", "GreaterThan", "0.85", null, 1.0m, 1) },
            new ActionDef[] { new("Alert Security Team", "SendNotification", "{\"template\": \"HighRiskAlert\", \"recipients\": [\"security-team\"]}", 1) }));

        // Security and Compliance Policies
        policies.Add(CreatePolicy(ManagerRequiredPolicyId, "Manager Required Policy",
            "All active accounts must have an assigned manager.", "Governance", 3, 8, 24, "SOX,HIPAA",
            new RuleDef[] { new("No Manager", "ManagerHierarchy", "ManagerId", "IsNull", null, null, 1.0m, 1) },
            new ActionDef[] { new("Assign to HR", "EscalateToManager", "{\"escalateTo\": \"HR\"}", 1) }));

        policies.Add(CreatePolicy(FailedLoginMonitoringPolicyId, "Failed Login Monitoring",
            "Detects accounts with excessive failed login attempts.", "Security", 2, 22, 1, "PCI-DSS,NIST80053",
            new RuleDef[] { new("Failed Logins Threshold", "LoginActivity", "FailedLoginCount24h", "GreaterThan", "10", null, 1.0m, 1) },
            new ActionDef[] { new("Alert Security", "SendNotification", "{\"template\": \"FailedLoginAlert\"}", 1) }));

        policies.Add(CreatePolicy(ServiceAccountReviewPolicyId, "Service Account Review",
            "Quarterly review of all service accounts.", "Compliance", 2, 18, 2160, "SOX,PCI-DSS,ISO27001",
            new RuleDef[] { new("Is Service Account", "AccountType", "AccountType", "Equals", "Service", null, 1.0m, 1) },
            new ActionDef[] { new("Create Service Account Review", "CreateAccessReview", "{\"reviewType\": \"ServiceAccountReview\"}", 1) }));

        policies.Add(CreatePolicy(ContractorAccessExpirationPolicyId, "Contractor Access Expiration",
            "Monitors and enforces contractor access end dates.", "Lifecycle", 2, 16, 24, "SOX,HIPAA,GDPR",
            new RuleDef[] { new("Contract Ending", "ContractDate", "ContractEndDate", "WithinDays", "30", 30, 1.0m, 1) },
            new ActionDef[] { new("Notify Manager", "SendNotification", "{\"template\": \"ContractorExpiration\"}", 1) }));

        policies.Add(CreatePolicy(MfaEnforcementPolicyId, "MFA Enforcement Policy",
            "Identifies accounts without MFA enabled.", "Security", 2, 24, 24, "PCI-DSS,NIST80053,ISO27001",
            new RuleDef[] { new("MFA Not Enabled", "SecuritySetting", "MfaEnabled", "Equals", "false", null, 1.0m, 1) },
            new ActionDef[] { new("Notify User", "SendNotification", "{\"template\": \"MfaRequired\"}", 1) }));

        policies.Add(CreatePolicy(SeparationOfDutiesPolicyId, "Separation of Duties",
            "Detects toxic access combinations violating SoD rules.", "Compliance", 1, 26, 24, "SOX,PCI-DSS",
            new RuleDef[] { new("SoD Violation", "SodCheck", "AccessCombinations", "HasViolation", null, null, 1.0m, 1) },
            new ActionDef[] { new("Create SoD Review", "CreateAccessReview", "{\"reviewType\": \"SodReview\"}", 1) }));

        // Group Management Policies
        policies.Add(CreatePolicy(StaleGroupDetectionPolicyId, "Stale Group Detection",
            "Identifies groups with no activity or updates.", "Governance", 4, 6, 168, "ISO27001",
            new RuleDef[] { new("No Recent Changes", "GroupActivity", "LastModifiedDate", "OlderThan", null, 180, 1.0m, 1) },
            new ActionDef[] { new("Notify Group Owner", "SendNotification", "{\"template\": \"StaleGroupAlert\"}", 1) }));

        policies.Add(CreatePolicy(EmptyGroupCleanupPolicyId, "Empty Group Cleanup",
            "Identifies groups with no members for cleanup.", "Governance", 4, 4, 168, "ISO27001",
            new RuleDef[] { new("No Members", "GroupMembership", "MemberCount", "Equals", "0", null, 1.0m, 1) },
            new ActionDef[] { new("Schedule Deletion", "ScheduleDeletion", "{\"gracePeriodDays\": 30}", 1) }));

        policies.Add(CreatePolicy(NestedGroupReviewPolicyId, "Nested Group Review",
            "Reviews groups with excessive nesting levels.", "Security", 3, 7, 168, "ISO27001,NIST80053",
            new RuleDef[] { new("Deep Nesting", "GroupStructure", "NestingLevel", "GreaterThan", "3", null, 1.0m, 1) },
            new ActionDef[] { new("Create Structure Review", "CreateAccessReview", "{\"reviewType\": \"GroupStructureReview\"}", 1) }));

        policies.Add(CreatePolicy(LargeGroupReviewPolicyId, "Large Group Review",
            "Reviews groups with large member counts.", "Governance", 3, 9, 168, "SOX,ISO27001",
            new RuleDef[] { new("Large Group", "GroupMembership", "MemberCount", "GreaterThan", "100", null, 1.0m, 1) },
            new ActionDef[] { new("Create Membership Review", "CreateAccessReview", "{\"reviewType\": \"LargeGroupReview\"}", 1) }));

        // External Access Policies
        policies.Add(CreatePolicy(ExternalUserReviewPolicyId, "External User Review",
            "Quarterly review of all external/guest users.", "Compliance", 2, 17, 2160, "GDPR,ISO27001",
            new RuleDef[] { new("Is External", "AccountType", "UserType", "Equals", "Guest", null, 1.0m, 1) },
            new ActionDef[] { new("Create External User Review", "CreateAccessReview", "{\"reviewType\": \"ExternalUserReview\"}", 1) }));

        policies.Add(CreatePolicy(GuestAccountExpirationPolicyId, "Guest Account Expiration",
            "Monitors and enforces guest account expiration.", "Lifecycle", 2, 13, 24, "GDPR,ISO27001",
            new RuleDef[] { new("Guest Expired", "AccountDate", "ExpirationDate", "Passed", null, null, 1.0m, 1) },
            new ActionDef[] { new("Disable Account", "DisableAccount", null, 1) }));

        // Account Security Policies
        policies.Add(CreatePolicy(SharedAccountDetectionPolicyId, "Shared Account Detection",
            "Identifies potentially shared accounts.", "Security", 2, 19, 168, "PCI-DSS,ISO27001",
            new RuleDef[] { new("Multiple Locations", "LoginActivity", "UniqueLocations24h", "GreaterThan", "3", null, 1.0m, 1) },
            new ActionDef[] { new("Alert Security", "SendNotification", "{\"template\": \"SharedAccountAlert\"}", 1) }));

        policies.Add(CreatePolicy(PasswordNeverExpiresPolicyId, "Password Never Expires",
            "Identifies accounts with non-expiring passwords.", "Security", 3, 11, 168, "PCI-DSS,NIST80053",
            new RuleDef[] { new("Password Never Expires", "PasswordPolicy", "PasswordNeverExpires", "Equals", "true", null, 1.0m, 1) },
            new ActionDef[] { new("Notify IT Admin", "SendNotification", "{\"template\": \"PasswordPolicyViolation\"}", 1) }));

        policies.Add(CreatePolicy(AdminAccountCreepPolicyId, "Admin Account Creep",
            "Detects gradual accumulation of admin privileges.", "Risk", 2, 21, 168, "SOX,PCI-DSS",
            new RuleDef[] { new("Admin Count Increase", "PermissionTrend", "AdminGroupCount", "IncreaseRate", "20", null, 1.0m, 1) },
            new ActionDef[] { new("Create Privilege Review", "CreateAccessReview", "{\"reviewType\": \"PrivilegeCreepReview\"}", 1) }));

        policies.Add(CreatePolicy(StalePasswordPolicyId, "Stale Password Detection",
            "Identifies accounts with passwords not changed in 1+ year.", "Security", 3, 12, 168, "PCI-DSS,NIST80053",
            new RuleDef[] { new("Password Age", "PasswordAge", "PasswordLastSet", "OlderThan", null, 365, 1.0m, 1) },
            new ActionDef[] { new("Force Password Change", "SendNotification", "{\"template\": \"ForcePasswordChange\"}", 1) }));

        return policies;
    }

    private static PolicyDefinition CreateDormantAccountPolicy(Guid id, string name, string description,
        int severity, int priority, int evalFrequency, int minDays, int? maxDays, string frameworks)
    {
        var now = DateTime.UtcNow;
        var policy = new
        {
            Id = id,
            Name = name,
            Description = description,
            Category = "Lifecycle",
            Severity = severity,
            Priority = priority,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = evalFrequency,
            ComplianceFramework = frameworks,
            CreatedBy = "System",
            CreatedAt = now
        };

        var ruleValue = maxDays.HasValue ? $"{minDays},{maxDays}" : null;
        var rule = new
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = id,
            Name = $"{minDays}-Day Inactivity Check",
            Description = $"Flags accounts with no login activity for {minDays}+ days",
            RuleType = "LoginDormancy",
            FieldName = "LastSignInDate",
            Operator = maxDays.HasValue ? "Between" : "OlderThan",
            ComparisonValue = ruleValue,
            DaysOffset = minDays,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true,
            CreatedAt = now
        };

        var action = new
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = id,
            Name = severity <= 2 ? "Disable Account" : "Send Warning",
            Description = severity <= 2 ? "Disables the dormant account" : "Sends warning notification",
            ActionType = severity <= 2 ? "DisableAccount" : "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = severity == 1,
            Configuration = severity > 2 ? $"{{\"template\": \"DormantWarning{minDays}Day\"}}" : null,
            IsActive = true,
            CreatedAt = now
        };

        return new PolicyDefinition { Policy = policy, Rules = new[] { rule }, Actions = new[] { action } };
    }

    private static PolicyDefinition CreatePolicy(Guid id, string name, string description, string category,
        int severity, int priority, int evalFrequency, string frameworks,
        RuleDef[] rules,
        ActionDef[] actions)
    {
        var now = DateTime.UtcNow;
        var policy = new
        {
            Id = id,
            Name = name,
            Description = description,
            Category = category,
            Severity = severity,
            Priority = priority,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = evalFrequency,
            ComplianceFramework = frameworks,
            CreatedBy = "System",
            CreatedAt = now
        };

        var ruleList = rules.Select(r => new
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = id,
            Name = r.Name,
            Description = (string?)null,
            RuleType = r.RuleType,
            FieldName = r.FieldName,
            Operator = r.Op,
            ComparisonValue = r.Value,
            DaysOffset = r.Days,
            Weight = r.Weight,
            SortOrder = r.Order,
            IsActive = true,
            CreatedAt = now
        }).ToArray();

        var actionList = actions.Select(a => new
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = id,
            Name = a.Name,
            Description = (string?)null,
            ActionType = a.ActionType,
            ExecutionTiming = "Immediate",
            Priority = a.Priority,
            RequiresApproval = false,
            Configuration = a.Config,
            IsActive = true,
            CreatedAt = now
        }).ToArray();

        return new PolicyDefinition { Policy = policy, Rules = ruleList, Actions = actionList };
    }

    private static List<object> GetFrameworkPolicyMappings()
    {
        var now = DateTime.UtcNow;
        var mappings = new List<object>();

        // SOX mappings
        var soxPolicies = new[] { DormantAccount45DayPolicyId, DormantAccount90DayPolicyId, DormantAccount180DayPolicyId,
            DormantAccount365DayPolicyId, OrphanedAccountPolicyId, ExcessivePermissionsPolicyId, PrivilegedAccessReviewPolicyId,
            NewHireAccessReviewPolicyId, TerminationProcessingPolicyId, ManagerRequiredPolicyId, SeparationOfDutiesPolicyId,
            LargeGroupReviewPolicyId, AdminAccountCreepPolicyId };
        foreach (var policyId in soxPolicies)
            mappings.Add(new { Id = Guid.NewGuid(), FrameworkId = DapperFrameworkSeedService.SoxFrameworkId, CompliancePolicyId = policyId, CreatedAt = now });

        // HIPAA mappings
        var hipaaPolicies = new[] { DormantAccount90DayPolicyId, DormantAccount180DayPolicyId, DormantAccount365DayPolicyId,
            OrphanedAccountPolicyId, NewHireAccessReviewPolicyId, TerminationProcessingPolicyId, ManagerRequiredPolicyId, ContractorAccessExpirationPolicyId };
        foreach (var policyId in hipaaPolicies)
            mappings.Add(new { Id = Guid.NewGuid(), FrameworkId = DapperFrameworkSeedService.HipaaFrameworkId, CompliancePolicyId = policyId, CreatedAt = now });

        // PCI-DSS mappings
        var pciPolicies = new[] { DormantAccount180DayPolicyId, DormantAccount365DayPolicyId, ExcessivePermissionsPolicyId,
            PrivilegedAccessReviewPolicyId, TerminationProcessingPolicyId, PasswordExpirationPolicyId, HighRiskUserMonitoringPolicyId,
            FailedLoginMonitoringPolicyId, ServiceAccountReviewPolicyId, MfaEnforcementPolicyId, SeparationOfDutiesPolicyId,
            SharedAccountDetectionPolicyId, PasswordNeverExpiresPolicyId, AdminAccountCreepPolicyId, StalePasswordPolicyId };
        foreach (var policyId in pciPolicies)
            mappings.Add(new { Id = Guid.NewGuid(), FrameworkId = DapperFrameworkSeedService.PciDssFrameworkId, CompliancePolicyId = policyId, CreatedAt = now });

        // GDPR mappings
        var gdprPolicies = new[] { DormantAccount365DayPolicyId, OrphanedAccountPolicyId, TerminationProcessingPolicyId,
            ContractorAccessExpirationPolicyId, ExternalUserReviewPolicyId, GuestAccountExpirationPolicyId };
        foreach (var policyId in gdprPolicies)
            mappings.Add(new { Id = Guid.NewGuid(), FrameworkId = DapperFrameworkSeedService.GdprFrameworkId, CompliancePolicyId = policyId, CreatedAt = now });

        // ISO 27001 mappings
        var isoPolicies = new[] { DormantAccount45DayPolicyId, DormantAccount90DayPolicyId, DormantAccount180DayPolicyId,
            DormantAccount365DayPolicyId, ExcessivePermissionsPolicyId, NewHireAccessReviewPolicyId, PasswordExpirationPolicyId,
            HighRiskUserMonitoringPolicyId, ServiceAccountReviewPolicyId, MfaEnforcementPolicyId, StaleGroupDetectionPolicyId,
            EmptyGroupCleanupPolicyId, NestedGroupReviewPolicyId, LargeGroupReviewPolicyId, ExternalUserReviewPolicyId,
            GuestAccountExpirationPolicyId, SharedAccountDetectionPolicyId };
        foreach (var policyId in isoPolicies)
            mappings.Add(new { Id = Guid.NewGuid(), FrameworkId = DapperFrameworkSeedService.Iso27001FrameworkId, CompliancePolicyId = policyId, CreatedAt = now });

        // NIST 800-53 mappings
        var nistPolicies = new[] { PrivilegedAccessReviewPolicyId, TerminationProcessingPolicyId, PasswordExpirationPolicyId,
            FailedLoginMonitoringPolicyId, MfaEnforcementPolicyId, NestedGroupReviewPolicyId, PasswordNeverExpiresPolicyId, StalePasswordPolicyId };
        foreach (var policyId in nistPolicies)
            mappings.Add(new { Id = Guid.NewGuid(), FrameworkId = DapperFrameworkSeedService.Nist80053FrameworkId, CompliancePolicyId = policyId, CreatedAt = now });

        return mappings;
    }

    private class PolicyDefinition
    {
        public object Policy { get; set; } = null!;
        public object[] Rules { get; set; } = Array.Empty<object>();
        public object[] Actions { get; set; } = Array.Empty<object>();
    }

    /// <summary>Rule definition helper record for type-safe policy creation.</summary>
    private record RuleDef(string Name, string RuleType, string FieldName, string Op, string? Value, int? Days, decimal Weight, int Order);

    /// <summary>Action definition helper record for type-safe policy creation.</summary>
    private record ActionDef(string Name, string ActionType, string? Config, int Priority);
}
