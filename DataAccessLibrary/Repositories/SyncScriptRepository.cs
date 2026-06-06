using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for Dev Center script CRUD and step assignment operations.
/// Uses Dapper for all database access.
/// </summary>
public class SyncScriptRepository : DapperRepositoryBase, ISyncScriptRepository
{
    public SyncScriptRepository(IConfiguration configuration, IGlobalLogger logger) : base(configuration, logger) { }

    /// <summary>
    /// Well-known script ID for the CreateOrUpdateIdentity person matching script.
    /// This script is seeded by DevCenterScriptsSeedService.
    /// </summary>
    public static readonly Guid CreateOrUpdateIdentityScriptId = new Guid("22222222-2222-2222-2222-222222222222");

    /// <inheritdoc />
    public async Task<List<StepScriptInfo>> GetStepScriptsAsync(
        Guid syncStepId,
        string executionPhase,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                sss.Id AS StepScriptId,
                sss.ScriptId,
                ISNULL(sps.Name, '') AS ScriptName,
                ISNULL(sps.ScriptType, '') AS ScriptType,
                ISNULL(sss.ExecutionPhase, '') AS ExecutionPhase,
                sss.ExecutionOrder,
                sss.IsEnabled,
                sss.ParameterOverrides,
                ISNULL(sps.ScriptCode, '') AS ScriptCode,
                ISNULL(sps.Category, '') AS Category,
                sps.IsSystem,
                sps.Version
            FROM SyncStepScripts sss
            JOIN SyncProcessingScripts sps ON sss.ScriptId = sps.Id
            WHERE sss.SyncStepId = @SyncStepId
              AND sss.ExecutionPhase = @ExecutionPhase
              AND sss.IsEnabled = 1
              AND sps.IsEnabled = 1
            ORDER BY sss.ExecutionOrder";

        if (string.IsNullOrEmpty(executionPhase))
        {
            _logger.LogWarning("GetStepScriptsAsync called with null/empty executionPhase for step {StepId}", syncStepId);
            return new List<StepScriptInfo>();
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var scripts = await connection.QueryAsync<StepScriptInfo>(
                new CommandDefinition(sql, new { SyncStepId = syncStepId, ExecutionPhase = executionPhase },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            return scripts?.ToList() ?? new List<StepScriptInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading scripts for step {StepId} phase {Phase}", syncStepId, executionPhase);
            return new List<StepScriptInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<SyncProcessingScript?> GetScriptByIdAsync(
        Guid scriptId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled,
                   Version, Category, CompilationStatus, CompilationError, LastCompiledAt,
                   CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, CopiedFromScriptId
            FROM SyncProcessingScripts
            WHERE Id = @ScriptId";

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return await connection.QueryFirstOrDefaultAsync<SyncProcessingScript>(
                new CommandDefinition(sql, new { ScriptId = scriptId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading script {ScriptId}", scriptId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> RecordScriptExecutionAsync(
        SyncScriptExecution execution,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO SyncScriptExecutions
                (Id, SyncStepRunId, ScriptId, ExecutionPhase, Status, StartedAt, CompletedAt,
                 DurationMs, ObjectsProcessed, ObjectsModified, IdentitiesCreated, ManagersResolved,
                 ErrorMessage, OutputLog)
            VALUES
                (@Id, @SyncStepRunId, @ScriptId, @ExecutionPhase, @Status, @StartedAt, @CompletedAt,
                 @DurationMs, @ObjectsProcessed, @ObjectsModified, @IdentitiesCreated, @ManagersResolved,
                 @ErrorMessage, @OutputLog)";

        try
        {
            if (execution.Id == Guid.Empty)
                execution.Id = Guid.NewGuid();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(sql, execution, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return execution.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording script execution for script {ScriptId}", execution.ScriptId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateScriptCompilationStatusAsync(
        Guid scriptId,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE SyncProcessingScripts
            SET CompilationStatus = @Status,
                CompilationError = @ErrorMessage,
                LastCompiledAt = GETUTCDATE()
            WHERE Id = @ScriptId";

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(sql,
                new { ScriptId = scriptId, Status = status, ErrorMessage = errorMessage },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating compilation status for script {ScriptId}", scriptId);
        }
    }

    /// <inheritdoc />
    public async Task<List<SyncProcessingScript>> GetAllScriptsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled,
                   Version, Category, CompilationStatus, CompilationError, LastCompiledAt,
                   CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, CopiedFromScriptId
            FROM SyncProcessingScripts
            ORDER BY IsSystem DESC, Category, Name";

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var scripts = await connection.QueryAsync<SyncProcessingScript>(
                new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return scripts.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading all scripts");
            return new List<SyncProcessingScript>();
        }
    }

    /// <inheritdoc />
    public async Task<Guid> SaveScriptAsync(
        SyncProcessingScript script,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Check if script exists
            var exists = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition("SELECT CASE WHEN EXISTS(SELECT 1 FROM SyncProcessingScripts WHERE Id = @Id) THEN 1 ELSE 0 END",
                    new { script.Id }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (exists)
            {
                // Update existing script
                const string updateSql = @"
                    UPDATE SyncProcessingScripts
                    SET Name = @Name,
                        Description = @Description,
                        ScriptType = @ScriptType,
                        ScriptCode = @ScriptCode,
                        IsEnabled = @IsEnabled,
                        Version = Version + 1,
                        Category = @Category,
                        CompilationStatus = 'NotCompiled',
                        CompilationError = NULL,
                        ModifiedAt = GETUTCDATE(),
                        ModifiedBy = @ModifiedBy
                    WHERE Id = @Id AND IsSystem = 0"; // Prevent modifying system scripts

                await connection.ExecuteAsync(new CommandDefinition(updateSql, script, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            else
            {
                // Insert new script
                if (script.Id == Guid.Empty)
                    script.Id = Guid.NewGuid();

                const string insertSql = @"
                    INSERT INTO SyncProcessingScripts
                        (Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled,
                         Version, Category, CompilationStatus, CreatedAt, CreatedBy, CopiedFromScriptId)
                    VALUES
                        (@Id, @Name, @Description, @ScriptType, @ScriptCode, 0, @IsEnabled,
                         1, @Category, 'NotCompiled', GETUTCDATE(), @CreatedBy, @CopiedFromScriptId)";

                await connection.ExecuteAsync(new CommandDefinition(insertSql, script, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            return script.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving script {ScriptName}", script.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteScriptAsync(
        Guid scriptId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DELETE FROM SyncProcessingScripts
            WHERE Id = @ScriptId AND IsSystem = 0"; // Prevent deleting system scripts

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(
                new CommandDefinition(sql, new { ScriptId = scriptId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting script {ScriptId}", scriptId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task AssignScriptToStepAsync(
        Guid syncStepId,
        Guid scriptId,
        string executionPhase,
        int executionOrder,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM SyncStepScripts WHERE SyncStepId = @SyncStepId AND ScriptId = @ScriptId AND ExecutionPhase = @ExecutionPhase)
            BEGIN
                INSERT INTO SyncStepScripts (Id, SyncStepId, ScriptId, ExecutionPhase, ExecutionOrder, IsEnabled)
                VALUES (NEWID(), @SyncStepId, @ScriptId, @ExecutionPhase, @ExecutionOrder, 1)
            END
            ELSE
            BEGIN
                UPDATE SyncStepScripts
                SET ExecutionOrder = @ExecutionOrder, IsEnabled = 1
                WHERE SyncStepId = @SyncStepId AND ScriptId = @ScriptId AND ExecutionPhase = @ExecutionPhase
            END";

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(sql,
                new { SyncStepId = syncStepId, ScriptId = scriptId, ExecutionPhase = executionPhase, ExecutionOrder = executionOrder },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning script {ScriptId} to step {StepId}", scriptId, syncStepId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveScriptFromStepAsync(
        Guid syncStepScriptId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM SyncStepScripts WHERE Id = @Id";

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = syncStepScriptId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing script assignment {StepScriptId}", syncStepScriptId);
            throw;
        }
    }

    /// <summary>
    /// Auto-assigns the default person matching script (CreateOrUpdateIdentity) to a sync step.
    /// Should be called when a step is created with EnableIdentityMatching=true or when the flag is toggled on.
    /// </summary>
    /// <param name="syncStepId">The step to assign the script to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task AutoAssignPersonMatchingScriptAsync(
        Guid syncStepId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Assign the CreateOrUpdateIdentity script as a post-processing script
            await AssignScriptToStepAsync(
                syncStepId,
                CreateOrUpdateIdentityScriptId,
                ScriptTypes.PostProcessing,
                executionOrder: 1, // First script to run (before manager resolution)
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Auto-assigned CreateOrUpdateIdentity script to step {StepId}", syncStepId);
        }
        catch (Exception ex)
        {
            // Log but don't throw - script assignment is not critical to step creation
            _logger.LogWarning(ex, "Failed to auto-assign person matching script to step {StepId}. " +
                "Script may not exist yet (run seed service) or step doesn't support scripts.", syncStepId);
        }
    }

    /// <summary>
    /// Removes the default person matching script from a sync step.
    /// Should be called when EnableIdentityMatching is toggled off.
    /// </summary>
    public async Task RemovePersonMatchingScriptAsync(
        Guid syncStepId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DELETE FROM SyncStepScripts
            WHERE SyncStepId = @SyncStepId
            AND ScriptId = @ScriptId
            AND ExecutionPhase = @ExecutionPhase";

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var deleted = await connection.ExecuteAsync(new CommandDefinition(sql,
                new { SyncStepId = syncStepId, ScriptId = CreateOrUpdateIdentityScriptId, ExecutionPhase = ScriptTypes.PostProcessing },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.LogInformation("Removed person matching script from step {StepId}", syncStepId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing person matching script from step {StepId}", syncStepId);
        }
    }
}
