using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Service to reset Certification Center to factory defaults.
/// Deletes all user-created data and re-seeds built-in templates, policies, frameworks, etc.
/// This enables a "Start Over" capability for the application.
/// </summary>
public class ResetToDefaultsService
{
    private readonly string _connectionString;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<ResetToDefaultsService> _logger;

    // Inject seed services
    private readonly DefaultRolesSeedService _rolesSeedService;
    private readonly DefaultTagsSeedService _tagsSeedService;
    private readonly DefaultEmailTemplatesSeedService _emailTemplatesSeedService;
    private readonly ComplianceFrameworksSeedService _frameworksSeedService;
    private readonly DefaultPoliciesSeedService _policiesSeedService;
    private readonly DefaultApprovalWorkflowsSeedService _workflowsSeedService;

    public ResetToDefaultsService(
        IConfiguration configuration,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ILogger<ResetToDefaultsService> logger,
        DefaultRolesSeedService rolesSeedService,
        DefaultTagsSeedService tagsSeedService,
        DefaultEmailTemplatesSeedService emailTemplatesSeedService,
        ComplianceFrameworksSeedService frameworksSeedService,
        DefaultPoliciesSeedService policiesSeedService,
        DefaultApprovalWorkflowsSeedService workflowsSeedService)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
        _rolesSeedService = rolesSeedService;
        _tagsSeedService = tagsSeedService;
        _emailTemplatesSeedService = emailTemplatesSeedService;
        _frameworksSeedService = frameworksSeedService;
        _policiesSeedService = policiesSeedService;
        _workflowsSeedService = workflowsSeedService;
    }

    /// <summary>
    /// Represents the result of a reset operation
    /// </summary>
    public class ResetResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public ResetStatistics Statistics { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Statistics from the reset operation
    /// </summary>
    public class ResetStatistics
    {
        public int WorkflowsDeleted { get; set; }
        public int CampaignsDeleted { get; set; }
        public int TagsDeleted { get; set; }
        public int EmailTemplatesDeleted { get; set; }
        public int PoliciesDeleted { get; set; }
        public int FrameworksDeleted { get; set; }
        public int UsersDeleted { get; set; }
        public int RolesDeleted { get; set; }

        public int RolesSeeded { get; set; }
        public int TagsSeeded { get; set; }
        public int EmailTemplatesSeeded { get; set; }
        public int FrameworksSeeded { get; set; }
        public int PoliciesSeeded { get; set; }
    }

    /// <summary>
    /// Options for what to reset
    /// </summary>
    public class ResetOptions
    {
        /// <summary>Reset workflows (keeps built-in templates)</summary>
        public bool ResetWorkflows { get; set; } = true;

        /// <summary>Reset access review campaigns</summary>
        public bool ResetCampaigns { get; set; } = true;

        /// <summary>Reset tags (keeps system tags)</summary>
        public bool ResetTags { get; set; } = true;

        /// <summary>Reset email templates (keeps built-in)</summary>
        public bool ResetEmailTemplates { get; set; } = true;

        /// <summary>Reset compliance policies (keeps built-in)</summary>
        public bool ResetPolicies { get; set; } = true;

        /// <summary>Reset compliance frameworks (keeps built-in)</summary>
        public bool ResetFrameworks { get; set; } = true;

        /// <summary>Reset users (keeps system users like admin)</summary>
        public bool ResetUsers { get; set; } = false;

        /// <summary>Reset roles (keeps system roles)</summary>
        public bool ResetRoles { get; set; } = false;

        /// <summary>Reset directory connections</summary>
        public bool ResetConnections { get; set; } = false;

        /// <summary>Reset sync projects</summary>
        public bool ResetSyncProjects { get; set; } = false;

        /// <summary>Reset synced identity data (people, objects, groups)</summary>
        public bool ResetIdentityData { get; set; } = false;
    }

    /// <summary>
    /// Performs a full reset to factory defaults based on options
    /// </summary>
    public async Task<ResetResult> ResetToDefaultsAsync(ResetOptions options)
    {
        var result = new ResetResult { Success = true };
        _logger.LogInformation("Starting reset to defaults operation...");

        try
        {
            // Phase 1: Delete user-created data
            _logger.LogInformation("Phase 1: Deleting user-created data...");

            if (options.ResetCampaigns)
            {
                result.Statistics.CampaignsDeleted = await DeleteCampaignsAsync();
            }

            if (options.ResetWorkflows)
            {
                result.Statistics.WorkflowsDeleted = await DeleteUserWorkflowsAsync();
            }

            if (options.ResetTags)
            {
                result.Statistics.TagsDeleted = await DeleteUserTagsAsync();
            }

            if (options.ResetEmailTemplates)
            {
                result.Statistics.EmailTemplatesDeleted = await DeleteUserEmailTemplatesAsync();
            }

            if (options.ResetPolicies)
            {
                result.Statistics.PoliciesDeleted = await DeleteUserPoliciesAsync();
            }

            if (options.ResetFrameworks)
            {
                result.Statistics.FrameworksDeleted = await DeleteUserFrameworksAsync();
            }

            if (options.ResetUsers)
            {
                result.Statistics.UsersDeleted = await DeleteNonSystemUsersAsync();
            }

            if (options.ResetRoles)
            {
                result.Statistics.RolesDeleted = await DeleteNonSystemRolesAsync();
            }

            // Delete in FK-safe order: identity data first, then sync projects, then connections
            if (options.ResetIdentityData)
            {
                await DeleteIdentityDataAsync();
            }

            if (options.ResetSyncProjects)
            {
                await DeleteSyncProjectsAsync();
            }

            if (options.ResetConnections)
            {
                await DeleteDirectoryConnectionsAsync();
            }

            // Phase 2: Re-seed built-in data
            _logger.LogInformation("Phase 2: Re-seeding built-in data...");

            if (options.ResetRoles)
            {
                await _rolesSeedService.SeedDefaultRolesAsync();
                await _rolesSeedService.EnsureSystemUsersHaveAdminRoleAsync();
            }

            if (options.ResetTags)
            {
                await _tagsSeedService.SeedDefaultTagsAsync();
            }

            if (options.ResetEmailTemplates)
            {
                await _emailTemplatesSeedService.SeedDefaultEmailTemplatesAsync();
            }

            if (options.ResetFrameworks)
            {
                await _frameworksSeedService.SeedComplianceFrameworksAsync();
            }

            if (options.ResetPolicies)
            {
                await _policiesSeedService.SeedDefaultPoliciesAsync();
            }

            result.Message = "Reset to defaults completed successfully!";
            _logger.LogInformation("Reset to defaults completed. Stats: {@Statistics}", result.Statistics);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Reset failed: {ex.Message}";
            result.Errors.Add(ex.ToString());
            _logger.LogError(ex, "Reset to defaults failed");
        }

        return result;
    }

    /// <summary>
    /// Clear all policy violations only.
    /// Preserves policies, rules, actions, and all other configuration.
    /// Violations will be re-detected on next policy evaluation.
    /// </summary>
    public async Task<ResetResult> ClearPolicyViolationsAsync()
    {
        var result = new ResetResult { Success = true };
        _logger.LogInformation("Clearing all policy violations...");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Delete all policy violations and related execution records
            var violationsDeleted = await connection.ExecuteAsync("DELETE FROM CompliancePolicyViolations");
            var executionsDeleted = await connection.ExecuteAsync("DELETE FROM CompliancePolicyExecutions");

            // Reset policy stats - use column names that exist in database
            // Handle both old and new column names
            try
            {
                await connection.ExecuteAsync(
                    "UPDATE CompliancePolicies SET LastViolationCount = 0, LastActionCount = 0, CurrentScope = 0");
            }
            catch
            {
                // Columns might not all exist, just log and continue
                _logger.LogDebug("Some policy stat columns may not exist - continuing");
            }

            result.Statistics.CampaignsDeleted = violationsDeleted; // Using this field for violations count
            result.Message = $"Successfully cleared {violationsDeleted} policy violations and {executionsDeleted} execution records.";
            _logger.LogInformation("Cleared {ViolationCount} policy violations and {ExecutionCount} execution records",
                violationsDeleted, executionsDeleted);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Failed to clear policy violations: {ex.Message}";
            result.Errors.Add(ex.ToString());
            _logger.LogError(ex, "Failed to clear policy violations");
        }

        return result;
    }

    /// <summary>
    /// Clear all access review data (campaigns, assignments, decisions).
    /// Preserves policies, synced identity data, and all configuration.
    /// </summary>
    public async Task<ResetResult> ClearAccessReviewsAsync()
    {
        var result = new ResetResult { Success = true };
        _logger.LogInformation("Clearing all access review data...");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            int decisionsDeleted = 0;
            int assignmentsDeleted = 0;
            int campaignsDeleted = 0;

            // Delete in correct order to respect foreign keys
            // Try to delete decisions if the table exists
            try
            {
                decisionsDeleted = await connection.ExecuteAsync("DELETE FROM AccessReviewDecisions");
            }
            catch { /* Table may not exist */ }

            // Delete assignments
            try
            {
                assignmentsDeleted = await connection.ExecuteAsync("DELETE FROM AccessReviewAssignments");
            }
            catch { /* Table may not exist */ }

            // Delete campaigns
            campaignsDeleted = await connection.ExecuteAsync("DELETE FROM Campaigns");

            result.Statistics.CampaignsDeleted = campaignsDeleted;
            result.Message = $"Successfully cleared {campaignsDeleted} campaigns, {assignmentsDeleted} assignments, and {decisionsDeleted} decisions.";
            _logger.LogInformation("Cleared {CampaignCount} campaigns, {AssignmentCount} assignments, {DecisionCount} decisions",
                campaignsDeleted, assignmentsDeleted, decisionsDeleted);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Failed to clear access reviews: {ex.Message}";
            result.Errors.Add(ex.ToString());
            _logger.LogError(ex, "Failed to clear access reviews");
        }

        return result;
    }

    /// <summary>
    /// Clear sync data only - deletes all synced identity data (objects, identities, groups, memberships)
    /// Preserves connections, sync projects, users, and all other configuration
    /// </summary>
    public Task<ResetResult> ClearSyncDataAsync()
    {
        return ResetToDefaultsAsync(new ResetOptions
        {
            ResetWorkflows = false,
            ResetCampaigns = false,
            ResetTags = false,
            ResetEmailTemplates = false,
            ResetPolicies = false,
            ResetFrameworks = false,
            ResetUsers = false,
            ResetRoles = false,
            ResetConnections = false,
            ResetSyncProjects = false,
            ResetIdentityData = true  // Only clear synced data
        });
    }

    /// <summary>
    /// Quick reset - resets everything except users, roles, connections, and identity data
    /// </summary>
    public Task<ResetResult> QuickResetAsync()
    {
        return ResetToDefaultsAsync(new ResetOptions
        {
            ResetWorkflows = true,
            ResetCampaigns = true,
            ResetTags = true,
            ResetEmailTemplates = true,
            ResetPolicies = true,
            ResetFrameworks = true,
            ResetUsers = false,
            ResetRoles = false,
            ResetConnections = false,
            ResetSyncProjects = false,
            ResetIdentityData = false
        });
    }

    /// <summary>
    /// Full factory reset - resets everything except directory connections and identity data
    /// </summary>
    public Task<ResetResult> FactoryResetAsync()
    {
        return ResetToDefaultsAsync(new ResetOptions
        {
            ResetWorkflows = true,
            ResetCampaigns = true,
            ResetTags = true,
            ResetEmailTemplates = true,
            ResetPolicies = true,
            ResetFrameworks = true,
            ResetUsers = true,
            ResetRoles = true,
            ResetConnections = false,
            ResetSyncProjects = true,
            ResetIdentityData = false
        });
    }

    /// <summary>
    /// Complete wipe - resets everything including connections and identity data
    /// WARNING: This will delete ALL synced data!
    /// </summary>
    public Task<ResetResult> CompleteWipeAsync()
    {
        return ResetToDefaultsAsync(new ResetOptions
        {
            ResetWorkflows = true,
            ResetCampaigns = true,
            ResetTags = true,
            ResetEmailTemplates = true,
            ResetPolicies = true,
            ResetFrameworks = true,
            ResetUsers = true,
            ResetRoles = true,
            ResetConnections = true,
            ResetSyncProjects = true,
            ResetIdentityData = true
        });
    }

    /// <summary>
    /// Re-seeds all built-in data without deleting anything.
    /// Safe operation - only adds missing items, skips existing ones.
    /// Use this to restore accidentally deleted built-in templates, tags, etc.
    /// </summary>
    public async Task<ReseedResult> ReseedDefaultsAsync()
    {
        var result = new ReseedResult { Success = true };
        _logger.LogInformation("Starting reseed of built-in defaults (non-destructive)...");

        try
        {
            // Reseed all built-in data - seed services skip existing items
            await _rolesSeedService.SeedDefaultRolesAsync();
            result.RolesSeeded = true;

            await _tagsSeedService.SeedDefaultTagsAsync();
            result.TagsSeeded = true;

            await _emailTemplatesSeedService.SeedDefaultEmailTemplatesAsync();
            result.EmailTemplatesSeeded = true;

            await _frameworksSeedService.SeedComplianceFrameworksAsync();
            result.FrameworksSeeded = true;

            await _policiesSeedService.SeedDefaultPoliciesAsync();
            result.PoliciesSeeded = true;

            await _workflowsSeedService.SeedDefaultWorkflowTemplatesAsync();
            result.WorkflowsSeeded = true;

            result.Message = "Reseed completed successfully! Any missing built-in items have been restored.";
            _logger.LogInformation("Reseed of built-in defaults completed successfully");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Reseed failed: {ex.Message}";
            result.Error = ex.ToString();
            _logger.LogError(ex, "Reseed of built-in defaults failed");
        }

        return result;
    }

    /// <summary>
    /// Result of a reseed operation
    /// </summary>
    public class ReseedResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Error { get; set; }
        public bool RolesSeeded { get; set; }
        public bool TagsSeeded { get; set; }
        public bool EmailTemplatesSeeded { get; set; }
        public bool FrameworksSeeded { get; set; }
        public bool PoliciesSeeded { get; set; }
        public bool WorkflowsSeeded { get; set; }
    }

    #region Delete Methods

    private async Task<int> DeleteUserWorkflowsAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Delete workflows that are NOT templates (user-created instances)
        var count = await connection.ExecuteAsync(
            "DELETE FROM ApprovalWorkflows WHERE IsTemplate = 0");

        _logger.LogInformation("Deleted {Count} user-created workflows", count);
        return count;
    }

    private async Task<int> DeleteCampaignsAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Delete all campaigns and related data
        var count = await connection.ExecuteAsync("DELETE FROM Campaigns");

        _logger.LogInformation("Deleted {Count} campaigns", count);
        return count;
    }

    private async Task<int> DeleteUserTagsAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Delete tags that are NOT system tags
        var count = await connection.ExecuteAsync(
            "DELETE FROM Tags WHERE IsSystem = 0");

        _logger.LogInformation("Deleted {Count} user-created tags", count);
        return count;
    }

    private async Task<int> DeleteUserEmailTemplatesAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Delete email templates that are NOT built-in
        var count = await connection.ExecuteAsync(
            "DELETE FROM EmailTemplates WHERE IsBuiltIn = 0");

        _logger.LogInformation("Deleted {Count} user-created email templates", count);
        return count;
    }

    private async Task<int> DeleteUserPoliciesAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // Get IDs of non-built-in policies
            var policyIds = (await connection.QueryAsync<Guid>(
                "SELECT Id FROM CompliancePolicies WHERE IsBuiltIn = 0",
                transaction: transaction)).ToList();

            if (policyIds.Count == 0)
            {
                await transaction.CommitAsync();
                _logger.LogInformation("Deleted 0 user-created compliance policies");
                return 0;
            }

            // Delete all dependent records first (cascade manually, respecting FK order)
            await connection.ExecuteAsync(
                "DELETE FROM ComplianceFrameworkPolicyMappings WHERE CompliancePolicyId IN @PolicyIds",
                new { PolicyIds = policyIds },
                transaction: transaction);

            await connection.ExecuteAsync(
                "DELETE FROM CompliancePolicyViolations WHERE CompliancePolicyId IN @PolicyIds",
                new { PolicyIds = policyIds },
                transaction: transaction);

            await connection.ExecuteAsync(
                "DELETE FROM CompliancePolicyExecutions WHERE CompliancePolicyId IN @PolicyIds",
                new { PolicyIds = policyIds },
                transaction: transaction);

            await connection.ExecuteAsync(
                "DELETE FROM CompliancePolicyRule WHERE CompliancePolicyId IN @PolicyIds",
                new { PolicyIds = policyIds },
                transaction: transaction);

            await connection.ExecuteAsync(
                "DELETE FROM CompliancePolicyAction WHERE CompliancePolicyId IN @PolicyIds",
                new { PolicyIds = policyIds },
                transaction: transaction);

            // Delete the policies
            var count = await connection.ExecuteAsync(
                "DELETE FROM CompliancePolicies WHERE IsBuiltIn = 0",
                transaction: transaction);

            await transaction.CommitAsync();

            _logger.LogInformation("Deleted {Count} user-created compliance policies", count);
            return count;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<int> DeleteUserFrameworksAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Delete frameworks that are NOT built-in
        var count = await connection.ExecuteAsync(
            "DELETE FROM ComplianceFrameworks WHERE IsBuiltIn = 0");

        _logger.LogInformation("Deleted {Count} user-created compliance frameworks", count);
        return count;
    }

    private async Task<int> DeleteNonSystemUsersAsync()
    {
        // Use UserManager for proper identity deletion (handles claims, roles, etc.)
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var userIds = await connection.QueryAsync<string>(
            "SELECT Id FROM AspNetUsers WHERE IsSystem = 0");

        var count = 0;
        foreach (var userId in userIds)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
                count++;
            }
        }

        _logger.LogInformation("Deleted {Count} non-system users", count);
        return count;
    }

    private async Task<int> DeleteNonSystemRolesAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var roleIds = (await connection.QueryAsync<string>(
            "SELECT Id FROM AspNetRoles WHERE IsSystem = 0")).ToList();

        if (roleIds.Count == 0)
        {
            _logger.LogInformation("Deleted 0 non-system roles");
            return 0;
        }

        // Remove user-role assignments first to avoid FK constraint violations
        await connection.ExecuteAsync(
            "DELETE FROM AspNetUserRoles WHERE RoleId IN @RoleIds",
            new { RoleIds = roleIds });

        // Also remove role claims
        await connection.ExecuteAsync(
            "DELETE FROM AspNetRoleClaims WHERE RoleId IN @RoleIds",
            new { RoleIds = roleIds });

        var count = 0;
        foreach (var roleId in roleIds)
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role != null)
            {
                await _roleManager.DeleteAsync(role);
                count++;
            }
        }

        _logger.LogInformation("Deleted {Count} non-system roles", count);
        return count;
    }

    private async Task DeleteSyncProjectsAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Delete entire sync infrastructure in FK-safe order
        // Use 5-minute timeout for large tables
        async Task SafeDelete(string table)
        {
            try { await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM [{table}]", commandTimeout: 300)); }
            catch (SqlException ex) when (ex.Number == 208) { /* table doesn't exist */ }
        }

        // Sync execution leaf tables first
        await SafeDelete("SyncAuditLogs");
        await SafeDelete("SyncStepRuns");
        await SafeDelete("PostSyncTasks");
        await SafeDelete("SyncProjectRuns");

        // Sync step children
        await SafeDelete("SyncStepScripts");
        await SafeDelete("SyncStepTags");
        await SafeDelete("AttributeMappings");
        await SafeDelete("HRFieldMappings");

        // Internal sync tables
        await SafeDelete("InternalSyncStepMappings");
        await SafeDelete("InternalSyncStepRuns");
        await SafeDelete("InternalSyncRuns");
        await SafeDelete("InternalSyncSteps");

        // Sync steps, workflows, chains, executions
        await SafeDelete("SyncSteps");
        await SafeDelete("SyncWorkflows");
        await SafeDelete("SyncProjectChains");
        await SafeDelete("SyncExecutions");

        // Finally the projects themselves
        var projectsDeleted = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM SyncProjects", commandTimeout: 300));

        _logger.LogInformation("Deleted {Count} sync projects and all related sync data", projectsDeleted);
    }

    private async Task DeleteDirectoryConnectionsAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Delete tables that reference DirectoryConnections
        try { await connection.ExecuteAsync(new CommandDefinition("DELETE FROM FrameworkAssignments", commandTimeout: 300)); }
        catch (SqlException ex) when (ex.Number == 208) { /* table doesn't exist */ }

        var count = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM DirectoryConnections", commandTimeout: 300));

        _logger.LogInformation("Deleted {Count} directory connections", count);
    }

    private async Task DeleteIdentityDataAsync()
    {
        _logger.LogWarning("Deleting ALL identity data - this is a destructive operation!");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Use 5-minute timeout for large tables
        async Task SafeDelete(string table)
        {
            try { await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM [{table}]", commandTimeout: 300)); }
            catch (SqlException ex) when (ex.Number == 208) { /* table doesn't exist */ }
        }

        // Clear self-referencing FKs first so rows can be deleted
        try { await connection.ExecuteAsync(new CommandDefinition("UPDATE Objects SET ManagerObjectId = NULL", commandTimeout: 300)); }
        catch (SqlException ex) when (ex.Number == 208) { }
        try { await connection.ExecuteAsync(new CommandDefinition("UPDATE Identities SET ManagerIdentityId = NULL", commandTimeout: 300)); }
        catch (SqlException ex) when (ex.Number == 208) { }

        // Leaf tables referencing Identities and Objects
        await SafeDelete("IdentityMatchLogs");
        await SafeDelete("BusinessRoleMembers");
        await SafeDelete("OrganizationalFolderMembers");
        await SafeDelete("IdentityGroupMemberships");
        await SafeDelete("IdentityTags");
        await SafeDelete("ObjectGroupMemberships");
        await SafeDelete("ObjectTags");
        await SafeDelete("ObjectAttributes");
        await SafeDelete("GroupAttributes");
        await SafeDelete("SyncAuditLogs");

        // Core tables
        var objectsDeleted = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM Objects", commandTimeout: 300));
        var groupsDeleted = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM Groups", commandTimeout: 300));
        var identitiesDeleted = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM Identities", commandTimeout: 300));

        _logger.LogInformation("Deleted all identity data: {Objects} objects, {Groups} groups, {Identities} identities",
            objectsDeleted, groupsDeleted, identitiesDeleted);
    }

    #endregion

    /// <summary>
    /// Marks all existing non-builtin data as built-in (for when you want to preserve current state as defaults)
    /// Useful for creating a baseline after initial configuration
    /// </summary>
    public async Task<int> MarkCurrentStateAsBuiltInAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            int count = 0;

            // Mark all workflows as templates
            count += await connection.ExecuteAsync(
                "UPDATE ApprovalWorkflows SET IsTemplate = 1 WHERE IsTemplate = 0",
                transaction: transaction);

            // Mark all tags as system
            count += await connection.ExecuteAsync(
                "UPDATE Tags SET IsSystem = 1 WHERE IsSystem = 0",
                transaction: transaction);

            // Mark all email templates as built-in
            count += await connection.ExecuteAsync(
                "UPDATE EmailTemplates SET IsBuiltIn = 1 WHERE IsBuiltIn = 0",
                transaction: transaction);

            // Mark all policies as built-in
            count += await connection.ExecuteAsync(
                "UPDATE CompliancePolicies SET IsBuiltIn = 1 WHERE IsBuiltIn = 0",
                transaction: transaction);

            // Mark all frameworks as built-in
            count += await connection.ExecuteAsync(
                "UPDATE ComplianceFrameworks SET IsBuiltIn = 1 WHERE IsBuiltIn = 0",
                transaction: transaction);

            await transaction.CommitAsync();

            _logger.LogInformation("Marked {Count} items as built-in/system", count);
            return count;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
