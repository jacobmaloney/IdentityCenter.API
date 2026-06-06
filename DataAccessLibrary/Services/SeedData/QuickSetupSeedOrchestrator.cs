using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// The BADASS orchestrator that seeds everything during Quick Configuration
/// This service makes Certification Center go from zero to production-ready in under 15 minutes!
/// </summary>
public class QuickSetupSeedOrchestrator
{
    private string _connectionString;
    private readonly DefaultRolesSeedService _rolesSeed;
    private readonly DefaultTagsSeedService _tagsSeed;
    private readonly ComplianceFrameworksSeedService _frameworksSeed;
    private readonly DefaultPoliciesSeedService _policiesSeed;
    private readonly WorkflowTriggerTemplatesSeedService _triggerTemplatesSeed;
    private readonly DevCenterScriptsSeedService _devCenterScriptsSeed;
    private readonly DefaultTeamsTemplatesSeedService _teamsTemplatesSeed;
    private readonly DefaultEmailTemplatesSeedService _emailTemplatesSeed;
    private readonly DefaultBusinessRolesSeedService _businessRolesSeed;
    private readonly DefaultApprovalWorkflowsSeedService _approvalWorkflowsSeed;
    private readonly HRImportTemplatesSeedService _hrImportSeed;
    private readonly ILogger<QuickSetupSeedOrchestrator> _logger;

    public QuickSetupSeedOrchestrator(
        IConfiguration configuration,
        DefaultRolesSeedService rolesSeed,
        DefaultTagsSeedService tagsSeed,
        ComplianceFrameworksSeedService frameworksSeed,
        DefaultPoliciesSeedService policiesSeed,
        WorkflowTriggerTemplatesSeedService triggerTemplatesSeed,
        DevCenterScriptsSeedService devCenterScriptsSeed,
        DefaultTeamsTemplatesSeedService teamsTemplatesSeed,
        DefaultEmailTemplatesSeedService emailTemplatesSeed,
        DefaultBusinessRolesSeedService businessRolesSeed,
        DefaultApprovalWorkflowsSeedService approvalWorkflowsSeed,
        HRImportTemplatesSeedService hrImportSeed,
        ILogger<QuickSetupSeedOrchestrator> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _rolesSeed = rolesSeed;
        _tagsSeed = tagsSeed;
        _frameworksSeed = frameworksSeed;
        _policiesSeed = policiesSeed;
        _triggerTemplatesSeed = triggerTemplatesSeed;
        _devCenterScriptsSeed = devCenterScriptsSeed;
        _teamsTemplatesSeed = teamsTemplatesSeed;
        _emailTemplatesSeed = emailTemplatesSeed;
        _businessRolesSeed = businessRolesSeed;
        _approvalWorkflowsSeed = approvalWorkflowsSeed;
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
    /// Seeds EVERYTHING needed for a production-ready Certification Center
    /// This is the rockstar method that makes magic happen!
    /// </summary>
    public async Task<SeedResult> SeedEverythingAsync() =>
        await SeedEverythingWithProgressAsync(null);

    /// <summary>
    /// Seeds everything with optional progress callback for UI updates
    /// </summary>
    public async Task<SeedResult> SeedEverythingWithProgressAsync(Action<SeedProgress>? onProgress)
    {
        var result = new SeedResult
        {
            StartTime = DateTime.UtcNow
        };

        void ReportProgress(int step, int total, string message, bool completed = false)
        {
            onProgress?.Invoke(new SeedProgress { Step = step, TotalSteps = total, Message = message, Completed = completed });
        }

        const int totalSteps = 11;
        _logger.LogInformation("QUICK SETUP SEED ORCHESTRATOR ACTIVATED!");
        _logger.LogInformation("About to make this Certification Center absolutely legendary...");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Step 1: Roles with AD/EntraID group mapping
            ReportProgress(1, totalSteps, "Seeding default roles with AD/EntraID mappings...");
            _logger.LogInformation("Step 1/{Total}: Seeding default roles with AD/EntraID mappings...", totalSteps);
            await _rolesSeed.SeedDefaultRolesAsync();
            result.RolesSeeded = true;
            ReportProgress(1, totalSteps, "Roles seeded with AD/EntraID mappings", true);

            // Step 2: Business roles for workflow routing
            ReportProgress(2, totalSteps, "Seeding business roles for workflow routing...");
            _logger.LogInformation("Step 2/{Total}: Seeding business roles for workflow routing...", totalSteps);
            await _businessRolesSeed.SeedDefaultBusinessRolesAsync();
            var businessRoleCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BusinessRoles");
            result.BusinessRolesSeeded = businessRoleCount > 0;
            result.BusinessRolesCount = businessRoleCount;
            ReportProgress(2, totalSteps, $"Business roles seeded ({businessRoleCount} roles)", true);

            // Step 3: Tags for organization
            ReportProgress(3, totalSteps, "Seeding default tags...");
            _logger.LogInformation("Step 3/{Total}: Seeding default tags...", totalSteps);
            await _tagsSeed.SeedDefaultTagsAsync();
            result.TagsSeeded = true;
            ReportProgress(3, totalSteps, "Tags seeded", true);

            // Step 4: Compliance frameworks (BEFORE policies - policies reference frameworks!)
            ReportProgress(4, totalSteps, "Seeding compliance frameworks...");
            _logger.LogInformation("Step 4/{Total}: Seeding compliance frameworks...", totalSteps);
            await _frameworksSeed.SeedComplianceFrameworksAsync();
            result.ComplianceFrameworksSeeded = true;
            ReportProgress(4, totalSteps, "Compliance frameworks seeded (SOX, HIPAA, PCI-DSS, GDPR, ISO 27001, NIST)", true);

            // Step 5: Compliance policies with rules, actions, and framework mappings
            ReportProgress(5, totalSteps, "Seeding compliance policies with framework mappings...");
            _logger.LogInformation("Step 5/{Total}: Seeding compliance policies with framework mappings...", totalSteps);
            await _policiesSeed.SeedDefaultPoliciesAsync();
            result.CompliancePoliciesSeeded = true;
            ReportProgress(5, totalSteps, "Compliance policies seeded with rules and actions", true);

            // Step 6: Workflow trigger templates (automation without scripting!)
            ReportProgress(6, totalSteps, "Seeding workflow trigger templates...");
            _logger.LogInformation("Step 6/{Total}: Seeding workflow trigger templates...", totalSteps);
            await _triggerTemplatesSeed.SeedDefaultTemplatesAsync();
            var triggerTemplateCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM WorkflowTriggerTemplates");
            result.TriggerTemplatesSeeded = triggerTemplateCount > 0;
            result.TriggerTemplatesCount = triggerTemplateCount;
            ReportProgress(6, totalSteps, $"Trigger templates seeded ({triggerTemplateCount} templates)", true);

            // Step 7: Approval workflow templates
            ReportProgress(7, totalSteps, "Seeding approval workflow templates...");
            _logger.LogInformation("Step 7/{Total}: Seeding approval workflow templates...", totalSteps);
            await _approvalWorkflowsSeed.SeedDefaultWorkflowTemplatesAsync();
            var workflowCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ApprovalWorkflows WHERE IsTemplate = 1");
            result.WorkflowTemplatesSeeded = workflowCount > 0;
            result.WorkflowTemplatesCount = workflowCount;
            ReportProgress(7, totalSteps, $"Approval workflow templates seeded ({workflowCount} templates)", true);

            // Step 8: Email templates (actually seed them!)
            ReportProgress(8, totalSteps, "Seeding email templates...");
            _logger.LogInformation("Step 8/{Total}: Seeding email templates...", totalSteps);
            await _emailTemplatesSeed.SeedDefaultEmailTemplatesAsync();
            var emailTemplateCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM EmailTemplates");
            result.EmailTemplatesSeeded = emailTemplateCount > 0;
            result.EmailTemplatesCount = emailTemplateCount;
            ReportProgress(8, totalSteps, $"Email templates seeded ({emailTemplateCount} templates)", true);

            // Step 9: Teams message templates
            ReportProgress(9, totalSteps, "Seeding Teams message templates...");
            _logger.LogInformation("Step 9/{Total}: Seeding Teams message templates...", totalSteps);
            await _teamsTemplatesSeed.SeedDefaultTeamsTemplatesAsync();
            var teamsTemplateCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TeamsMessageTemplates");
            result.TeamsTemplatesSeeded = teamsTemplateCount > 0;
            result.TeamsTemplatesCount = teamsTemplateCount;
            ReportProgress(9, totalSteps, $"Teams templates seeded ({teamsTemplateCount} templates)", true);

            // Step 10: Dev Center system scripts (sync pre/post-processing scripts)
            ReportProgress(10, totalSteps, "Seeding Dev Center system scripts...");
            _logger.LogInformation("Step 10/{Total}: Seeding Dev Center system scripts...", totalSteps);
            await _devCenterScriptsSeed.SeedSystemScriptsAsync();
            result.DevCenterScriptsSeeded = true;
            ReportProgress(10, totalSteps, "Dev Center scripts seeded", true);

            // Step 11: HR Import templates
            ReportProgress(11, totalSteps, "Seeding HR Import templates...");
            _logger.LogInformation("Step 11/{Total}: Seeding HR Import templates...", totalSteps);
            await _hrImportSeed.SeedAsync();
            ReportProgress(11, totalSteps, "HR Import templates seeded", true);

            result.Success = true;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime.Value - result.StartTime;

            _logger.LogInformation("SEED ORCHESTRATOR COMPLETE!");
            _logger.LogInformation("Roles: Seeded with AD/EntraID mappings");
            _logger.LogInformation("Tags: Seeded");
            _logger.LogInformation("Compliance Frameworks: 6 frameworks seeded");
            _logger.LogInformation("Compliance Policies: 8 policies with rules, actions & framework mappings");
            _logger.LogInformation("Trigger Templates: {Count} automation templates loaded", result.TriggerTemplatesCount);
            _logger.LogInformation("Workflow Templates: {Count} approval templates loaded", result.WorkflowTemplatesCount);
            _logger.LogInformation("Email Templates: {Count} notification templates loaded", result.EmailTemplatesCount);
            _logger.LogInformation("Teams Templates: {Count} Teams message templates loaded", result.TeamsTemplatesCount);
            _logger.LogInformation("Dev Center Scripts: 3 system scripts seeded (ConvertBinaryValues, CreateOrUpdateIdentity, ResolveManagerRelationships)");
            _logger.LogInformation("Total time: {Duration:0.00} seconds", result.Duration.TotalSeconds);
            _logger.LogInformation("Certification Center is now PRODUCTION-READY with full compliance automation!");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            onProgress?.Invoke(new SeedProgress { Step = 0, TotalSteps = totalSteps, Message = $"Error: {ex.Message}", IsError = true });
            _logger.LogError(ex, "Seed orchestrator encountered an error");
        }

        return result;
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
        public int BusinessRolesCount { get; set; }
        public bool TagsSeeded { get; set; }
        public bool ComplianceFrameworksSeeded { get; set; }
        public bool CompliancePoliciesSeeded { get; set; }
        public bool TriggerTemplatesSeeded { get; set; }
        public int TriggerTemplatesCount { get; set; }
        public bool WorkflowTemplatesSeeded { get; set; }
        public int WorkflowTemplatesCount { get; set; }
        public bool EmailTemplatesSeeded { get; set; }
        public int EmailTemplatesCount { get; set; }
        public bool TeamsTemplatesSeeded { get; set; }
        public int TeamsTemplatesCount { get; set; }
        public bool DevCenterScriptsSeeded { get; set; }
        public string? ErrorMessage { get; set; }

        public string GetSummary()
        {
            if (!Success)
                return $"Seeding failed: {ErrorMessage}";

            return $@"
Quick Setup Seed Complete!
============================================
Roles: {(RolesSeeded ? "8 roles with AD/EntraID mapping" : "Failed")}
Business Roles: {(BusinessRolesSeeded ? $"{BusinessRolesCount} workflow routing roles (CEO, CISO, IT Admin, etc.)" : "Failed")}
Tags: {(TagsSeeded ? "12 organizational tags" : "Failed")}
Frameworks: {(ComplianceFrameworksSeeded ? "6 compliance frameworks (SOX, HIPAA, PCI-DSS, GDPR, ISO27001, NIST)" : "Failed")}
Policies: {(CompliancePoliciesSeeded ? "8 compliance policies with rules, actions & framework mappings" : "Failed")}
Trigger Templates: {(TriggerTemplatesSeeded ? $"{TriggerTemplatesCount} automation templates (scheduled triggers, events)" : "Failed")}
Workflows: {(WorkflowTemplatesSeeded ? $"{WorkflowTemplatesCount} approval workflow templates" : "Failed")}
Email Templates: {(EmailTemplatesSeeded ? $"{EmailTemplatesCount} notification templates" : "Failed")}
Teams Templates: {(TeamsTemplatesSeeded ? $"{TeamsTemplatesCount} Teams message templates" : "Failed")}
Dev Center Scripts: {(DevCenterScriptsSeeded ? "3 system scripts (ConvertBinaryValues, CreateOrUpdateIdentity, ResolveManagerRelationships)" : "Failed")}
Duration: {Duration.TotalSeconds:0.00} seconds

Framework-Policy Integration Active!
When you apply a framework, all mapped policies automatically activate.

Your Certification Center is now PRODUCTION-READY with full compliance automation!
";
        }
    }
}
