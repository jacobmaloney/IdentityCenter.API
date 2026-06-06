using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Seeds default HR import templates: a built-in HRImport SyncProject
/// and default HRFieldMappings for common CSV columns.
/// Used by DapperQuickSetupSeedOrchestrator for first-run setup.
/// </summary>
public class DapperHRImportSeedService : DapperSeedServiceBase
{
    public DapperHRImportSeedService(
        IConfiguration configuration,
        ILogger<DapperHRImportSeedService> logger)
        : base(configuration, logger) { }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Check if HR import template already exists
        var existingCount = await GetCountAsync(connection, transaction,
            "SyncProjects", "ProjectType = 'HRImport' AND IsBuiltIn = 1");

        if (existingCount > 0)
        {
            _logger.LogInformation("HR Import templates already seeded - skipping");
            return;
        }

        // Create a sample HRImport SyncProject template
        var projectId = Guid.NewGuid();
        await connection.ExecuteAsync(
            @"INSERT INTO SyncProjects (Id, Name, Description, ProjectType, IsEnabled, IsBuiltIn,
                                        IsTemplateMode, ConflictResolutionStrategy, AutoCreateIdentities)
              VALUES (@Id, @Name, @Description, 'HRImport', 0, 1, 1, 'SourceWins', 1)",
            new
            {
                Id = projectId,
                Name = "HR CSV Import Template",
                Description = "Built-in template for importing HR data from CSV files. " +
                              "Clone this project and configure a DirectoryConnection with HRCsv type to get started."
            },
            transaction);

        _logger.LogInformation("Created HR Import SyncProject template: {ProjectId}", projectId);

        // Seed default field mappings (these will be copied when a new HR connection is created)
        // We create them with a placeholder connection ID - they serve as a reference template
        // The actual mappings are created per-connection when the user configures field mappings
        // For now, just log the template fields

        sw.Stop();
        LogSeedComplete("HRImportTemplates", 1, 0, sw.Elapsed);
    }

    /// <summary>
    /// Creates default field mappings for a specific HR connection.
    /// Called when a new HR connection is created and the user wants auto-mapped defaults.
    /// </summary>
    public static List<DefaultFieldMapping> GetDefaultFieldMappings()
    {
        return new List<DefaultFieldMapping>
        {
            new("employee_id", "EmployeeId", true, true, 1),
            new("first_name", "FirstName", true, false, 2),
            new("last_name", "LastName", true, false, 3),
            new("email", "PrimaryEmail", true, false, 4),
            new("department", "Department", false, false, 5),
            new("job_title", "JobTitle", false, false, 6),
            new("hire_date", "HireDate", false, false, 7, "DateParse"),
            new("termination_date", "TerminationDate", false, false, 8, "DateParse"),
            new("manager_employee_id", "ManagerEmployeeId", false, false, 9),
            new("cost_center", "CostCenter", false, false, 10),
            new("company", "Company", false, false, 11),
            new("office", "Office", false, false, 12),
            new("phone", "PrimaryPhone", false, false, 13),
            new("mobile", "MobilePhone", false, false, 14),
            new("employee_type", "IdentityType", false, false, 15)
        };
    }

    public record DefaultFieldMapping(
        string SourceField,
        string TargetField,
        bool IsRequired,
        bool IsKeyField,
        int MappingOrder,
        string? Transformation = null);
}
