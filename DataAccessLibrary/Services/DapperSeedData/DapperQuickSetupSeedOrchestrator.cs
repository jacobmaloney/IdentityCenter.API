using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// High-performance Dapper-based seed orchestrator.
/// Seeds everything needed for a production-ready Certification Center in under 30 seconds.
/// Uses a single shared SqlConnection and SqlTransaction for maximum performance.
/// Executes independent seeds in parallel where possible.
/// </summary>
public class DapperQuickSetupSeedOrchestrator
{
    private string _connectionString;
    private readonly DapperRolesSeedService _rolesSeed;
    private readonly DapperBusinessRolesSeedService _businessRolesSeed;
    private readonly DapperTagsSeedService _tagsSeed;
    private readonly DapperFrameworkSeedService _frameworksSeed;
    private readonly DapperPolicySeedService _policiesSeed;
    private readonly DapperTriggerTemplatesSeedService _triggerTemplatesSeed;
    private readonly DapperApprovalWorkflowsSeedService _workflowsSeed;
    private readonly DapperEmailTemplatesSeedService _emailTemplatesSeed;
    private readonly DapperTeamsTemplatesSeedService _teamsTemplatesSeed;
    private readonly DapperDevCenterScriptsSeedService _devCenterScriptsSeed;
    private readonly DapperHRImportSeedService _hrImportSeed;
    private readonly ILogger<DapperQuickSetupSeedOrchestrator> _logger;

    public DapperQuickSetupSeedOrchestrator(
        IConfiguration configuration,
        DapperRolesSeedService rolesSeed,
        DapperBusinessRolesSeedService businessRolesSeed,
        DapperTagsSeedService tagsSeed,
        DapperFrameworkSeedService frameworksSeed,
        DapperPolicySeedService policiesSeed,
        DapperTriggerTemplatesSeedService triggerTemplatesSeed,
        DapperApprovalWorkflowsSeedService workflowsSeed,
        DapperEmailTemplatesSeedService emailTemplatesSeed,
        DapperTeamsTemplatesSeedService teamsTemplatesSeed,
        DapperDevCenterScriptsSeedService devCenterScriptsSeed,
        DapperHRImportSeedService hrImportSeed,
        ILogger<DapperQuickSetupSeedOrchestrator> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _rolesSeed = rolesSeed;
        _businessRolesSeed = businessRolesSeed;
        _tagsSeed = tagsSeed;
        _frameworksSeed = frameworksSeed;
        _policiesSeed = policiesSeed;
        _triggerTemplatesSeed = triggerTemplatesSeed;
        _workflowsSeed = workflowsSeed;
        _emailTemplatesSeed = emailTemplatesSeed;
        _teamsTemplatesSeed = teamsTemplatesSeed;
        _devCenterScriptsSeed = devCenterScriptsSeed;
        _hrImportSeed = hrImportSeed;
        _logger = logger;
    }

    /// <summary>
    /// Overrides the connection string used by the orchestrator.
    /// Call this before seeding when the connection string was updated after DI construction
    /// (e.g., during first-run setup wizard).
    /// </summary>
    public void SetConnectionString(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Seeds everything needed for a production-ready Certification Center.
    /// Uses parallel execution where dependencies allow.
    /// Target: Complete in under 30 seconds (vs ~20 minutes with EF Core).
    /// </summary>
    public async Task<SeedResult> SeedEverythingAsync() =>
        await SeedEverythingWithProgressAsync(null);

    /// <summary>
    /// Seeds everything with optional progress callback for UI updates.
    ///
    /// Dependency Graph:
    /// Stage 1 (parallel): Roles, BusinessRoles, Tags, Frameworks, EmailTemplates, TeamsTemplates, TriggerTemplates, DevCenterScripts
    /// Stage 2 (sequential, depends on Frameworks): Policies (references Frameworks for mappings)
    /// Stage 3 (parallel): ApprovalWorkflows (independent of policies)
    /// </summary>
    public async Task<SeedResult> SeedEverythingWithProgressAsync(Action<SeedProgress>? onProgress)
    {
        var result = new SeedResult
        {
            StartTime = DateTime.UtcNow
        };

        var totalSteps = 12;
        var sw = Stopwatch.StartNew();

        void ReportProgress(int step, string message, bool completed = false)
        {
            onProgress?.Invoke(new SeedProgress
            {
                Step = step,
                TotalSteps = totalSteps,
                Message = message,
                Completed = completed
            });
        }

        _logger.LogInformation("DAPPER QUICK SETUP SEED ORCHESTRATOR ACTIVATED!");
        _logger.LogInformation("Target: Sub-30-second complete database seed");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = connection.BeginTransaction();

            try
            {
                // ========================================
                // STAGE 1: Independent seeds (parallel)
                // ========================================
                ReportProgress(1, "Stage 1: Seeding independent entities (parallel)...");
                _logger.LogInformation("Stage 1: Seeding independent entities in parallel...");

                var stage1Start = sw.Elapsed;

                // Run all Stage 1 seeds in parallel
                var stage1Tasks = new[]
                {
                    Task.Run(async () => { await _rolesSeed.SeedAsync(connection, transaction); result.RolesSeeded = true; }),
                    Task.Run(async () => { await _businessRolesSeed.SeedAsync(connection, transaction); result.BusinessRolesSeeded = true; }),
                    Task.Run(async () => { await _tagsSeed.SeedAsync(connection, transaction); result.TagsSeeded = true; }),
                    Task.Run(async () => { await _frameworksSeed.SeedAsync(connection, transaction); result.ComplianceFrameworksSeeded = true; }),
                    Task.Run(async () => { await _emailTemplatesSeed.SeedAsync(connection, transaction); result.EmailTemplatesSeeded = true; }),
                    Task.Run(async () => { await _teamsTemplatesSeed.SeedAsync(connection, transaction); result.TeamsTemplatesSeeded = true; }),
                    Task.Run(async () => { await _triggerTemplatesSeed.SeedAsync(connection, transaction); result.TriggerTemplatesSeeded = true; }),
                    Task.Run(async () => { await _devCenterScriptsSeed.SeedAsync(connection, transaction); result.DevCenterScriptsSeeded = true; }),
                    Task.Run(async () => { await _hrImportSeed.SeedAsync(connection, transaction); result.HRImportTemplatesSeeded = true; })
                };

                await Task.WhenAll(stage1Tasks);

                var stage1Duration = sw.Elapsed - stage1Start;
                _logger.LogInformation("Stage 1 complete in {Duration:0.00}ms", stage1Duration.TotalMilliseconds);
                ReportProgress(8, $"Stage 1 complete ({stage1Duration.TotalMilliseconds:0.00}ms)", true);

                // ========================================
                // STAGE 2: Policies (depends on Frameworks)
                // ========================================
                ReportProgress(9, "Stage 2: Seeding compliance policies...");
                _logger.LogInformation("Stage 2: Seeding compliance policies (depends on frameworks)...");

                var stage2Start = sw.Elapsed;
                await _policiesSeed.SeedAsync(connection, transaction);
                result.CompliancePoliciesSeeded = true;

                var stage2Duration = sw.Elapsed - stage2Start;
                _logger.LogInformation("Stage 2 complete in {Duration:0.00}ms", stage2Duration.TotalMilliseconds);
                ReportProgress(9, $"Policies seeded ({stage2Duration.TotalMilliseconds:0.00}ms)", true);

                // ========================================
                // STAGE 3: Approval Workflows (independent)
                // ========================================
                ReportProgress(10, "Stage 3: Seeding approval workflows...");
                _logger.LogInformation("Stage 3: Seeding approval workflow templates...");

                var stage3Start = sw.Elapsed;
                await _workflowsSeed.SeedAsync(connection, transaction);
                result.WorkflowTemplatesSeeded = true;

                var stage3Duration = sw.Elapsed - stage3Start;
                _logger.LogInformation("Stage 3 complete in {Duration:0.00}ms", stage3Duration.TotalMilliseconds);
                ReportProgress(10, $"Workflows seeded ({stage3Duration.TotalMilliseconds:0.00}ms)", true);

                // ========================================
                // COMMIT TRANSACTION
                // ========================================
                ReportProgress(11, "Committing transaction...");
                await transaction.CommitAsync();

                sw.Stop();
                result.Success = true;
                result.EndTime = DateTime.UtcNow;
                result.Duration = sw.Elapsed;

                ReportProgress(11, "Seed complete!", true);

                _logger.LogInformation("========================================");
                _logger.LogInformation("DAPPER SEED ORCHESTRATOR COMPLETE!");
                _logger.LogInformation("========================================");
                _logger.LogInformation("Total time: {Duration:0.00} seconds", result.Duration.TotalSeconds);
                _logger.LogInformation("Roles: {Status}", result.RolesSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Business Roles: {Status}", result.BusinessRolesSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Tags: {Status}", result.TagsSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Frameworks: {Status}", result.ComplianceFrameworksSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Policies: {Status}", result.CompliancePoliciesSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Trigger Templates: {Status}", result.TriggerTemplatesSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Workflows: {Status}", result.WorkflowTemplatesSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Email Templates: {Status}", result.EmailTemplatesSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Teams Templates: {Status}", result.TeamsTemplatesSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("Dev Center Scripts: {Status}", result.DevCenterScriptsSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("HR Import Templates: {Status}", result.HRImportTemplatesSeeded ? "Seeded" : "Skipped");
                _logger.LogInformation("========================================");

                if (result.Duration.TotalSeconds < 30)
                {
                    _logger.LogInformation("TARGET ACHIEVED! Seed completed in under 30 seconds!");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Seed failed, rolling back transaction");
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            onProgress?.Invoke(new SeedProgress
            {
                Step = 0,
                TotalSteps = totalSteps,
                Message = $"Error: {ex.Message}",
                IsError = true
            });
            _logger.LogError(ex, "Dapper seed orchestrator encountered an error");
        }

        return result;
    }

    /// <summary>
    /// Seeds only specific entity types (for partial seeding scenarios).
    /// </summary>
    public async Task SeedSpecificAsync(params SeedEntityType[] entityTypes)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var entityType in entityTypes)
            {
                switch (entityType)
                {
                    case SeedEntityType.Roles:
                        await _rolesSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.BusinessRoles:
                        await _businessRolesSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.Tags:
                        await _tagsSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.Frameworks:
                        await _frameworksSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.Policies:
                        await _policiesSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.TriggerTemplates:
                        await _triggerTemplatesSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.Workflows:
                        await _workflowsSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.EmailTemplates:
                        await _emailTemplatesSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.TeamsTemplates:
                        await _teamsTemplatesSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.DevCenterScripts:
                        await _devCenterScriptsSeed.SeedAsync(connection, transaction);
                        break;
                    case SeedEntityType.HRImportTemplates:
                        await _hrImportSeed.SeedAsync(connection, transaction);
                        break;
                }
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public class SeedProgress
    {
        public int Step { get; set; }
        public int TotalSteps { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public bool IsError { get; set; }
    }

    public class SeedResult
    {
        public bool Success { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool RolesSeeded { get; set; }
        public bool BusinessRolesSeeded { get; set; }
        public bool TagsSeeded { get; set; }
        public bool ComplianceFrameworksSeeded { get; set; }
        public bool CompliancePoliciesSeeded { get; set; }
        public bool TriggerTemplatesSeeded { get; set; }
        public bool WorkflowTemplatesSeeded { get; set; }
        public bool EmailTemplatesSeeded { get; set; }
        public bool TeamsTemplatesSeeded { get; set; }
        public bool DevCenterScriptsSeeded { get; set; }
        public bool HRImportTemplatesSeeded { get; set; }
        public string? ErrorMessage { get; set; }

        public string GetSummary()
        {
            if (!Success)
                return $"Seeding failed: {ErrorMessage}";

            return $@"
Dapper Quick Setup Seed Complete!
============================================
Roles: {(RolesSeeded ? "8 roles with AD/EntraID mapping" : "Skipped")}
Business Roles: {(BusinessRolesSeeded ? "16 workflow routing roles" : "Skipped")}
Tags: {(TagsSeeded ? "16 organizational tags" : "Skipped")}
Frameworks: {(ComplianceFrameworksSeeded ? "6 compliance frameworks (SOX, HIPAA, PCI-DSS, GDPR, ISO27001, NIST)" : "Skipped")}
Policies: {(CompliancePoliciesSeeded ? "27 compliance policies with rules, actions & framework mappings" : "Skipped")}
Trigger Templates: {(TriggerTemplatesSeeded ? "14 automation templates" : "Skipped")}
Workflows: {(WorkflowTemplatesSeeded ? "14 approval workflow templates" : "Skipped")}
Email Templates: {(EmailTemplatesSeeded ? "10 notification templates" : "Skipped")}
Teams Templates: {(TeamsTemplatesSeeded ? "8 Teams message templates" : "Skipped")}
Dev Center Scripts: {(DevCenterScriptsSeeded ? "3 system scripts" : "Skipped")}
HR Import Templates: {(HRImportTemplatesSeeded ? "1 HR CSV import template" : "Skipped")}
Duration: {Duration.TotalSeconds:0.00} seconds

{(Duration.TotalSeconds < 30 ? "TARGET ACHIEVED! Sub-30-second seed!" : "")}
Your Certification Center is now PRODUCTION-READY with full compliance automation!
";
        }
    }

    public enum SeedEntityType
    {
        Roles,
        BusinessRoles,
        Tags,
        Frameworks,
        Policies,
        TriggerTemplates,
        Workflows,
        EmailTemplates,
        TeamsTemplates,
        DevCenterScripts,
        HRImportTemplates
    }
}
