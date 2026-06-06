using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds built-in PersonMatch and PersonCreate sync project templates.
/// These templates can be copied and customized by users.
/// </summary>
public class PersonMatchTemplatesSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<PersonMatchTemplatesSeedService> _logger;

    // Fixed GUIDs for built-in templates - allows consistent referencing
    public static readonly Guid PersonMatchTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111001");
    public static readonly Guid PersonCreateTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111002");

    // Fixed GUIDs for workflows
    public static readonly Guid PersonMatchWorkflowId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    public static readonly Guid PersonCreateWorkflowId = Guid.Parse("11111111-1111-1111-1111-111111111102");

    // Fixed GUIDs for steps
    public static readonly Guid PersonMatchStepId = Guid.Parse("11111111-1111-1111-1111-111111111201");
    public static readonly Guid PersonCreateMatchStepId = Guid.Parse("11111111-1111-1111-1111-111111111202");
    public static readonly Guid PersonCreateNewStepId = Guid.Parse("11111111-1111-1111-1111-111111111203");

    public PersonMatchTemplatesSeedService(
        IConfiguration configuration,
        ILogger<PersonMatchTemplatesSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds built-in PersonMatch and PersonCreate sync project templates.
    /// </summary>
    public async Task SeedPersonMatchTemplatesAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Quick check - if templates already exist, skip
        var existingCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SyncProjects WHERE IsBuiltIn = 1 AND (ProjectType = 'PersonMatch' OR ProjectType = 'PersonCreate')");

        if (existingCount >= 2)
        {
            _logger.LogDebug("PersonMatch templates already seeded ({Count} found), skipping", existingCount);
            return;
        }

        _logger.LogInformation("Seeding PersonMatch and PersonCreate sync project templates");

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // Seed PersonMatch template
            await SeedPersonMatchTemplateAsync(connection, transaction);

            // Seed PersonCreate template
            await SeedPersonCreateTemplateAsync(connection, transaction);

            await transaction.CommitAsync();
            _logger.LogInformation("PersonMatch templates seeded successfully");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task SeedPersonMatchTemplateAsync(SqlConnection connection, System.Data.Common.DbTransaction transaction)
    {
        var existing = await connection.QueryFirstOrDefaultAsync<SyncProject>(
            "SELECT * FROM SyncProjects WHERE Id = @Id",
            new { Id = PersonMatchTemplateId },
            transaction);

        if (existing != null)
        {
            // Update ProjectType if needed
            if (existing.ProjectType != "PersonMatch")
            {
                await connection.ExecuteAsync(
                    "UPDATE SyncProjects SET ProjectType = 'PersonMatch' WHERE Id = @Id",
                    new { Id = PersonMatchTemplateId },
                    transaction);
                _logger.LogInformation("Updated PersonMatch template ProjectType");
            }

            // Check if workflow exists
            var hasWorkflow = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM SyncWorkflows WHERE SyncProjectId = @ProjectId",
                new { ProjectId = PersonMatchTemplateId },
                transaction) > 0;

            if (!hasWorkflow)
            {
                await AddPersonMatchWorkflowAsync(connection, transaction, PersonMatchTemplateId);
            }
            return;
        }

        const string insertProjectSql = @"
            INSERT INTO SyncProjects
                (Id, Name, Description, ProjectType, IsBuiltIn, IsReadOnly, IsEnabled,
                 IdentityMatchingStrategy, MinMatchConfidenceThreshold, LogLevel,
                 PauseOnError, MaxErrorsBeforePause, CreatedAt, CreatedBy)
            VALUES
                (@Id, @Name, @Description, @ProjectType, @IsBuiltIn, @IsReadOnly, @IsEnabled,
                 @IdentityMatchingStrategy, @MinMatchConfidenceThreshold, @LogLevel,
                 @PauseOnError, @MaxErrorsBeforePause, @CreatedAt, @CreatedBy)";

        var template = new
        {
            Id = PersonMatchTemplateId,
            Name = "[Template] Person Match - Link to Existing Identities",
            Description = "Links synced Objects to existing Identities by matching email, employee ID, UPN, or name. " +
                         "Does NOT create new Identities - only links to existing ones. " +
                         "Copy this template and configure a source sync project to process.",
            ProjectType = "PersonMatch",
            IsBuiltIn = true,
            IsReadOnly = true,
            IsEnabled = false,
            IdentityMatchingStrategy = "composite",
            MinMatchConfidenceThreshold = 75,
            LogLevel = "Information",
            PauseOnError = true,
            MaxErrorsBeforePause = 10,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "System"
        };

        await connection.ExecuteAsync(insertProjectSql, template, transaction);
        await AddPersonMatchWorkflowAsync(connection, transaction, PersonMatchTemplateId);
        _logger.LogInformation("Created PersonMatch template: {Name}", template.Name);
    }

    private async Task AddPersonMatchWorkflowAsync(SqlConnection connection, System.Data.Common.DbTransaction transaction, Guid projectId)
    {
        // Check if workflow already exists
        var existingWorkflow = await connection.QueryFirstOrDefaultAsync<SyncWorkflow>(
            "SELECT * FROM SyncWorkflows WHERE Id = @Id",
            new { Id = PersonMatchWorkflowId },
            transaction);

        if (existingWorkflow != null) return;

        const string insertWorkflowSql = @"
            INSERT INTO SyncWorkflows (Id, SyncProjectId, Name, ObjectClass, IsEnabled, ExecutionOrder, CreatedAt)
            VALUES (@Id, @SyncProjectId, @Name, @ObjectClass, @IsEnabled, @ExecutionOrder, @CreatedAt)";

        await connection.ExecuteAsync(insertWorkflowSql, new
        {
            Id = PersonMatchWorkflowId,
            SyncProjectId = projectId,
            Name = "Match Objects to Identities",
            ObjectClass = "user",
            IsEnabled = true,
            ExecutionOrder = 1,
            CreatedAt = DateTime.UtcNow
        }, transaction);

        const string insertStepSql = @"
            INSERT INTO SyncSteps
                (Id, SyncWorkflowId, Name, ObjectClass, ExecutionOrder, IsEnabled,
                 EnableIdentityMatching, EnablePersonMatching, CreatePersonIfNotFound,
                 IdentityMatchingAttribute, CreatedAt)
            VALUES
                (@Id, @SyncWorkflowId, @Name, @ObjectClass, @ExecutionOrder, @IsEnabled,
                 @EnableIdentityMatching, @EnablePersonMatching, @CreatePersonIfNotFound,
                 @IdentityMatchingAttribute, @CreatedAt)";

        await connection.ExecuteAsync(insertStepSql, new
        {
            Id = PersonMatchStepId,
            SyncWorkflowId = PersonMatchWorkflowId,
            Name = "Match by Email/Username/Name",
            ObjectClass = "user",
            ExecutionOrder = 1,
            IsEnabled = true,
            EnableIdentityMatching = true,
            EnablePersonMatching = true,
            CreatePersonIfNotFound = false,
            IdentityMatchingAttribute = "composite",
            CreatedAt = DateTime.UtcNow
        }, transaction);

        _logger.LogInformation("Added PersonMatch workflow and steps");
    }

    private async Task SeedPersonCreateTemplateAsync(SqlConnection connection, System.Data.Common.DbTransaction transaction)
    {
        var existing = await connection.QueryFirstOrDefaultAsync<SyncProject>(
            "SELECT * FROM SyncProjects WHERE Id = @Id",
            new { Id = PersonCreateTemplateId },
            transaction);

        if (existing != null)
        {
            // Update ProjectType if needed
            if (existing.ProjectType != "PersonCreate")
            {
                await connection.ExecuteAsync(
                    "UPDATE SyncProjects SET ProjectType = 'PersonCreate' WHERE Id = @Id",
                    new { Id = PersonCreateTemplateId },
                    transaction);
                _logger.LogInformation("Updated PersonCreate template ProjectType");
            }

            // Check if workflow exists
            var hasWorkflow = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM SyncWorkflows WHERE SyncProjectId = @ProjectId",
                new { ProjectId = PersonCreateTemplateId },
                transaction) > 0;

            if (!hasWorkflow)
            {
                await AddPersonCreateWorkflowAsync(connection, transaction, PersonCreateTemplateId);
            }
            return;
        }

        const string insertProjectSql = @"
            INSERT INTO SyncProjects
                (Id, Name, Description, ProjectType, IsBuiltIn, IsReadOnly, IsEnabled,
                 IdentityMatchingStrategy, MinMatchConfidenceThreshold, LogLevel,
                 PauseOnError, MaxErrorsBeforePause, CreatedAt, CreatedBy)
            VALUES
                (@Id, @Name, @Description, @ProjectType, @IsBuiltIn, @IsReadOnly, @IsEnabled,
                 @IdentityMatchingStrategy, @MinMatchConfidenceThreshold, @LogLevel,
                 @PauseOnError, @MaxErrorsBeforePause, @CreatedAt, @CreatedBy)";

        var template = new
        {
            Id = PersonCreateTemplateId,
            Name = "[Template] Person Create - Match or Create New Identities",
            Description = "Links synced Objects to existing Identities OR creates new Identities if no match found. " +
                         "Use this when you want to automatically create person records for new users. " +
                         "Copy this template and configure a source sync project to process.",
            ProjectType = "PersonCreate",
            IsBuiltIn = true,
            IsReadOnly = true,
            IsEnabled = true,
            IdentityMatchingStrategy = "composite",
            MinMatchConfidenceThreshold = 80,
            LogLevel = "Information",
            PauseOnError = true,
            MaxErrorsBeforePause = 10,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "System"
        };

        await connection.ExecuteAsync(insertProjectSql, template, transaction);
        await AddPersonCreateWorkflowAsync(connection, transaction, PersonCreateTemplateId);
        _logger.LogInformation("Created PersonCreate template: {Name}", template.Name);
    }

    private async Task AddPersonCreateWorkflowAsync(SqlConnection connection, System.Data.Common.DbTransaction transaction, Guid projectId)
    {
        // Check if workflow already exists
        var existingWorkflow = await connection.QueryFirstOrDefaultAsync<SyncWorkflow>(
            "SELECT * FROM SyncWorkflows WHERE Id = @Id",
            new { Id = PersonCreateWorkflowId },
            transaction);

        if (existingWorkflow != null) return;

        const string insertWorkflowSql = @"
            INSERT INTO SyncWorkflows (Id, SyncProjectId, Name, ObjectClass, IsEnabled, ExecutionOrder, CreatedAt)
            VALUES (@Id, @SyncProjectId, @Name, @ObjectClass, @IsEnabled, @ExecutionOrder, @CreatedAt)";

        await connection.ExecuteAsync(insertWorkflowSql, new
        {
            Id = PersonCreateWorkflowId,
            SyncProjectId = projectId,
            Name = "Match or Create Identities",
            ObjectClass = "user",
            IsEnabled = true,
            ExecutionOrder = 1,
            CreatedAt = DateTime.UtcNow
        }, transaction);

        const string insertStepSql = @"
            INSERT INTO SyncSteps
                (Id, SyncWorkflowId, Name, ObjectClass, ExecutionOrder, IsEnabled,
                 EnableIdentityMatching, EnablePersonMatching, CreatePersonIfNotFound,
                 IdentityMatchingAttribute, CreatedAt)
            VALUES
                (@Id, @SyncWorkflowId, @Name, @ObjectClass, @ExecutionOrder, @IsEnabled,
                 @EnableIdentityMatching, @EnablePersonMatching, @CreatePersonIfNotFound,
                 @IdentityMatchingAttribute, @CreatedAt)";

        await connection.ExecuteAsync(insertStepSql, new
        {
            Id = PersonCreateMatchStepId,
            SyncWorkflowId = PersonCreateWorkflowId,
            Name = "Match or Create Identities",
            ObjectClass = "user",
            ExecutionOrder = 1,
            IsEnabled = true,
            EnableIdentityMatching = true,
            EnablePersonMatching = true,
            CreatePersonIfNotFound = true,
            IdentityMatchingAttribute = "composite",
            CreatedAt = DateTime.UtcNow
        }, transaction);

        _logger.LogInformation("Added PersonCreate workflow and steps");
    }
}
