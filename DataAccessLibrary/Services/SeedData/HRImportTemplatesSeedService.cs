using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default HR import templates using Dapper.
/// Creates a built-in HRImport SyncProject template; idempotent (skips if one already exists).
/// Used by QuickSetupSeedOrchestrator for first-run setup.
/// Was previously the lone EF runtime-write in the seed pipeline; converted to Dapper to keep
/// ApplicationDbContext out of the runtime path (only used for ASP.NET Identity now).
/// </summary>
public class HRImportTemplatesSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<HRImportTemplatesSeedService> _logger;

    public HRImportTemplatesSeedService(
        IConfiguration configuration,
        ILogger<HRImportTemplatesSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        var existingId = await conn.QueryFirstOrDefaultAsync<Guid?>(
            @"SELECT TOP 1 Id FROM SyncProjects
              WHERE ProjectType = N'HRImport' AND IsBuiltIn = 1");

        if (existingId.HasValue)
        {
            _logger.LogInformation("HR Import templates already seeded - skipping (existing project {ProjectId})", existingId.Value);
            return;
        }

        var projectId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO SyncProjects (Id, Name, Description, ProjectType, IsEnabled, IsBuiltIn,
                IsTemplateMode, ConflictResolutionStrategy, AutoCreateIdentities, CreatedAt)
            VALUES (@Id, @Name, @Description, @ProjectType, @IsEnabled, @IsBuiltIn,
                @IsTemplateMode, @ConflictResolutionStrategy, @AutoCreateIdentities, GETUTCDATE())",
            new
            {
                Id = projectId,
                Name = "HR CSV Import Template",
                Description = "Built-in template for importing HR data from CSV files. " +
                              "Clone this project and configure a DirectoryConnection with HRCsv type to get started.",
                ProjectType = "HRImport",
                IsEnabled = false,
                IsBuiltIn = true,
                IsTemplateMode = true,
                ConflictResolutionStrategy = "SourceWins",
                AutoCreateIdentities = true
            });

        _logger.LogInformation("Created HR Import SyncProject template: {ProjectId}", projectId);
    }
}
