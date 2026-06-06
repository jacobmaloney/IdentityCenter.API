using System.Text.Json;
using Common.Encryption;
using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services.HRImport;

/// <summary>
/// Orchestrates HR data import: reads from source → maps fields → upserts Identities.
/// Called by SyncProjectOrchestrator for ProjectType == "HRImport".
/// After import, triggers lifecycle events (Joiner/Mover/Leaver) if matching templates exist.
/// </summary>
public class HRImportOrchestrator
{
    private readonly IHRImportRepository _hrRepo;
    private readonly IEncryptionService _encryptionService;
    private readonly IEnumerable<IHRDataSourceReader> _readers;
    private readonly ILogger<HRImportOrchestrator> _logger;
    private readonly IProcessEventPublisher? _eventPublisher;
    private readonly string _connectionString;

    public HRImportOrchestrator(
        IHRImportRepository hrRepo,
        IEncryptionService encryptionService,
        IEnumerable<IHRDataSourceReader> readers,
        ILogger<HRImportOrchestrator> logger,
        IConfiguration configuration,
        IProcessEventPublisher? eventPublisher = null)
    {
        _hrRepo = hrRepo;
        _encryptionService = encryptionService;
        _readers = readers;
        _logger = logger;
        _eventPublisher = eventPublisher;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    /// <summary>
    /// Execute the HR import for a given SyncProject.
    /// Returns the import run with metrics, plus the detailed import result (with per-identity IDs).
    /// </summary>
    public async Task<(HRImportRun Run, HRImportResult? Result)> ExecuteAsync(
        SyncProject project,
        SyncProjectRun run,
        CancellationToken ct = default,
        SyncStep? importStep = null)
    {
        var importRun = new HRImportRun
        {
            SyncProjectId = project.Id,
            Status = "Running"
        };
        HRImportResult? importResult = null;

        await _hrRepo.CreateImportRunAsync(importRun, ct);

        try
        {
            // 1. Load DirectoryConnection
            if (!project.SourceConnectionId.HasValue)
                throw new InvalidOperationException("HRImport project has no SourceConnectionId");

            var connection = await GetDirectoryConnectionAsync(project.SourceConnectionId.Value, ct);
            if (connection == null)
                throw new InvalidOperationException($"DirectoryConnection {project.SourceConnectionId} not found");

            // 2. Decrypt config + credentials
            var config = DeserializeConfig(connection.Configuration);
            var credentials = await DecryptCredentials(connection.Credentials);

            // Resolve source type: prefer explicit config, fall back to ConnectionType
            var sourceType = config.SourceType;
            if (string.IsNullOrWhiteSpace(sourceType) || sourceType.Equals("CSV", StringComparison.OrdinalIgnoreCase))
            {
                // If connection is SCIM but config says CSV (default), use SCIM
                if (connection.ConnectionType?.Equals("SCIM", StringComparison.OrdinalIgnoreCase) == true)
                    sourceType = "SCIM";
            }

            // For SCIM connections: populate ScimEndpoint and credentials from the connection's
            // encrypted fields if not already set in the HR config
            if (sourceType.Equals("SCIM", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(config.ScimEndpoint) && !string.IsNullOrWhiteSpace(connection.ConnectionString))
                {
                    config.ScimEndpoint = await _encryptionService.DecryptAsync(connection.ConnectionString);
                }

                // Bridge ScimCredentials (BearerToken) to HRCredentials (BearerToken)
                if (string.IsNullOrWhiteSpace(credentials.BearerToken) && !string.IsNullOrWhiteSpace(connection.Credentials))
                {
                    try
                    {
                        var decryptedCreds = await _encryptionService.DecryptAsync(connection.Credentials);
                        var scimCreds = JsonSerializer.Deserialize<Models.ScimCredentials>(decryptedCreds,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (scimCreds != null)
                        {
                            credentials.BearerToken ??= scimCreds.BearerToken;
                            credentials.Username ??= scimCreds.Username;
                            credentials.Password ??= scimCreds.Password;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse SCIM credentials from connection");
                    }
                }
            }

            _logger.LogInformation("HR Import starting: Source={SourceType}, Connection={Name}",
                sourceType, connection.Name);

            // 3. Select reader by source type
            var reader = _readers.FirstOrDefault(r =>
                r.SourceType.Equals(sourceType, StringComparison.OrdinalIgnoreCase));
            if (reader == null)
                throw new InvalidOperationException($"No reader available for source type '{sourceType}'");

            // 4. Read records from source
            var readResult = await reader.ReadAsync(connection, config, credentials, ct);
            if (!readResult.Success)
                throw new InvalidOperationException($"Source read failed: {readResult.ErrorMessage}");

            importRun.TotalRecords = readResult.TotalRecords;

            _logger.LogInformation("HR Import: Read {Count} records from {SourceType}",
                readResult.TotalRecords, config.SourceType);

            // 5. Load field mappings — prefer step-level AttributeMappings, fall back to connection-level HRFieldMappings
            var mappings = ConvertStepMappingsToFieldMappings(importStep, connection.Id, config.UniqueIdField);
            if (mappings.Count > 0)
            {
                _logger.LogInformation("HR Import: Using {Count} field mappings from step AttributeMappings", mappings.Count);
            }
            else
            {
                mappings = await _hrRepo.GetFieldMappingsAsync(connection.Id, ct);
                _logger.LogInformation("HR Import: Using {Count} field mappings from connection HRFieldMappings", mappings.Count);
            }
            if (mappings.Count == 0)
                throw new InvalidOperationException("No field mappings configured — add attribute mappings to the Import step in the Sync Project wizard");

            // 5.5 Parse lifecycle config from step
            HRImportStepConfig? stepConfig = null;
            if (!string.IsNullOrWhiteSpace(importStep?.Configuration))
            {
                try { stepConfig = JsonSerializer.Deserialize<HRImportStepConfig>(importStep.Configuration); }
                catch { _logger.LogWarning("Failed to parse HRImportStepConfig from step Configuration"); }
            }

            // 6. Bulk upsert into Identities table
            importResult = await _hrRepo.BulkUpsertIdentitiesAsync(
                readResult.Records, mappings, config.UniqueIdField, connection.Id, ct, stepConfig);

            importRun.CreatedRecords = importResult.Created;
            importRun.UpdatedRecords = importResult.Updated;
            importRun.SkippedRecords = importResult.Skipped;
            importRun.ErrorRecords = importResult.Errors;
            importRun.EnabledRecords = importResult.Enabled;
            importRun.DisabledRecords = importResult.Disabled;

            if (importResult.ErrorList.Count > 0)
            {
                importRun.ErrorDetails = JsonSerializer.Serialize(importResult.ErrorList,
                    new JsonSerializerOptions { WriteIndented = false });
            }

            // 7. Post-import: resolve manager references
            await ResolveManagerReferencesAsync(mappings, config.UniqueIdField, ct);

            // 7.5. Publish IdentityCreated events for process workflows (non-fatal)
            await PublishIdentityEventsAsync(importResult, ct);

            // 8. Finalize
            importRun.Status = importResult.Errors > 0 ? "CompletedWithErrors" : "Completed";
            importRun.CompletedAt = DateTime.UtcNow;
            importRun.DurationSeconds = (int)(importRun.CompletedAt.Value - importRun.StartedAt).TotalSeconds;

            _logger.LogInformation(
                "HR Import complete: Created={Created}, Updated={Updated}, Skipped={Skipped}, Errors={Errors}, Enabled={Enabled}, Disabled={Disabled}, Duration={Duration}s",
                importRun.CreatedRecords, importRun.UpdatedRecords,
                importRun.SkippedRecords, importRun.ErrorRecords, importRun.EnabledRecords, importRun.DisabledRecords, importRun.DurationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HR Import failed for project {ProjectId}", project.Id);
            importRun.Status = "Failed";
            importRun.ErrorDetails = JsonSerializer.Serialize(new[] { new { error = ex.Message } });
            importRun.CompletedAt = DateTime.UtcNow;
            importRun.DurationSeconds = (int)(importRun.CompletedAt.Value - importRun.StartedAt).TotalSeconds;
        }

        await _hrRepo.UpdateImportRunAsync(importRun, ct);
        return (importRun, importResult);
    }

    /// <summary>
    /// Post-import pass: resolve manager_employee_id → ManagerIdentityId.
    /// Looks up manager by EmployeeId in the Identities table.
    /// </summary>
    private async Task ResolveManagerReferencesAsync(
        List<HRFieldMapping> mappings,
        string uniqueIdField,
        CancellationToken ct)
    {
        // Check if there's a mapping for manager
        var managerMapping = mappings.FirstOrDefault(m =>
            m.TargetField.Equals("ManagerEmployeeId", StringComparison.OrdinalIgnoreCase) ||
            m.TargetField.Equals("ManagerIdentityId", StringComparison.OrdinalIgnoreCase) ||
            m.SourceField.Contains("manager", StringComparison.OrdinalIgnoreCase));

        if (managerMapping == null)
            return;

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Resolve ManagerEmployeeId → ManagerIdentityId by looking up the manager's EmployeeId
            var resolved = await conn.ExecuteAsync(
                @"UPDATE child
                  SET child.ManagerIdentityId = manager.Id,
                      child.ModifiedAt = SYSUTCDATETIME()
                  FROM Identities child
                  INNER JOIN Identities manager ON manager.EmployeeId = child.ManagerEmployeeId
                  WHERE child.ManagerIdentityId IS NULL
                    AND child.ManagerEmployeeId IS NOT NULL
                    AND LEN(child.ManagerEmployeeId) > 0
                    AND manager.Id != child.Id",
                commandTimeout: 60);

            if (resolved > 0)
            {
                _logger.LogInformation("Manager resolution: linked {Count} manager relationships", resolved);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manager resolution encountered errors (non-fatal)");
        }
    }

    private async Task<DirectoryConnection?> GetDirectoryConnectionAsync(Guid connectionId, CancellationToken ct)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<DirectoryConnection>(
            "SELECT * FROM DirectoryConnections WHERE Id = @Id",
            new { Id = connectionId });
    }

    private static HRConnectionConfig DeserializeConfig(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return new HRConnectionConfig();

        try
        {
            return JsonSerializer.Deserialize<HRConnectionConfig>(configuration) ?? new HRConnectionConfig();
        }
        catch
        {
            return new HRConnectionConfig();
        }
    }

    private async Task<HRCredentials> DecryptCredentials(string? encryptedCredentials)
    {
        if (string.IsNullOrWhiteSpace(encryptedCredentials))
            return new HRCredentials();

        // If the value looks like plain JSON (not encrypted), try to parse directly
        var trimmed = encryptedCredentials.Trim();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            try
            {
                return JsonSerializer.Deserialize<HRCredentials>(trimmed) ?? new HRCredentials();
            }
            catch
            {
                return new HRCredentials();
            }
        }

        // Otherwise, try to decrypt first
        try
        {
            var decrypted = await _encryptionService.DecryptAsync(encryptedCredentials);
            return JsonSerializer.Deserialize<HRCredentials>(decrypted) ?? new HRCredentials();
        }
        catch
        {
            return new HRCredentials();
        }
    }

    /// <summary>
    /// Publishes IdentityCreated events for process workflows (e.g. New Identity Notification).
    /// Non-fatal: errors are logged but don't fail the HR import.
    /// </summary>
    private async Task PublishIdentityEventsAsync(HRImportResult importResult, CancellationToken ct)
    {
        if (_eventPublisher == null || importResult.CreatedIdentityIds.Count == 0) return;

        try
        {
            _logger.LogInformation("HR Import: Publishing IdentityCreated events for {Count} new identities",
                importResult.CreatedIdentityIds.Count);
            foreach (var identityId in importResult.CreatedIdentityIds)
            {
                ct.ThrowIfCancellationRequested();
                await _eventPublisher.PublishAsync(
                    Models.WorkflowEventType.IdentityCreated,
                    identityId,
                    "Identity",
                    new Dictionary<string, object>
                    {
                        { "Status", "Active" },
                        { "Source", "HRImportOrchestrator" }
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Identity event publishing encountered errors (non-fatal)");
        }
    }

    /// <summary>
    /// Converts a step's AttributeMappings into HRFieldMappings for the import engine.
    /// This allows field mappings to be configured directly on the workflow step
    /// instead of requiring a separate connection-level HRFieldMappings configuration.
    /// </summary>
    private static List<HRFieldMapping> ConvertStepMappingsToFieldMappings(SyncStep? step, Guid connectionId, string uniqueIdField = "EmployeeId")
    {
        if (step?.AttributeMappings == null || step.AttributeMappings.Count == 0)
            return new List<HRFieldMapping>();

        var identityMappings = step.AttributeMappings
            .Where(m => m.IsEnabled && !string.IsNullOrWhiteSpace(m.SourceAttribute) && !string.IsNullOrWhiteSpace(m.TargetAttribute))
            .Where(m => m.TargetType == "IdentityColumn" || m.TargetType == "ExtendedAttribute")
            .OrderBy(m => m.ExecutionOrder)
            .ToList();

        if (identityMappings.Count == 0)
            return new List<HRFieldMapping>();

        var result = new List<HRFieldMapping>();
        int order = 1;
        foreach (var am in identityMappings)
        {
            // Map TransformationType to HRFieldMapping Transformation format
            var transformation = am.TransformationType switch
            {
                "Direct" => (string?)null,
                "ToUpper" => "Uppercase",
                "ToLower" => "Lowercase",
                "Trim" => "Trim",
                "DateParse" => "DateParse",
                _ => am.TransformationType
            };

            result.Add(new HRFieldMapping
            {
                Id = am.Id,
                DirectoryConnectionId = connectionId,
                SourceField = am.SourceAttribute,
                TargetField = am.TargetAttribute,
                IsRequired = am.IsRequired,
                IsKeyField = am.TargetAttribute.Equals(uniqueIdField, StringComparison.OrdinalIgnoreCase),
                Transformation = transformation,
                MappingOrder = order++,
                IsEnabled = true
            });
        }

        return result;
    }
}
