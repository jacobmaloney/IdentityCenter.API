using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;

namespace DataAccessLibrary.Services;

/// <summary>
/// Service for verifying database schema integrity after migrations.
/// Ensures all expected tables exist and are accessible.
/// </summary>
public class SchemaVerificationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SchemaVerificationService> _logger;

    // Expected core tables that must exist for Certification Center to function
    private static readonly string[] CoreTables = new[]
    {
        // ASP.NET Identity tables
        "AspNetUsers",
        "AspNetRoles",
        "AspNetUserRoles",
        "AspNetUserClaims",
        "AspNetUserLogins",
        "AspNetUserTokens",
        "AspNetRoleClaims",

        // Core configuration
        "Settings",
        "SystemConfigurations",
        "IdentityProviders",
        "DirectoryConnections",

        // Audit
        "AuditLogs",
        "ChangeAuditLogs",

        // Identity management (refactored schema)
        "Identities",
        "Objects",
        "ObjectAttributes",
        "Groups",
        "GroupAttributes",
        "ObjectGroupMemberships",
        "IdentityGroupMemberships",
        "IdentityMatchLogs",

        // Tagging system
        "Tags",
        "ObjectTags",
        "IdentityTags",
        "MembershipTags",
        "WorkflowTags",
        "SyncStepTags",

        // Sync management
        "SyncProjects",
        "SyncProjectChains",
        "SyncWorkflows",
        "SyncSteps",
        "AttributeMappings",
        "SyncProjectRuns",
        "SyncStepRuns",
        "SyncAuditLogs",
        "SyncExecutions",
        "PostSyncTasks",

        // Internal sync
        "InternalSyncRuns",
        "InternalSyncSteps",
        "InternalSyncStepMappings",
        "InternalSyncStepRuns",

        // Dev Center scripts
        "SyncProcessingScripts",
        "SyncStepScripts",
        "SyncScriptExecutions",

        // Templates
        "SyncProjectTemplates",
        "SyncWorkflowTemplates",
        "ScheduleTemplates",

        // Access management
        "AccessRequests",
        "ApprovalWorkflows",
        "ApprovalWorkflowNodes",
        "ApprovalWorkflowConnections",
        "UserAccess",

        // Policy management
        "AccessPolicies",
        "PolicyConditions",
        "PolicyActions",

        // Compliance
        "ComplianceFrameworks",
        "CompliancePolicies",
        "ComplianceFrameworkPolicyMappings",
        "CompliancePolicyExecutions",
        "CompliancePolicyViolations",
        "CompliancePolicyAction",
        "CompliancePolicyRule",
        "FrameworkAssignments",
        "FrameworkAssignmentPolicyOverrides",

        // Email and Notifications
        "SMTPConfiguration",
        "EmailTemplates",
        "EmailQueue",
        "TeamsMessageTemplates",
        "TeamsMessageQueue",
        "AdminNotifications",

        // Ticketing
        "TicketingConfigurations",
        "TicketingLogs",

        // Maintenance
        "MaintenanceSettings",

        // Access Review
        "Campaigns",
        "AccessReviewAssignments",
        "ReviewDecisionHistory",
        "RemediationActions",
        "CampaignTemplates",
        "AccessReviewSettings",

        // Reporting
        "Reports",
        "ReportColumns",
        "ReportParameters",
        "ReportSchedules",
        "ReportExecutions",
        "UserReportFavorites",
        "ReportTemplates",

        // Job scheduling
        "JobExecutionHistory",
        "RemoteAgents",
        "JobQueue",
        "ApiKeys",

        // Business roles
        "BusinessRoles",
        "BusinessRoleMembers",
        "BusinessRoleCategories",

        // Workflow triggers
        "WorkflowTriggers",
        "TriggerConditions",
        "TriggerActions",
        "TriggerEvents",
        "TriggerExecutions",
        "TriggerActionLogs",
        "WorkflowTriggerTemplates",
        "WorkflowStep",

        // Organization
        "OrganizationalFolders",
        "OrganizationalFolderMembers",
        "OrganizationalFolderPolicies"
    };

    public SchemaVerificationService(
        IConfiguration configuration,
        ILogger<SchemaVerificationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    /// <summary>
    /// Verifies that all expected database tables exist and are accessible.
    /// </summary>
    public async Task<SchemaVerificationResult> VerifySchemaAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString();
        return await VerifySchemaWithConnectionStringAsync(connectionString, cancellationToken);
    }

    /// <summary>
    /// Verifies schema using a specific connection string. Use this during setup wizard
    /// when the default connection string hasn't been configured yet.
    /// </summary>
    public async Task<SchemaVerificationResult> VerifySchemaAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        return await VerifySchemaWithConnectionStringAsync(connectionString, cancellationToken);
    }

    /// <summary>
    /// Internal method that performs schema verification with a given connection string.
    /// </summary>
    private async Task<SchemaVerificationResult> VerifySchemaWithConnectionStringAsync(string connectionString, CancellationToken cancellationToken)
    {
        var result = new SchemaVerificationResult
        {
            StartTime = DateTime.UtcNow,
            ExpectedTableCount = CoreTables.Length
        };

        try
        {
            _logger.LogInformation("Starting schema verification. Checking {TableCount} expected tables...", CoreTables.Length);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get all tables from the database
            var existingTables = await GetExistingTablesAsync(connection, cancellationToken);
            result.ActualTableCount = existingTables.Count;

            _logger.LogInformation("Found {ActualCount} tables in database", existingTables.Count);

            // Find missing tables
            var missingTables = CoreTables
                .Where(t => !existingTables.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToList();

            result.MissingTables = missingTables;
            result.VerifiedTables = CoreTables
                .Where(t => existingTables.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToList();

            // Find extra tables (not in our expected list - informational only)
            result.ExtraTables = existingTables
                .Where(t => !CoreTables.Contains(t, StringComparer.OrdinalIgnoreCase))
                .Where(t => !t.StartsWith("__")) // Exclude EF migration history
                .ToList();

            // Check if migrations table exists and has entries
            result.MigrationsApplied = await GetAppliedMigrationsCountAsync(connection, cancellationToken);

            result.Success = !missingTables.Any();
            result.EndTime = DateTime.UtcNow;

            if (result.Success)
            {
                _logger.LogInformation(
                    "Schema verification PASSED. All {Count} core tables verified. {MigrationCount} migrations applied.",
                    result.VerifiedTables.Count,
                    result.MigrationsApplied);
            }
            else
            {
                _logger.LogError(
                    "Schema verification FAILED. Missing {MissingCount} tables: {MissingTables}",
                    missingTables.Count,
                    string.Join(", ", missingTables));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema verification encountered an error");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
            return result;
        }
    }

    /// <summary>
    /// Gets all table names from the database.
    /// </summary>
    private async Task<List<string>> GetExistingTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = new List<string>();

        try
        {
            const string sql = @"
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                AND TABLE_SCHEMA = 'dbo'
                ORDER BY TABLE_NAME";

            var commandDefinition = new CommandDefinition(sql, cancellationToken: cancellationToken);
            var result = await connection.QueryAsync<string>(commandDefinition);
            tables = result.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve table list from database");
        }

        return tables;
    }

    /// <summary>
    /// Gets the count of applied EF Core migrations.
    /// </summary>
    private async Task<int> GetAppliedMigrationsCountAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            // Check if __EFMigrationsHistory table exists first
            const string checkTableSql = @"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = '__EFMigrationsHistory'
                AND TABLE_SCHEMA = 'dbo'";

            var checkCommand = new CommandDefinition(checkTableSql, cancellationToken: cancellationToken);
            var tableExists = await connection.ExecuteScalarAsync<int>(checkCommand);

            if (tableExists == 0)
            {
                return 0;
            }

            // Count migrations
            const string countSql = "SELECT COUNT(*) FROM [__EFMigrationsHistory]";
            var countCommand = new CommandDefinition(countSql, cancellationToken: cancellationToken);
            return await connection.ExecuteScalarAsync<int>(countCommand);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve migration count");
            return 0;
        }
    }

    /// <summary>
    /// Performs a quick health check on the database connection and basic schema.
    /// </summary>
    public async Task<bool> QuickHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = GetConnectionString();
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Test basic connectivity by querying the Settings table
            const string sql = "SELECT TOP 1 * FROM [Settings]";
            var commandDefinition = new CommandDefinition(sql, cancellationToken: cancellationToken);
            await connection.QueryFirstOrDefaultAsync(commandDefinition);

            _logger.LogDebug("Quick health check passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quick health check failed");
            return false;
        }
    }

    /// <summary>
    /// Gets a summary of the database schema status.
    /// </summary>
    public async Task<string> GetSchemaSummaryAsync(CancellationToken cancellationToken = default)
    {
        var result = await VerifySchemaAsync(cancellationToken);
        return result.GetSummary();
    }
}

/// <summary>
/// Result of a schema verification operation.
/// </summary>
public class SchemaVerificationResult
{
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;

    public int ExpectedTableCount { get; set; }
    public int ActualTableCount { get; set; }
    public int MigrationsApplied { get; set; }

    public List<string> MissingTables { get; set; } = new();
    public List<string> VerifiedTables { get; set; } = new();
    public List<string> ExtraTables { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }

    public string GetSummary()
    {
        if (!Success && !string.IsNullOrEmpty(ErrorMessage))
        {
            return $"Schema verification failed: {ErrorMessage}";
        }

        var summary = new System.Text.StringBuilder();
        summary.AppendLine("=== Schema Verification Summary ===");
        summary.AppendLine($"Status: {(Success ? "PASSED" : "FAILED")}");
        summary.AppendLine($"Duration: {Duration.TotalMilliseconds:F0}ms");
        summary.AppendLine($"Expected tables: {ExpectedTableCount}");
        summary.AppendLine($"Verified tables: {VerifiedTables.Count}");
        summary.AppendLine($"Actual tables in database: {ActualTableCount}");
        summary.AppendLine($"Migrations applied: {MigrationsApplied}");

        if (MissingTables.Any())
        {
            summary.AppendLine();
            summary.AppendLine($"MISSING TABLES ({MissingTables.Count}):");
            foreach (var table in MissingTables.Take(20))
            {
                summary.AppendLine($"  - {table}");
            }
            if (MissingTables.Count > 20)
            {
                summary.AppendLine($"  ... and {MissingTables.Count - 20} more");
            }
        }

        return summary.ToString();
    }
}
