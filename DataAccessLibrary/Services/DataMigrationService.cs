using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using System.Data;

namespace DataAccessLibrary.Services;

/// <summary>
/// Service for migrating data between Certification Center databases during upgrades.
/// Implements blue-green style migration: create new database, migrate data, then cutover.
/// </summary>
public class DataMigrationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataMigrationService> _logger;

    public DataMigrationService(
        IConfiguration configuration,
        ILogger<DataMigrationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Categories of data that can be migrated (user-selectable).
    /// </summary>
    [Flags]
    public enum MigrationScope
    {
        None = 0,
        CoreConfiguration = 1,      // Settings, IdentityProviders, DirectoryConnections
        IdentityData = 2,           // Identities, Groups, Objects, Memberships
        ComplianceData = 4,         // Frameworks, Policies, Assignments
        AuditHistory = 8,           // AuditLogs, ChangeAuditLogs, SyncAuditLogs
        AnalyticsData = 16,         // Report executions, job history
        Templates = 32,             // Email, Teams, Workflow templates (if customized)
        All = CoreConfiguration | IdentityData | ComplianceData | AuditHistory | AnalyticsData | Templates
    }

    /// <summary>
    /// Migrates data from a source database to the target (current) database.
    /// </summary>
    public async Task<DataMigrationResult> MigrateDataAsync(
        string sourceConnectionString,
        MigrationScope scope,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new DataMigrationResult
        {
            StartTime = DateTime.UtcNow,
            Scope = scope
        };

        try
        {
            _logger.LogInformation("Starting data migration with scope: {Scope}", scope);
            progress?.Report(new MigrationProgress { Phase = "Initializing", PercentComplete = 0 });

            // Validate source database connection
            if (!await ValidateSourceConnectionAsync(sourceConnectionString, cancellationToken))
            {
                result.Success = false;
                result.ErrorMessage = "Could not connect to source database";
                return result;
            }

            // Detect source database version
            var sourceVersion = await DetectSourceVersionAsync(sourceConnectionString, cancellationToken);
            result.SourceVersion = sourceVersion;
            _logger.LogInformation("Source database version: {Version}", sourceVersion);
            progress?.Report(new MigrationProgress { Phase = "Source version detected", PercentComplete = 5, Message = $"Version: {sourceVersion}" });

            var targetConnectionString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection connection string not found in configuration");

            int totalSteps = GetTotalSteps(scope);
            int currentStep = 0;

            // Migrate each category based on scope
            if (scope.HasFlag(MigrationScope.CoreConfiguration))
            {
                progress?.Report(new MigrationProgress { Phase = "Core Configuration", PercentComplete = GetPercent(++currentStep, totalSteps) });
                var coreResult = await MigrateCoreConfigurationAsync(sourceConnectionString, targetConnectionString, cancellationToken);
                result.CoreConfigMigrated = coreResult;
                result.RecordsMigrated += coreResult.RecordCount;
            }

            if (scope.HasFlag(MigrationScope.IdentityData))
            {
                progress?.Report(new MigrationProgress { Phase = "Identity Data", PercentComplete = GetPercent(++currentStep, totalSteps) });
                var identityResult = await MigrateIdentityDataAsync(sourceConnectionString, targetConnectionString, cancellationToken);
                result.IdentityDataMigrated = identityResult;
                result.RecordsMigrated += identityResult.RecordCount;
            }

            if (scope.HasFlag(MigrationScope.ComplianceData))
            {
                progress?.Report(new MigrationProgress { Phase = "Compliance Data", PercentComplete = GetPercent(++currentStep, totalSteps) });
                var complianceResult = await MigrateComplianceDataAsync(sourceConnectionString, targetConnectionString, cancellationToken);
                result.ComplianceDataMigrated = complianceResult;
                result.RecordsMigrated += complianceResult.RecordCount;
            }

            if (scope.HasFlag(MigrationScope.AuditHistory))
            {
                progress?.Report(new MigrationProgress { Phase = "Audit History", PercentComplete = GetPercent(++currentStep, totalSteps) });
                var auditResult = await MigrateAuditHistoryAsync(sourceConnectionString, targetConnectionString, cancellationToken);
                result.AuditHistoryMigrated = auditResult;
                result.RecordsMigrated += auditResult.RecordCount;
            }

            if (scope.HasFlag(MigrationScope.AnalyticsData))
            {
                progress?.Report(new MigrationProgress { Phase = "Analytics Data", PercentComplete = GetPercent(++currentStep, totalSteps) });
                var analyticsResult = await MigrateAnalyticsDataAsync(sourceConnectionString, targetConnectionString, cancellationToken);
                result.AnalyticsDataMigrated = analyticsResult;
                result.RecordsMigrated += analyticsResult.RecordCount;
            }

            if (scope.HasFlag(MigrationScope.Templates))
            {
                progress?.Report(new MigrationProgress { Phase = "Templates", PercentComplete = GetPercent(++currentStep, totalSteps) });
                var templatesResult = await MigrateTemplatesAsync(sourceConnectionString, targetConnectionString, cancellationToken);
                result.TemplatesMigrated = templatesResult;
                result.RecordsMigrated += templatesResult.RecordCount;
            }

            result.Success = true;
            result.EndTime = DateTime.UtcNow;
            progress?.Report(new MigrationProgress { Phase = "Complete", PercentComplete = 100, Message = $"Migrated {result.RecordsMigrated} records" });

            _logger.LogInformation("Data migration completed successfully. {Records} records migrated in {Duration}",
                result.RecordsMigrated, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data migration failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
            return result;
        }
    }

    /// <summary>
    /// Validates that we can connect to the source database.
    /// </summary>
    public async Task<bool> ValidateSourceConnectionAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate source connection");
            return false;
        }
    }

    /// <summary>
    /// Detects the version of the source database by examining the migration history.
    /// </summary>
    public async Task<string> DetectSourceVersionAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var result = await connection.ExecuteScalarAsync<string>(
                @"SELECT TOP 1 MigrationId
                  FROM __EFMigrationsHistory
                  ORDER BY MigrationId DESC");

            if (result != null)
            {
                // Extract version from migration ID (format: YYYYMMDDHHMMSS_MigrationName)
                var timestamp = result.Split('_')[0];
                return $"Migration: {timestamp}";
            }

            return "Unknown";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect source version");
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets a preview of what data would be migrated.
    /// </summary>
    public async Task<MigrationPreview> GetMigrationPreviewAsync(
        string sourceConnectionString,
        MigrationScope scope,
        CancellationToken cancellationToken = default)
    {
        var preview = new MigrationPreview { Scope = scope };

        try
        {
            await using var connection = new SqlConnection(sourceConnectionString);
            await connection.OpenAsync(cancellationToken);

            if (scope.HasFlag(MigrationScope.CoreConfiguration))
            {
                preview.SettingsCount = await GetTableCountAsync(connection, "Settings");
                preview.IdentityProvidersCount = await GetTableCountAsync(connection, "IdentityProviders");
                preview.DirectoryConnectionsCount = await GetTableCountAsync(connection, "DirectoryConnections");
            }

            if (scope.HasFlag(MigrationScope.IdentityData))
            {
                preview.IdentitiesCount = await GetTableCountAsync(connection, "Identities");
                preview.ObjectsCount = await GetTableCountAsync(connection, "Objects");
                preview.GroupsCount = await GetTableCountAsync(connection, "Groups");
                preview.MembershipsCount = await GetTableCountAsync(connection, "IdentityGroupMemberships") +
                                          await GetTableCountAsync(connection, "ObjectGroupMemberships");
            }

            if (scope.HasFlag(MigrationScope.ComplianceData))
            {
                preview.ComplianceFrameworksCount = await GetTableCountAsync(connection, "ComplianceFrameworks");
                preview.CompliancePoliciesCount = await GetTableCountAsync(connection, "CompliancePolicies");
            }

            if (scope.HasFlag(MigrationScope.AuditHistory))
            {
                preview.AuditLogsCount = await GetTableCountAsync(connection, "AuditLogs");
                preview.SyncAuditLogsCount = await GetTableCountAsync(connection, "SyncAuditLogs");
            }

            preview.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not generate migration preview");
            preview.Success = false;
            preview.ErrorMessage = ex.Message;
        }

        return preview;
    }

    private async Task<int> GetTableCountAsync(SqlConnection connection, string tableName)
    {
        try
        {
            return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{tableName}]");
        }
        catch
        {
            return 0; // Table might not exist in source
        }
    }

    private int GetTotalSteps(MigrationScope scope)
    {
        int steps = 0;
        if (scope.HasFlag(MigrationScope.CoreConfiguration)) steps++;
        if (scope.HasFlag(MigrationScope.IdentityData)) steps++;
        if (scope.HasFlag(MigrationScope.ComplianceData)) steps++;
        if (scope.HasFlag(MigrationScope.AuditHistory)) steps++;
        if (scope.HasFlag(MigrationScope.AnalyticsData)) steps++;
        if (scope.HasFlag(MigrationScope.Templates)) steps++;
        return Math.Max(steps, 1);
    }

    private int GetPercent(int current, int total) => (int)((current / (double)total) * 100);

    #region Migration Methods

    private async Task<CategoryMigrationResult> MigrateCoreConfigurationAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        var result = new CategoryMigrationResult { Category = "Core Configuration" };

        try
        {
            // Migrate Settings (non-default ones)
            result.RecordCount += await BulkCopyTableAsync(source, target, "Settings",
                "SELECT * FROM Settings WHERE Id > 3", cancellationToken); // Skip seeded defaults

            // Migrate IdentityProviders
            result.RecordCount += await BulkCopyTableAsync(source, target, "IdentityProviders",
                "SELECT * FROM IdentityProviders", cancellationToken);

            // Migrate DirectoryConnections
            result.RecordCount += await BulkCopyTableAsync(source, target, "DirectoryConnections",
                "SELECT * FROM DirectoryConnections", cancellationToken);

            // Migrate SystemConfigurations (non-default)
            result.RecordCount += await BulkCopyTableAsync(source, target, "SystemConfigurations",
                "SELECT * FROM SystemConfigurations WHERE Id > 1", cancellationToken);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate core configuration");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<CategoryMigrationResult> MigrateIdentityDataAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        var result = new CategoryMigrationResult { Category = "Identity Data" };

        try
        {
            // Order matters due to foreign key constraints

            // 1. Identities first (no FK dependencies)
            result.RecordCount += await BulkCopyTableAsync(source, target, "Identities",
                "SELECT * FROM Identities", cancellationToken);

            // 2. Groups (depends on DirectoryConnections)
            result.RecordCount += await BulkCopyTableAsync(source, target, "Groups",
                "SELECT * FROM Groups", cancellationToken);

            // 3. Objects (depends on Identities and DirectoryConnections)
            result.RecordCount += await BulkCopyTableAsync(source, target, "Objects",
                "SELECT * FROM Objects", cancellationToken);

            // 4. Attributes
            result.RecordCount += await BulkCopyTableAsync(source, target, "ObjectAttributes",
                "SELECT * FROM ObjectAttributes", cancellationToken);
            result.RecordCount += await BulkCopyTableAsync(source, target, "GroupAttributes",
                "SELECT * FROM GroupAttributes", cancellationToken);

            // 5. Memberships
            result.RecordCount += await BulkCopyTableAsync(source, target, "ObjectGroupMemberships",
                "SELECT * FROM ObjectGroupMemberships", cancellationToken);
            result.RecordCount += await BulkCopyTableAsync(source, target, "IdentityGroupMemberships",
                "SELECT * FROM IdentityGroupMemberships", cancellationToken);

            // 6. Tags
            result.RecordCount += await BulkCopyTableAsync(source, target, "Tags",
                "SELECT * FROM Tags WHERE IsSystem = 0", cancellationToken); // Only custom tags
            result.RecordCount += await BulkCopyTableAsync(source, target, "ObjectTags",
                "SELECT * FROM ObjectTags", cancellationToken);
            result.RecordCount += await BulkCopyTableAsync(source, target, "IdentityTags",
                "SELECT * FROM IdentityTags", cancellationToken);

            // 7. Match logs
            result.RecordCount += await BulkCopyTableAsync(source, target, "IdentityMatchLogs",
                "SELECT * FROM IdentityMatchLogs", cancellationToken);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate identity data");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<CategoryMigrationResult> MigrateComplianceDataAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        var result = new CategoryMigrationResult { Category = "Compliance Data" };

        try
        {
            // Note: System frameworks and policies are seeded, so we skip IsSystem = 1 items
            // and only migrate custom/user-created items

            result.RecordCount += await BulkCopyTableAsync(source, target, "ComplianceFrameworks",
                "SELECT * FROM ComplianceFrameworks WHERE IsSystem = 0", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "CompliancePolicies",
                "SELECT * FROM CompliancePolicies WHERE IsSystem = 0", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "FrameworkAssignments",
                "SELECT * FROM FrameworkAssignments", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "CompliancePolicyViolations",
                "SELECT * FROM CompliancePolicyViolations", cancellationToken);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate compliance data");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<CategoryMigrationResult> MigrateAuditHistoryAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        var result = new CategoryMigrationResult { Category = "Audit History" };

        try
        {
            // Audit logs can be large - migrate in batches
            result.RecordCount += await BulkCopyTableAsync(source, target, "AuditLogs",
                "SELECT * FROM AuditLogs", cancellationToken, batchSize: 10000);

            result.RecordCount += await BulkCopyTableAsync(source, target, "ChangeAuditLogs",
                "SELECT * FROM ChangeAuditLogs", cancellationToken, batchSize: 10000);

            result.RecordCount += await BulkCopyTableAsync(source, target, "SyncAuditLogs",
                "SELECT * FROM SyncAuditLogs", cancellationToken, batchSize: 10000);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate audit history");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<CategoryMigrationResult> MigrateAnalyticsDataAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        var result = new CategoryMigrationResult { Category = "Analytics Data" };

        try
        {
            result.RecordCount += await BulkCopyTableAsync(source, target, "ReportExecutions",
                "SELECT * FROM ReportExecutions", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "JobExecutionHistory",
                "SELECT * FROM JobExecutionHistory", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "SyncProjectRuns",
                "SELECT * FROM SyncProjectRuns", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "SyncStepRuns",
                "SELECT * FROM SyncStepRuns", cancellationToken);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate analytics data");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<CategoryMigrationResult> MigrateTemplatesAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        var result = new CategoryMigrationResult { Category = "Templates" };

        try
        {
            // Only migrate custom (non-system) templates
            result.RecordCount += await BulkCopyTableAsync(source, target, "EmailTemplates",
                "SELECT * FROM EmailTemplates WHERE IsSystem = 0", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "TeamsMessageTemplates",
                "SELECT * FROM TeamsMessageTemplates WHERE IsSystem = 0", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "ApprovalWorkflows",
                "SELECT * FROM ApprovalWorkflows WHERE IsTemplate = 0", cancellationToken);

            result.RecordCount += await BulkCopyTableAsync(source, target, "SyncProjectTemplates",
                "SELECT * FROM SyncProjectTemplates WHERE IsSystem = 0", cancellationToken);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate templates");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<int> BulkCopyTableAsync(
        string sourceConnectionString,
        string targetConnectionString,
        string tableName,
        string selectQuery,
        CancellationToken cancellationToken,
        int batchSize = 5000)
    {
        try
        {
            await using var sourceConnection = new SqlConnection(sourceConnectionString);
            await sourceConnection.OpenAsync(cancellationToken);

            using var command = sourceConnection.CreateCommand();
            command.CommandText = selectQuery;
            command.CommandTimeout = 300; // 5 minutes

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            await using var targetConnection = new SqlConnection(targetConnectionString);
            await targetConnection.OpenAsync(cancellationToken);

            // Enable identity insert if needed
            try
            {
                await targetConnection.ExecuteAsync($"SET IDENTITY_INSERT [{tableName}] ON");
            }
            catch
            {
                // Ignore if table doesn't have identity
            }

            using var bulkCopy = new SqlBulkCopy(targetConnection)
            {
                DestinationTableName = tableName,
                BatchSize = batchSize,
                BulkCopyTimeout = 600 // 10 minutes
            };

            // Map columns
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                bulkCopy.ColumnMappings.Add(columnName, columnName);
            }

            await bulkCopy.WriteToServerAsync(reader, cancellationToken);

            // Disable identity insert
            try
            {
                await targetConnection.ExecuteAsync($"SET IDENTITY_INSERT [{tableName}] OFF");
            }
            catch
            {
                // Ignore
            }

            _logger.LogInformation("Migrated {Table}: {Count} rows", tableName, bulkCopy.RowsCopied);
            return (int)bulkCopy.RowsCopied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not migrate table {Table} (may not exist in source)", tableName);
            return 0;
        }
    }

    #endregion
}

#region Result Classes

public class DataMigrationResult
{
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;

    public string? SourceVersion { get; set; }
    public DataMigrationService.MigrationScope Scope { get; set; }
    public int RecordsMigrated { get; set; }

    public CategoryMigrationResult? CoreConfigMigrated { get; set; }
    public CategoryMigrationResult? IdentityDataMigrated { get; set; }
    public CategoryMigrationResult? ComplianceDataMigrated { get; set; }
    public CategoryMigrationResult? AuditHistoryMigrated { get; set; }
    public CategoryMigrationResult? AnalyticsDataMigrated { get; set; }
    public CategoryMigrationResult? TemplatesMigrated { get; set; }

    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
}

public class CategoryMigrationResult
{
    public string Category { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int RecordCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class MigrationProgress
{
    public string Phase { get; set; } = string.Empty;
    public int PercentComplete { get; set; }
    public string? Message { get; set; }
}

public class MigrationPreview
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DataMigrationService.MigrationScope Scope { get; set; }

    // Core Configuration
    public int SettingsCount { get; set; }
    public int IdentityProvidersCount { get; set; }
    public int DirectoryConnectionsCount { get; set; }

    // Identity Data
    public int IdentitiesCount { get; set; }
    public int ObjectsCount { get; set; }
    public int GroupsCount { get; set; }
    public int MembershipsCount { get; set; }

    // Compliance
    public int ComplianceFrameworksCount { get; set; }
    public int CompliancePoliciesCount { get; set; }

    // Audit
    public int AuditLogsCount { get; set; }
    public int SyncAuditLogsCount { get; set; }

    public int TotalRecords =>
        SettingsCount + IdentityProvidersCount + DirectoryConnectionsCount +
        IdentitiesCount + ObjectsCount + GroupsCount + MembershipsCount +
        ComplianceFrameworksCount + CompliancePoliciesCount +
        AuditLogsCount + SyncAuditLogsCount;
}

#endregion
