using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default compliance policies with rules, actions, and framework mappings
/// When a framework is applied, all its associated policies become active!
/// This is the missing piece that makes compliance automation actually work!
/// </summary>
public class DefaultPoliciesSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<DefaultPoliciesSeedService> _logger;

    // Fixed GUIDs for policies - allows consistent referencing
    // Tiered Dormancy Escalation Policies (45/90/180/365 days)
    public static readonly Guid DormantAccount45DayPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222201");
    public static readonly Guid DormantAccount90DayPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222215");
    public static readonly Guid DormantAccount180DayPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222216");
    public static readonly Guid DormantAccount365DayPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222217");

    // Core Governance Policies
    public static readonly Guid OrphanedAccountPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222202");
    public static readonly Guid ExcessivePermissionsPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222203");
    public static readonly Guid PrivilegedAccessReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222204");
    public static readonly Guid NewHireAccessReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222205");
    public static readonly Guid TerminationProcessingPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222206");
    public static readonly Guid PasswordExpirationPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222207");
    public static readonly Guid HighRiskUserMonitoringPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222208");

    // Security and Compliance Policies
    public static readonly Guid ManagerRequiredPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222209");
    public static readonly Guid FailedLoginMonitoringPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222210");
    public static readonly Guid ServiceAccountReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222211");
    public static readonly Guid ContractorAccessExpirationPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222212");
    public static readonly Guid MfaEnforcementPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222213");
    public static readonly Guid SeparationOfDutiesPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222214");

    // Group Management Policies
    public static readonly Guid StaleGroupDetectionPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222218");
    public static readonly Guid EmptyGroupCleanupPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222219");
    public static readonly Guid NestedGroupReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222220");
    public static readonly Guid LargeGroupReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222221");

    // External Access Policies
    public static readonly Guid ExternalUserReviewPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid GuestAccountExpirationPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222223");

    // Account Security Policies
    public static readonly Guid SharedAccountDetectionPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222224");
    public static readonly Guid PasswordNeverExpiresPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222225");
    public static readonly Guid AdminAccountCreepPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222226");
    public static readonly Guid StalePasswordPolicyId = Guid.Parse("22222222-2222-2222-2222-222222222227");

    public DefaultPoliciesSeedService(
        IConfiguration configuration,
        ILogger<DefaultPoliciesSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds 8 production-ready compliance policies with rules, actions, and framework mappings
    /// </summary>
    public async Task SeedDefaultPoliciesAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Quick check - if any built-in policies exist, we've already seeded
        var existingPolicyCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM CompliancePolicies WHERE IsBuiltIn = 1");

        if (existingPolicyCount >= 27) // We have 27 built-in policies
        {
            _logger.LogDebug("Compliance policies already seeded ({Count} built-in policies found), skipping", existingPolicyCount);
            return;
        }

        _logger.LogInformation("Seeding compliance policies - the engine that powers automated governance!");

        int created = 0;
        int skipped = 0;

        // Create all policies
        var policies = GetDefaultPolicies();

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var policy in policies)
            {
                var existingPolicy = await connection.QueryFirstOrDefaultAsync<CompliancePolicy>(
                    "SELECT * FROM CompliancePolicies WHERE Id = @Id",
                    new { policy.Id },
                    transaction);

                if (existingPolicy != null)
                {
                    // Policy exists - check if it's missing rules
                    var existingRuleCount = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM CompliancePolicyRules WHERE CompliancePolicyId = @PolicyId",
                        new { PolicyId = policy.Id },
                        transaction);

                    if (existingRuleCount == 0 && policy.Rules.Any())
                    {
                        // Add missing rules to existing policy
                        foreach (var rule in policy.Rules)
                        {
                            rule.CompliancePolicyId = existingPolicy.Id;
                            await InsertPolicyRuleAsync(connection, transaction, rule);
                        }
                        _logger.LogInformation("Added {RuleCount} missing rules to existing policy '{PolicyName}'",
                            policy.Rules.Count, policy.Name);
                    }
                    else
                    {
                        _logger.LogDebug("Policy '{PolicyName}' already exists with {RuleCount} rules, skipping", policy.Name, existingRuleCount);
                    }
                    skipped++;
                    continue;
                }

                // Insert the policy
                await InsertPolicyAsync(connection, transaction, policy);

                // Insert rules
                foreach (var rule in policy.Rules)
                {
                    await InsertPolicyRuleAsync(connection, transaction, rule);
                }

                // Insert actions
                foreach (var action in policy.Actions)
                {
                    await InsertPolicyActionAsync(connection, transaction, action);
                }

                _logger.LogDebug("Created policy '{PolicyName}' with {RuleCount} rules and {ActionCount} actions",
                    policy.Name, policy.Rules.Count, policy.Actions.Count);
                created++;
            }

            await transaction.CommitAsync();

            // Now seed framework-policy mappings
            await SeedFrameworkPolicyMappingsAsync();

            _logger.LogInformation("Compliance policies seeding complete! Created: {Created}, Skipped: {Skipped}", created, skipped);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task InsertPolicyAsync(SqlConnection connection, System.Data.Common.DbTransaction transaction, CompliancePolicy policy)
    {
        const string sql = @"
            INSERT INTO CompliancePolicies
                (Id, Name, Description, Category, Severity, Priority, IsActive, IsBuiltIn,
                 EvaluationFrequencyHours, ComplianceFramework, CreatedBy, CreatedAt)
            VALUES
                (@Id, @Name, @Description, @Category, @Severity, @Priority, @IsActive, @IsBuiltIn,
                 @EvaluationFrequencyHours, @ComplianceFramework, @CreatedBy, @CreatedAt)";

        await connection.ExecuteAsync(sql, new
        {
            policy.Id,
            policy.Name,
            policy.Description,
            policy.Category,
            policy.Severity,
            policy.Priority,
            policy.IsActive,
            policy.IsBuiltIn,
            policy.EvaluationFrequencyHours,
            policy.ComplianceFramework,
            policy.CreatedBy,
            CreatedAt = DateTime.UtcNow
        }, transaction);
    }

    private async Task InsertPolicyRuleAsync(SqlConnection connection, System.Data.Common.DbTransaction transaction, CompliancePolicyRule rule)
    {
        const string sql = @"
            INSERT INTO CompliancePolicyRules
                (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator,
                 ComparisonValue, DaysOffset, Weight, SortOrder, IsActive)
            VALUES
                (@Id, @CompliancePolicyId, @Name, @Description, @RuleType, @FieldName, @Operator,
                 @ComparisonValue, @DaysOffset, @Weight, @SortOrder, @IsActive)";

        await connection.ExecuteAsync(sql, rule, transaction);
    }

    private async Task InsertPolicyActionAsync(SqlConnection connection, System.Data.Common.DbTransaction transaction, CompliancePolicyAction action)
    {
        const string sql = @"
            INSERT INTO CompliancePolicyActions
                (Id, CompliancePolicyId, Name, Description, ActionType, ExecutionTiming,
                 Priority, RequiresApproval, Configuration, IsActive)
            VALUES
                (@Id, @CompliancePolicyId, @Name, @Description, @ActionType, @ExecutionTiming,
                 @Priority, @RequiresApproval, @Configuration, @IsActive)";

        await connection.ExecuteAsync(sql, action, transaction);
    }

    /// <summary>
    /// Seeds the mappings between frameworks and policies
    /// This is what makes "Apply Framework" work - it activates all associated policies!
    /// </summary>
    private async Task SeedFrameworkPolicyMappingsAsync()
    {
        _logger.LogInformation("Seeding framework-policy mappings...");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var mappings = GetFrameworkPolicyMappings();
        int created = 0;
        int skipped = 0;

        const string checkSql = @"
            SELECT COUNT(*) FROM ComplianceFrameworkPolicyMappings
            WHERE FrameworkId = @FrameworkId AND CompliancePolicyId = @CompliancePolicyId";

        const string insertSql = @"
            INSERT INTO ComplianceFrameworkPolicyMappings
                (Id, FrameworkId, CompliancePolicyId, RequirementId, RequirementDescription,
                 ComplianceStatus, CoveragePercentage)
            VALUES
                (@Id, @FrameworkId, @CompliancePolicyId, @RequirementId, @RequirementDescription,
                 @ComplianceStatus, @CoveragePercentage)";

        foreach (var mapping in mappings)
        {
            var existingCount = await connection.ExecuteScalarAsync<int>(checkSql, new
            {
                mapping.FrameworkId,
                mapping.CompliancePolicyId
            });

            if (existingCount > 0)
            {
                skipped++;
                continue;
            }

            await connection.ExecuteAsync(insertSql, mapping);
            created++;
        }

        _logger.LogInformation("Framework-policy mappings: Created {Created}, Skipped {Skipped}", created, skipped);
    }

    private List<CompliancePolicy> GetDefaultPolicies()
    {
        return new List<CompliancePolicy>
        {
            // === TIERED DORMANCY ESCALATION (4 levels) ===
            CreateDormantAccount45DayPolicy(),
            CreateDormantAccount90DayPolicy(),
            CreateDormantAccount180DayPolicy(),
            CreateDormantAccount365DayPolicy(),

            // === CORE GOVERNANCE POLICIES ===
            CreateOrphanedAccountPolicy(),
            CreateExcessivePermissionsPolicy(),
            CreatePrivilegedAccessReviewPolicy(),
            CreateNewHireAccessReviewPolicy(),
            CreateTerminationProcessingPolicy(),
            CreatePasswordExpirationPolicy(),
            CreateHighRiskUserMonitoringPolicy(),

            // === SECURITY AND COMPLIANCE POLICIES ===
            CreateManagerRequiredPolicy(),
            CreateFailedLoginMonitoringPolicy(),
            CreateServiceAccountReviewPolicy(),
            CreateContractorAccessExpirationPolicy(),
            CreateMfaEnforcementPolicy(),
            CreateSeparationOfDutiesPolicy(),

            // === GROUP MANAGEMENT POLICIES ===
            CreateStaleGroupDetectionPolicy(),
            CreateEmptyGroupCleanupPolicy(),
            CreateNestedGroupReviewPolicy(),
            CreateLargeGroupReviewPolicy(),

            // === EXTERNAL ACCESS POLICIES ===
            CreateExternalUserReviewPolicy(),
            CreateGuestAccountExpirationPolicy(),

            // === ACCOUNT SECURITY POLICIES ===
            CreateSharedAccountDetectionPolicy(),
            CreatePasswordNeverExpiresPolicy(),
            CreateAdminAccountCreepPolicy(),
            CreateStalePasswordPolicy()
        };
    }

    // === TIERED DORMANCY ESCALATION POLICIES ===

    private CompliancePolicy CreateDormantAccount45DayPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = DormantAccount45DayPolicyId,
            Name = "Dormant Account - 45 Day Warning",
            Description = "Early warning for accounts inactive for 45+ days. Notifies user and manager to verify account is still needed. First tier of dormancy escalation.",
            Category = "Lifecycle",
            Severity = 4,
            Priority = 8,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "45-Day Inactivity Check",
            Description = "Flags accounts with no login activity for 45-89 days",
            RuleType = "LoginDormancy",
            FieldName = "LastSignInDate",
            Operator = "Between",
            ComparisonValue = "45,89",
            DaysOffset = 45,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Send Warning Email",
            Description = "Sends warning notification to user and manager",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"DormantWarning45Day\", \"recipients\": [\"user\", \"manager\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateDormantAccount90DayPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = DormantAccount90DayPolicyId,
            Name = "Dormant Account - 90 Day Review Required",
            Description = "Accounts inactive for 90+ days require manager review. Creates access certification and escalates if not responded within 14 days.",
            Category = "Lifecycle",
            Severity = 3,
            Priority = 10,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,HIPAA,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "90-Day Inactivity Check",
            Description = "Flags accounts with no login activity for 90-179 days",
            RuleType = "LoginDormancy",
            FieldName = "LastSignInDate",
            Operator = "Between",
            ComparisonValue = "90,179",
            DaysOffset = 90,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Access Review",
            Description = "Creates access review for manager certification",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"DormantAccountReview\", \"dueInDays\": 14}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Manager",
            Description = "Sends notification to manager about dormant account requiring review",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"DormantReviewRequired\", \"recipients\": [\"manager\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateDormantAccount180DayPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = DormantAccount180DayPolicyId,
            Name = "Dormant Account - 180 Day Auto-Disable",
            Description = "Accounts inactive for 180+ days are automatically disabled. Account can be re-enabled by IT upon manager request.",
            Category = "Lifecycle",
            Severity = 2,
            Priority = 12,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,HIPAA,PCI-DSS,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "180-Day Inactivity Check",
            Description = "Flags accounts with no login activity for 180-364 days",
            RuleType = "LoginDormancy",
            FieldName = "LastSignInDate",
            Operator = "Between",
            ComparisonValue = "180,364",
            DaysOffset = 180,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Account Still Enabled",
            Description = "Only process if account is currently enabled",
            RuleType = "AccountStatus",
            FieldName = "IsEnabled",
            Operator = "Equals",
            ComparisonValue = "true",
            Weight = 0.5m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Disable Account",
            Description = "Automatically disables the dormant account",
            ActionType = "DisableAccount",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify IT and Manager",
            Description = "Notifies IT team and manager that account was auto-disabled",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"AccountAutoDisabled\", \"recipients\": [\"it-team\", \"manager\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Log Compliance Action",
            Description = "Creates audit trail for compliance reporting",
            ActionType = "LogViolation",
            ExecutionTiming = "Immediate",
            Priority = 3,
            RequiresApproval = false,
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateDormantAccount365DayPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = DormantAccount365DayPolicyId,
            Name = "Dormant Account - 365 Day Archive/Delete",
            Description = "Accounts inactive for 1+ year are scheduled for deletion/archival. Requires IT director approval before permanent action.",
            Category = "Lifecycle",
            Severity = 1,
            Priority = 14,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 168,
            ComplianceFramework = "SOX,HIPAA,PCI-DSS,GDPR,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "365-Day Inactivity Check",
            Description = "Flags accounts with no login activity for 365+ days",
            RuleType = "LoginDormancy",
            FieldName = "LastSignInDate",
            Operator = "OlderThan",
            DaysOffset = 365,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Schedule Account Deletion",
            Description = "Schedules account for deletion with 30-day grace period",
            ActionType = "ScheduleDeletion",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = true,
            Configuration = "{\"gracePeriodDays\": 30, \"approverRole\": \"IT-Director\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify All Stakeholders",
            Description = "Notifies IT, HR, manager, and compliance team of pending deletion",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"AccountDeletionScheduled\", \"recipients\": [\"it-director\", \"hr\", \"manager\", \"compliance-team\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create GDPR Record",
            Description = "Creates data retention record for GDPR compliance",
            ActionType = "LogViolation",
            ExecutionTiming = "Immediate",
            Priority = 3,
            RequiresApproval = false,
            Configuration = "{\"category\": \"DataRetention\", \"retentionYears\": 7}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateOrphanedAccountPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = OrphanedAccountPolicyId,
            Name = "Orphaned Account Detection",
            Description = "Identifies accounts without valid managers or with terminated/disabled managers.",
            Category = "Governance",
            Severity = 2,
            Priority = 15,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,HIPAA,GDPR",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "No Manager Assigned",
            Description = "Detects accounts with no manager relationship",
            RuleType = "ManagerHierarchy",
            FieldName = "ManagerId",
            Operator = "IsNull",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Manager Is Disabled",
            Description = "Detects accounts where the assigned manager is disabled",
            RuleType = "ManagerHierarchy",
            FieldName = "Manager.IsEnabled",
            Operator = "Equals",
            ComparisonValue = "false",
            Weight = 0.8m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Escalate to HR",
            Description = "Escalates orphaned accounts to HR for manager reassignment",
            ActionType = "EscalateToManager",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"escalateTo\": \"HR\", \"urgency\": \"High\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Log Violation",
            Description = "Creates detailed audit log entry for compliance reporting",
            ActionType = "LogViolation",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateExcessivePermissionsPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = ExcessivePermissionsPolicyId,
            Name = "Excessive Permissions Detection",
            Description = "Identifies users with permission counts or risk scores exceeding organizational thresholds.",
            Category = "Risk",
            Severity = 2,
            Priority = 20,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 168,
            ComplianceFramework = "SOX,PCI-DSS,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "High Risk Score",
            Description = "Flags users with risk score above 0.7 threshold",
            RuleType = "RiskThreshold",
            FieldName = "RiskScore",
            Operator = "GreaterThan",
            ComparisonValue = "0.7",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Excessive Group Memberships",
            Description = "Flags users with more than 20 group memberships",
            RuleType = "PermissionCount",
            FieldName = "GroupMembershipCount",
            Operator = "GreaterThan",
            ComparisonValue = "20",
            Weight = 0.8m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Access Review",
            Description = "Creates targeted access review for permission validation",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"PermissionReview\", \"urgency\": \"High\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Security Team",
            Description = "Alerts security team of high-risk permission accumulation",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"ExcessivePermissionsAlert\", \"recipients\": [\"security-team\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreatePrivilegedAccessReviewPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = PrivilegedAccessReviewPolicyId,
            Name = "Privileged Access Review",
            Description = "Mandates quarterly review of all privileged/admin accounts.",
            Category = "Compliance",
            Severity = 1,
            Priority = 25,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 2160,
            ComplianceFramework = "SOX,PCI-DSS,NIST80053",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Admin Group Membership",
            Description = "Identifies users in privileged/admin groups",
            RuleType = "GroupMembership",
            FieldName = "GroupMemberships",
            Operator = "Contains",
            ComparisonValue = "Admin,Administrators,Domain Admins,Enterprise Admins",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Last Review Age",
            Description = "Flags privileged accounts not reviewed in 90+ days",
            RuleType = "ReviewAge",
            FieldName = "LastAccessReviewDate",
            Operator = "OlderThan",
            DaysOffset = 90,
            Weight = 0.9m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Privileged Access Review",
            Description = "Initiates mandatory quarterly review campaign for privileged users",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"PrivilegedAccessReview\", \"reviewers\": [\"security-officer\", \"it-director\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Compliance Team",
            Description = "Alerts compliance team of privileged access review initiation",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"PrivilegedAccessReviewNotification\", \"recipients\": [\"compliance-team\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateNewHireAccessReviewPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = NewHireAccessReviewPolicyId,
            Name = "New Hire Access Review",
            Description = "Triggers 30-day access review for newly provisioned accounts.",
            Category = "Lifecycle",
            Severity = 3,
            Priority = 5,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,HIPAA,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Account Age 30 Days",
            Description = "Triggers review for accounts created 30 days ago",
            RuleType = "AccountAge",
            FieldName = "CreatedAt",
            Operator = "Between",
            ComparisonValue = "29,31",
            DaysOffset = 30,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "No Prior Review",
            Description = "Ensures account hasn't already been reviewed",
            RuleType = "ReviewAge",
            FieldName = "LastAccessReviewDate",
            Operator = "IsNull",
            Weight = 0.5m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create New Hire Review",
            Description = "Initiates 30-day new hire access validation review",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"NewHireReview\", \"reviewers\": [\"manager\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateTerminationProcessingPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = TerminationProcessingPolicyId,
            Name = "Termination Processing",
            Description = "Automatically disables accounts flagged for termination and removes access.",
            Category = "Lifecycle",
            Severity = 1,
            Priority = 30,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 1,
            ComplianceFramework = "SOX,HIPAA,PCI-DSS,GDPR,ISO27001,NIST80053",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Termination Flag Set",
            Description = "Detects accounts marked for termination in HR system",
            RuleType = "AccountStatus",
            FieldName = "EmploymentStatus",
            Operator = "Equals",
            ComparisonValue = "Terminated",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Account Still Enabled",
            Description = "Ensures we only process enabled accounts",
            RuleType = "AccountStatus",
            FieldName = "IsEnabled",
            Operator = "Equals",
            ComparisonValue = "true",
            Weight = 0.5m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Disable Account",
            Description = "Immediately disables the terminated user's account",
            ActionType = "DisableAccount",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Remove Group Memberships",
            Description = "Removes all group memberships from terminated account",
            ActionType = "RemovePermissions",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"removeAll\": true}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Audit Record",
            Description = "Creates detailed termination audit trail",
            ActionType = "LogViolation",
            ExecutionTiming = "Immediate",
            Priority = 3,
            RequiresApproval = false,
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreatePasswordExpirationPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = PasswordExpirationPolicyId,
            Name = "Password Expiration Monitoring",
            Description = "Monitors password age and triggers notifications for accounts with passwords older than policy threshold.",
            Category = "Security",
            Severity = 3,
            Priority = 8,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "PCI-DSS,ISO27001,NIST80053",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Password Age 90 Days",
            Description = "Flags accounts with passwords older than 90 days",
            RuleType = "PasswordAge",
            FieldName = "PasswordLastSet",
            Operator = "OlderThan",
            DaysOffset = 90,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Password Never Expires Not Set",
            Description = "Excludes service accounts with 'password never expires'",
            RuleType = "AccountFlag",
            FieldName = "PasswordNeverExpires",
            Operator = "Equals",
            ComparisonValue = "false",
            Weight = 0.5m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Send Password Expiration Warning",
            Description = "Notifies user and manager of pending password expiration",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"PasswordExpirationWarning\", \"recipients\": [\"user\", \"manager\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateHighRiskUserMonitoringPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = HighRiskUserMonitoringPolicyId,
            Name = "High-Risk User Monitoring",
            Description = "Implements enhanced scrutiny for users with elevated risk scores.",
            Category = "Risk",
            Severity = 1,
            Priority = 22,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,PCI-DSS,NIST80053",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Critical Risk Score",
            Description = "Flags users with risk score above 0.85 (critical threshold)",
            RuleType = "RiskThreshold",
            FieldName = "RiskScore",
            Operator = "GreaterThan",
            ComparisonValue = "0.85",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Multiple Risk Indicators",
            Description = "Checks for combination of risk factors",
            RuleType = "RiskIndicators",
            FieldName = "RiskIndicatorCount",
            Operator = "GreaterThan",
            ComparisonValue = "3",
            Weight = 0.9m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Alert Security Operations",
            Description = "Immediately alerts SOC/security team of high-risk user",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"HighRiskUserAlert\", \"recipients\": [\"soc-team\", \"security-officer\"], \"urgency\": \"Critical\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Emergency Review",
            Description = "Creates urgent access review for immediate certification",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"EmergencyReview\", \"dueInDays\": 3, \"urgency\": \"Critical\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Security Ticket",
            Description = "Creates security incident ticket for investigation",
            ActionType = "CreateServiceTicket",
            ExecutionTiming = "Immediate",
            Priority = 3,
            RequiresApproval = false,
            Configuration = "{\"ticketType\": \"SecurityIncident\", \"priority\": \"High\"}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateManagerRequiredPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = ManagerRequiredPolicyId,
            Name = "Manager Required Policy",
            Description = "Ensures all user accounts have a valid manager assigned.",
            Category = "Governance",
            Severity = 2,
            Priority = 18,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,HIPAA,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Manager Not Assigned",
            Description = "Detects accounts with no manager in the organization hierarchy",
            RuleType = "ManagerHierarchy",
            FieldName = "ManagerId",
            Operator = "IsNull",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Account Age > 7 Days",
            Description = "Only flag accounts older than 7 days (allow grace period for new hires)",
            RuleType = "AccountAge",
            FieldName = "CreatedAt",
            Operator = "OlderThan",
            DaysOffset = 7,
            Weight = 0.5m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Escalate to HR",
            Description = "Escalates to HR department for manager assignment",
            ActionType = "EscalateToManager",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"escalateTo\": \"HR\", \"urgency\": \"High\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Block Access Request Approvals",
            Description = "Prevents approval workflows until manager is assigned",
            ActionType = "BlockWorkflow",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"blockType\": \"AccessRequest\", \"reason\": \"NoManagerAssigned\"}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateFailedLoginMonitoringPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = FailedLoginMonitoringPolicyId,
            Name = "Failed Login Monitoring",
            Description = "Monitors and alerts on excessive failed login attempts.",
            Category = "Security",
            Severity = 2,
            Priority = 28,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 1,
            ComplianceFramework = "PCI-DSS,ISO27001,NIST80053",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Failed Logins > 10 in 1 Hour",
            Description = "Flags accounts with more than 10 failed logins in the past hour",
            RuleType = "LoginActivity",
            FieldName = "FailedLoginCount",
            Operator = "GreaterThan",
            ComparisonValue = "10",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Multiple Source IPs",
            Description = "Detects failed logins from multiple different IP addresses",
            RuleType = "LoginActivity",
            FieldName = "DistinctFailedLoginIPs",
            Operator = "GreaterThan",
            ComparisonValue = "3",
            Weight = 0.8m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Alert Security Operations",
            Description = "Immediately alerts SOC team of potential attack",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"FailedLoginAlert\", \"recipients\": [\"soc-team\"], \"urgency\": \"Critical\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Temporary Account Lock",
            Description = "Temporarily locks account pending security review",
            ActionType = "LockAccount",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"lockDurationMinutes\": 30, \"notifyUser\": true}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateServiceAccountReviewPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = ServiceAccountReviewPolicyId,
            Name = "Service Account Review",
            Description = "Mandates semi-annual review of service accounts and non-interactive accounts.",
            Category = "Compliance",
            Severity = 2,
            Priority = 24,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 4320,
            ComplianceFramework = "SOX,PCI-DSS,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Service Account Type",
            Description = "Identifies service accounts and system accounts",
            RuleType = "AccountType",
            FieldName = "AccountType",
            Operator = "Contains",
            ComparisonValue = "Service,System,Application,Batch",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Last Review > 180 Days",
            Description = "Flags service accounts not reviewed in 180+ days",
            RuleType = "ReviewAge",
            FieldName = "LastAccessReviewDate",
            Operator = "OlderThan",
            DaysOffset = 180,
            Weight = 0.9m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Service Account Review",
            Description = "Initiates service account review with application owner",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"ServiceAccountReview\", \"reviewers\": [\"application-owner\", \"it-security\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateContractorAccessExpirationPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = ContractorAccessExpirationPolicyId,
            Name = "Contractor Access Expiration",
            Description = "Ensures contractor and temporary worker accounts have defined end dates.",
            Category = "Lifecycle",
            Severity = 2,
            Priority = 16,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,HIPAA,GDPR,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Contractor Account Type",
            Description = "Identifies contractor and temporary worker accounts",
            RuleType = "AccountType",
            FieldName = "IdentityType",
            Operator = "Contains",
            ComparisonValue = "Contractor,Temporary,Consultant,Vendor",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Contract End Date Passed",
            Description = "Detects contractor accounts past their contract end date",
            RuleType = "DateExpiration",
            FieldName = "ContractEndDate",
            Operator = "LessThan",
            ComparisonValue = "Today",
            Weight = 1.0m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "No End Date Defined",
            Description = "Flags contractor accounts without a defined end date",
            RuleType = "DateExpiration",
            FieldName = "ContractEndDate",
            Operator = "IsNull",
            Weight = 0.8m,
            SortOrder = 3,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Disable Expired Account",
            Description = "Automatically disables contractor account past end date",
            ActionType = "DisableAccount",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Contractor Manager",
            Description = "Notifies the contractor's manager and procurement team",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"ContractorExpiration\", \"recipients\": [\"manager\", \"procurement\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateMfaEnforcementPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = MfaEnforcementPolicyId,
            Name = "MFA Enforcement",
            Description = "Ensures multi-factor authentication is enabled for privileged users.",
            Category = "Security",
            Severity = 1,
            Priority = 26,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "PCI-DSS,ISO27001,NIST80053",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "MFA Not Enabled",
            Description = "Detects users without MFA configured",
            RuleType = "AccountFlag",
            FieldName = "MfaEnabled",
            Operator = "Equals",
            ComparisonValue = "false",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Has Privileged Access",
            Description = "Applies to users with admin or sensitive access",
            RuleType = "GroupMembership",
            FieldName = "GroupMemberships",
            Operator = "Contains",
            ComparisonValue = "Admin,VPN,RemoteAccess,FinancialSystems",
            Weight = 0.9m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Send MFA Enrollment Reminder",
            Description = "Sends reminder to user to enroll in MFA",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"MfaEnrollmentRequired\", \"recipients\": [\"user\", \"manager\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Compliance Ticket",
            Description = "Creates IT ticket for MFA enrollment follow-up",
            ActionType = "CreateServiceTicket",
            ExecutionTiming = "After3Days",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"ticketType\": \"SecurityCompliance\", \"priority\": \"High\"}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateSeparationOfDutiesPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = SeparationOfDutiesPolicyId,
            Name = "Separation of Duties",
            Description = "Detects toxic access combinations that violate separation of duties principles.",
            Category = "Risk",
            Severity = 1,
            Priority = 32,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "SOX,PCI-DSS,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create + Approve Access",
            Description = "Detects users with both creator and approver roles",
            RuleType = "SoDViolation",
            FieldName = "GroupMemberships",
            Operator = "ContainsBoth",
            ComparisonValue = "PO-Create,PO-Approve",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "HR + Finance Access",
            Description = "Detects users with access to both HR and Finance systems",
            RuleType = "SoDViolation",
            FieldName = "GroupMemberships",
            Operator = "ContainsBoth",
            ComparisonValue = "HR-Users,Finance-Users",
            Weight = 0.9m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Developer + Production Access",
            Description = "Detects developers with production system access",
            RuleType = "SoDViolation",
            FieldName = "GroupMemberships",
            Operator = "ContainsBoth",
            ComparisonValue = "Developers,Production-Admin",
            Weight = 0.95m,
            SortOrder = 3,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Alert Compliance Team",
            Description = "Immediately alerts compliance team of SoD violation",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"SoDViolationAlert\", \"recipients\": [\"compliance-team\", \"internal-audit\"], \"urgency\": \"Critical\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Emergency Review",
            Description = "Creates urgent access review to remediate SoD violation",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"SoDRemediation\", \"dueInDays\": 7, \"urgency\": \"Critical\"}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Log Compliance Violation",
            Description = "Creates detailed audit trail of SoD violation for regulators",
            ActionType = "LogViolation",
            ExecutionTiming = "Immediate",
            Priority = 3,
            RequiresApproval = false,
            Configuration = "{\"category\": \"SoD\", \"retentionYears\": 7}",
            IsActive = true
        });

        return policy;
    }

    // === GROUP MANAGEMENT POLICIES ===

    private CompliancePolicy CreateStaleGroupDetectionPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = StaleGroupDetectionPolicyId,
            Name = "Stale Group Detection",
            Description = "Identifies groups with no membership changes or usage in 180+ days.",
            Category = "Governance",
            Severity = 3,
            Priority = 15,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 168,
            ComplianceFramework = "ISO27001,NIST80053",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "No Membership Changes",
            Description = "Flags groups with no membership changes for 180+ days",
            RuleType = "GroupActivity",
            FieldName = "LastMembershipChange",
            Operator = "OlderThan",
            DaysOffset = 180,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Group Review",
            Description = "Creates review for group owner to validate group is still needed",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"GroupReview\", \"reviewers\": [\"owner\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Group Owner",
            Description = "Notifies group owner of stale group",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"StaleGroupNotification\", \"recipients\": [\"owner\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateEmptyGroupCleanupPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = EmptyGroupCleanupPolicyId,
            Name = "Empty Group Cleanup",
            Description = "Identifies security groups with zero members.",
            Category = "Governance",
            Severity = 4,
            Priority = 6,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 168,
            ComplianceFramework = "ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Zero Members",
            Description = "Detects groups with no members",
            RuleType = "GroupMembership",
            FieldName = "MemberCount",
            Operator = "Equals",
            ComparisonValue = "0",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Group Age > 30 Days",
            Description = "Only flag groups older than 30 days (grace period for new groups)",
            RuleType = "AccountAge",
            FieldName = "CreatedAt",
            Operator = "OlderThan",
            DaysOffset = 30,
            Weight = 0.5m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Owner for Cleanup",
            Description = "Notifies owner that empty group will be removed unless confirmed needed",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"EmptyGroupCleanup\", \"recipients\": [\"owner\", \"it-team\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateNestedGroupReviewPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = NestedGroupReviewPolicyId,
            Name = "Nested Group Review",
            Description = "Reviews deeply nested group structures that can obscure effective permissions.",
            Category = "Risk",
            Severity = 3,
            Priority = 18,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 720,
            ComplianceFramework = "SOX,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Nesting Depth > 3",
            Description = "Flags groups with nesting depth exceeding 3 levels",
            RuleType = "GroupStructure",
            FieldName = "NestingDepth",
            Operator = "GreaterThan",
            ComparisonValue = "3",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Structure Review",
            Description = "Creates review to simplify nested group structure",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"GroupStructureReview\", \"reviewers\": [\"it-security\", \"owner\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateLargeGroupReviewPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = LargeGroupReviewPolicyId,
            Name = "Large Group Review",
            Description = "Reviews groups with 100+ members. Large groups often indicate overly broad access grants.",
            Category = "Risk",
            Severity = 3,
            Priority = 16,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 720,
            ComplianceFramework = "SOX,PCI-DSS,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Large Membership",
            Description = "Flags groups with 100+ members",
            RuleType = "GroupMembership",
            FieldName = "MemberCount",
            Operator = "GreaterThan",
            ComparisonValue = "100",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Scope Review",
            Description = "Creates review to evaluate if group scope is too broad",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"GroupScopeReview\", \"reviewers\": [\"owner\", \"it-security\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Security Team",
            Description = "Alerts security team about large group for monitoring",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"LargeGroupAlert\", \"recipients\": [\"it-security\"]}",
            IsActive = true
        });

        return policy;
    }

    // === EXTERNAL ACCESS POLICIES ===

    private CompliancePolicy CreateExternalUserReviewPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = ExternalUserReviewPolicyId,
            Name = "External User Review",
            Description = "Quarterly review of all external/guest users (B2B accounts).",
            Category = "Compliance",
            Severity = 2,
            Priority = 22,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 2160,
            ComplianceFramework = "GDPR,HIPAA,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "External User Type",
            Description = "Identifies external/guest/B2B users",
            RuleType = "AccountType",
            FieldName = "UserType",
            Operator = "Contains",
            ComparisonValue = "Guest,External,B2B,Partner",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Last Review > 90 Days",
            Description = "Flags external users not reviewed in 90+ days",
            RuleType = "ReviewAge",
            FieldName = "LastAccessReviewDate",
            Operator = "OlderThan",
            DaysOffset = 90,
            Weight = 0.8m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create External User Review",
            Description = "Creates review for sponsor to validate external user access",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"ExternalUserReview\", \"reviewers\": [\"sponsor\", \"manager\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Sponsor",
            Description = "Notifies sponsor that external user access requires validation",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"ExternalUserReviewRequired\", \"recipients\": [\"sponsor\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateGuestAccountExpirationPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = GuestAccountExpirationPolicyId,
            Name = "Guest Account Expiration",
            Description = "Automatically disables guest accounts that exceed their defined expiration date.",
            Category = "Lifecycle",
            Severity = 2,
            Priority = 20,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "GDPR,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Guest User Type",
            Description = "Identifies guest accounts",
            RuleType = "AccountType",
            FieldName = "UserType",
            Operator = "Contains",
            ComparisonValue = "Guest,External",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Expiration Date Passed",
            Description = "Detects guest accounts past their expiration date",
            RuleType = "DateExpiration",
            FieldName = "AccountExpirationDate",
            Operator = "LessThan",
            ComparisonValue = "Today",
            Weight = 1.0m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Disable Expired Guest",
            Description = "Automatically disables expired guest account",
            ActionType = "DisableAccount",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Sponsor",
            Description = "Notifies sponsor that guest account was disabled",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"GuestAccountExpired\", \"recipients\": [\"sponsor\", \"it-team\"]}",
            IsActive = true
        });

        return policy;
    }

    // === ACCOUNT SECURITY POLICIES ===

    private CompliancePolicy CreateSharedAccountDetectionPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = SharedAccountDetectionPolicyId,
            Name = "Shared Account Detection",
            Description = "Identifies accounts with naming patterns suggesting shared usage.",
            Category = "Security",
            Severity = 2,
            Priority = 24,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 168,
            ComplianceFramework = "SOX,PCI-DSS,HIPAA,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Shared Account Pattern",
            Description = "Detects accounts with generic/shared naming patterns",
            RuleType = "NamingPattern",
            FieldName = "SamAccountName",
            Operator = "MatchesPattern",
            ComparisonValue = "^(admin|test|shared|generic|temp|service|backup|training).*",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Not Service Account",
            Description = "Excludes legitimate service accounts",
            RuleType = "AccountType",
            FieldName = "AccountType",
            Operator = "NotEquals",
            ComparisonValue = "Service",
            Weight = 0.5m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Alert Security Team",
            Description = "Alerts security team about potential shared account",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"SharedAccountAlert\", \"recipients\": [\"it-security\", \"compliance-team\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Investigation Ticket",
            Description = "Creates ticket to investigate shared account usage",
            ActionType = "CreateServiceTicket",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"ticketType\": \"SecurityInvestigation\", \"priority\": \"High\"}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreatePasswordNeverExpiresPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = PasswordNeverExpiresPolicyId,
            Name = "Password Never Expires Check",
            Description = "Identifies user accounts with 'password never expires' flag enabled.",
            Category = "Security",
            Severity = 2,
            Priority = 26,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 168,
            ComplianceFramework = "PCI-DSS,NIST80053,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Password Never Expires Enabled",
            Description = "Detects accounts with password never expires flag",
            RuleType = "AccountFlag",
            FieldName = "PasswordNeverExpires",
            Operator = "Equals",
            ComparisonValue = "true",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Not Service Account",
            Description = "Only flag non-service accounts",
            RuleType = "AccountType",
            FieldName = "AccountType",
            Operator = "NotEquals",
            ComparisonValue = "Service",
            Weight = 0.8m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Security Review",
            Description = "Creates review to justify password never expires setting",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"PasswordPolicyException\", \"reviewers\": [\"it-security\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify Security Team",
            Description = "Alerts security team about password policy exception",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"PasswordPolicyViolation\", \"recipients\": [\"it-security\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateAdminAccountCreepPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = AdminAccountCreepPolicyId,
            Name = "Admin Account Creep",
            Description = "Monitors growth of admin/privileged accounts.",
            Category = "Risk",
            Severity = 2,
            Priority = 28,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 168,
            ComplianceFramework = "SOX,PCI-DSS,NIST80053",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Admin Group Member",
            Description = "Identifies users in admin groups",
            RuleType = "GroupMembership",
            FieldName = "GroupMemberships",
            Operator = "Contains",
            ComparisonValue = "Admin,Administrators,Domain Admins,Enterprise Admins,Schema Admins",
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Recently Added",
            Description = "Flags recently added admin accounts (last 30 days)",
            RuleType = "GroupMembership",
            FieldName = "GroupMembershipAddedDate",
            Operator = "NewerThan",
            DaysOffset = 30,
            Weight = 0.9m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Alert Security Officer",
            Description = "Alerts security officer about new admin account",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            Configuration = "{\"template\": \"NewAdminAccountAlert\", \"recipients\": [\"security-officer\", \"it-director\"]}",
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Create Justification Review",
            Description = "Creates review requiring justification for admin access",
            ActionType = "CreateAccessReview",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"reviewType\": \"AdminJustification\", \"dueInDays\": 7, \"reviewers\": [\"security-officer\"]}",
            IsActive = true
        });

        return policy;
    }

    private CompliancePolicy CreateStalePasswordPolicy()
    {
        var policy = new CompliancePolicy
        {
            Id = StalePasswordPolicyId,
            Name = "Stale Password Detection",
            Description = "Identifies accounts with passwords unchanged for 180+ days.",
            Category = "Security",
            Severity = 2,
            Priority = 12,
            IsActive = false,
            IsBuiltIn = true,
            EvaluationFrequencyHours = 24,
            ComplianceFramework = "PCI-DSS,NIST80053,ISO27001",
            CreatedBy = "System"
        };

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Password Age > 180 Days",
            Description = "Flags accounts with passwords older than 180 days",
            RuleType = "PasswordAge",
            FieldName = "PasswordLastSet",
            Operator = "OlderThan",
            DaysOffset = 180,
            Weight = 1.0m,
            SortOrder = 1,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Account Enabled",
            Description = "Only check enabled accounts",
            RuleType = "AccountStatus",
            FieldName = "IsEnabled",
            Operator = "Equals",
            ComparisonValue = "true",
            Weight = 0.5m,
            SortOrder = 2,
            IsActive = true
        });

        policy.Rules.Add(new CompliancePolicyRule
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Not Password Never Expires",
            Description = "Excludes service accounts with password never expires",
            RuleType = "AccountFlag",
            FieldName = "PasswordNeverExpires",
            Operator = "Equals",
            ComparisonValue = "false",
            Weight = 0.5m,
            SortOrder = 3,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Force Password Change",
            Description = "Requires user to change password at next login",
            ActionType = "ForcePasswordChange",
            ExecutionTiming = "Immediate",
            Priority = 1,
            RequiresApproval = false,
            IsActive = true
        });

        policy.Actions.Add(new CompliancePolicyAction
        {
            Id = Guid.NewGuid(),
            CompliancePolicyId = policy.Id,
            Name = "Notify User",
            Description = "Notifies user about mandatory password change",
            ActionType = "SendNotification",
            ExecutionTiming = "Immediate",
            Priority = 2,
            RequiresApproval = false,
            Configuration = "{\"template\": \"MandatoryPasswordChange\", \"recipients\": [\"user\"]}",
            IsActive = true
        });

        return policy;
    }

    /// <summary>
    /// Creates framework-to-policy mappings with requirement references
    /// </summary>
    private List<ComplianceFrameworkPolicyMapping> GetFrameworkPolicyMappings()
    {
        var mappings = new List<ComplianceFrameworkPolicyMapping>();

        // SOX Framework Mappings
        mappings.AddRange(new[]
        {
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.SoxFrameworkId, CompliancePolicyId = DormantAccount90DayPolicyId, RequirementId = "SOX-404", RequirementDescription = "Internal control over financial reporting", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.SoxFrameworkId, CompliancePolicyId = OrphanedAccountPolicyId, RequirementId = "SOX-302", RequirementDescription = "Corporate responsibility for financial reports", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.SoxFrameworkId, CompliancePolicyId = ExcessivePermissionsPolicyId, RequirementId = "SOX-404", RequirementDescription = "Segregation of duties and least privilege", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.SoxFrameworkId, CompliancePolicyId = PrivilegedAccessReviewPolicyId, RequirementId = "SOX-404", RequirementDescription = "Quarterly review of privileged access", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.SoxFrameworkId, CompliancePolicyId = NewHireAccessReviewPolicyId, RequirementId = "SOX-404", RequirementDescription = "New employee access validation", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.SoxFrameworkId, CompliancePolicyId = TerminationProcessingPolicyId, RequirementId = "SOX-404", RequirementDescription = "Timely access revocation", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.SoxFrameworkId, CompliancePolicyId = HighRiskUserMonitoringPolicyId, RequirementId = "SOX-302", RequirementDescription = "Fraud risk monitoring", ComplianceStatus = "Compliant", CoveragePercentage = 100m }
        });

        // HIPAA Framework Mappings
        mappings.AddRange(new[]
        {
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.HipaaFrameworkId, CompliancePolicyId = DormantAccount90DayPolicyId, RequirementId = "164.312(a)(1)", RequirementDescription = "Access control", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.HipaaFrameworkId, CompliancePolicyId = OrphanedAccountPolicyId, RequirementId = "164.308(a)(3)", RequirementDescription = "Workforce security", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.HipaaFrameworkId, CompliancePolicyId = NewHireAccessReviewPolicyId, RequirementId = "164.308(a)(3)", RequirementDescription = "Workforce clearance", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.HipaaFrameworkId, CompliancePolicyId = TerminationProcessingPolicyId, RequirementId = "164.308(a)(3)(ii)(C)", RequirementDescription = "Termination procedures", ComplianceStatus = "Compliant", CoveragePercentage = 100m }
        });

        // PCI-DSS Framework Mappings
        mappings.AddRange(new[]
        {
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.PciDssFrameworkId, CompliancePolicyId = ExcessivePermissionsPolicyId, RequirementId = "7.1", RequirementDescription = "Limit access to system components", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.PciDssFrameworkId, CompliancePolicyId = PrivilegedAccessReviewPolicyId, RequirementId = "8.1.4", RequirementDescription = "Review user accounts quarterly", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.PciDssFrameworkId, CompliancePolicyId = TerminationProcessingPolicyId, RequirementId = "8.1.3", RequirementDescription = "Revoke terminated user access", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.PciDssFrameworkId, CompliancePolicyId = PasswordExpirationPolicyId, RequirementId = "8.2.4", RequirementDescription = "Password change every 90 days", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.PciDssFrameworkId, CompliancePolicyId = HighRiskUserMonitoringPolicyId, RequirementId = "10.2", RequirementDescription = "Automated audit trails", ComplianceStatus = "Compliant", CoveragePercentage = 100m }
        });

        // GDPR Framework Mappings
        mappings.AddRange(new[]
        {
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.GdprFrameworkId, CompliancePolicyId = OrphanedAccountPolicyId, RequirementId = "Article 5(1)(f)", RequirementDescription = "Integrity and confidentiality", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.GdprFrameworkId, CompliancePolicyId = TerminationProcessingPolicyId, RequirementId = "Article 32", RequirementDescription = "Security of processing", ComplianceStatus = "Compliant", CoveragePercentage = 100m }
        });

        // ISO 27001 Framework Mappings
        mappings.AddRange(new[]
        {
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Iso27001FrameworkId, CompliancePolicyId = DormantAccount90DayPolicyId, RequirementId = "A.9.2.6", RequirementDescription = "Removal of access rights", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Iso27001FrameworkId, CompliancePolicyId = ExcessivePermissionsPolicyId, RequirementId = "A.9.1.2", RequirementDescription = "Access to networks", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Iso27001FrameworkId, CompliancePolicyId = NewHireAccessReviewPolicyId, RequirementId = "A.9.2.2", RequirementDescription = "User access provisioning", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Iso27001FrameworkId, CompliancePolicyId = TerminationProcessingPolicyId, RequirementId = "A.9.2.6", RequirementDescription = "Removal on termination", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Iso27001FrameworkId, CompliancePolicyId = PasswordExpirationPolicyId, RequirementId = "A.9.4.3", RequirementDescription = "Password management", ComplianceStatus = "Compliant", CoveragePercentage = 100m }
        });

        // NIST 800-53 Framework Mappings
        mappings.AddRange(new[]
        {
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Nist80053FrameworkId, CompliancePolicyId = PrivilegedAccessReviewPolicyId, RequirementId = "AC-6", RequirementDescription = "Least Privilege", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Nist80053FrameworkId, CompliancePolicyId = TerminationProcessingPolicyId, RequirementId = "PS-4", RequirementDescription = "Personnel Termination", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Nist80053FrameworkId, CompliancePolicyId = PasswordExpirationPolicyId, RequirementId = "IA-5", RequirementDescription = "Authenticator Management", ComplianceStatus = "Compliant", CoveragePercentage = 100m },
            new ComplianceFrameworkPolicyMapping { Id = Guid.NewGuid(), FrameworkId = ComplianceFrameworksSeedService.Nist80053FrameworkId, CompliancePolicyId = HighRiskUserMonitoringPolicyId, RequirementId = "AU-6", RequirementDescription = "Audit Review and Reporting", ComplianceStatus = "Compliant", CoveragePercentage = 100m }
        });

        return mappings;
    }
}
