using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Dapper;
using DataAccessLibrary.Data;
using DataAccessLibrary.Models;
using DataAccessLibrary.Configuration;
using System.Text.Json;
using Common.Encryption;
using ModelDirectoryConnection = DataAccessLibrary.Models.DirectoryConnection;
using DataAccessLibrary.Services.Scripting;
using DataAccessLibrary.Services.PersonMatching;
using DataAccessLibrary.Services.HRImport;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Orchestrates synchronization for SyncProjects with workflows and steps.
    /// Implements UC-SYNC-03 Multi-Step Sync Projects.
    /// Uses pure Dapper for all database operations.
    /// Integrates Dapper-based high-performance operations via ISyncRepository.
    /// Uses configurable thresholds and timeouts from SyncOptions.
    /// </summary>
    public class SyncProjectOrchestrator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<SyncProjectOrchestrator> _logger;
        private readonly DataAccessLibrary.Repositories.ISyncRepository _syncRepository;
        private readonly SyncOptions _syncOptions;
        private readonly ConnectorQueryServiceFactory _connectorQueryServiceFactory;
        private readonly AttributeMappingService _attributeMappingService;
        private readonly GroupSyncService _groupSyncService;
        private readonly GroupMembershipSyncService _groupMembershipService;
        private readonly string _connectionString;

        private readonly SyncLoggerProvider _syncLoggerProvider;
        private readonly SyncLogBuffer _syncLogBuffer;
        private readonly IScriptCompilationService? _scriptCompilationService;
        private readonly IScriptLoggerFactory? _scriptLoggerFactory;
        private readonly IPersonMatchingService? _personMatchingService;
        private readonly PersonMatchOrchestrator? _personMatchOrchestrator;
        private readonly IDatabaseOptimizationService? _databaseOptimizationService;
        private readonly HRImportOrchestrator? _hrImportOrchestrator;
        private readonly IAdminNotificationService? _adminNotificationService;
        private readonly ILicenseRepository? _licenseRepository;
        private readonly ILicenseSyncQueryService? _licenseSyncQueryService;
        private readonly ICloudActivityRepository? _cloudActivityRepository;
        private readonly ILicenseOptimizationEngine? _licenseOptimizationEngine;

        // SKU part number -> friendly display name mapping for license pools
        private static readonly Dictionary<string, string> SkuFriendlyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ENTERPRISEPREMIUM"] = "Microsoft 365 E5",
            ["SPE_E5"] = "Microsoft 365 E5",
            ["ENTERPRISEPACK"] = "Microsoft 365 E3",
            ["SPE_E3"] = "Microsoft 365 E3",
            ["FLOW_FREE"] = "Power Automate Free",
            ["O365_BUSINESS_ESSENTIALS"] = "Microsoft 365 Business Basic",
            ["O365_BUSINESS_PREMIUM"] = "Microsoft 365 Business Standard",
            ["POWER_BI_PRO"] = "Power BI Pro",
            ["POWER_BI_STANDARD"] = "Power BI Free",
            ["TEAMS_EXPLORATORY"] = "Teams Exploratory",
            ["AAD_PREMIUM"] = "Entra ID P1",
            ["AAD_PREMIUM_P2"] = "Entra ID P2",
            ["INTUNE_A"] = "Microsoft Intune Plan 1",
            ["EMS_E5"] = "Enterprise Mobility + Security E5",
            ["EMSPREMIUM"] = "Enterprise Mobility + Security E5",
            ["EXCHANGESTANDARD"] = "Exchange Online Plan 1",
            ["EXCHANGEENTERPRISE"] = "Exchange Online Plan 2",
            ["VISIOCLIENT"] = "Visio Plan 2",
            ["PROJECTPROFESSIONAL"] = "Project Plan 3",
            ["PROJECTPREMIUM"] = "Project Plan 5",
            ["WIN10_PRO_ENT_SUB"] = "Windows 10/11 Enterprise E3",
            ["DEVELOPERPACK_E5"] = "Microsoft 365 E5 Developer",
            ["ATP_ENTERPRISE"] = "Defender for Office 365 P1",
            ["THREAT_INTELLIGENCE"] = "Defender for Office 365 P2",
            ["MCOMEETADV"] = "Audio Conferencing",
            ["PHONESYSTEM_VIRTUALUSER"] = "Phone System Virtual User",
            ["SMB_BUSINESS"] = "Microsoft 365 Business Basic",
            ["SMB_BUSINESS_PREMIUM"] = "Microsoft 365 Business Standard",
        };

        private static readonly Dictionary<string, decimal> SkuDefaultPricing = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ENTERPRISEPREMIUM"] = 57.00m, ["SPE_E5"] = 57.00m,
            ["ENTERPRISEPACK"] = 36.00m, ["SPE_E3"] = 36.00m,
            ["O365_BUSINESS_ESSENTIALS"] = 6.00m, ["SMB_BUSINESS"] = 6.00m,
            ["O365_BUSINESS_PREMIUM"] = 12.50m, ["SMB_BUSINESS_PREMIUM"] = 12.50m,
            ["POWER_BI_PRO"] = 10.00m, ["POWER_BI_STANDARD"] = 0m,
            ["AAD_PREMIUM"] = 6.00m, ["AAD_PREMIUM_P2"] = 9.00m,
            ["INTUNE_A"] = 8.00m, ["EMS_E5"] = 16.40m, ["EMSPREMIUM"] = 16.40m,
            ["EXCHANGESTANDARD"] = 4.00m, ["EXCHANGEENTERPRISE"] = 8.00m,
            ["VISIOCLIENT"] = 15.00m, ["PROJECTPROFESSIONAL"] = 30.00m, ["PROJECTPREMIUM"] = 55.00m,
            ["MCOMEETADV"] = 4.00m, ["FLOW_FREE"] = 0m, ["TEAMS_EXPLORATORY"] = 0m,
            ["DEVELOPERPACK_E5"] = 0m, ["PHONESYSTEM_VIRTUALUSER"] = 0m,
            ["WIN10_PRO_ENT_SUB"] = 7.00m, ["ATP_ENTERPRISE"] = 2.00m, ["THREAT_INTELLIGENCE"] = 5.00m,
        };

        // Thread-safe counters for parallel workflow execution
        private int _personsCreatedCount = 0;
        private int _completedStepsCount = 0;
        private int _totalErrorsCount = 0;
        private int _failedStepsCount = 0;

        // PHASE 1 sync-sink seam. Resolved once per run (fail-fast) in ExecuteSyncProjectAsync
        // and used at the single bulk-write site in ExecuteStepAsync. The orchestrator is
        // registered Scoped (one instance per run scope), so an instance field is run-local
        // exactly like the counters above. Null target => IdentityStoreSink (byte-identical to
        // the historical FastBulkUpsertObjectsAsync call). External target => the run already
        // failed fast at start, so this is never used for an external target.
        private ISyncSink? _activeSink;

        public SyncProjectOrchestrator(
            IServiceScopeFactory scopeFactory,
            IEncryptionService encryptionService,
            ILogger<SyncProjectOrchestrator> logger,
            DataAccessLibrary.Repositories.ISyncRepository syncRepository,
            IOptions<SyncOptions> syncOptions,
            ConnectorQueryServiceFactory connectorQueryServiceFactory,
            AttributeMappingService attributeMappingService,
            IConfiguration configuration,
            GroupSyncService groupSyncService,
            GroupMembershipSyncService groupMembershipService,

            SyncLoggerProvider syncLoggerProvider,
            SyncLogBuffer syncLogBuffer,
            IScriptCompilationService? scriptCompilationService = null,
            IScriptLoggerFactory? scriptLoggerFactory = null,
            IPersonMatchingService? personMatchingService = null,
            PersonMatchOrchestrator? personMatchOrchestrator = null,
            IDatabaseOptimizationService? databaseOptimizationService = null,
            HRImportOrchestrator? hrImportOrchestrator = null,
            IAdminNotificationService? adminNotificationService = null,
            ILicenseRepository? licenseRepository = null,
            ILicenseSyncQueryService? licenseSyncQueryService = null,
            ICloudActivityRepository? cloudActivityRepository = null,
            ILicenseOptimizationEngine? licenseOptimizationEngine = null)
        {
            _scopeFactory = scopeFactory;
            _encryptionService = encryptionService;
            _logger = logger;
            _syncRepository = syncRepository;
            _syncOptions = syncOptions.Value;
            _connectorQueryServiceFactory = connectorQueryServiceFactory;
            _attributeMappingService = attributeMappingService;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            _groupSyncService = groupSyncService;
            _groupMembershipService = groupMembershipService;

            _syncLoggerProvider = syncLoggerProvider;
            _syncLogBuffer = syncLogBuffer;
            _scriptCompilationService = scriptCompilationService;
            _scriptLoggerFactory = scriptLoggerFactory;
            _personMatchingService = personMatchingService;
            _personMatchOrchestrator = personMatchOrchestrator;
            _databaseOptimizationService = databaseOptimizationService;
            _hrImportOrchestrator = hrImportOrchestrator;
            _adminNotificationService = adminNotificationService;
            _licenseRepository = licenseRepository;
            _licenseSyncQueryService = licenseSyncQueryService;
            _cloudActivityRepository = cloudActivityRepository;
            _licenseOptimizationEngine = licenseOptimizationEngine;
        }

        /// <summary>
        /// Creates a new SqlConnection using the connection string.
        /// </summary>
        private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

        /// <summary>
        /// Executes a complete sync project with all workflows and steps.
        /// Main entry point for sync execution.
        /// Uses Dapper for all database operations.
        /// </summary>
        public async Task<SyncProjectRun> ExecuteSyncProjectAsync(
            Guid syncProjectId,
            string triggerType = "Manual",
            string? triggeredBy = null,
            CancellationToken cancellationToken = default,
            List<Guid>? selectedWorkflowIds = null)
        {
            var selectionInfo = selectedWorkflowIds != null
                ? $", Selected Workflows: {selectedWorkflowIds.Count}"
                : ", All Workflows";
            _logger.LogInformation("=== SYNC START === Project: {ProjectId}, Trigger: {TriggerType}, By: {TriggeredBy}{Selection}",
                syncProjectId, triggerType, triggeredBy ?? "Unknown", selectionInfo);

            _logger.LogInformation("Loading sync project configuration from database...");

            // Load sync project with all workflows and steps using Dapper
            SyncProject project;
            using (var connection = CreateConnection())
            {
                await connection.OpenAsync(cancellationToken);

                // Load project
                project = await connection.QueryFirstOrDefaultAsync<SyncProject>(
                    @"SELECT * FROM SyncProjects WHERE Id = @Id",
                    new { Id = syncProjectId });

                if (project == null)
                {
                    _logger.LogError("Sync project {ProjectId} not found in database", syncProjectId);
                    throw new ArgumentException($"Sync project {syncProjectId} not found");
                }

                // Load source connection
                if (project.SourceConnectionId.HasValue)
                {
                    project.SourceConnection = await connection.QueryFirstOrDefaultAsync<ModelDirectoryConnection>(
                        @"SELECT * FROM DirectoryConnections WHERE Id = @Id",
                        new { Id = project.SourceConnectionId.Value });
                }

                // Load workflows
                var workflows = (await connection.QueryAsync<SyncWorkflow>(
                    @"SELECT * FROM SyncWorkflows WHERE SyncProjectId = @ProjectId AND IsEnabled = 1 ORDER BY ExecutionOrder",
                    new { ProjectId = syncProjectId })).ToList();

                // Load steps for each workflow
                foreach (var workflow in workflows)
                {
                    var steps = (await connection.QueryAsync<SyncStep>(
                        @"SELECT * FROM SyncSteps WHERE SyncWorkflowId = @WorkflowId ORDER BY ExecutionOrder",
                        new { WorkflowId = workflow.Id })).ToList();

                    // Load attribute mappings for each step
                    foreach (var step in steps)
                    {
                        step.AttributeMappings = (await connection.QueryAsync<AttributeMapping>(
                            @"SELECT * FROM AttributeMappings WHERE SyncStepId = @StepId ORDER BY ExecutionOrder",
                            new { StepId = step.Id })).ToList();

                        // Load step tags
                        step.StepTags = (await connection.QueryAsync<SyncStepTag>(
                            @"SELECT st.*, t.Name as TagName FROM SyncStepTags st
                              LEFT JOIN Tags t ON st.TagId = t.Id
                              WHERE st.SyncStepId = @StepId",
                            new { StepId = step.Id })).ToList();

                        // Load tag details for each step tag
                        foreach (var stepTag in step.StepTags)
                        {
                            stepTag.Tag = await connection.QueryFirstOrDefaultAsync<Tag>(
                                @"SELECT * FROM Tags WHERE Id = @Id",
                                new { Id = stepTag.TagId });
                        }
                    }

                    workflow.Steps = steps;
                }

                project.Workflows = workflows;
            }

            _logger.LogInformation("Loaded project '{ProjectName}' with {WorkflowCount} workflows", project.Name, project.Workflows.Count);

            // Configure logging level for this sync based on project settings
            var projectLogLevel = project.LogLevel ?? "Information";
            _logger.LogInformation("Sync project '{ProjectName}' log level: {LogLevel}", project.Name, projectLogLevel);

            // Parse and convert log level for trace logging activation
            var logLevel = ParseLogLevel(projectLogLevel);
            _logger.LogInformation("Parsed logging level: {LogLevel} for trace logging", logLevel);

            if (!project.IsEnabled)
            {
                _logger.LogWarning("Sync project '{ProjectName}' is disabled", project.Name);
                throw new InvalidOperationException($"Sync project '{project.Name}' is disabled");
            }

            if (project.IsRunning)
            {
                _logger.LogWarning("Sync project '{ProjectName}' is already running", project.Name);
                throw new InvalidOperationException($"Sync project '{project.Name}' is already running");
            }

            // PHASE 1 SYNC-SINK FAIL-FAST GUARD.
            // Resolve the write sink BEFORE marking the project running or creating a run
            // record. For a null TargetConnectionId this returns the IdentityStoreSink (the
            // historical Objects-store write). For a non-null (external) target there is no
            // sink implemented in IdentityCenter -- the factory throws here with a clear
            // message, so the run fails fast with no IsRunning flip and no partial work.
            // (PersonMatch/PersonCreate/internal projects also resolve to the identity store,
            // which is exactly their existing behavior.)
            var sinkFactory = new SyncSinkFactory(_syncRepository, _connectionString);
            _activeSink = await sinkFactory.ResolveSinkAsync(project, cancellationToken);
            _logger.LogInformation("Sync run will write via sink '{SinkType}' (TargetConnectionId={TargetConnectionId})",
                _activeSink.SinkType, project.TargetConnectionId?.ToString() ?? "null/IdentityStore");

            // CRITICAL SECTION: Wrap everything after this point in try-finally to guarantee IsRunning cleanup
            SyncProjectRun run = null!;
            try
            {
                // DAPPER: Mark project as running (no EF Core overhead)
                _logger.LogInformation("Marking project '{ProjectName}' as running in database", project.Name);
                await _syncRepository.UpdateProjectStatusAsync(project.Id, isRunning: true, lastRunAt: DateTime.UtcNow, cancellationToken: cancellationToken);
                project.IsRunning = true;
                project.LastRunAt = DateTime.UtcNow;
                _logger.LogInformation("Project marked as running, starting execution");

                // Calculate total steps - if selective execution, only count selected workflow steps
                var workflowsToRun = selectedWorkflowIds != null && selectedWorkflowIds.Any()
                    ? project.Workflows.Where(w => w.IsEnabled && selectedWorkflowIds.Contains(w.Id))
                    : project.Workflows.Where(w => w.IsEnabled);
                var totalStepsToRun = workflowsToRun.SelectMany(w => w.Steps).Count(s => s.IsEnabled);

                // Create sync run record
                run = new SyncProjectRun
                {
                    Id = Guid.NewGuid(),
                    SyncProjectId = syncProjectId,
                    TriggerType = triggerType,
                    TriggeredBy = triggeredBy,
                    StartedAt = DateTime.UtcNow,
                    Status = "Running",
                    TotalSteps = totalStepsToRun,
                    CompletedSteps = 0,
                    ProgressPercentage = 0
                };

                // DAPPER: Create run record directly
                await _syncRepository.CreateSyncProjectRunAsync(run, cancellationToken);

                // Reset all thread-safe counters for this run
                _personsCreatedCount = 0;
                _completedStepsCount = 0;
                _totalErrorsCount = 0;
                _failedStepsCount = 0;

                // Activate trace logging for this sync run
                _syncLogBuffer.StartCapture(run.Id);
                _logger.LogInformation("TRACE LOGGING ACTIVATED - Real-time streaming enabled, logs saved only on error");

                _ = _syncLogBuffer.AddLogAsync(run.Id, Microsoft.Extensions.Logging.LogLevel.Information,
                    $"TRACE LOGGING ACTIVATED for sync run {run.Id} at level {logLevel}", "SyncProjectOrchestrator");

                // INNER try-catch for execution-specific error handling
                try
                {
                    // PROJECT TYPE ROUTING
                    if (project.ProjectType == "PersonMatch" || project.ProjectType == "PersonCreate")
                    {
                        _logger.LogInformation("{ProjectType}: Routing to PersonMatchOrchestrator", project.ProjectType);

                        if (_personMatchOrchestrator == null)
                        {
                            throw new InvalidOperationException(
                                $"PersonMatchOrchestrator not available for {project.ProjectType} project");
                        }

                        var personMatchResult = await _personMatchOrchestrator.ExecuteAsync(project, run, cancellationToken);

                        // Update run metrics from person match result
                        run.TotalObjectsProcessed = personMatchResult.Matched + personMatchResult.Created + personMatchResult.Skipped;
                        run.TotalPersonsCreated = personMatchResult.Created;
                        run.TotalErrors = personMatchResult.Errors;
                        run.Status = personMatchResult.Success ? "Completed" : "Failed";
                        run.ErrorMessage = personMatchResult.ErrorMessage;
                        run.CompletedAt = DateTime.UtcNow;
                        run.DurationSeconds = (int)(run.CompletedAt.Value - run.StartedAt).TotalSeconds;
                        run.ProgressPercentage = 100;

                        // Update run via Dapper
                        await _syncRepository.UpdateSyncProjectRunStatusAsync(
                            run.Id, run.Status, run.CompletedAt, run.DurationSeconds, run.ErrorMessage,
                            run.TotalObjectsProcessed, run.TotalObjectsCreated, run.TotalObjectsUpdated,
                            run.TotalObjectsDeleted, run.TotalPersonsCreated, run.TotalErrors,
                            run.CompletedSteps, run.ProgressPercentage, cancellationToken);

                        // Mark project as not running
                        project.IsRunning = false;
                        if (personMatchResult.Success)
                        {
                            project.SuccessfulExecutions++;
                            project.LastSuccessfulRunAt = DateTime.UtcNow;
                        }
                        else
                        {
                            project.FailedExecutions++;
                        }
                        project.TotalExecutions++;

                        // Update project stats via Dapper
                        using (var connection = CreateConnection())
                        {
                            await connection.ExecuteAsync(
                                @"UPDATE SyncProjects SET IsRunning = 0, TotalExecutions = @TotalExecutions,
                                  SuccessfulExecutions = @SuccessfulExecutions, FailedExecutions = @FailedExecutions,
                                  LastSuccessfulRunAt = @LastSuccessfulRunAt WHERE Id = @Id",
                                new {
                                    Id = project.Id,
                                    TotalExecutions = project.TotalExecutions,
                                    SuccessfulExecutions = project.SuccessfulExecutions,
                                    FailedExecutions = project.FailedExecutions,
                                    LastSuccessfulRunAt = project.LastSuccessfulRunAt
                                });
                        }

                        _logger.LogInformation("{ProjectType}: Completed for project '{Name}'",
                            project.ProjectType, project.Name);

                        await ExecuteProjectChainsAsync(project.Id, run.Status, cancellationToken);
                        return run;
                    }

                    // HR IMPORT ROUTING - with step-level execution tracking
                    if (project.ProjectType == "HRImport")
                    {
                        _logger.LogInformation("HRImport: Routing to HRImportOrchestrator for project '{Name}'", project.Name);

                        if (_hrImportOrchestrator == null)
                            throw new InvalidOperationException("HRImportOrchestrator not available for HRImport project");

                        // Get the workflow steps so we can create proper SyncStepRun records
                        var hrWorkflow = project.Workflows.FirstOrDefault(w => w.IsEnabled);
                        var hrSteps = hrWorkflow?.Steps?.Where(s => s.IsEnabled).OrderBy(s => s.ExecutionOrder).ToList()
                            ?? new List<SyncStep>();

                        var completedSteps = 0;

                        // === STEP 1: HR Import (read source + upsert Identities) ===
                        var importStep = hrSteps.FirstOrDefault(s => s.StepType == "HRImport")
                            ?? hrSteps.FirstOrDefault(s => s.Name.Contains("Import", StringComparison.OrdinalIgnoreCase))
                            ?? hrSteps.FirstOrDefault();
                        SyncStepRun? importStepRun = null;

                        if (importStep != null)
                        {
                            importStepRun = new SyncStepRun
                            {
                                Id = Guid.NewGuid(),
                                SyncProjectRunId = run.Id,
                                SyncStepId = importStep.Id,
                                StepName = importStep.Name,
                                ObjectClass = "Identity",
                                StartedAt = DateTime.UtcNow,
                                Status = "Running",
                                ExecutionLog = ""
                            };
                            await _syncRepository.CreateSyncStepRunAsync(importStepRun, cancellationToken);

                            run.CurrentStep = importStep.Name;
                            await _syncRepository.UpdateRunProgressAsync(run.Id, currentStepName: importStep.Name, cancellationToken: cancellationToken);
                        }

                        var (hrResult, hrImportResult) = await _hrImportOrchestrator.ExecuteAsync(project, run, cancellationToken, importStep);

                        // Update import step run with results
                        if (importStepRun != null)
                        {
                            importStepRun.ObjectsQueried = hrResult.TotalRecords;
                            importStepRun.ObjectsProcessed = hrResult.TotalRecords;
                            importStepRun.ObjectsCreated = hrResult.CreatedRecords;
                            importStepRun.ObjectsUpdated = hrResult.UpdatedRecords;
                            importStepRun.ObjectsSkipped = hrResult.SkippedRecords;
                            importStepRun.ErrorCount = hrResult.ErrorRecords;
                            importStepRun.Status = hrResult.Status == "Failed" ? "Failed" : "Completed";
                            importStepRun.ErrorMessage = hrResult.Status == "Failed" ? hrResult.ErrorDetails : null;
                            importStepRun.CompletedAt = DateTime.UtcNow;
                            importStepRun.DurationSeconds = (int)(importStepRun.CompletedAt.Value - importStepRun.StartedAt).TotalSeconds;

                            await _syncRepository.UpdateStepRunMetricsAsync(
                                importStepRun.Id, importStepRun.ObjectsQueried, importStepRun.ObjectsProcessed,
                                importStepRun.ObjectsCreated, importStepRun.ObjectsUpdated, importStepRun.ObjectsSkipped,
                                importStepRun.ErrorCount, cancellationToken,
                                status: importStepRun.Status, completedAt: importStepRun.CompletedAt,
                                durationSeconds: importStepRun.DurationSeconds);

                            // Write SyncAuditLog records for created/updated/error identities
                            try
                            {
                                var auditLogs = new List<SyncAuditLog>();

                                // Audit logs for created identities
                                if (hrImportResult?.CreatedIdentityIds.Count > 0)
                                {
                                    // Bulk-load display names for created identities
                                    var createdNames = await BulkLoadIdentityNamesAsync(hrImportResult.CreatedIdentityIds, cancellationToken);
                                    foreach (var id in hrImportResult.CreatedIdentityIds)
                                    {
                                        createdNames.TryGetValue(id, out var displayName);
                                        auditLogs.Add(new SyncAuditLog
                                        {
                                            SyncStepRunId = importStepRun.Id,
                                            ObjectId = id,
                                            OperationType = "Created",
                                            ObjectDisplayName = displayName ?? id.ToString(),
                                            Timestamp = DateTime.UtcNow
                                        });
                                    }
                                }

                                // Audit logs for updated identities
                                if (hrImportResult?.UpdatedIdentityIds.Count > 0)
                                {
                                    var updatedNames = await BulkLoadIdentityNamesAsync(hrImportResult.UpdatedIdentityIds, cancellationToken);
                                    var changesByIdentity = hrImportResult.UpdatedIdentityChanges
                                        .ToDictionary(c => c.IdentityId, c => c);

                                    foreach (var id in hrImportResult.UpdatedIdentityIds)
                                    {
                                        updatedNames.TryGetValue(id, out var displayName);
                                        changesByIdentity.TryGetValue(id, out var change);
                                        auditLogs.Add(new SyncAuditLog
                                        {
                                            SyncStepRunId = importStepRun.Id,
                                            ObjectId = id,
                                            OperationType = "Updated",
                                            ObjectDisplayName = displayName ?? id.ToString(),
                                            ChangeCount = change?.ChangedFields.Count ?? 0,
                                            ChangeDetails = change?.ChangedFields.Count > 0
                                                ? System.Text.Json.JsonSerializer.Serialize(change.ChangedFields)
                                                : null,
                                            Timestamp = DateTime.UtcNow
                                        });
                                    }
                                }

                                // Audit logs for errors
                                if (hrResult.ErrorRecords > 0 && !string.IsNullOrEmpty(hrResult.ErrorDetails))
                                {
                                    var errorItems = System.Text.Json.JsonSerializer.Deserialize<List<HRImportError>>(
                                        hrResult.ErrorDetails, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                    if (errorItems?.Count > 0)
                                    {
                                        foreach (var e in errorItems)
                                        {
                                            auditLogs.Add(new SyncAuditLog
                                            {
                                                SyncStepRunId = importStepRun.Id,
                                                OperationType = "Error",
                                                ObjectDisplayName = e.Row > 0 ? $"Row {e.Row}" : "Unknown",
                                                SourceUniqueId = e.Field,
                                                ErrorMessage = e.Error,
                                                Timestamp = DateTime.UtcNow
                                            });
                                        }
                                    }
                                }

                                if (auditLogs.Count > 0)
                                {
                                    await _syncRepository.BulkInsertAuditLogsAsync(auditLogs, cancellationToken);
                                    _logger.LogInformation("Wrote {Count} HR Import audit logs (Created={Created}, Updated={Updated}, Errors={Errors})",
                                        auditLogs.Count, hrResult.CreatedRecords, hrResult.UpdatedRecords, hrResult.ErrorRecords);
                                }
                            }
                            catch (Exception auditEx)
                            {
                                _logger.LogWarning(auditEx, "Failed to write HR Import audit logs (non-fatal)");
                            }

                            // Auto-assign step tags to imported identities AND their linked objects
                            if (importStep?.StepTags != null && importStep.StepTags.Any() && hrImportResult != null)
                            {
                                try
                                {
                                    var allIdentityIds = new List<Guid>();
                                    allIdentityIds.AddRange(hrImportResult.CreatedIdentityIds);
                                    allIdentityIds.AddRange(hrImportResult.UpdatedIdentityIds);
                                    var distinctIds = allIdentityIds.Distinct().ToList();

                                    if (distinctIds.Count > 0)
                                    {
                                        using var tagConn = CreateConnection();
                                        await tagConn.OpenAsync(cancellationToken);

                                        foreach (var stepTag in importStep.StepTags)
                                        {
                                            var tagName = stepTag.Tag?.Name ?? stepTag.TagId.ToString();
                                            var identityTagged = 0;
                                            var objectTagged = 0;

                                            foreach (var batch in distinctIds.Chunk(500))
                                            {
                                                foreach (var identityId in batch)
                                                {
                                                    // Tag the Identity
                                                    var inserted = await tagConn.ExecuteAsync(
                                                        @"IF NOT EXISTS (SELECT 1 FROM IdentityTags WHERE IdentityId = @IdentityId AND TagId = @TagId)
                                                          INSERT INTO IdentityTags (Id, IdentityId, TagId, IsInherited, CreatedAt, CreatedBy)
                                                          VALUES (NEWID(), @IdentityId, @TagId, 1, GETUTCDATE(), 'HRImport')",
                                                        new { IdentityId = identityId, TagId = stepTag.TagId });
                                                    identityTagged += inserted;

                                                    // Also tag any linked Objects so tags appear on the Objects page
                                                    var objInserted = await tagConn.ExecuteAsync(
                                                        @"INSERT INTO ObjectTags (Id, ObjectId, TagId, IsInherited, CreatedAt, CreatedBy)
                                                          SELECT NEWID(), o.Id, @TagId, 1, GETUTCDATE(), 'HRImport'
                                                          FROM Objects o
                                                          WHERE o.IdentityId = @IdentityId
                                                            AND NOT EXISTS (SELECT 1 FROM ObjectTags ot WHERE ot.ObjectId = o.Id AND ot.TagId = @TagId)",
                                                        new { IdentityId = identityId, TagId = stepTag.TagId });
                                                    objectTagged += objInserted;
                                                }
                                            }

                                            _logger.LogInformation("HR Import: Auto-assigned tag '{TagName}' to {IdentityCount} identities and {ObjectCount} linked objects (of {TotalCount} total)",
                                                tagName, identityTagged, objectTagged, distinctIds.Count);
                                        }
                                    }
                                }
                                catch (Exception tagEx)
                                {
                                    _logger.LogWarning(tagEx, "HR Import: Auto-tag assignment encountered errors (non-fatal)");
                                }
                            }

                            completedSteps++;
                            var pct = run.TotalSteps > 0 ? (int)((completedSteps / (double)run.TotalSteps) * 100) : 50;
                            _ = UpdateRunProgressAsync(run.Id, completedSteps, pct, cancellationToken);
                        }

                        // === STEP 2: Identity Manager Lookup (resolve ManagerEmployeeId → ManagerIdentityId) ===
                        var managerStep = hrSteps.FirstOrDefault(s => s.StepType == "IdentityManagerLookup")
                            ?? hrSteps.FirstOrDefault(s => s.Name.Contains("Manager", StringComparison.OrdinalIgnoreCase) && s != importStep);
                        if (managerStep != null)
                        {
                            await ProcessIdentityManagerLookupStepAsync(managerStep, run, project, cancellationToken);
                            completedSteps++;
                            var pct = run.TotalSteps > 0 ? (int)((completedSteps / (double)run.TotalSteps) * 100) : 100;
                            _ = UpdateRunProgressAsync(run.Id, completedSteps, pct, cancellationToken);
                        }

                        // Finalize the run
                        run.TotalObjectsProcessed = hrResult.TotalRecords;
                        run.TotalObjectsCreated = hrResult.CreatedRecords;
                        run.TotalObjectsUpdated = hrResult.UpdatedRecords;
                        run.TotalPersonsCreated = hrResult.CreatedRecords;
                        run.TotalErrors = hrResult.ErrorRecords;
                        run.CompletedSteps = completedSteps;
                        run.Status = hrResult.Status == "Failed" ? "Failed" : "Completed";
                        run.ErrorMessage = hrResult.Status == "Failed" ? "HR Import failed - see import run details" : null;
                        run.CompletedAt = DateTime.UtcNow;
                        run.DurationSeconds = (int)(run.CompletedAt.Value - run.StartedAt).TotalSeconds;
                        run.ProgressPercentage = 100;

                        await _syncRepository.UpdateSyncProjectRunStatusAsync(
                            run.Id, run.Status, run.CompletedAt, run.DurationSeconds, run.ErrorMessage,
                            run.TotalObjectsProcessed, run.TotalObjectsCreated, run.TotalObjectsUpdated,
                            run.TotalObjectsDeleted, run.TotalPersonsCreated, run.TotalErrors,
                            run.CompletedSteps, run.ProgressPercentage, cancellationToken);

                        project.IsRunning = false;
                        if (run.Status == "Completed") { project.SuccessfulExecutions++; project.LastSuccessfulRunAt = DateTime.UtcNow; }
                        else { project.FailedExecutions++; }
                        project.TotalExecutions++;

                        using (var connection = CreateConnection())
                        {
                            await connection.ExecuteAsync(
                                @"UPDATE SyncProjects SET IsRunning = 0, TotalExecutions = @TotalExecutions,
                                  SuccessfulExecutions = @SuccessfulExecutions, FailedExecutions = @FailedExecutions,
                                  LastSuccessfulRunAt = @LastSuccessfulRunAt WHERE Id = @Id",
                                new { Id = project.Id, project.TotalExecutions, project.SuccessfulExecutions, project.FailedExecutions, project.LastSuccessfulRunAt });
                        }

                        _logger.LogInformation("HRImport: Completed for project '{Name}': Created={Created}, Updated={Updated}",
                            project.Name, hrResult.CreatedRecords, hrResult.UpdatedRecords);

                        await ExecuteProjectChainsAsync(project.Id, run.Status, cancellationToken);
                        return run;
                    }

                    _logger.LogInformation("Executing {WorkflowCount} workflows for project '{ProjectName}'",
                        project.Workflows.Count, project.Name);

                    _ = _syncLogBuffer.AddLogAsync(run.Id, Microsoft.Extensions.Logging.LogLevel.Information,
                        $"Executing {project.Workflows.Count} workflows for project '{project.Name}'", "SyncProjectOrchestrator");

                    // PRE-SYNC DATABASE OPTIMIZATION (Optional)
                    if (project.EnablePreSyncIndexing && _databaseOptimizationService != null)
                    {
                        await ExecutePreSyncOptimizationAsync(run, cancellationToken);
                    }

                    // PERFORMANCE OPTIMIZATION: Load caches ONCE before all workflows
                    _logger.LogInformation("BULK CACHE LOADING: Loading all existing objects ONCE for entire sync run");
                    var existingObjectsCache = await _syncRepository.BulkLoadExistingObjectsAsync(
                        project.SourceConnectionId!.Value,
                        cancellationToken);
                    _logger.LogInformation("Cached {Count} existing objects for O(1) lookups across ALL workflows",
                        existingObjectsCache.Count);

                    _logger.LogInformation("BULK CACHE LOADING: Loading all persons ONCE for entire sync run");
                    var personCache = await _syncRepository.BulkLoadIdentitiesAsync(cancellationToken);
                    _logger.LogInformation("Cached {EmailCount} persons by email + {NameCount} by name for O(1) lookups across ALL workflows",
                        personCache.ByEmail.Count, personCache.ByName.Count);

                    // PARALLEL WORKFLOW EXECUTION (LIMITED CONCURRENCY)
                    var enabledWorkflows = project.Workflows.Where(w => w.IsEnabled).ToList();

                    if (selectedWorkflowIds != null && selectedWorkflowIds.Any())
                    {
                        enabledWorkflows = enabledWorkflows.Where(w => selectedWorkflowIds.Contains(w.Id)).ToList();
                        _logger.LogInformation("SELECTIVE EXECUTION: Running only {SelectedCount} selected workflows out of {TotalCount} enabled",
                            enabledWorkflows.Count, project.Workflows.Count(w => w.IsEnabled));
                    }
                    const int maxConcurrentWorkflows = 1;
                    _logger.LogInformation("PARALLEL EXECUTION: Starting {Count} workflows (max {Max} concurrent)",
                        enabledWorkflows.Count, maxConcurrentWorkflows);

                    var completedWorkflows = new ConcurrentBag<string>();
                    var workflowErrors = new ConcurrentBag<Exception>();

                    using var semaphore = new SemaphoreSlim(maxConcurrentWorkflows);

                    var workflowTasks = enabledWorkflows.Select(async workflow =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            if (cancellationToken.IsCancellationRequested) return;

                            await ExecuteWorkflowAsync(workflow, run, project, existingObjectsCache, personCache, cancellationToken);
                            completedWorkflows.Add(workflow.Name);
                            _logger.LogInformation("Workflow '{WorkflowName}' completed ({Completed}/{Total})",
                                workflow.Name, completedWorkflows.Count, enabledWorkflows.Count);
                        }
                        catch (Exception ex)
                        {
                            workflowErrors.Add(ex);
                            _logger.LogError(ex, "Workflow '{WorkflowName}' failed", workflow.Name);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(workflowTasks);

                    _logger.LogInformation("PARALLEL EXECUTION COMPLETE: {Completed}/{Total} workflows succeeded",
                        completedWorkflows.Count, enabledWorkflows.Count);

                    if (workflowErrors.Any())
                    {
                        run.TotalErrors += workflowErrors.Count;
                        if (project.PauseOnError && run.TotalErrors >= project.MaxErrorsBeforePause)
                        {
                            _logger.LogWarning("Maximum errors reached ({Errors}), marking sync as paused", run.TotalErrors);
                            run.Status = "Paused";
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        run.Status = "Cancelled";
                    }

                    // SYNC COMPLETION - PURE DAPPER/SQL
                    _logger.LogInformation("COMPLETION START: Beginning sync completion sequence for run {RunId}", run.Id);

                    // ROLLUP FROM ACTUAL STEP RUNS (source of truth).
                    // Previously the run was marked Completed whenever run.Status was still "Running",
                    // ignoring the fact that individual steps may have ended Status='Failed' (e.g. when
                    // ContinueOnError swallowed the step exception). That produced the "green success /
                    // 0 objects" bug. We now derive the run outcome from the persisted SyncStepRuns.
                    int rolledFailedSteps = 0;
                    int rolledCompletedSteps = 0;
                    int rolledSkippedSteps = 0;
                    int rolledTotalErrors = run.TotalErrors;
                    string? rolledStepError = null;
                    try
                    {
                        using var rollupConn = CreateConnection();
                        await rollupConn.OpenAsync(cancellationToken);
                        var stepOutcomes = (await rollupConn.QueryAsync<(string Status, int ErrorCount, string? ErrorMessage)>(
                            @"SELECT Status, ErrorCount, ErrorMessage FROM SyncStepRuns WHERE SyncProjectRunId = @RunId",
                            new { RunId = run.Id })).AsList();

                        rolledFailedSteps = stepOutcomes.Count(s => string.Equals(s.Status, "Failed", StringComparison.OrdinalIgnoreCase));
                        rolledCompletedSteps = stepOutcomes.Count(s => string.Equals(s.Status, "Completed", StringComparison.OrdinalIgnoreCase));
                        rolledSkippedSteps = stepOutcomes.Count(s => string.Equals(s.Status, "Skipped", StringComparison.OrdinalIgnoreCase));
                        rolledTotalErrors = Math.Max(rolledTotalErrors, stepOutcomes.Sum(s => s.ErrorCount));
                        rolledStepError = stepOutcomes
                            .Where(s => string.Equals(s.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                                        && !string.IsNullOrWhiteSpace(s.ErrorMessage))
                            .Select(s => s.ErrorMessage)
                            .FirstOrDefault();
                    }
                    catch (Exception rollupEx)
                    {
                        _logger.LogError(rollupEx, "STEP ROLLUP FAILED for run {RunId}; falling back to in-memory counters", run.Id);
                        rolledFailedSteps = _failedStepsCount;
                        rolledTotalErrors = Math.Max(rolledTotalErrors, _totalErrorsCount);
                    }

                    string finalStatus;
                    int successDelta = 0;
                    int failDelta = 0;

                    if (run.Status == "Cancelled" || run.Status == "Paused")
                    {
                        finalStatus = run.Status;
                        failDelta = 1;
                        _logger.LogInformation("STATUS UPDATE: Run {RunId} ended with status '{Status}'", run.Id, finalStatus);
                    }
                    else if (run.Status == "Running")
                    {
                        // Derive the true outcome from step results.
                        if (rolledFailedSteps > 0 && rolledCompletedSteps > 0)
                        {
                            finalStatus = "PartialSuccess";
                            failDelta = 1;
                        }
                        else if (rolledFailedSteps > 0)
                        {
                            finalStatus = "Failed";
                            failDelta = 1;
                        }
                        else
                        {
                            finalStatus = "Completed";
                            successDelta = 1;
                        }
                        _logger.LogInformation(
                            "STATUS UPDATE: Run {RunId} -> {Status} (completedSteps={Completed}, failedSteps={Failed}, totalErrors={Errors})",
                            run.Id, finalStatus, rolledCompletedSteps, rolledFailedSteps, rolledTotalErrors);
                    }
                    else
                    {
                        finalStatus = run.Status;
                        _logger.LogInformation("STATUS UPDATE: Run {RunId} status is '{Status}'", run.Id, finalStatus);
                    }

                    // Surface a run-level error message when steps failed but no top-level exception was thrown.
                    if ((finalStatus == "Failed" || finalStatus == "PartialSuccess") && string.IsNullOrWhiteSpace(run.ErrorMessage))
                    {
                        run.ErrorMessage = rolledStepError
                            ?? $"{rolledFailedSteps} of {run.TotalSteps} step(s) failed. See step details for the reason.";
                    }

                    run.FailedSteps = rolledFailedSteps;
                    run.CompletedSteps = rolledCompletedSteps;
                    run.SkippedSteps = rolledSkippedSteps;
                    run.TotalErrors = rolledTotalErrors;

                    var completedAt = DateTime.UtcNow;
                    var durationSeconds = (int)(completedAt - run.StartedAt).TotalSeconds;

                    _logger.LogInformation("COMPLETION TIME: CompletedAt = {CompletedAt}, Duration = {Duration}s",
                        completedAt, durationSeconds);

                    _logger.LogInformation("Trace logging completed - logs kept in memory for viewing ({LogCount} entries)",
                        _syncLogBuffer.GetLogCount(run.Id));

                    // DIRECT SQL PERSISTENCE - Simple, Reliable
                    _logger.LogInformation("DAPPER PERSISTENCE: Persisting sync completion via direct SQL for run {RunId}", run.Id);

                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();

                        using (var connection = CreateConnection())
                        {
                            await connection.OpenAsync(cancellationToken);

                            await connection.ExecuteAsync(@"
                                -- Close any step still flagged Running at run completion. It never finished,
                                -- so mark it Failed (NOT Completed) so a stuck/abandoned step is not reported green.
                                UPDATE SyncStepRuns
                                SET Status = 'Failed',
                                    CompletedAt = @CompletedAt,
                                    ErrorMessage = COALESCE(ErrorMessage, 'Step did not complete before the run ended.')
                                WHERE SyncProjectRunId = @RunId
                                  AND Status = 'Running';

                                -- Update sync run completion status with the rolled-up outcome
                                UPDATE SyncProjectRuns
                                SET Status = @Status,
                                    CompletedAt = @CompletedAt,
                                    DurationSeconds = @DurationSeconds,
                                    ProgressPercentage = 100,
                                    CompletedSteps = @CompletedSteps,
                                    FailedSteps = @FailedSteps,
                                    SkippedSteps = @SkippedSteps,
                                    TotalErrors = @TotalErrors,
                                    ErrorMessage = COALESCE(@ErrorMessage, ErrorMessage)
                                WHERE Id = @RunId;

                                -- Update project statistics and clear running flag
                                UPDATE SyncProjects
                                SET IsRunning = 0,
                                    TotalExecutions = TotalExecutions + 1,
                                    SuccessfulExecutions = SuccessfulExecutions + @SuccessDelta,
                                    FailedExecutions = FailedExecutions + @FailDelta,
                                    LastSuccessfulRunAt = CASE WHEN @SuccessDelta > 0 THEN @CompletedAt ELSE LastSuccessfulRunAt END
                                WHERE Id = @ProjectId;

                                -- Bump source connection's LastSyncAt only on a genuinely successful run
                                UPDATE DirectoryConnections
                                SET LastSyncAt = @CompletedAt
                                WHERE @SourceConnectionId IS NOT NULL
                                  AND Id = @SourceConnectionId
                                  AND @SuccessDelta > 0;",
                                new {
                                    Status = finalStatus,
                                    CompletedAt = completedAt,
                                    DurationSeconds = durationSeconds,
                                    RunId = run.Id,
                                    ProjectId = project.Id,
                                    SuccessDelta = successDelta,
                                    FailDelta = failDelta,
                                    CompletedSteps = run.CompletedSteps,
                                    FailedSteps = run.FailedSteps,
                                    SkippedSteps = run.SkippedSteps,
                                    TotalErrors = run.TotalErrors,
                                    ErrorMessage = run.ErrorMessage,
                                    SourceConnectionId = project.SourceConnectionId
                                });
                        }

                        sw.Stop();
                        _logger.LogInformation("DAPPER SUCCESS: Persisted sync completion in {ElapsedMs}ms for run {RunId}",
                            sw.ElapsedMilliseconds, run.Id);

                        // Update in-memory objects to match database
                        run.Status = finalStatus;
                        run.CompletedAt = completedAt;
                        run.DurationSeconds = durationSeconds;
                        run.ProgressPercentage = 100;
                        project.IsRunning = false;
                        project.TotalExecutions++;
                        project.SuccessfulExecutions += successDelta;
                        project.FailedExecutions += failDelta;
                        if (successDelta > 0)
                        {
                            project.LastSuccessfulRunAt = completedAt;
                        }

                        _logger.LogInformation("COMPLETION VERIFIED: Sync completion successfully persisted for run {RunId}", run.Id);

                        await ExecuteProjectChainsAsync(project.Id, run.Status, cancellationToken);
                    }
                    catch (Exception sqlEx)
                    {
                        _logger.LogCritical(sqlEx,
                            "PERSISTENCE FAILED: Direct SQL failed to persist completion for run {RunId}: {Error}",
                            run.Id, sqlEx.Message);
                        throw new InvalidOperationException(
                            $"CRITICAL: Failed to persist sync completion via direct SQL. Run ID: {run.Id}",
                            sqlEx);
                    }

                    // POST-SYNC INDEX OPTIMIZATION
                    await OptimizeIndexesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Sync project '{ProjectName}' completed: Status={Status}, " +
                        "Objects Created={Created}, Updated={Updated}, Deleted={Deleted}, Persons={Persons}, Errors={Errors}",
                        project.Name, run.Status,
                        run.TotalObjectsCreated, run.TotalObjectsUpdated, run.TotalObjectsDeleted, run.TotalPersonsCreated, run.TotalErrors);

                    // ADMIN NOTIFICATION
                    await PostSyncNotificationAsync(project, run, cancellationToken);

                    return run;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sync project '{ProjectName}' failed: {ErrorMessage}",
                        project.Name, ex.Message);

                    run.Status = "Failed";
                    run.ErrorMessage = ex.Message;

                    var traceLogs = _syncLogBuffer.GetFormattedLogs(run.Id);
                    if (!string.IsNullOrEmpty(traceLogs))
                    {
                        run.ExecutionLog = $"=== TRACE LOGS ===\n{traceLogs}\n\n=== EXCEPTION DETAILS ===\n{ex}";
                        _logger.LogWarning("Persisting {LogCount} trace log entries to database due to error",
                            _syncLogBuffer.GetLogCount(run.Id));
                    }
                    else
                    {
                        run.ExecutionLog = ex.ToString();
                    }

                    run.CompletedAt = DateTime.UtcNow;
                    run.DurationSeconds = (int)(run.CompletedAt.Value - run.StartedAt).TotalSeconds;

                    project.IsRunning = false;
                    project.TotalExecutions++;
                    project.FailedExecutions++;

                    // DAPPER: Persist error state
                    await _syncRepository.UpdateSyncProjectRunStatusAsync(
                        run.Id, run.Status, run.CompletedAt, run.DurationSeconds, run.ErrorMessage,
                        run.TotalObjectsProcessed, run.TotalObjectsCreated, run.TotalObjectsUpdated,
                        run.TotalObjectsDeleted, run.TotalPersonsCreated, run.TotalErrors,
                        run.CompletedSteps, run.ProgressPercentage, CancellationToken.None);
                    await _syncRepository.UpdateSyncProjectExecutionStatusAsync(project.Id, false, false, CancellationToken.None);

                    _syncLogBuffer.ClearBuffer(run.Id);

                    throw;
                }
            }
            finally
            {
                try
                {
                    _logger.LogDebug("Finally block executing - checking IsRunning flag");

                    if (project.IsRunning)
                    {
                        _logger.LogWarning("CLEANUP: Resetting IsRunning flag");
                        project.IsRunning = false;
                        await _syncRepository.UpdateProjectStatusAsync(project.Id, isRunning: false, cancellationToken: CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CRITICAL: Failed to reset IsRunning");
                }
            }
        }

        /// <summary>
        /// Executes a single workflow within a sync project.
        /// Uses Dapper for all database operations.
        /// </summary>
        private async Task ExecuteWorkflowAsync(
            SyncWorkflow workflow,
            SyncProjectRun run,
            SyncProject project,
            Dictionary<string, DataAccessLibrary.Repositories.ObjectWithAttributes> existingObjectsCache,
            DataAccessLibrary.Repositories.IdentityLookupCache personCache,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Executing workflow '{WorkflowName}' ({ObjectClass}) in parallel",
                workflow.Name, workflow.ObjectClass);

            // Load PersonMatchingService from scope
            using var workflowScope = _scopeFactory.CreateScope();
            var matchingService = workflowScope.ServiceProvider.GetRequiredService<PersonMatchingService>();
            var syncRepository = workflowScope.ServiceProvider.GetRequiredService<ISyncRepository>();

            await UpdateRunCurrentStepAsync(run.Id, $"Workflow: {workflow.Name}", cancellationToken);

            foreach (var step in workflow.Steps.Where(s => s.IsEnabled).OrderBy(s => s.ExecutionOrder))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await ExecuteStepAsync(matchingService, syncRepository, step, run, project, existingObjectsCache, personCache, cancellationToken);

                    var newCompletedSteps = Interlocked.Increment(ref _completedStepsCount);
                    var progressPct = (int)((newCompletedSteps / (double)run.TotalSteps) * 100);

                    _ = UpdateRunProgressAsync(run.Id, newCompletedSteps, progressPct, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("=== CANCELLATION IN WORKFLOW === Step '{StepName}' cancelled, stopping workflow '{WorkflowName}'",
                        step.Name, workflow.Name);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing step '{StepName}': {ErrorMessage}",
                        step.Name, ex.Message);

                    Interlocked.Increment(ref _totalErrorsCount);
                    Interlocked.Increment(ref _failedStepsCount);

                    if (!step.ContinueOnError && !workflow.ContinueOnError)
                    {
                        throw;
                    }

                    _logger.LogWarning("Continuing after step error due to ContinueOnError setting");
                }
            }
        }

        /// <summary>
        /// Executes a single step: queries directory, applies mappings, writes to Identities table.
        /// Uses pre-loaded caches for O(1) lookups instead of per-step bulk loading.
        /// </summary>
        private async Task ExecuteStepAsync(
            PersonMatchingService matchingService,
            ISyncRepository syncRepository,
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            Dictionary<string, DataAccessLibrary.Repositories.ObjectWithAttributes> existingObjectsCache,
            DataAccessLibrary.Repositories.IdentityLookupCache personCache,
            CancellationToken cancellationToken)
        {
            var configuredSearchBases = step.GetSearchBaseList();
            var configuredTags = step.StepTags?.Select(st => st.Tag?.Name ?? st.TagId.ToString()).ToList() ?? new List<string>();
            _logger.LogInformation("STEP CONFIG: '{StepName}' Order={Order} ObjectClass={ObjectClass} SearchBases=[{SearchBases}] Tags=[{Tags}]",
                step.Name, step.ExecutionOrder, step.ObjectClass,
                string.Join("; ", configuredSearchBases),
                string.Join(", ", configuredTags));

            // Route GroupMembership steps to specialized processor
            if (step.ObjectClass?.Equals("GroupMembership", StringComparison.OrdinalIgnoreCase) == true)
            {
                await ProcessGroupMembershipStepAsync(step, run, project, cancellationToken);
                return;
            }

            // Route Lookup steps to specialized processor
            if (step.StepType?.Equals("Lookup", StringComparison.OrdinalIgnoreCase) == true ||
                step.ObjectClass?.Equals("ManagerLookup", StringComparison.OrdinalIgnoreCase) == true)
            {
                SyncWorkflow? workflow = null;
                using (var connection = CreateConnection())
                {
                    workflow = await connection.QueryFirstOrDefaultAsync<SyncWorkflow>(
                        @"SELECT * FROM SyncWorkflows WHERE Id = @Id",
                        new { Id = step.SyncWorkflowId });
                }

                if (workflow?.ObjectClass?.Equals("group", StringComparison.OrdinalIgnoreCase) == true ||
                    step.Name?.Contains("Group Owner", StringComparison.OrdinalIgnoreCase) == true ||
                    step.Name?.Contains("Resolve Group", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogInformation("Routing step '{StepName}' to Group Owner Lookup processor", step.Name);
                    await ProcessGroupOwnerLookupStepAsync(step, run, project, cancellationToken);
                    return;
                }
                await ProcessLookupStepAsync(step, run, project, cancellationToken);
                return;
            }

            // Route GroupOwnerLookup steps to specialized processor
            if (step.ObjectClass?.Equals("GroupOwnerLookup", StringComparison.OrdinalIgnoreCase) == true)
            {
                await ProcessGroupOwnerLookupStepAsync(step, run, project, cancellationToken);
                return;
            }

            // Route LicenseSync steps to specialized processor
            if (step.StepType?.Equals("LicenseSync", StringComparison.OrdinalIgnoreCase) == true)
            {
                await ProcessLicenseSyncStepAsync(step, run, project, cancellationToken);
                return;
            }

            // Route SignInLogSync steps to specialized processor
            if (step.StepType?.Equals("SignInLogSync", StringComparison.OrdinalIgnoreCase) == true)
            {
                await ProcessSignInLogSyncStepAsync(step, run, project, cancellationToken);
                return;
            }

            // Route UsageReportSync steps to specialized processor
            if (step.StepType?.Equals("UsageReportSync", StringComparison.OrdinalIgnoreCase) == true)
            {
                await ProcessUsageReportSyncStepAsync(step, run, project, cancellationToken);
                return;
            }

            // Route AppRoleSync steps to specialized processor
            if (step.StepType?.Equals("AppRoleSync", StringComparison.OrdinalIgnoreCase) == true)
            {
                await ProcessAppRoleSyncStepAsync(step, run, project, cancellationToken);
                return;
            }

            run.CurrentStep = step.Name;
            await _syncRepository.UpdateRunProgressAsync(run.Id, currentStepName: step.Name, cancellationToken: cancellationToken);

            // Create step run record
            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = step.ObjectClass,
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = "",
                ObjectsQueried = -1
            };

            var skipReasons = new Dictionary<string, int>();
            var detailedSkipLog = new List<string>();

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            try
            {
                // Query directory using step configuration, routing through connector factory
                var connectionType = project.SourceConnection?.ConnectionType ?? "ActiveDirectory";
                var sourceData = await _connectorQueryServiceFactory.GetService(connectionType)
                    .QueryDirectoryForStepAsync(step, project.SourceConnection, cancellationToken);

                // Execute pre-processing scripts
                sourceData = await ExecutePreProcessingScriptsAsync(sourceData, step, project, stepRun, cancellationToken);

                stepRun.ObjectsQueried = sourceData.Count;
                _logger.LogInformation("Step '{StepName}' queried {Count} objects from directory",
                    step.Name, sourceData.Count);

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    0, 0, 0, 0, 0,
                    cancellationToken);

                _logger.LogDebug("Using pre-loaded cache with {ObjectCount} objects and {PersonCount} persons for step '{StepName}'",
                    existingObjectsCache.Count, personCache.ByEmail.Count + personCache.ByName.Count, step.Name);

                var auditLogBatch = new List<SyncAuditLog>();
                var identitiesForMembershipSync = new ConcurrentBag<(IdentityObject identityObject, Dictionary<string, object> sourceObject)>();

                int batchSize = step.BatchSize > 0 ? step.BatchSize : _syncOptions.DefaultBatchSize;
                _logger.LogInformation("Using batch size of {BatchSize} for step '{StepName}'", batchSize, step.Name);

                int processedCount = 0;

                _logger.LogInformation("PARALLEL PROCESSING: Processing {TotalRecords} records in batches of {BatchSize}",
                    sourceData.Count, batchSize);

                for (int batchStart = 0; batchStart < sourceData.Count; batchStart += batchSize)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var currentBatch = sourceData.Skip(batchStart).Take(batchSize).ToList();
                    var batchNumber = (batchStart / batchSize) + 1;
                    var totalBatches = (int)Math.Ceiling(sourceData.Count / (double)batchSize);

                    _logger.LogDebug("Processing batch {BatchNumber}/{TotalBatches} ({BatchSize} items)",
                        batchNumber, totalBatches, currentBatch.Count);

                    var batchResults = new List<SyncAuditLog?>();
                    var bulkUpsertList = new List<(IdentityObject identityObject, List<ObjectAttribute> attributes)>();
                    var bulkUpsertMetadata = new List<(bool isNew, Dictionary<string, object> sourceObject, DateTime startTime)>();
                    DataAccessLibrary.Repositories.BulkUpsertResult? lastBulkResult = null;

                    foreach (var sourceObject in currentBatch)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (step.ObjectClass.Equals("Group", StringComparison.OrdinalIgnoreCase))
                            {
                                var prepared = await PrepareGroupObjectForBulkAsync(existingObjectsCache, sourceObject, step, project, stepRun,
                                    skipReasons, detailedSkipLog, cancellationToken);

                                if (prepared != null)
                                {
                                    bulkUpsertList.Add((prepared.Value.identityObject, prepared.Value.attributes));
                                    bulkUpsertMetadata.Add((prepared.Value.isNew, prepared.Value.sourceObject, DateTime.UtcNow));
                                }
                                else
                                {
                                    batchResults.Add(null);
                                }
                            }
                            else if (step.ObjectClass.Equals("GroupMembership", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogWarning("GroupMembership step encountered in normal processing loop - skipping");
                                batchResults.Add(null);
                            }
                            else
                            {
                                var prepared = await PrepareSourceObjectForBulkAsync(matchingService, existingObjectsCache,
                                    personCache, sourceObject, step, project, stepRun, skipReasons, detailedSkipLog, cancellationToken);

                                if (prepared != null)
                                {
                                    bulkUpsertList.Add((prepared.Value.identityObject, prepared.Value.attributes));
                                    bulkUpsertMetadata.Add((prepared.Value.isNew, prepared.Value.sourceObject, DateTime.UtcNow));
                                }
                                else
                                {
                                    batchResults.Add(null);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error processing object in step '{StepName}': {Error}",
                                step.Name, ex.Message);

                            var errorAuditLog = new SyncAuditLog
                            {
                                SyncStepRunId = stepRun.Id,
                                ObjectId = null,
                                OperationType = "Error",
                                ObjectDisplayName = sourceObject.ContainsKey("displayName") ? sourceObject["displayName"]?.ToString() : null,
                                SourceUniqueId = sourceObject.ContainsKey("objectGuid") ? sourceObject["objectGuid"]?.ToString() : null,
                                Email = sourceObject.ContainsKey("mail") ? sourceObject["mail"]?.ToString() : null,
                                Username = sourceObject.ContainsKey("sAMAccountName") ? sourceObject["sAMAccountName"]?.ToString() : null,
                                UserPrincipalName = sourceObject.ContainsKey("userPrincipalName") ? sourceObject["userPrincipalName"]?.ToString() : null,
                                ErrorMessage = FormatFullException(ex),
                                ChangeCount = 0,
                                ProcessingTimeMs = 0,
                                Timestamp = DateTime.UtcNow
                            };

                            batchResults.Add(errorAuditLog);
                        }
                    }

                    // BULK UPSERT
                    if (bulkUpsertList.Any())
                    {
                        var originalCount = bulkUpsertList.Count;
                        var deduplicatedList = bulkUpsertList
                            .GroupBy(x => (x.identityObject.SourceConnectionId, x.identityObject.SourceUniqueId?.ToUpperInvariant()))
                            .Select(g => g.First())
                            .ToList();

                        if (deduplicatedList.Count < originalCount)
                        {
                            _logger.LogWarning("DEDUP: Removed {DuplicateCount} duplicates. FIRST 3 IDs: " + string.Join(", ", bulkUpsertList.Take(3).Select(x => x.identityObject.SourceUniqueId ?? "NULL")),
                                originalCount - deduplicatedList.Count, originalCount, deduplicatedList.Count);

                            var deduplicatedMetadata = new List<(bool isNew, Dictionary<string, object> sourceObject, DateTime startTime)>();
                            var seenKeys = new HashSet<(Guid, string?)>();
                            for (int i = 0; i < bulkUpsertList.Count; i++)
                            {
                                var key = (bulkUpsertList[i].identityObject.SourceConnectionId,
                                           bulkUpsertList[i].identityObject.SourceUniqueId?.ToUpperInvariant());
                                if (seenKeys.Add(key))
                                {
                                    deduplicatedMetadata.Add(bulkUpsertMetadata[i]);
                                }
                            }
                            bulkUpsertMetadata = deduplicatedMetadata;
                            bulkUpsertList = deduplicatedList;
                        }

                        _logger.LogInformation("DAPPER BULK: Processing {Count} objects (fast batch approach)", bulkUpsertList.Count);

                        try
                        {
                            var baseProcessed = processedCount;
                            DateTime lastMetricsUpdate = DateTime.MinValue;
                            Func<int, int, Task> onProgress = async (batchProcessed, batchTotal) =>
                            {
                                // Debounce: only update metrics at most every 5 seconds to reduce lock contention
                                if ((DateTime.UtcNow - lastMetricsUpdate).TotalSeconds < 5 && batchProcessed < batchTotal)
                                    return;
                                try
                                {
                                    await _syncRepository.UpdateStepRunMetricsAsync(
                                        stepRun.Id,
                                        stepRun.ObjectsQueried,
                                        baseProcessed + batchProcessed,
                                        stepRun.ObjectsCreated,
                                        stepRun.ObjectsUpdated,
                                        stepRun.ObjectsSkipped,
                                        stepRun.ErrorCount,
                                        cancellationToken);
                                    lastMetricsUpdate = DateTime.UtcNow;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Error updating progress metrics");
                                }
                            };

                            // PHASE 1 SEAM: route the single bulk-write through the run's sink
                            // instead of calling the repository directly. For the null-target
                            // (identity-store) path the IdentityStoreSink forwards the SAME
                            // bulkUpsertList to FastBulkUpsertObjectsAsync with the SAME
                            // cancellationToken and onProgress callback, then maps every
                            // BulkUpsertResult field back below -- byte-identical to the prior
                            // direct call. External targets never reach here (the run failed
                            // fast at start). _activeSink is always non-null here because
                            // ExecuteSyncProjectAsync resolves it before any step runs.
                            // targetConnection is null here: only the identity-store path
                            // reaches this write site (external targets failed fast at run start).
                            var sinkResult = await _activeSink!.WriteBatchAsync(
                                step,
                                targetConnection: null,
                                bulkUpsertList,
                                new SinkWriteOptions(),
                                cancellationToken,
                                onProgress);

                            lastBulkResult = new DataAccessLibrary.Repositories.BulkUpsertResult
                            {
                                ObjectsProcessed = sinkResult.Processed,
                                ObjectsCreated = sinkResult.Created,
                                ObjectsUpdated = sinkResult.Updated,
                                ObjectsSkipped = sinkResult.Skipped,
                                AttributesAffected = sinkResult.AttributesAffected,
                                SkippedSourceIds = sinkResult.SkippedSourceIds
                            };

                            _logger.LogInformation("BULK UPSERT COMPLETE: {Processed} processed, {Created} created, {Updated} updated",
                                lastBulkResult.ObjectsProcessed, lastBulkResult.ObjectsCreated, lastBulkResult.ObjectsUpdated);

                            // CACHE UPDATE: Add newly-inserted objects to the shared cache so
                            // subsequent steps/workflows find them and don't try to re-insert
                            foreach (var item in bulkUpsertList)
                            {
                                var uid = item.identityObject.SourceUniqueId;
                                if (!string.IsNullOrEmpty(uid) && !existingObjectsCache.ContainsKey(uid))
                                {
                                    existingObjectsCache[uid] = new DataAccessLibrary.Repositories.ObjectWithAttributes
                                    {
                                        Object = item.identityObject,
                                        Attributes = item.attributes?.Select(a => a).ToList() ?? new List<ObjectAttribute>()
                                    };
                                }
                            }

                            var sourceUniqueIdsForLookup = bulkUpsertList
                                .Select(x => x.identityObject.SourceUniqueId)
                                .Where(x => !string.IsNullOrEmpty(x))
                                .Distinct()
                                .ToList();

                            var actualObjectIds = await _syncRepository.GetObjectIdsBySourceUniqueIdsAsync(
                                project.SourceConnectionId!.Value,
                                sourceUniqueIdsForLookup,
                                cancellationToken);

                            _logger.LogInformation("AUDIT LOG FIX: Resolved {Count}/{Total} actual ObjectIds",
                                actualObjectIds.Count, sourceUniqueIdsForLookup.Count);

                            for (int i = 0; i < bulkUpsertList.Count; i++)
                            {
                                var metadata = bulkUpsertMetadata[i];
                                var obj = bulkUpsertList[i];
                                var sourceId = obj.identityObject.SourceUniqueId ?? "";

                                var actualObjectId = actualObjectIds.TryGetValue(sourceId, out var dbId)
                                    ? dbId
                                    : obj.identityObject.Id;

                                string operationType;
                                if (metadata.isNew)
                                    operationType = "Created";
                                else if (lastBulkResult.SkippedSourceIds.Contains(sourceId))
                                    operationType = "Skipped";
                                else
                                    operationType = "Updated";

                                // Detect changed fields by comparing with cached old object
                                int changeCount = 0;
                                string? changeDetails = null;
                                if (operationType == "Updated" && !string.IsNullOrEmpty(sourceId)
                                    && existingObjectsCache.TryGetValue(sourceId, out var cachedObj))
                                {
                                    var changedFields = CompareObjectFields(cachedObj.Object, obj.identityObject);
                                    changeCount = changedFields.Count;
                                    if (changedFields.Count > 0)
                                    {
                                        changeDetails = System.Text.Json.JsonSerializer.Serialize(changedFields);
                                    }
                                }
                                else if (operationType == "Created")
                                {
                                    changeCount = -1; // new object, all fields are "new"
                                }

                                var auditLog = new SyncAuditLog
                                {
                                    SyncStepRunId = stepRun.Id,
                                    ObjectId = actualObjectId,
                                    OperationType = operationType,
                                    ObjectDisplayName = obj.identityObject.DisplayName,
                                    SourceUniqueId = sourceId,
                                    Email = obj.identityObject.Email,
                                    Username = obj.identityObject.Username,
                                    UserPrincipalName = obj.identityObject.UserPrincipalName,
                                    ChangeCount = changeCount,
                                    ChangeDetails = changeDetails,
                                    ProcessingTimeMs = (decimal)(DateTime.UtcNow - metadata.startTime).TotalMilliseconds,
                                    Timestamp = DateTime.UtcNow
                                };

                                batchResults.Add(auditLog);

                                if (identitiesForMembershipSync != null)
                                {
                                    if (metadata.sourceObject.ContainsKey("memberOf"))
                                    {
                                        identitiesForMembershipSync.Add((obj.identityObject, metadata.sourceObject));
                                    }
                                }
                            }

                            // Auto-assign tags
                            var stepSearchBases = step.GetSearchBaseList();
                            var tagNames = step.StepTags?.Select(st => st.Tag?.Name ?? st.TagId.ToString()).ToList() ?? new List<string>();
                            _logger.LogInformation("STEP TAG CHECK: Step '{StepName}' SearchBases=[{SearchBases}] Tags=[{TagNames}] TagCount={TagCount}",
                                step.Name,
                                string.Join("; ", stepSearchBases),
                                string.Join(", ", tagNames),
                                step.StepTags?.Count ?? 0);

                            if (step.StepTags != null && step.StepTags.Any())
                            {
                                var sourceUniqueIds = bulkUpsertList
                                    .Where(x => !string.IsNullOrEmpty(x.identityObject.SourceUniqueId))
                                    .Select(x => x.identityObject.SourceUniqueId!)
                                    .ToList();

                                _logger.LogInformation("DEBUG: SourceUniqueIds count={Count}, First3={First3}",
                                    sourceUniqueIds.Count,
                                    string.Join(", ", sourceUniqueIds.Take(3)));

                                foreach (var stepTag in step.StepTags)
                                {
                                    var tagName = stepTag.Tag?.Name ?? "Unknown";
                                    _logger.LogInformation("APPLYING TAG '{TagName}' (ID={TagId}) to {ObjectCount} objects from step '{StepName}'",
                                        tagName, stepTag.TagId, sourceUniqueIds.Count, step.Name);

                                    var taggedCount = await _syncRepository.BulkAssignTagToObjectsBySourceAsync(
                                        stepTag.TagId,
                                        project.SourceConnectionId!.Value,
                                        sourceUniqueIds,
                                        isInherited: true,
                                        cancellationToken);
                                    _logger.LogInformation("RESULT: Tagged {TaggedCount} objects with '{TagName}' from step '{StepName}'",
                                        taggedCount, tagName, step.Name);
                                }
                                _logger.LogInformation("Total: Applied {TagCount} tags to {ObjectCount} objects from step '{StepName}'",
                                    step.StepTags.Count, sourceUniqueIds.Count, step.Name);
                            }

                            // Execute post-processing scripts
                            await ExecutePostProcessingScriptsAsync(bulkUpsertList, step, project, stepRun, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "BULK UPSERT FAILED for {Count} objects: {Error}", bulkUpsertList.Count, ex.Message);
                            foreach (var item in bulkUpsertList)
                            {
                                var errorLog = new SyncAuditLog
                                {
                                    SyncStepRunId = stepRun.Id,
                                    ObjectId = null,
                                    OperationType = "Error",
                                    ObjectDisplayName = item.identityObject.DisplayName,
                                    SourceUniqueId = item.identityObject.SourceUniqueId,
                                    Email = item.identityObject.Email,
                                    Username = item.identityObject.Username,
                                    UserPrincipalName = item.identityObject.UserPrincipalName,
                                    ErrorMessage = FormatFullException(ex),
                                    ChangeCount = 0,
                                    ProcessingTimeMs = 0,
                                    Timestamp = DateTime.UtcNow
                                };
                                batchResults.Add(errorLog);
                            }
                        }
                    }

                    int batchErrors = 0;
                    var nonNullResults = batchResults.Where(al => al != null).ToList();
                    foreach (var auditLog in nonNullResults)
                    {
                        auditLogBatch.Add(auditLog!);
                        if (auditLog!.OperationType == "Error")
                        {
                            batchErrors++;
                        }
                    }

                    stepRun.ErrorCount += batchErrors;
                    run.TotalErrors += batchErrors;

                    stepRun.ObjectsCreated += lastBulkResult?.ObjectsCreated ?? 0;
                    stepRun.ObjectsUpdated += lastBulkResult?.ObjectsUpdated ?? 0;
                    stepRun.ObjectsSkipped += lastBulkResult?.ObjectsSkipped ?? 0;

                    if (stepRun.ErrorCount >= _syncOptions.MaxErrorsThreshold && project.PauseOnError)
                    {
                        throw new InvalidOperationException($"Too many errors in step '{step.Name}' ({_syncOptions.MaxErrorsThreshold}+)");
                    }

                    processedCount += currentBatch.Count;

                    if (auditLogBatch.Any())
                    {
                        await _syncRepository.BulkInsertAuditLogsAsync(auditLogBatch, cancellationToken);
                        auditLogBatch.Clear();
                    }

                    await _syncRepository.UpdateStepRunMetricsAsync(
                        stepRun.Id,
                        stepRun.ObjectsQueried,
                        processedCount,
                        stepRun.ObjectsCreated,
                        stepRun.ObjectsUpdated,
                        stepRun.ObjectsSkipped,
                        stepRun.ErrorCount,
                        cancellationToken);

                    _logger.LogInformation("Batch {BatchNumber}/{TotalBatches} complete: {Processed} total processed",
                        batchNumber, totalBatches, processedCount);
                }

                if (auditLogBatch.Any())
                {
                    _logger.LogDebug("Bulk inserting final {Count} audit logs using Dapper", auditLogBatch.Count);
                    await _syncRepository.BulkInsertAuditLogsAsync(auditLogBatch, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("=== CANCELLATION AFTER LOOP === Throwing OperationCanceledException for step '{StepName}'", step.Name);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                FinalizeStepLog(stepRun, skipReasons, detailedSkipLog);

                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                {
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;
                }

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsCreated += stepRun.ObjectsCreated;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;
                run.TotalObjectsDeleted += stepRun.ObjectsDeleted;
                run.TotalPersonsCreated = _personsCreatedCount;

                stepRun.ObjectsProcessed = processedCount;
                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                _logger.LogInformation(
                    "Step '{StepName}' completed: Processed={Processed}, Created={Created}, " +
                    "Updated={Updated}, Deleted={Deleted}, Errors={Errors}",
                    step.Name, stepRun.ObjectsProcessed, stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated, stepRun.ObjectsDeleted, stepRun.ErrorCount);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning("=== STEP CANCELLED === Step '{StepName}' was cancelled", step.Name);
                stepRun.Status = "Cancelled";
                stepRun.ErrorMessage = "Step cancelled by user request";
                stepRun.ExecutionLog += $"\n\n=== CANCELLED ===\nStep cancelled at {DateTime.Now:MM-dd-yyyy HH:mm:ss} after processing {stepRun.ObjectsProcessed} objects";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    CancellationToken.None,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                throw;
            }
            catch (Exception ex)
            {
                stepRun.Status = "Failed";
                // Surface the real reason. ex.Message can be empty for some directory/bind
                // failures, so fall back to the exception type name to avoid a blank ErrorMessage.
                stepRun.ErrorMessage = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                stepRun.ExecutionLog = ex.ToString();
                // The directory query may have thrown before ObjectsQueried was assigned, leaving the
                // -1 sentinel. Normalize it to 0 so the row is not misread as "unknown / never ran".
                if (stepRun.ObjectsQueried < 0) stepRun.ObjectsQueried = 0;
                // Count this failed step as at least one error so run-level rollup and the UI agree.
                if (stepRun.ErrorCount < 1) stepRun.ErrorCount = 1;
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    CancellationToken.None,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                // UpdateStepRunMetricsAsync does NOT persist ErrorMessage/ExecutionLog, so the failure
                // reason would be lost. Write them directly here so the operator can see WHY the step failed.
                try
                {
                    using var failConn = CreateConnection();
                    await failConn.OpenAsync(CancellationToken.None);
                    await failConn.ExecuteAsync(
                        @"UPDATE SyncStepRuns
                          SET ErrorMessage = @ErrorMessage,
                              ExecutionLog = @ExecutionLog
                          WHERE Id = @Id",
                        new { stepRun.Id, stepRun.ErrorMessage, stepRun.ExecutionLog });
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "Failed to persist step error detail for step run {StepRunId}", stepRun.Id);
                }

                throw;
            }
        }

        /// <summary>
        /// BULK VERSION: Prepares a source object for bulk upsert (no database write).
        /// Returns prepared IdentityObject with attributes.
        /// </summary>
        private async Task<(IdentityObject identityObject, List<ObjectAttribute> attributes, bool isNew, Dictionary<string, object> sourceObject)?> PrepareSourceObjectForBulkAsync(
            PersonMatchingService matchingService,
            Dictionary<string, DataAccessLibrary.Repositories.ObjectWithAttributes> existingIdentitiesCache,
            DataAccessLibrary.Repositories.IdentityLookupCache personCache,
            Dictionary<string, object> sourceObject,
            SyncStep step,
            SyncProject project,
            SyncStepRun stepRun,
            Dictionary<string, int> skipReasons,
            List<string> detailedSkipLog,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!sourceObject.ContainsKey("objectGuid"))
            {
                RecordSkip(stepRun, "Missing objectGuid", null, skipReasons, detailedSkipLog);
                return null;
            }

            var objectGuid = sourceObject["objectGuid"].ToString();
            if (string.IsNullOrWhiteSpace(objectGuid))
            {
                RecordSkip(stepRun, "Empty objectGuid", null, skipReasons, detailedSkipLog);
                return null;
            }

            var identity = await _attributeMappingService.ApplyAttributeMappingsAsync(sourceObject, step, project, cancellationToken);

            existingIdentitiesCache.TryGetValue(identity.SourceUniqueId!, out var existingIdentity);

            if (existingIdentity == null)
            {
                var prepared = await PrepareIdentityForBulkUpsertAsync(matchingService, null, identity, project, step, existingIdentitiesCache, personCache, sourceObject, cancellationToken);
                if (prepared == null) return null;
                return (prepared.Value.identityObject, prepared.Value.identityObject.Attributes.ToList(), prepared.Value.isNew, sourceObject);
            }
            else if (step.UpdateExisting)
            {
                var prepared = await PrepareIdentityForBulkUpsertAsync(matchingService, existingIdentity, identity, project, step, existingIdentitiesCache, personCache, sourceObject, cancellationToken);
                if (prepared == null) return null;
                return (prepared.Value.identityObject, prepared.Value.identityObject.Attributes.ToList(), prepared.Value.isNew, sourceObject);
            }
            else
            {
                var displayInfo = identity.DisplayName ?? identity.Username ?? objectGuid;
                RecordSkip(stepRun, "UpdateExisting disabled - identity already exists", displayInfo, skipReasons, detailedSkipLog);
                return null;
            }
        }

        /// <summary>
        /// BULK VERSION: Prepares a group object for bulk upsert (no database write).
        /// </summary>
        private async Task<(IdentityObject identityObject, List<ObjectAttribute> attributes, bool isNew, Dictionary<string, object> sourceObject)?> PrepareGroupObjectForBulkAsync(
            Dictionary<string, DataAccessLibrary.Repositories.ObjectWithAttributes> existingObjectsCache,
            Dictionary<string, object> sourceObject,
            SyncStep step,
            SyncProject project,
            SyncStepRun stepRun,
            Dictionary<string, int> skipReasons,
            List<string> detailedSkipLog,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!sourceObject.ContainsKey("objectGuid"))
            {
                RecordSkip(stepRun, "Missing objectGuid", null, skipReasons, detailedSkipLog);
                return null;
            }

            var objectGuid = sourceObject["objectGuid"].ToString();
            if (string.IsNullOrWhiteSpace(objectGuid))
            {
                RecordSkip(stepRun, "Empty objectGuid", null, skipReasons, detailedSkipLog);
                return null;
            }

            var group = await _groupSyncService.ApplyAttributeMappingsAsync(
                sourceObject,
                step,
                project,
                "Group",
                cancellationToken);

            // Check if this group already exists in the cache
            bool isNew = true;
            Guid objectId = Guid.NewGuid();
            DateTime firstSyncedAt = group.FirstSyncedAt;
            if (group.SourceUniqueId != null &&
                existingObjectsCache.TryGetValue(group.SourceUniqueId, out var existing))
            {
                isNew = false;
                objectId = existing.Object.Id;
                firstSyncedAt = existing.Object.FirstSyncedAt;
            }

            var identityObject = new IdentityObject
            {
                Id = objectId,
                SourceConnectionId = group.SourceConnectionId,
                SourceUniqueId = group.SourceUniqueId,
                SourceType = "Group",
                DisplayName = group.Name,
                Email = group.Email,
                ObjectClass = "group",
                DN = group.DistinguishedName,
                CN = group.Name,
                IsActive = group.IsActive,
                FirstSyncedAt = firstSyncedAt,
                LastSyncedAt = group.LastSyncedAt,
                LastSeenAt = group.LastSeenAt
            };

            var objectAttributes = group.Attributes.Select(a => new ObjectAttribute
            {
                ObjectId = identityObject.Id,
                AttributeName = a.AttributeName,
                AttributeValue = a.AttributeValue,
                DataType = a.DataType,
                LastSyncedAt = DateTime.UtcNow
            }).ToList();

            return (identityObject, objectAttributes, isNew, sourceObject);
        }

        /// <summary>
        /// BULK UPSERT: Prepares an identity for bulk processing.
        /// </summary>
        private async Task<(IdentityObject identityObject, bool isNew, Dictionary<string, object>? sourceObject)?> PrepareIdentityForBulkUpsertAsync(
            PersonMatchingService matchingService,
            DataAccessLibrary.Repositories.ObjectWithAttributes? existingIdentityData,
            IdentityObject newIdentityData,
            SyncProject project,
            SyncStep step,
            Dictionary<string, DataAccessLibrary.Repositories.ObjectWithAttributes> existingIdentitiesCache,
            DataAccessLibrary.Repositories.IdentityLookupCache personCache,
            Dictionary<string, object>? sourceObject,
            CancellationToken cancellationToken)
        {
            bool isNew = existingIdentityData == null;

            if (isNew)
            {
                newIdentityData.IdentityId = null;
            }
            else
            {
                newIdentityData.IdentityId = existingIdentityData!.Object.IdentityId;
            }

            return (newIdentityData, isNew, sourceObject);
        }

        /// <summary>
        /// Processes a GroupMembership sync step.
        /// </summary>
        private async Task ProcessGroupMembershipStepAsync(
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("GROUP MEMBERSHIP STEP: Querying groups with 'member' attribute from AD for connection {ConnectionId}",
                project.SourceConnectionId);

            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = "GroupMembership",
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = "",
                ObjectsQueried = -1
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            try
            {
                ModelDirectoryConnection? sourceConnection;
                using (var conn = CreateConnection())
                {
                    sourceConnection = await conn.QueryFirstOrDefaultAsync<ModelDirectoryConnection>(
                        @"SELECT * FROM DirectoryConnections WHERE Id = @Id",
                        new { Id = project.SourceConnectionId });
                }

                if (sourceConnection == null)
                {
                    throw new InvalidOperationException($"Source connection {project.SourceConnectionId} not found");
                }

                // Route group membership query through connector factory
                var membershipConnectionType = sourceConnection.ConnectionType ?? "ActiveDirectory";
                var groupMembersFromAD = await _connectorQueryServiceFactory.GetService(membershipConnectionType)
                    .QueryGroupMembersAsync(sourceConnection, cancellationToken);

                stepRun.ObjectsQueried = groupMembersFromAD.Count;
                _logger.LogInformation("Retrieved {Count} groups from AD with member attribute", groupMembersFromAD.Count);

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, 0, 0, 0, 0, 0, cancellationToken);

                if (!groupMembersFromAD.Any())
                {
                    _logger.LogWarning("No groups found in AD");
                    stepRun.Status = "Completed";
                    stepRun.CompletedAt = DateTime.UtcNow;
                    stepRun.DurationSeconds = 0;
                    await _syncRepository.UpdateStepRunMetricsAsync(
                        stepRun.Id, stepRun.ObjectsQueried, 0, 0, 0, 0, 0, cancellationToken,
                        status: "Completed", completedAt: stepRun.CompletedAt, durationSeconds: 0);
                    return;
                }

                // Build lookup dictionaries using Dapper
                List<dynamic> allGroupsInDB;
                List<dynamic> allObjectsInDB;

                using (var conn = CreateConnection())
                {
                    allGroupsInDB = (await conn.QueryAsync(
                        @"SELECT Id, DN, DisplayName, SourceUniqueId FROM Objects WHERE ObjectClass = 'group' AND SourceConnectionId = @SourceConnectionId AND IsActive = 1",
                        new { SourceConnectionId = project.SourceConnectionId })).ToList();

                    allObjectsInDB = (await conn.QueryAsync(
                        @"SELECT Id, DN, DisplayName, SourceUniqueId FROM Objects WHERE SourceConnectionId = @SourceConnectionId AND IsActive = 1",
                        new { SourceConnectionId = project.SourceConnectionId })).ToList();
                }

                var groupDnToId = allGroupsInDB
                    .Where(g => !string.IsNullOrWhiteSpace((string?)g.DN))
                    .GroupBy(g => (string)g.DN, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => (Guid)g.First().Id, StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation("Built group DN lookup: {Count} groups in database", groupDnToId.Count);

                var objectDnToId = allObjectsInDB
                    .Where(o => !string.IsNullOrWhiteSpace((string?)o.DN))
                    .GroupBy(o => (string)o.DN, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => (Guid)g.First().Id, StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation("Built object DN lookup: {Count} objects in database", objectDnToId.Count);

                // Build SourceUniqueId-based lookups (used by Entra ID where identifiers are object IDs, not DNs)
                var groupSourceUniqueIdToId = allGroupsInDB
                    .Where(g => !string.IsNullOrWhiteSpace((string?)g.SourceUniqueId))
                    .GroupBy(g => (string)g.SourceUniqueId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => (Guid)g.First().Id, StringComparer.OrdinalIgnoreCase);

                var objectSourceUniqueIdToId = allObjectsInDB
                    .Where(o => !string.IsNullOrWhiteSpace((string?)o.SourceUniqueId))
                    .GroupBy(o => (string)o.SourceUniqueId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => (Guid)g.First().Id, StringComparer.OrdinalIgnoreCase);

                _logger.LogDebug("Built SourceUniqueId lookups: {GroupCount} groups, {ObjectCount} objects",
                    groupSourceUniqueIdToId.Count, objectSourceUniqueIdToId.Count);

                var objectIdToDisplayName = allObjectsInDB
                    .ToDictionary(o => (Guid)o.Id, o => (string?)o.DisplayName);
                var groupIdToDisplayName = allGroupsInDB
                    .ToDictionary(g => (Guid)g.Id, g => (string?)g.DisplayName);

                var membershipsToUpsert = new List<(Guid ObjectId, Guid GroupId, bool IsDirect, bool IsPrimary)>();
                int groupsProcessed = 0;
                int groupsSkipped = 0;
                int membersResolved = 0;
                int membersUnresolved = 0;

                foreach (KeyValuePair<string, List<string>> kvp in groupMembersFromAD)
                {
                    string groupIdentifier = kvp.Key;
                    List<string> memberIdentifiers = kvp.Value;

                    if (memberIdentifiers.Count == 0)
                        continue;

                    // Try DN first, then SourceUniqueId (for Entra ID)
                    Guid groupId;
                    if (!groupDnToId.TryGetValue(groupIdentifier, out groupId) &&
                        !groupSourceUniqueIdToId.TryGetValue(groupIdentifier, out groupId))
                    {
                        groupsSkipped++;
                        _logger.LogDebug("Group identifier not found in database: {GroupIdentifier}", groupIdentifier);
                        continue;
                    }

                    groupsProcessed++;

                    foreach (var memberIdentifier in memberIdentifiers)
                    {
                        Guid memberId;
                        if (objectDnToId.TryGetValue(memberIdentifier, out memberId) ||
                            objectSourceUniqueIdToId.TryGetValue(memberIdentifier, out memberId))
                        {
                            membershipsToUpsert.Add((ObjectId: memberId, GroupId: groupId, IsDirect: true, IsPrimary: false));
                            membersResolved++;
                        }
                        else
                        {
                            membersUnresolved++;
                        }
                    }
                }

                _logger.LogInformation("Membership resolution: {GroupsProcessed} groups, {MembersResolved} members resolved, {MembersUnresolved} unresolved, {GroupsSkipped} groups not in DB",
                    groupsProcessed, membersResolved, membersUnresolved, groupsSkipped);

                if (membersUnresolved > 0)
                {
                    _logger.LogWarning("{Count} member DNs could not be resolved (objects may not be synced yet or are from other domains)",
                        membersUnresolved);
                }

                // ===========================================
                // PRIMARY GROUP RESOLUTION
                // Primary groups (e.g., Domain Users) are NOT in the 'member' attribute.
                // They're stored on each user's primaryGroupID attribute.
                // Reconstruct the primary group SID and match to a group object.
                // ===========================================
                int primaryGroupsResolved = 0;
                int primaryGroupsMissing = 0;

                using (var pgConn = CreateConnection())
                {
                    var objectAttrs = (await pgConn.QueryAsync<(Guid ObjectId, string AttributeName, string? AttributeValue)>(
                        @"SELECT oa.ObjectId, oa.AttributeName, oa.AttributeValue
                          FROM ObjectAttributes oa
                          INNER JOIN Objects o ON oa.ObjectId = o.Id
                          WHERE o.SourceConnectionId = @SourceConnectionId
                            AND o.IsActive = 1
                            AND oa.AttributeName IN ('primaryGroupID', 'objectSid')",
                        new { SourceConnectionId = project.SourceConnectionId })).ToList();

                    var attrsByObject = objectAttrs
                        .GroupBy(a => a.ObjectId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToDictionary(a => a.AttributeName, a => a.AttributeValue, StringComparer.OrdinalIgnoreCase));

                    var groupIds = allGroupsInDB.Select(g => (Guid)g.Id).ToList();

                    var groupSidRows = await pgConn.QueryAsync<(Guid ObjectId, string? AttributeValue)>(
                        @"SELECT ObjectId, AttributeValue
                          FROM ObjectAttributes
                          WHERE AttributeName = 'objectSid'
                            AND ObjectId IN @GroupIds",
                        new { GroupIds = groupIds });

                    var groupsBySid = groupSidRows
                        .Where(x => !string.IsNullOrEmpty(x.AttributeValue))
                        .ToDictionary(x => x.AttributeValue!, x => x.ObjectId, StringComparer.OrdinalIgnoreCase);

                    foreach (var pgKvp in attrsByObject)
                    {
                        var objectId = pgKvp.Key;
                        var attrs = pgKvp.Value;

                        if (!attrs.TryGetValue("primaryGroupID", out var primaryGroupIdStr) || string.IsNullOrWhiteSpace(primaryGroupIdStr))
                            continue;
                        if (!attrs.TryGetValue("objectSid", out var objectSidStr) || string.IsNullOrWhiteSpace(objectSidStr))
                            continue;

                        var sidParts = objectSidStr.Split('-');
                        if (sidParts.Length > 4)
                        {
                            var domainSid = string.Join("-", sidParts.Take(sidParts.Length - 1));
                            var primaryGroupSid = string.Concat(domainSid, "-", primaryGroupIdStr);

                            if (groupsBySid.TryGetValue(primaryGroupSid, out var primaryGroupObjectId))
                            {
                                membershipsToUpsert.Add((ObjectId: objectId, GroupId: primaryGroupObjectId, IsDirect: true, IsPrimary: true));
                                primaryGroupsResolved++;
                            }
                            else
                            {
                                primaryGroupsMissing++;
                            }
                        }
                    }
                }

                _logger.LogInformation("Primary groups: {Resolved} resolved, {Missing} missing",
                    primaryGroupsResolved, primaryGroupsMissing);

                var deduplicatedMemberships = membershipsToUpsert
                    .GroupBy(m => (m.ObjectId, m.GroupId))
                    .Select(g => g.OrderByDescending(m => m.IsPrimary).First())
                    .ToList();

                _logger.LogInformation("Bulk upserting {Count} memberships (deduplicated from {Original})...",
                    deduplicatedMemberships.Count, membershipsToUpsert.Count);

                int affected = 0;
                if (deduplicatedMemberships.Any())
                {
                    affected = await _syncRepository.BulkUpsertObjectGroupMembershipsAsync(
                        deduplicatedMemberships,
                        cancellationToken);
                }

                _logger.LogInformation("Bulk upsert complete: {Affected} memberships affected", affected);

                // Write audit logs for group membership changes
                var auditLogs = new List<SyncAuditLog>();
                foreach (var membership in deduplicatedMemberships)
                {
                    objectIdToDisplayName.TryGetValue(membership.ObjectId, out var memberDisplayName);
                    groupIdToDisplayName.TryGetValue(membership.GroupId, out var groupDisplayName);
                    var groupLabel = groupDisplayName ?? membership.GroupId.ToString();

                    auditLogs.Add(new SyncAuditLog
                    {
                        SyncStepRunId = stepRun.Id,
                        ObjectId = membership.ObjectId,
                        OperationType = "Updated",
                        ObjectDisplayName = memberDisplayName,
                        ChangeDetails = $"[{{\"Field\":\"GroupMembership\",\"Before\":null,\"After\":\"{groupLabel}\"}}]",
                        ChangeCount = 1,
                        ProcessingTimeMs = 0,
                        Timestamp = DateTime.UtcNow
                    });
                }

                if (auditLogs.Any())
                {
                    await _syncRepository.BulkInsertAuditLogsAsync(auditLogs, cancellationToken);
                    _logger.LogInformation("Wrote {Count} group membership audit log entries", auditLogs.Count);
                }

                stepRun.ObjectsProcessed = groupsProcessed;
                stepRun.ObjectsUpdated = affected;
                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                {
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;
                }

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;
                run.TotalErrors += stepRun.ErrorCount;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                await _syncRepository.UpdateProjectRunMetricsAsync(
                    run.Id,
                    run.TotalObjectsProcessed,
                    run.TotalObjectsCreated,
                    run.TotalObjectsUpdated,
                    run.TotalErrors,
                    run.CompletedSteps,
                    run.ProgressPercentage,
                    cancellationToken);

                _logger.LogInformation(
                    "GroupMembership step '{StepName}' completed: Groups={Groups}, Memberships={Memberships}, Duration={Duration}s",
                    step.Name, groupsProcessed, affected, stepRun.DurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GroupMembership step '{StepName}' failed: {Error}",
                    step.Name, ex.Message);

                stepRun.Status = "Failed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;
                stepRun.ErrorMessage = ex.Message;
                stepRun.ErrorCount = 1;

                run.TotalErrors++;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                throw;
            }
        }

        /// <summary>
        /// Processes a Lookup sync step.
        /// </summary>
        private async Task ProcessLookupStepAsync(
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            var lookupMappings = step.AttributeMappings?
                .Where(m => m.IsEnabled && m.TransformationType == "DNLookup")
                .ToList() ?? new List<AttributeMapping>();

            var sourceAttr = lookupMappings.FirstOrDefault()?.SourceAttribute ?? "ManagerSourceId";
            var targetAttr = lookupMappings.FirstOrDefault()?.TargetAttribute ?? "ManagerObjectId";

            _logger.LogInformation("LOOKUP STEP '{StepName}': Resolving {Source} -> {Target} for connection {ConnectionId}",
                step.Name, sourceAttr, targetAttr, project.SourceConnectionId);

            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = step.ObjectClass ?? "Lookup",
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = "",
                ObjectsQueried = -1
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            try
            {
                var (totalWithManagerDN, alreadyResolved, needingResolution) =
                    await _syncRepository.GetManagerResolutionStatsAsync(project.SourceConnectionId!.Value, cancellationToken);

                stepRun.ObjectsQueried = totalWithManagerDN;
                _logger.LogInformation("Found {NeedingResolution} objects needing resolution, {AlreadyResolved} already resolved (Total: {Total} with {Attr})",
                    needingResolution, alreadyResolved, totalWithManagerDN, sourceAttr);

                if (needingResolution == 0)
                {
                    _logger.LogInformation("All {Count} manager relationships already resolved - step complete (no audit logs needed, already logged in previous sync)", alreadyResolved);
                    stepRun.ObjectsProcessed = totalWithManagerDN;
                    stepRun.ObjectsUpdated = 0; // Nothing updated THIS run
                    stepRun.ObjectsSkipped = alreadyResolved; // All skipped as already resolved
                    stepRun.Status = "Completed";
                    stepRun.CompletedAt = DateTime.UtcNow;
                    stepRun.DurationSeconds = 0;
                    stepRun.ExecutionLog = $"All {alreadyResolved} manager relationships were already resolved in previous syncs.";

                    await _syncRepository.UpdateStepRunMetricsAsync(
                        stepRun.Id, stepRun.ObjectsQueried, stepRun.ObjectsProcessed,
                        stepRun.ObjectsCreated, stepRun.ObjectsUpdated, stepRun.ObjectsSkipped,
                        stepRun.ErrorCount, cancellationToken,
                        status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: stepRun.DurationSeconds);
                    return;
                }

                var resolved = await _syncRepository.ResolveManagerRelationshipsAsync(project.SourceConnectionId!.Value, cancellationToken);
                _logger.LogInformation("Resolved {Count} manager relationships via UPDATE JOIN", resolved);

                // Get details for audit logging (after the update, so we can see what was resolved)
                var auditDetails = await _syncRepository.GetManagerResolutionDetailsAsync(project.SourceConnectionId!.Value, cancellationToken);
                _logger.LogInformation("Retrieved {Count} objects for audit logging", auditDetails.Count);

                var notFound = needingResolution - resolved;

                stepRun.ObjectsProcessed = needingResolution;
                stepRun.ObjectsUpdated = resolved;
                stepRun.ObjectsSkipped = notFound > 0 ? notFound : 0;
                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                {
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;
                }

                // Write audit logs for each object
                var auditLogs = new List<SyncAuditLog>();
                foreach (var item in auditDetails)
                {
                    var auditLog = new SyncAuditLog
                    {
                        SyncStepRunId = stepRun.Id,
                        ObjectId = item.ObjectId,
                        OperationType = item.WasResolved ? "Updated" : "Skipped",
                        ObjectDisplayName = item.DisplayName,
                        SourceUniqueId = item.SourceUniqueId,
                        Email = item.Email,
                        Username = item.Username,
                        UserPrincipalName = item.UserPrincipalName,
                        ChangeCount = item.WasResolved ? 1 : 0,
                        ChangeDetails = item.WasResolved
                            ? $"[{{\"Field\":\"ManagerObjectId\",\"Before\":null,\"After\":\"{item.ManagerObjectId}\",\"ManagerName\":\"{item.ManagerDisplayName}\"}}]"
                            : $"[{{\"Field\":\"ManagerSourceId\",\"Value\":\"{item.ManagerSourceId}\",\"Reason\":\"Manager not found in database\"}}]",
                        Timestamp = DateTime.UtcNow,
                        ProcessingTimeMs = 0
                    };
                    auditLogs.Add(auditLog);
                }

                if (auditLogs.Any())
                {
                    await _syncRepository.BulkInsertAuditLogsAsync(auditLogs, cancellationToken);
                    _logger.LogInformation("Wrote {Count} audit log entries for manager resolution step", auditLogs.Count);
                }

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;

                await _syncRepository.UpdateProjectRunMetricsAsync(
                    run.Id,
                    run.TotalObjectsProcessed,
                    run.TotalObjectsCreated,
                    run.TotalObjectsUpdated,
                    run.TotalErrors,
                    run.CompletedSteps,
                    run.ProgressPercentage,
                    cancellationToken);

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated, stepRun.ObjectsUpdated, stepRun.ObjectsSkipped,
                    stepRun.ErrorCount, cancellationToken,
                    status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: stepRun.DurationSeconds);

                _logger.LogInformation(
                    "Lookup step '{StepName}' completed: Resolved={Resolved}, NotFound={NotFound}, Duration={Duration}s",
                    step.Name, resolved, notFound > 0 ? notFound : 0, stepRun.DurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lookup step '{StepName}' failed: {Error}", step.Name, ex.Message);

                stepRun.Status = "Failed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;
                stepRun.ErrorMessage = ex.Message;

                run.TotalErrors++;

                using (var connection = CreateConnection())
                {
                    await connection.ExecuteAsync(
                        @"UPDATE SyncStepRuns SET Status = @Status, CompletedAt = @CompletedAt, DurationSeconds = @DurationSeconds, ErrorMessage = @ErrorMessage WHERE Id = @Id",
                        new { Id = stepRun.Id, Status = stepRun.Status, CompletedAt = stepRun.CompletedAt, DurationSeconds = stepRun.DurationSeconds, ErrorMessage = stepRun.ErrorMessage });
                }

                throw;
            }
        }

        /// <summary>
        /// Processes a GroupOwnerLookup sync step.
        /// </summary>
        private async Task ProcessGroupOwnerLookupStepAsync(
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("GROUP OWNER LOOKUP STEP: Resolving group owner relationships for connection {ConnectionId}",
                project.SourceConnectionId);

            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = "GroupOwnerLookup",
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = "",
                ObjectsQueried = -1
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            try
            {
                int totalExistingOwners;
                List<dynamic> groupsWithManagedBy;

                using (var connection = CreateConnection())
                {
                    totalExistingOwners = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM Objects WHERE SourceConnectionId = @SourceConnectionId AND ObjectClass = 'group' AND IsActive = 1 AND OwnerObjectId IS NOT NULL",
                        new { SourceConnectionId = project.SourceConnectionId });

                    groupsWithManagedBy = (await connection.QueryAsync(
                        @"SELECT o.*, oa.AttributeValue as ManagedByDN
                          FROM Objects o
                          INNER JOIN ObjectAttributes oa ON o.Id = oa.ObjectId
                          WHERE o.SourceConnectionId = @SourceConnectionId
                            AND o.ObjectClass = 'group'
                            AND o.IsActive = 1
                            AND LOWER(oa.AttributeName) = 'managedby'
                            AND oa.AttributeValue IS NOT NULL
                            AND oa.AttributeValue <> ''",
                        new { SourceConnectionId = project.SourceConnectionId })).ToList();
                }

                var totalWithManagedBy = groupsWithManagedBy.Count;

                var groupsNeedingOwner = groupsWithManagedBy
                    .Where(g => g.OwnerObjectId == null)
                    .ToList();

                stepRun.ObjectsQueried = groupsNeedingOwner.Count > 0 ? groupsNeedingOwner.Count : totalWithManagedBy;
                _logger.LogInformation("Found {NeedingResolution} groups needing owner resolution, {AlreadyResolved} already resolved (Total: {Total} with ManagedBy)",
                    groupsNeedingOwner.Count, totalExistingOwners, totalWithManagedBy);

                if (!groupsNeedingOwner.Any())
                {
                    if (totalExistingOwners > 0)
                    {
                        _logger.LogInformation("All {Count} group owner relationships already resolved - step complete", totalExistingOwners);
                        stepRun.ObjectsProcessed = totalWithManagedBy;
                        stepRun.ObjectsUpdated = totalExistingOwners;
                        stepRun.ExecutionLog = $"All {totalExistingOwners} group owner relationships were already resolved in previous syncs.";
                    }
                    else
                    {
                        _logger.LogInformation("No groups with ManagedBy attribute found - step complete");
                        stepRun.ExecutionLog = "No groups have the managedBy attribute set in Active Directory.";
                    }
                    stepRun.Status = "Completed";
                    stepRun.CompletedAt = DateTime.UtcNow;
                    stepRun.DurationSeconds = 0;
                    await _syncRepository.UpdateStepRunMetricsAsync(
                        stepRun.Id, stepRun.ObjectsQueried, stepRun.ObjectsProcessed,
                        stepRun.ObjectsCreated, stepRun.ObjectsUpdated, stepRun.ObjectsSkipped,
                        stepRun.ErrorCount, cancellationToken,
                        status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: stepRun.DurationSeconds);
                    return;
                }

                // Build DN->Object lookup (handle duplicates by taking first match)
                Dictionary<string, dynamic> dnLookup;
                using (var connection = CreateConnection())
                {
                    var allObjects = await connection.QueryAsync(
                        @"SELECT Id, DN FROM Objects WHERE SourceConnectionId = @SourceConnectionId AND IsActive = 1 AND DN IS NOT NULL",
                        new { SourceConnectionId = project.SourceConnectionId });

                    dnLookup = allObjects
                        .Where(o => !string.IsNullOrEmpty((string?)o.DN))
                        .GroupBy(o => ((string)o.DN).ToLowerInvariant())
                        .ToDictionary(g => g.Key, g => g.First());
                }

                _logger.LogInformation("Built DN lookup with {Count} entries for owner resolution", dnLookup.Count);

                int resolved = 0;
                int notFound = 0;
                var updates = new List<(Guid groupId, Guid ownerId)>();

                foreach (var group in groupsNeedingOwner)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var ownerDn = ((string?)group.ManagedByDN)?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(ownerDn))
                        continue;

                    if (dnLookup.TryGetValue(ownerDn, out var ownerObject))
                    {
                        updates.Add(((Guid)group.Id, (Guid)ownerObject.Id));
                        resolved++;

                        _logger.LogDebug("Resolved owner for group {Group}: {OwnerDN} -> ObjectId {OwnerId}",
                            (string?)group.DisplayName ?? (string?)group.DN, ownerDn, (Guid)ownerObject.Id);
                    }
                    else
                    {
                        notFound++;
                        if (notFound <= 10)
                        {
                            _logger.LogDebug("Owner not found for group {Group}: {OwnerDN}",
                                (string?)group.DisplayName ?? (string?)group.DN, ownerDn);
                        }
                    }
                }

                // Batch update all OwnerObjectIds
                if (updates.Any())
                {
                    using (var connection = CreateConnection())
                    {
                        foreach (var (groupId, ownerId) in updates)
                        {
                            await connection.ExecuteAsync(
                                @"UPDATE Objects SET OwnerObjectId = @OwnerId WHERE Id = @GroupId",
                                new { GroupId = groupId, OwnerId = ownerId });
                        }
                    }
                }

                stepRun.ObjectsProcessed = groupsNeedingOwner.Count;
                stepRun.ObjectsUpdated = resolved;
                stepRun.ObjectsSkipped = notFound;
                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                {
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;
                }

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;

                await _syncRepository.UpdateProjectRunMetricsAsync(
                    run.Id,
                    run.TotalObjectsProcessed,
                    run.TotalObjectsCreated,
                    run.TotalObjectsUpdated,
                    run.TotalErrors,
                    run.CompletedSteps,
                    run.ProgressPercentage,
                    cancellationToken);

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated, stepRun.ObjectsUpdated, stepRun.ObjectsSkipped,
                    stepRun.ErrorCount, cancellationToken,
                    status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: stepRun.DurationSeconds);

                _logger.LogInformation(
                    "GroupOwnerLookup step '{StepName}' completed: Resolved={Resolved}, NotFound={NotFound}, Duration={Duration}s",
                    step.Name, resolved, notFound, stepRun.DurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GroupOwnerLookup step '{StepName}' failed: {Error}", step.Name, ex.Message);

                stepRun.Status = "Failed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;
                stepRun.ErrorMessage = ex.Message;

                run.TotalErrors++;

                using (var connection = CreateConnection())
                {
                    await connection.ExecuteAsync(
                        @"UPDATE SyncStepRuns SET Status = @Status, CompletedAt = @CompletedAt, DurationSeconds = @DurationSeconds, ErrorMessage = @ErrorMessage WHERE Id = @Id",
                        new { Id = stepRun.Id, Status = stepRun.Status, CompletedAt = stepRun.CompletedAt, DurationSeconds = stepRun.DurationSeconds, ErrorMessage = stepRun.ErrorMessage });
                }

                throw;
            }
        }

        /// <summary>
        /// Processes a LicenseSync step: pulls license pools and user assignments from Entra ID
        /// via Graph API, upserts into the LicenseMonitoring tables, deactivates stale assignments,
        /// and creates daily usage snapshots.
        /// </summary>
        private async Task ProcessLicenseSyncStepAsync(
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing LicenseSync step '{StepName}' for connection {ConnectionId}",
                step.Name, project.SourceConnectionId);

            if (_licenseRepository == null || _licenseSyncQueryService == null)
            {
                throw new InvalidOperationException(
                    "LicenseSync step requires ILicenseRepository and ILicenseSyncQueryService to be registered.");
            }

            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = "LicenseSync",
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = "",
                ObjectsQueried = -1
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            try
            {
                var syncStart = DateTime.UtcNow;
                var connectionId = project.SourceConnectionId
                    ?? throw new InvalidOperationException("SyncProject.SourceConnectionId is null");

                // Load the source connection
                ModelDirectoryConnection? sourceConnection;
                using (var conn = CreateConnection())
                {
                    sourceConnection = await conn.QueryFirstOrDefaultAsync<ModelDirectoryConnection>(
                        @"SELECT * FROM DirectoryConnections WHERE Id = @Id",
                        new { Id = connectionId });
                }

                if (sourceConnection == null)
                    throw new InvalidOperationException($"Source connection {connectionId} not found");

                // Parse Entra ID configuration
                if (string.IsNullOrEmpty(sourceConnection.Configuration))
                    throw new InvalidOperationException("Entra ID connection has no configuration");

                var config = System.Text.Json.JsonSerializer.Deserialize<DataAccessLibrary.Models.EntraIdConnectionConfig>(
                    sourceConnection.Configuration,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Failed to deserialize Entra ID connection configuration");

                // Decrypt credentials to get client secret
                var credentialsJson = await _encryptionService.DecryptAsync(sourceConnection.Credentials).ConfigureAwait(false);
                var credentials = System.Text.Json.JsonSerializer.Deserialize<DataAccessLibrary.Models.EntraIdCredentials>(
                    credentialsJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Failed to deserialize Entra ID credentials");

                // ── Phase 1: Sync license pools ────────────────────────────────────
                _logger.LogInformation("LicenseSync: Querying license pools from Entra ID tenant {TenantId}", config.TenantId);

                var poolResults = await _licenseSyncQueryService.QueryLicensePoolsAsync(
                    connectionId, config.TenantId, config.ClientId, credentials.ClientSecret, cancellationToken);

                int poolsUpserted = 0;
                int totalPlans = 0;

                foreach (var (pool, plans) in poolResults)
                {
                    // Auto-set friendly name and default pricing from known SKU part numbers
                    if (pool.SkuPartNumber != null)
                    {
                        if (string.IsNullOrEmpty(pool.FriendlyName))
                        {
                            SkuFriendlyNames.TryGetValue(pool.SkuPartNumber, out var friendly);
                            pool.FriendlyName = friendly;
                        }
                        if (!pool.CostPerUnitMonthly.HasValue && SkuDefaultPricing.TryGetValue(pool.SkuPartNumber, out var price))
                        {
                            pool.CostPerUnitMonthly = price;
                            pool.Currency = "USD";
                        }
                    }

                    await _licenseRepository.UpsertLicensePoolAsync(pool, cancellationToken);
                    await _licenseRepository.ReplaceServicePlansAsync(pool.Id, plans, cancellationToken);
                    poolsUpserted++;
                    totalPlans += plans.Count;
                }

                _logger.LogInformation("LicenseSync: Upserted {PoolCount} license pools with {PlanCount} service plans",
                    poolsUpserted, totalPlans);

                stepRun.ObjectsQueried = poolsUpserted;
                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, 0, 0, 0, 0, 0, cancellationToken);

                // ── Phase 2: Sync user license assignments ─────────────────────────
                _logger.LogInformation("LicenseSync: Querying user license assignments from Entra ID");

                var skuToPoolId = await _licenseRepository.GetPoolIdsBySkuAsync(connectionId, cancellationToken);

                var userLicenses = await _licenseSyncQueryService.QueryUserLicenseAssignmentsAsync(
                    config.TenantId, config.ClientId, credentials.ClientSecret, cancellationToken);

                // Resolve Entra user IDs to internal Object IDs
                var entraUserIds = userLicenses.Select(u => u.EntraUserId).Distinct().ToList();
                var userIdMap = await _licenseRepository.ResolveEntraUserIdsAsync(connectionId, entraUserIds, cancellationToken);

                _logger.LogInformation("LicenseSync: {UserCount} Entra users with licenses, {ResolvedCount} resolved to Objects",
                    entraUserIds.Count, userIdMap.Count);

                int assignmentsUpserted = 0;
                int assignmentsSkipped = 0;

                foreach (var userLicense in userLicenses)
                {
                    if (!userIdMap.TryGetValue(userLicense.EntraUserId, out var objectId))
                    {
                        assignmentsSkipped += userLicense.Assignments.Count;
                        continue;
                    }

                    foreach (var skuAssignment in userLicense.Assignments)
                    {
                        if (!skuToPoolId.TryGetValue(skuAssignment.SkuId, out var poolId))
                        {
                            assignmentsSkipped++;
                            continue;
                        }

                        Guid? sourceGroupId = null;
                        if (!string.IsNullOrEmpty(skuAssignment.SourceGroupId) &&
                            Guid.TryParse(skuAssignment.SourceGroupId, out var parsedGroupId))
                        {
                            sourceGroupId = parsedGroupId;
                        }

                        var assignment = new LicenseAssignment
                        {
                            LicensePoolId = poolId,
                            ObjectId = objectId,
                            AssignmentSource = skuAssignment.AssignmentSource,
                            SourceGroupId = sourceGroupId,
                            IsActive = true,
                            LastSyncedAt = syncStart
                        };

                        await _licenseRepository.UpsertLicenseAssignmentAsync(assignment, cancellationToken);
                        assignmentsUpserted++;
                    }
                }

                _logger.LogInformation("LicenseSync: Upserted {Upserted} assignments, skipped {Skipped} (unresolved user or pool)",
                    assignmentsUpserted, assignmentsSkipped);

                // ── Phase 3: Deactivate stale assignments ──────────────────────────
                var deactivated = await _licenseRepository.DeactivateStaleAssignmentsAsync(connectionId, syncStart, cancellationToken);
                _logger.LogInformation("LicenseSync: Deactivated {Count} stale license assignments", deactivated);

                // ── Phase 4: Create daily usage snapshots ──────────────────────────
                var activePools = await _licenseRepository.GetLicensePoolsAsync(connectionId, cancellationToken);
                int snapshotsCreated = 0;
                foreach (var pool in activePools)
                {
                    await _licenseRepository.CreateSnapshotAsync(pool.Id, ct: cancellationToken);
                    snapshotsCreated++;
                }

                _logger.LogInformation("LicenseSync: Created {Count} usage snapshots", snapshotsCreated);

                // ── Phase 5: Generate optimization recommendations ────────────────
                try
                {
                    if (_licenseOptimizationEngine != null)
                    {
                        var recCount = await _licenseOptimizationEngine.GenerateRecommendationsAsync(connectionId, cancellationToken);
                        _logger.LogInformation("LicenseSync: Generated {Count} license optimization recommendations", recCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LicenseSync: Failed to generate license recommendations (non-fatal)");
                }

                // ── Finalize step run ──────────────────────────────────────────────
                stepRun.ObjectsProcessed = assignmentsUpserted + poolsUpserted;
                stepRun.ObjectsCreated = poolsUpserted;
                stepRun.ObjectsUpdated = assignmentsUpserted;
                stepRun.ObjectsSkipped = assignmentsSkipped;
                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                {
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;
                }

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsCreated += stepRun.ObjectsCreated;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;
                run.TotalErrors += stepRun.ErrorCount;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                await _syncRepository.UpdateProjectRunMetricsAsync(
                    run.Id,
                    run.TotalObjectsProcessed,
                    run.TotalObjectsCreated,
                    run.TotalObjectsUpdated,
                    run.TotalErrors,
                    run.CompletedSteps,
                    run.ProgressPercentage,
                    cancellationToken);

                _logger.LogInformation(
                    "LicenseSync step '{StepName}' completed: Pools={Pools}, Assignments={Assignments}, Deactivated={Deactivated}, Snapshots={Snapshots}, Duration={Duration}s",
                    step.Name, poolsUpserted, assignmentsUpserted, deactivated, snapshotsCreated, stepRun.DurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LicenseSync step '{StepName}' failed: {Error}",
                    step.Name, ex.Message);

                stepRun.Status = "Failed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;
                stepRun.ErrorMessage = ex.Message;
                stepRun.ErrorCount = 1;

                run.TotalErrors++;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                throw;
            }
        }

        /// <summary>
        /// Processes a SignInLogSync step: pulls sign-in logs from Entra ID audit logs,
        /// resolves user IDs, upserts logs and daily summaries.
        /// </summary>
        private async Task ProcessSignInLogSyncStepAsync(
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing SignInLogSync step '{StepName}' for connection {ConnectionId}",
                step.Name, project.SourceConnectionId);

            if (_cloudActivityRepository == null || _licenseSyncQueryService == null)
            {
                throw new InvalidOperationException(
                    "SignInLogSync step requires ICloudActivityRepository and ILicenseSyncQueryService to be registered.");
            }

            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = "SignInLogSync",
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = "",
                ObjectsQueried = -1
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            try
            {
                var syncStart = DateTime.UtcNow;
                var connectionId = project.SourceConnectionId
                    ?? throw new InvalidOperationException("SyncProject.SourceConnectionId is null");

                // Load the source connection
                ModelDirectoryConnection? sourceConnection;
                using (var conn = CreateConnection())
                {
                    sourceConnection = await conn.QueryFirstOrDefaultAsync<ModelDirectoryConnection>(
                        @"SELECT * FROM DirectoryConnections WHERE Id = @Id",
                        new { Id = connectionId });
                }

                if (sourceConnection == null)
                    throw new InvalidOperationException($"Source connection {connectionId} not found");

                // Parse Entra ID configuration
                if (string.IsNullOrEmpty(sourceConnection.Configuration))
                    throw new InvalidOperationException("Entra ID connection has no configuration");

                var config = System.Text.Json.JsonSerializer.Deserialize<DataAccessLibrary.Models.EntraIdConnectionConfig>(
                    sourceConnection.Configuration,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Failed to deserialize Entra ID connection configuration");

                // Decrypt credentials to get client secret
                var credentialsJson = await _encryptionService.DecryptAsync(sourceConnection.Credentials).ConfigureAwait(false);
                var credentials = System.Text.Json.JsonSerializer.Deserialize<DataAccessLibrary.Models.EntraIdCredentials>(
                    credentialsJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Failed to deserialize Entra ID credentials");

                // ── Phase 1: Determine incremental sync start date ────────────────────
                var latestSignIn = await _cloudActivityRepository.GetLatestSignInDateAsync(connectionId, cancellationToken);
                var since = latestSignIn ?? DateTime.UtcNow.AddDays(-30);

                _logger.LogInformation("SignInLogSync: Querying sign-in logs since {Since} for tenant {TenantId}",
                    since, config.TenantId);

                // ── Phase 2: Query sign-in logs from Graph API ────────────────────────
                var logs = await _licenseSyncQueryService.QuerySignInLogsAsync(
                    connectionId, config.TenantId, config.ClientId, credentials.ClientSecret, since, cancellationToken);

                stepRun.ObjectsQueried = logs.Count;
                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, 0, 0, 0, 0, 0, cancellationToken);

                // ── Phase 3: Resolve Entra user IDs to Object IDs ─────────────────────
                var entraUserIds = logs
                    .Where(l => !string.IsNullOrEmpty(l.EntraUserId))
                    .Select(l => l.EntraUserId!)
                    .Distinct()
                    .ToList();

                var userIdMap = await _cloudActivityRepository.ResolveEntraUserIdsAsync(connectionId, entraUserIds, cancellationToken);

                _logger.LogInformation("SignInLogSync: {LogCount} logs queried, {UserCount} unique Entra users, {ResolvedCount} resolved to Objects",
                    logs.Count, entraUserIds.Count, userIdMap.Count);

                // Set ObjectId on resolved logs, filter out unresolved
                var resolvedLogs = new List<SignInLog>();
                int skipped = 0;

                foreach (var log in logs)
                {
                    if (!string.IsNullOrEmpty(log.EntraUserId) && userIdMap.TryGetValue(log.EntraUserId, out var objectId))
                    {
                        log.ObjectId = objectId;
                        resolvedLogs.Add(log);
                    }
                    else
                    {
                        skipped++;
                    }
                }

                // ── Phase 4: Bulk upsert sign-in logs ─────────────────────────────────
                var inserted = await _cloudActivityRepository.BulkUpsertSignInLogsAsync(resolvedLogs, cancellationToken);

                _logger.LogInformation("SignInLogSync: Inserted {Inserted} sign-in logs, skipped {Skipped} (unresolved users)",
                    inserted, skipped);

                // ── Phase 5: Build and upsert daily summaries ─────────────────────────
                int summariesUpserted = 0;

                var summaryGroups = resolvedLogs
                    .GroupBy(l => new { l.ObjectId, AppName = l.AppDisplayName ?? "Unknown", Date = l.SignInDateTime.Date });

                foreach (var group in summaryGroups)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var logList = group.ToList();
                    var locations = logList
                        .Where(l => !string.IsNullOrEmpty(l.IpAddress))
                        .Select(l => l.IpAddress)
                        .Distinct()
                        .Count();

                    var summary = new SignInSummary
                    {
                        Id = Guid.NewGuid(),
                        ObjectId = group.Key.ObjectId,
                        SourceConnectionId = connectionId,
                        AppDisplayName = group.Key.AppName,
                        SummaryDate = group.Key.Date,
                        SuccessCount = logList.Count(l => l.Status == "Success"),
                        FailureCount = logList.Count(l => l.Status == "Failure"),
                        InteractiveCount = logList.Count(l => l.IsInteractive),
                        NonInteractiveCount = logList.Count(l => !l.IsInteractive),
                        UniqueLocations = locations
                    };

                    await _cloudActivityRepository.UpsertSignInSummaryAsync(summary, cancellationToken);
                    summariesUpserted++;
                }

                _logger.LogInformation("SignInLogSync: Upserted {Count} daily sign-in summaries", summariesUpserted);

                // ── Finalize step run ─────────────────────────────────────────────────
                stepRun.ObjectsProcessed = inserted + summariesUpserted;
                stepRun.ObjectsCreated = inserted;
                stepRun.ObjectsUpdated = summariesUpserted;
                stepRun.ObjectsSkipped = skipped;
                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                {
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;
                }

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsCreated += stepRun.ObjectsCreated;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;
                run.TotalErrors += stepRun.ErrorCount;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                await _syncRepository.UpdateProjectRunMetricsAsync(
                    run.Id,
                    run.TotalObjectsProcessed,
                    run.TotalObjectsCreated,
                    run.TotalObjectsUpdated,
                    run.TotalErrors,
                    run.CompletedSteps,
                    run.ProgressPercentage,
                    cancellationToken);

                _logger.LogInformation(
                    "SignInLogSync step '{StepName}' completed: Logs={Logs}, Summaries={Summaries}, Skipped={Skipped}, Duration={Duration}s",
                    step.Name, inserted, summariesUpserted, skipped, stepRun.DurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SignInLogSync step '{StepName}' failed: {Error}",
                    step.Name, ex.Message);

                stepRun.Status = "Failed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;
                stepRun.ErrorMessage = ex.Message;
                stepRun.ErrorCount = 1;

                run.TotalErrors++;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                throw;
            }
        }

        /// <summary>
        /// Processes a UsageReportSync step: pulls M365 active user detail report from Graph API,
        /// resolves UPNs to Object IDs, and upserts usage reports.
        /// </summary>
        private async Task ProcessUsageReportSyncStepAsync(
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing UsageReportSync step '{StepName}' for connection {ConnectionId}",
                step.Name, project.SourceConnectionId);

            if (_cloudActivityRepository == null || _licenseSyncQueryService == null)
            {
                throw new InvalidOperationException(
                    "UsageReportSync step requires ICloudActivityRepository and ILicenseSyncQueryService to be registered.");
            }

            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = "UsageReportSync",
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = "",
                ObjectsQueried = -1
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            try
            {
                var syncStart = DateTime.UtcNow;
                var connectionId = project.SourceConnectionId
                    ?? throw new InvalidOperationException("SyncProject.SourceConnectionId is null");

                // Load the source connection
                ModelDirectoryConnection? sourceConnection;
                using (var conn = CreateConnection())
                {
                    sourceConnection = await conn.QueryFirstOrDefaultAsync<ModelDirectoryConnection>(
                        @"SELECT * FROM DirectoryConnections WHERE Id = @Id",
                        new { Id = connectionId });
                }

                if (sourceConnection == null)
                    throw new InvalidOperationException($"Source connection {connectionId} not found");

                // Parse Entra ID configuration
                if (string.IsNullOrEmpty(sourceConnection.Configuration))
                    throw new InvalidOperationException("Entra ID connection has no configuration");

                var config = System.Text.Json.JsonSerializer.Deserialize<DataAccessLibrary.Models.EntraIdConnectionConfig>(
                    sourceConnection.Configuration,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Failed to deserialize Entra ID connection configuration");

                // Decrypt credentials to get client secret
                var credentialsJson = await _encryptionService.DecryptAsync(sourceConnection.Credentials).ConfigureAwait(false);
                var credentials = System.Text.Json.JsonSerializer.Deserialize<DataAccessLibrary.Models.EntraIdCredentials>(
                    credentialsJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Failed to deserialize Entra ID credentials");

                // ── Phase 1: Query M365 usage report from Graph API ───────────────────
                _logger.LogInformation("UsageReportSync: Querying M365 active user detail report for tenant {TenantId}",
                    config.TenantId);

                var reports = await _licenseSyncQueryService.QueryM365UsageReportAsync(
                    connectionId, config.TenantId, config.ClientId, credentials.ClientSecret, cancellationToken);

                stepRun.ObjectsQueried = reports.Count;
                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, 0, 0, 0, 0, 0, cancellationToken);

                // ── Phase 2: Resolve UPNs to Object IDs ───────────────────────────────
                var upns = reports
                    .Where(r => !string.IsNullOrEmpty(r.EntraUserPrincipalName))
                    .Select(r => r.EntraUserPrincipalName!)
                    .Distinct()
                    .ToList();

                var upnMap = await _cloudActivityRepository.ResolveByUPNAsync(connectionId, upns, cancellationToken);

                _logger.LogInformation("UsageReportSync: {ReportCount} report rows, {UPNCount} unique UPNs, {ResolvedCount} resolved to Objects",
                    reports.Count, upns.Count, upnMap.Count);

                // Set ObjectId on resolved reports, filter out unresolved
                var resolvedReports = new List<M365UsageReport>();
                int skipped = 0;

                foreach (var report in reports)
                {
                    if (!string.IsNullOrEmpty(report.EntraUserPrincipalName) &&
                        upnMap.TryGetValue(report.EntraUserPrincipalName, out var objectId))
                    {
                        report.ObjectId = objectId;
                        resolvedReports.Add(report);
                    }
                    else
                    {
                        skipped++;
                    }
                }

                // ── Phase 3: Bulk upsert usage reports ────────────────────────────────
                var upserted = await _cloudActivityRepository.BulkUpsertUsageReportsAsync(resolvedReports, cancellationToken);

                _logger.LogInformation("UsageReportSync: Upserted {Upserted} usage reports, skipped {Skipped} (unresolved UPNs)",
                    upserted, skipped);

                // ── Finalize step run ─────────────────────────────────────────────────
                stepRun.ObjectsProcessed = upserted;
                stepRun.ObjectsCreated = 0;
                stepRun.ObjectsUpdated = upserted;
                stepRun.ObjectsSkipped = skipped;
                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                {
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;
                }

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsCreated += stepRun.ObjectsCreated;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;
                run.TotalErrors += stepRun.ErrorCount;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                await _syncRepository.UpdateProjectRunMetricsAsync(
                    run.Id,
                    run.TotalObjectsProcessed,
                    run.TotalObjectsCreated,
                    run.TotalObjectsUpdated,
                    run.TotalErrors,
                    run.CompletedSteps,
                    run.ProgressPercentage,
                    cancellationToken);

                _logger.LogInformation(
                    "UsageReportSync step '{StepName}' completed: Upserted={Upserted}, Skipped={Skipped}, Duration={Duration}s",
                    step.Name, upserted, skipped, stepRun.DurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UsageReportSync step '{StepName}' failed: {Error}",
                    step.Name, ex.Message);

                stepRun.Status = "Failed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;
                stepRun.ErrorMessage = ex.Message;
                stepRun.ErrorCount = 1;

                run.TotalErrors++;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                throw;
            }
        }

        /// <summary>
        /// Processes an AppRoleSync step: pulls service principals and app role assignments
        /// from Entra ID, resolves principal/resource IDs, upserts enterprise apps and assignments,
        /// and deactivates stale assignments.
        /// </summary>
        private async Task ProcessAppRoleSyncStepAsync(
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing AppRoleSync step '{StepName}' for connection {ConnectionId}",
                step.Name, project.SourceConnectionId);

            if (_cloudActivityRepository == null || _licenseSyncQueryService == null)
            {
                throw new InvalidOperationException(
                    "AppRoleSync step requires ICloudActivityRepository and ILicenseSyncQueryService to be registered.");
            }

            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = "AppRoleSync",
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = "",
                ObjectsQueried = -1
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            try
            {
                var syncStart = DateTime.UtcNow;
                var connectionId = project.SourceConnectionId
                    ?? throw new InvalidOperationException("SyncProject.SourceConnectionId is null");

                // Load the source connection
                ModelDirectoryConnection? sourceConnection;
                using (var conn = CreateConnection())
                {
                    sourceConnection = await conn.QueryFirstOrDefaultAsync<ModelDirectoryConnection>(
                        @"SELECT * FROM DirectoryConnections WHERE Id = @Id",
                        new { Id = connectionId });
                }

                if (sourceConnection == null)
                    throw new InvalidOperationException($"Source connection {connectionId} not found");

                // Parse Entra ID configuration
                if (string.IsNullOrEmpty(sourceConnection.Configuration))
                    throw new InvalidOperationException("Entra ID connection has no configuration");

                var config = System.Text.Json.JsonSerializer.Deserialize<DataAccessLibrary.Models.EntraIdConnectionConfig>(
                    sourceConnection.Configuration,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Failed to deserialize Entra ID connection configuration");

                // Decrypt credentials to get client secret
                var credentialsJson = await _encryptionService.DecryptAsync(sourceConnection.Credentials).ConfigureAwait(false);
                var credentials = System.Text.Json.JsonSerializer.Deserialize<DataAccessLibrary.Models.EntraIdCredentials>(
                    credentialsJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Failed to deserialize Entra ID credentials");

                // ── Phase 1: Query app role assignments from Graph API ─────────────────
                _logger.LogInformation("AppRoleSync: Querying service principals and app role assignments for tenant {TenantId}",
                    config.TenantId);

                var (assignments, apps) = await _licenseSyncQueryService.QueryAppRoleAssignmentsAsync(
                    connectionId, config.TenantId, config.ClientId, credentials.ClientSecret, cancellationToken);

                stepRun.ObjectsQueried = assignments.Count + apps.Count;
                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, 0, 0, 0, 0, 0, cancellationToken);

                // ── Phase 2: Resolve principal and resource IDs to Object IDs ──────────
                var allEntraIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var a in assignments)
                {
                    if (a.PrincipalId.HasValue)
                        allEntraIds.Add(a.PrincipalId.Value.ToString());
                    if (a.ResourceId.HasValue)
                        allEntraIds.Add(a.ResourceId.Value.ToString());
                }

                foreach (var app in apps)
                {
                    if (!string.IsNullOrEmpty(app.ServicePrincipalId))
                        allEntraIds.Add(app.ServicePrincipalId);
                }

                var objectIdMap = await _cloudActivityRepository.ResolveEntraObjectIdsAsync(
                    connectionId, allEntraIds, cancellationToken);

                _logger.LogInformation("AppRoleSync: {AssignmentCount} assignments, {AppCount} apps, {EntraIdCount} unique Entra IDs, {ResolvedCount} resolved to Objects",
                    assignments.Count, apps.Count, allEntraIds.Count, objectIdMap.Count);

                // Set PrincipalObjectId and ResourceObjectId on assignments
                foreach (var a in assignments)
                {
                    if (a.PrincipalId.HasValue && objectIdMap.TryGetValue(a.PrincipalId.Value.ToString(), out var principalObjectId))
                        a.PrincipalObjectId = principalObjectId;

                    if (a.ResourceId.HasValue && objectIdMap.TryGetValue(a.ResourceId.Value.ToString(), out var resourceObjectId))
                        a.ResourceObjectId = resourceObjectId;
                }

                // Set ObjectId on enterprise apps
                foreach (var app in apps)
                {
                    if (!string.IsNullOrEmpty(app.ServicePrincipalId) &&
                        objectIdMap.TryGetValue(app.ServicePrincipalId, out var appObjectId))
                    {
                        app.ObjectId = appObjectId;
                    }
                }

                // ── Phase 3: Bulk upsert enterprise apps ──────────────────────────────
                var appsUpserted = await _cloudActivityRepository.BulkUpsertEnterpriseAppsAsync(apps, cancellationToken);
                _logger.LogInformation("AppRoleSync: Upserted {Count} enterprise apps", appsUpserted);

                // ── Phase 4: Bulk upsert app role assignments ─────────────────────────
                var assignmentsInserted = await _cloudActivityRepository.BulkUpsertAppRoleAssignmentsAsync(assignments, cancellationToken);
                _logger.LogInformation("AppRoleSync: Inserted {Count} app role assignments", assignmentsInserted);

                // ── Phase 5: Deactivate stale assignments ─────────────────────────────
                var deactivated = await _cloudActivityRepository.DeactivateStaleAppRoleAssignmentsAsync(connectionId, syncStart, cancellationToken);
                _logger.LogInformation("AppRoleSync: Deactivated {Count} stale app role assignments", deactivated);

                // ── Finalize step run ─────────────────────────────────────────────────
                stepRun.ObjectsProcessed = appsUpserted + assignmentsInserted;
                stepRun.ObjectsCreated = appsUpserted;
                stepRun.ObjectsUpdated = assignmentsInserted;
                stepRun.ObjectsSkipped = deactivated;
                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                {
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;
                }

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsCreated += stepRun.ObjectsCreated;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;
                run.TotalErrors += stepRun.ErrorCount;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                await _syncRepository.UpdateProjectRunMetricsAsync(
                    run.Id,
                    run.TotalObjectsProcessed,
                    run.TotalObjectsCreated,
                    run.TotalObjectsUpdated,
                    run.TotalErrors,
                    run.CompletedSteps,
                    run.ProgressPercentage,
                    cancellationToken);

                _logger.LogInformation(
                    "AppRoleSync step '{StepName}' completed: Apps={Apps}, Assignments={Assignments}, Deactivated={Deactivated}, Duration={Duration}s",
                    step.Name, appsUpserted, assignmentsInserted, deactivated, stepRun.DurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AppRoleSync step '{StepName}' failed: {Error}",
                    step.Name, ex.Message);

                stepRun.Status = "Failed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;
                stepRun.ErrorMessage = ex.Message;
                stepRun.ErrorCount = 1;

                run.TotalErrors++;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id,
                    stepRun.ObjectsQueried,
                    stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated,
                    stepRun.ObjectsUpdated,
                    stepRun.ObjectsSkipped,
                    stepRun.ErrorCount,
                    cancellationToken,
                    status: stepRun.Status,
                    completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                throw;
            }
        }

        /// <summary>
        /// Bulk-loads DisplayName for a list of Identity IDs.
        /// Used for writing audit logs after HR Import.
        /// </summary>
        private async Task<Dictionary<Guid, string>> BulkLoadIdentityNamesAsync(
            List<Guid> identityIds,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<Guid, string>();
            if (identityIds.Count == 0) return result;

            try
            {
                using var conn = CreateConnection();
                // Process in batches of 500 to avoid parameter limits
                foreach (var batch in identityIds.Chunk(500))
                {
                    var rows = await conn.QueryAsync<(Guid Id, string? DisplayName, string? EmployeeId)>(
                        "SELECT Id, DisplayName, EmployeeId FROM Identities WHERE Id IN @Ids",
                        new { Ids = batch });

                    foreach (var row in rows)
                    {
                        result[row.Id] = !string.IsNullOrEmpty(row.DisplayName)
                            ? row.DisplayName
                            : row.EmployeeId ?? row.Id.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to bulk-load identity names for audit logs");
            }

            return result;
        }

        /// <summary>
        /// Whitelist of valid Identities table columns for dynamic SQL in identity lookups.
        /// Used to prevent SQL injection when building dynamic lookup queries.
        /// </summary>
        private static readonly HashSet<string> ValidIdentityColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            // Core identity
            "Id", "CentralId", "SourceUniqueId",
            // Biographic & personal
            "DisplayName", "FirstName", "LastName", "MiddleName", "Suffix", "Salutation",
            "PreferredName", "DateOfBirth", "Gender", "NationalId", "PhotoUrl",
            // Contact
            "PrimaryEmail", "SecondaryEmail", "PrimaryPhone", "MobilePhone", "HomePhone", "Fax",
            "StreetAddress", "City", "State", "PostalCode", "Country",
            // Organizational & job
            "EmployeeId", "JobTitle", "Department", "Division", "Company", "Office", "Building",
            "Floor", "Room", "CostCenter", "ProfitCenter", "IdentityType", "EmployeeType",
            "ContractType", "JobCode", "JobFamily", "PayGrade", "Organization", "BusinessUnit",
            "LegalEntity", "Region", "Site", "WorkSchedule",
            // Dates
            "HireDate", "TerminationDate", "LastWorkDay", "StartDate", "EndDate",
            // Description & notes
            "Description", "Notes",
            // Manager & sponsor
            "ManagerIdentityId", "ManagerEmployeeId", "ManagerDisplayName", "Sponsor", "SponsorEmail",
            // Contractor / vendor
            "VendorName", "PONumber",
            // Physical access
            "BadgeNumber",
            // Technical & security
            "Username", "UserPrincipalName", "Status", "IsActive", "SecurityClearance",
            "RiskScore", "RiskLevel", "AuthoritativeSourceId",
            // Localization
            "PreferredLanguage", "TimeZone", "Locale",
            // Audit & lifecycle
            "CreatedAt", "ModifiedAt", "LastSeenAt", "LastLoginAt", "PasswordLastChangedAt",
            "LastAccessReviewAt", "CreatedBy", "ModifiedBy",
            // Custom attributes (1-20)
            "CustomAttribute1", "CustomAttribute2", "CustomAttribute3", "CustomAttribute4",
            "CustomAttribute5", "CustomAttribute6", "CustomAttribute7", "CustomAttribute8",
            "CustomAttribute9", "CustomAttribute10", "CustomAttribute11", "CustomAttribute12",
            "CustomAttribute13", "CustomAttribute14", "CustomAttribute15", "CustomAttribute16",
            "CustomAttribute17", "CustomAttribute18", "CustomAttribute19", "CustomAttribute20",
            // Custom JSON
            "CustomAttributes"
        };

        /// <summary>
        /// Processes an IdentityManagerLookup step for HR Import projects.
        /// Reads AttributeMappings with TransformationType == "IdentityLookup" to dynamically
        /// resolve manager references. Falls back to hardcoded ManagerEmployeeId → EmployeeId
        /// if no mappings are configured.
        /// </summary>
        private async Task ProcessIdentityManagerLookupStepAsync(
            SyncStep step,
            SyncProjectRun run,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("IDENTITY MANAGER LOOKUP STEP: Resolving manager references for HR Import project '{Name}'",
                project.Name);

            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = step.Id,
                StepName = step.Name,
                ObjectClass = "Identity",
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                ExecutionLog = ""
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            run.CurrentStep = step.Name;
            await _syncRepository.UpdateRunProgressAsync(run.Id, currentStepName: step.Name, cancellationToken: cancellationToken);

            try
            {
                // Read IdentityLookup mappings from step configuration
                var lookupMappings = step.AttributeMappings?
                    .Where(m => m.IsEnabled && m.TransformationType == "IdentityLookup")
                    .ToList() ?? new List<AttributeMapping>();

                // Determine source, lookup-by, and target columns
                string sourceColumn;
                string lookupByColumn;
                string targetColumn;

                if (lookupMappings.Any())
                {
                    var mapping = lookupMappings.First();
                    sourceColumn = string.IsNullOrWhiteSpace(mapping.SourceAttribute) ? "ManagerEmployeeId" : mapping.SourceAttribute;
                    lookupByColumn = string.IsNullOrWhiteSpace(mapping.TransformationExpression) ? "EmployeeId" : mapping.TransformationExpression;
                    targetColumn = string.IsNullOrWhiteSpace(mapping.TargetAttribute) ? "ManagerIdentityId" : mapping.TargetAttribute;

                    _logger.LogInformation("Identity Manager Lookup using configured mapping: {Source} → lookup by {LookupBy} → {Target}",
                        sourceColumn, lookupByColumn, targetColumn);
                }
                else
                {
                    // Fallback to hardcoded behavior for backwards compatibility
                    sourceColumn = "ManagerEmployeeId";
                    lookupByColumn = "EmployeeId";
                    targetColumn = "ManagerIdentityId";

                    _logger.LogInformation("Identity Manager Lookup using default mapping (no IdentityLookup mappings configured): {Source} → lookup by {LookupBy} → {Target}",
                        sourceColumn, lookupByColumn, targetColumn);
                }

                // Validate all column names against whitelist to prevent SQL injection
                if (!ValidIdentityColumns.Contains(sourceColumn))
                {
                    _logger.LogError("Invalid source column '{Column}' - not in whitelist. Skipping identity manager lookup.", sourceColumn);
                    stepRun.Status = "Failed";
                    stepRun.CompletedAt = DateTime.UtcNow;
                    stepRun.ErrorMessage = $"Invalid source column '{sourceColumn}' - not in allowed columns list.";
                    await _syncRepository.UpdateStepRunMetricsAsync(
                        stepRun.Id, 0, 0, 0, 0, 0, 1, cancellationToken,
                        status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: 0);
                    return;
                }
                if (!ValidIdentityColumns.Contains(lookupByColumn))
                {
                    _logger.LogError("Invalid lookup-by column '{Column}' - not in whitelist. Skipping identity manager lookup.", lookupByColumn);
                    stepRun.Status = "Failed";
                    stepRun.CompletedAt = DateTime.UtcNow;
                    stepRun.ErrorMessage = $"Invalid lookup-by column '{lookupByColumn}' - not in allowed columns list.";
                    await _syncRepository.UpdateStepRunMetricsAsync(
                        stepRun.Id, 0, 0, 0, 0, 0, 1, cancellationToken,
                        status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: 0);
                    return;
                }
                if (!ValidIdentityColumns.Contains(targetColumn))
                {
                    _logger.LogError("Invalid target column '{Column}' - not in whitelist. Skipping identity manager lookup.", targetColumn);
                    stepRun.Status = "Failed";
                    stepRun.CompletedAt = DateTime.UtcNow;
                    stepRun.ErrorMessage = $"Invalid target column '{targetColumn}' - not in allowed columns list.";
                    await _syncRepository.UpdateStepRunMetricsAsync(
                        stepRun.Id, 0, 0, 0, 0, 0, 1, cancellationToken,
                        status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: 0);
                    return;
                }

                int totalWithManager;
                int alreadyResolved;
                int resolved;

                using (var connection = CreateConnection())
                {
                    // Count identities with the source column set
                    totalWithManager = await connection.ExecuteScalarAsync<int>(
                        $"SELECT COUNT(*) FROM Identities WHERE [{sourceColumn}] IS NOT NULL AND LEN([{sourceColumn}]) > 0",
                        commandTimeout: 60);

                    // Count already resolved
                    alreadyResolved = await connection.ExecuteScalarAsync<int>(
                        $@"SELECT COUNT(*) FROM Identities
                          WHERE [{sourceColumn}] IS NOT NULL AND LEN([{sourceColumn}]) > 0
                            AND [{targetColumn}] IS NOT NULL",
                        commandTimeout: 60);

                    var needingResolution = totalWithManager - alreadyResolved;

                    stepRun.ObjectsQueried = totalWithManager;
                    _logger.LogInformation("Identity Manager Lookup: {Total} identities with manager ref, {Resolved} already resolved, {Needing} needing resolution",
                        totalWithManager, alreadyResolved, needingResolution);

                    if (needingResolution <= 0)
                    {
                        stepRun.ObjectsProcessed = totalWithManager;
                        stepRun.ObjectsUpdated = 0;
                        stepRun.ObjectsSkipped = alreadyResolved;
                        stepRun.Status = "Completed";
                        stepRun.CompletedAt = DateTime.UtcNow;
                        stepRun.DurationSeconds = 0;
                        stepRun.ExecutionLog = $"All {alreadyResolved} manager relationships already resolved.";

                        await _syncRepository.UpdateStepRunMetricsAsync(
                            stepRun.Id, stepRun.ObjectsQueried, stepRun.ObjectsProcessed,
                            stepRun.ObjectsCreated, stepRun.ObjectsUpdated, stepRun.ObjectsSkipped,
                            stepRun.ErrorCount, cancellationToken,
                            status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: stepRun.DurationSeconds);
                        return;
                    }

                    // Dynamic lookup SQL: UPDATE child SET child.[targetColumn] = manager.Id
                    // WHERE child.[sourceColumn] = manager.[lookupByColumn]
                    resolved = await connection.ExecuteAsync(
                        $@"UPDATE child
                          SET child.[{targetColumn}] = manager.Id,
                              child.ModifiedAt = SYSUTCDATETIME()
                          FROM Identities child
                          INNER JOIN Identities manager ON manager.[{lookupByColumn}] = child.[{sourceColumn}]
                          WHERE child.[{targetColumn}] IS NULL
                            AND child.[{sourceColumn}] IS NOT NULL
                            AND LEN(child.[{sourceColumn}]) > 0
                            AND manager.Id != child.Id",
                        commandTimeout: 120);

                    var notFound = needingResolution - resolved;

                    stepRun.ObjectsProcessed = needingResolution;
                    stepRun.ObjectsUpdated = resolved;
                    stepRun.ObjectsSkipped = notFound > 0 ? notFound : 0;
                    stepRun.ExecutionLog = $"Resolved {resolved} manager relationships ({sourceColumn} → {lookupByColumn} → {targetColumn}). {notFound} managers not found in Identities table.";
                }

                stepRun.Status = "Completed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                if (stepRun.ObjectsProcessed > 0)
                    stepRun.AvgProcessingTimeMs = (stepRun.DurationSeconds * 1000m) / stepRun.ObjectsProcessed;

                run.TotalObjectsProcessed += stepRun.ObjectsProcessed;
                run.TotalObjectsUpdated += stepRun.ObjectsUpdated;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated, stepRun.ObjectsUpdated, stepRun.ObjectsSkipped,
                    stepRun.ErrorCount, cancellationToken,
                    status: stepRun.Status, completedAt: stepRun.CompletedAt, durationSeconds: stepRun.DurationSeconds);

                _logger.LogInformation(
                    "Identity Manager Lookup step '{StepName}' completed: Resolved={Resolved}, NotFound={NotFound}, Duration={Duration}s",
                    step.Name, resolved, stepRun.ObjectsSkipped, stepRun.DurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Identity Manager Lookup step '{StepName}' failed: {Error}", step.Name, ex.Message);

                stepRun.Status = "Failed";
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;
                stepRun.ErrorMessage = ex.Message;

                run.TotalErrors++;

                using (var connection = CreateConnection())
                {
                    await connection.ExecuteAsync(
                        @"UPDATE SyncStepRuns SET Status = @Status, CompletedAt = @CompletedAt, DurationSeconds = @DurationSeconds, ErrorMessage = @ErrorMessage WHERE Id = @Id",
                        new { Id = stepRun.Id, Status = stepRun.Status, CompletedAt = stepRun.CompletedAt, DurationSeconds = stepRun.DurationSeconds, ErrorMessage = stepRun.ErrorMessage });
                }

                // Non-fatal for HR import - log but don't throw
                _logger.LogWarning("Identity Manager Lookup step failed but HR import data is already saved. Continuing.");
            }
        }

        /// <summary>
        /// Records a skip with detailed logging.
        /// </summary>
        private void RecordSkip(
            SyncStepRun stepRun,
            string reason,
            string? objectIdentifier,
            Dictionary<string, int> skipReasons,
            List<string> detailedSkipLog)
        {
            stepRun.ObjectsSkipped++;

            if (skipReasons.ContainsKey(reason))
            {
                skipReasons[reason]++;
            }
            else
            {
                skipReasons[reason] = 1;
            }

            if (detailedSkipLog.Count < _syncOptions.MaxDetailedSkips)
            {
                var timestamp = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss");
                var message = string.IsNullOrWhiteSpace(objectIdentifier)
                    ? $"[{timestamp}] SKIP: {reason}"
                    : $"[{timestamp}] SKIP: {reason} - {objectIdentifier}";
                detailedSkipLog.Add(message);
            }
        }

        /// <summary>
        /// Finalizes the step execution log with skip details and summary.
        /// </summary>
        private void FinalizeStepLog(
            SyncStepRun stepRun,
            Dictionary<string, int> skipReasons,
            List<string> detailedSkipLog)
        {
            if (stepRun.ObjectsSkipped == 0)
            {
                return;
            }

            var logBuilder = new System.Text.StringBuilder();
            logBuilder.AppendLine("=== SKIP DETAILS ===");
            logBuilder.AppendLine();

            if (detailedSkipLog.Any())
            {
                logBuilder.AppendLine($"Detailed Skip Log (first {_syncOptions.MaxDetailedSkips}):");
                foreach (var entry in detailedSkipLog)
                {
                    logBuilder.AppendLine(entry);
                }
                logBuilder.AppendLine();
            }

            logBuilder.AppendLine("Skip Summary:");
            foreach (var kvp in skipReasons.OrderByDescending(x => x.Value))
            {
                logBuilder.AppendLine($"  {kvp.Key}: {kvp.Value} object(s)");
            }

            if (stepRun.ObjectsSkipped > _syncOptions.MaxDetailedSkips)
            {
                logBuilder.AppendLine();
                logBuilder.AppendLine($"Note: Showing detailed logs for first {_syncOptions.MaxDetailedSkips} of {stepRun.ObjectsSkipped} total skipped objects.");
            }

            stepRun.ExecutionLog += logBuilder.ToString();
        }

        // Helper methods
        private object? GetSourceValue(Dictionary<string, object> sourceObject, string attributeName)
        {
            var key = sourceObject.Keys.FirstOrDefault(k => k.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
            return key != null ? sourceObject[key] : null;
        }

        private string? ApplyTransformation(object? sourceValue, AttributeMapping mapping)
        {
            if (sourceValue == null)
            {
                return mapping.DefaultValue;
            }

            var stringValue = sourceValue.ToString();

            return mapping.TransformationType switch
            {
                "Direct" => stringValue,
                "ToUpper" => stringValue?.ToUpper(),
                "ToLower" => stringValue?.ToLower(),
                "Trim" => stringValue?.Trim(),
                _ => stringValue
            };
        }

        private string GetFinalMappedValue(IdentityObject identityObject, string targetAttribute)
        {
            switch (targetAttribute.ToLower())
            {
                case "sourceuniqueid":
                    return identityObject.SourceUniqueId ?? "(null)";
                case "displayname":
                    return identityObject.DisplayName ?? "(null)";
                case "email":
                    return identityObject.Email ?? "(null)";
                case "username":
                    return identityObject.Username ?? "(null)";
                case "firstname":
                    return identityObject.FirstName ?? "(null)";
                case "lastname":
                    return identityObject.LastName ?? "(null)";
                case "department":
                    return identityObject.Department ?? "(null)";
                case "jobtitle":
                    return identityObject.JobTitle ?? "(null)";
                case "phone":
                    return identityObject.Phone ?? "(null)";
                default:
                    var attr = identityObject.Attributes.FirstOrDefault(a => a.AttributeName == targetAttribute);
                    return attr?.AttributeValue ?? "(null)";
            }
        }

        private LogLevel ParseLogLevel(string? logLevelString)
        {
            return logLevelString?.ToLower() switch
            {
                "trace" => LogLevel.Trace,
                "debug" => LogLevel.Debug,
                "information" => LogLevel.Information,
                "warning" => LogLevel.Warning,
                "error" => LogLevel.Error,
                "critical" => LogLevel.Critical,
                _ => LogLevel.Information
            };
        }

        private string FormatFullException(Exception ex)
        {
            var messages = new List<string>();
            var currentEx = ex;
            int level = 0;

            while (currentEx != null)
            {
                var prefix = level == 0 ? "ERROR" : $"INNER EXCEPTION #{level}";
                messages.Add($"=== {prefix} ===");
                messages.Add($"Type: {currentEx.GetType().FullName}");
                messages.Add($"Message: {currentEx.Message}");

                if (!string.IsNullOrEmpty(currentEx.StackTrace))
                {
                    messages.Add($"\nStack Trace:");
                    messages.Add(currentEx.StackTrace);
                }

                currentEx = currentEx.InnerException;
                level++;
            }

            return string.Join("\n", messages);
        }

        /// <summary>
        /// Compares two IdentityObject instances and returns a list of changed fields
        /// with before/after values for the sync audit log.
        /// </summary>
        private static List<Dictionary<string, string?>> CompareObjectFields(IdentityObject oldObj, IdentityObject newObj)
        {
            var changes = new List<Dictionary<string, string?>>();

            void Check(string field, string? oldVal, string? newVal)
            {
                // Treat null and empty as equivalent to avoid noise
                var o = string.IsNullOrEmpty(oldVal) ? null : oldVal;
                var n = string.IsNullOrEmpty(newVal) ? null : newVal;
                if (!string.Equals(o, n, StringComparison.Ordinal))
                {
                    changes.Add(new Dictionary<string, string?>
                    {
                        { "Field", field },
                        { "Before", o },
                        { "After", n }
                    });
                }
            }

            Check("DisplayName", oldObj.DisplayName, newObj.DisplayName);
            Check("Email", oldObj.Email, newObj.Email);
            Check("Username", oldObj.Username, newObj.Username);
            Check("FirstName", oldObj.FirstName, newObj.FirstName);
            Check("LastName", oldObj.LastName, newObj.LastName);
            Check("MiddleName", oldObj.MiddleName, newObj.MiddleName);
            Check("Department", oldObj.Department, newObj.Department);
            Check("JobTitle", oldObj.JobTitle, newObj.JobTitle);
            Check("Phone", oldObj.Phone, newObj.Phone);
            Check("MobilePhone", oldObj.MobilePhone, newObj.MobilePhone);
            Check("HomePhone", oldObj.HomePhone, newObj.HomePhone);
            Check("Fax", oldObj.Fax, newObj.Fax);
            Check("Company", oldObj.Company, newObj.Company);
            Check("Division", oldObj.Division, newObj.Division);
            Check("Office", oldObj.Office, newObj.Office);
            Check("EmployeeId", oldObj.EmployeeId, newObj.EmployeeId);
            Check("EmployeeType", oldObj.EmployeeType, newObj.EmployeeType);
            Check("UserPrincipalName", oldObj.UserPrincipalName, newObj.UserPrincipalName);
            Check("Description", oldObj.Description, newObj.Description);
            Check("DN", oldObj.DN, newObj.DN);
            Check("CN", oldObj.CN, newObj.CN);
            Check("StreetAddress", oldObj.StreetAddress, newObj.StreetAddress);
            Check("City", oldObj.City, newObj.City);
            Check("State", oldObj.State, newObj.State);
            Check("PostalCode", oldObj.PostalCode, newObj.PostalCode);
            Check("Country", oldObj.Country, newObj.Country);
            Check("ManagerSourceId", oldObj.ManagerSourceId, newObj.ManagerSourceId);
            Check("ObjectClass", oldObj.ObjectClass, newObj.ObjectClass);
            Check("SourceType", oldObj.SourceType, newObj.SourceType);

            if (oldObj.IsActive != newObj.IsActive)
            {
                changes.Add(new Dictionary<string, string?>
                {
                    { "Field", "IsActive" },
                    { "Before", oldObj.IsActive.ToString() },
                    { "After", newObj.IsActive.ToString() }
                });
            }

            if (oldObj.ManagerObjectId != newObj.ManagerObjectId)
            {
                changes.Add(new Dictionary<string, string?>
                {
                    { "Field", "ManagerObjectId" },
                    { "Before", oldObj.ManagerObjectId?.ToString() },
                    { "After", newObj.ManagerObjectId?.ToString() }
                });
            }

            if (oldObj.UserAccountControl != newObj.UserAccountControl)
            {
                changes.Add(new Dictionary<string, string?>
                {
                    { "Field", "UserAccountControl" },
                    { "Before", oldObj.UserAccountControl?.ToString() },
                    { "After", newObj.UserAccountControl?.ToString() }
                });
            }

            return changes;
        }

        /// <summary>
        /// Optimizes indexes on sync-related tables after bulk operations.
        /// </summary>
        private async Task OptimizeIndexesAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("INDEX OPTIMIZATION: Starting post-sync index maintenance...");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var result = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    DECLARE @TableName NVARCHAR(128)
                    DECLARE @IndexName NVARCHAR(128)
                    DECLARE @SchemaName NVARCHAR(128)
                    DECLARE @Fragmentation FLOAT
                    DECLARE @SQL NVARCHAR(MAX)
                    DECLARE @RebuiltCount INT = 0
                    DECLARE @ReorganizedCount INT = 0
                    DECLARE @SkippedCount INT = 0

                    DECLARE index_cursor CURSOR FOR
                    SELECT
                        s.name AS SchemaName,
                        t.name AS TableName,
                        i.name AS IndexName,
                        ps.avg_fragmentation_in_percent AS Fragmentation
                    FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
                    INNER JOIN sys.indexes i ON ps.object_id = i.object_id AND ps.index_id = i.index_id
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE t.name IN ('Objects', 'ObjectAttributes', 'Identities', 'Groups', 'ObjectGroupMemberships')
                      AND i.name IS NOT NULL
                      AND ps.avg_fragmentation_in_percent > 5
                    ORDER BY ps.avg_fragmentation_in_percent DESC

                    OPEN index_cursor
                    FETCH NEXT FROM index_cursor INTO @SchemaName, @TableName, @IndexName, @Fragmentation

                    WHILE @@FETCH_STATUS = 0
                    BEGIN
                        IF @Fragmentation > 30
                        BEGIN
                            SET @SQL = 'ALTER INDEX [' + @IndexName + '] ON [' + @SchemaName + '].[' + @TableName + '] REBUILD WITH (ONLINE = ON)'
                            EXEC sp_executesql @SQL
                            SET @RebuiltCount = @RebuiltCount + 1
                        END
                        ELSE IF @Fragmentation > 5
                        BEGIN
                            SET @SQL = 'ALTER INDEX [' + @IndexName + '] ON [' + @SchemaName + '].[' + @TableName + '] REORGANIZE'
                            EXEC sp_executesql @SQL
                            SET @ReorganizedCount = @ReorganizedCount + 1
                        END
                        ELSE
                        BEGIN
                            SET @SkippedCount = @SkippedCount + 1
                        END

                        FETCH NEXT FROM index_cursor INTO @SchemaName, @TableName, @IndexName, @Fragmentation
                    END

                    CLOSE index_cursor
                    DEALLOCATE index_cursor

                    UPDATE STATISTICS [dbo].[Objects] WITH FULLSCAN
                    UPDATE STATISTICS [dbo].[ObjectAttributes] WITH FULLSCAN
                    UPDATE STATISTICS [dbo].[Identities] WITH FULLSCAN
                    UPDATE STATISTICS [dbo].[Groups] WITH FULLSCAN
                    UPDATE STATISTICS [dbo].[ObjectGroupMemberships] WITH FULLSCAN

                    SELECT @RebuiltCount AS Rebuilt, @ReorganizedCount AS Reorganized, @SkippedCount AS Skipped",
                    commandTimeout: 300);

                sw.Stop();
                if (result != null)
                {
                    _logger.LogInformation(
                        "INDEX OPTIMIZATION COMPLETE: Rebuilt={Rebuilt}, Reorganized={Reorganized}, Skipped={Skipped} in {ElapsedMs}ms",
                        (int)result.Rebuilt, (int)result.Reorganized, (int)result.Skipped, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex,
                    "INDEX OPTIMIZATION FAILED (non-fatal): {Error} after {ElapsedMs}ms",
                    ex.Message, sw.ElapsedMilliseconds);
            }
        }

        // =========================================================================
        // DEV CENTER - SCRIPT EXECUTION METHODS
        // =========================================================================

        private async Task<List<Dictionary<string, object>>> ExecutePreProcessingScriptsAsync(
            List<Dictionary<string, object>> sourceData,
            SyncStep step,
            SyncProject project,
            SyncStepRun stepRun,
            CancellationToken cancellationToken)
        {
            if (_scriptCompilationService == null || _scriptLoggerFactory == null)
            {
                var hasScripts = step.StepScripts?.Any(ss => ss.IsEnabled && ss.ExecutionPhase == ScriptTypes.PreProcessing) ?? false;
                if (hasScripts)
                {
                    _logger.LogWarning("Step '{StepName}' has pre-processing scripts configured but script services are not available.", step.Name);
                }
                return sourceData;
            }

            try
            {
                var scripts = await _syncRepository.GetStepScriptsAsync(step.Id, ScriptTypes.PreProcessing, cancellationToken);

                if (!scripts.Any())
                {
                    return sourceData;
                }

                _logger.LogInformation("Executing {Count} pre-processing script(s) for step '{StepName}'",
                    scripts.Count, step.Name);

                foreach (var scriptInfo in scripts)
                {
                    var script = new SyncProcessingScript
                    {
                        Id = scriptInfo.ScriptId,
                        Name = scriptInfo.ScriptName,
                        ScriptType = scriptInfo.ScriptType,
                        ScriptCode = scriptInfo.ScriptCode,
                        Version = scriptInfo.Version,
                        Category = scriptInfo.Category,
                        IsSystem = scriptInfo.IsSystem
                    };

                    var context = new PreProcessingContext(
                        sourceData,
                        step,
                        project,
                        stepRun,
                        _scriptLoggerFactory.CreateLogger(script.Name, enableDebug: true),
                        _syncRepository,
                        project.SourceConnectionId!.Value,
                        cancellationToken);

                    var result = await _scriptCompilationService.ExecutePreProcessingScriptAsync(script, context, cancellationToken);

                    await RecordScriptExecutionAsync(stepRun.Id, script, ScriptTypes.PreProcessing, result, cancellationToken);

                    if (!result.Success)
                    {
                        _logger.LogWarning("Pre-processing script '{ScriptName}' failed: {Error}",
                            script.Name, result.ErrorMessage);
                    }
                    else
                    {
                        _logger.LogInformation("Pre-processing script '{ScriptName}' completed in {Duration}ms",
                            script.Name, result.DurationMs);

                        sourceData = context.SourceObjects;
                    }
                }

                return sourceData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing pre-processing scripts for step '{StepName}'", step.Name);
                return sourceData;
            }
        }

        private async Task ExecutePostProcessingScriptsAsync(
            List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> syncedObjects,
            SyncStep step,
            SyncProject project,
            SyncStepRun stepRun,
            CancellationToken cancellationToken)
        {
            if (_scriptCompilationService == null || _scriptLoggerFactory == null)
            {
                var hasScripts = step.StepScripts?.Any(ss => ss.IsEnabled && ss.ExecutionPhase == ScriptTypes.PostProcessing) ?? false;
                if (hasScripts)
                {
                    _logger.LogWarning("Step '{StepName}' has post-processing scripts configured but script services are not available.", step.Name);
                }
                return;
            }

            try
            {
                var scripts = await _syncRepository.GetStepScriptsAsync(step.Id, ScriptTypes.PostProcessing, cancellationToken);

                if (!scripts.Any())
                {
                    return;
                }

                _logger.LogInformation("Executing {Count} post-processing script(s) for step '{StepName}'",
                    scripts.Count, step.Name);

                var objects = syncedObjects.Select(x => x.identityObject).ToList();
                var attributes = syncedObjects.ToDictionary(x => x.identityObject.Id, x => x.attributes);

                foreach (var scriptInfo in scripts)
                {
                    var script = new SyncProcessingScript
                    {
                        Id = scriptInfo.ScriptId,
                        Name = scriptInfo.ScriptName,
                        ScriptType = scriptInfo.ScriptType,
                        ScriptCode = scriptInfo.ScriptCode,
                        Version = scriptInfo.Version,
                        Category = scriptInfo.Category,
                        IsSystem = scriptInfo.IsSystem
                    };

                    var metrics = new ScriptMetrics();
                    var context = new PostProcessingContext(
                        objects,
                        attributes,
                        step,
                        project,
                        stepRun,
                        _scriptLoggerFactory.CreateLogger(script.Name, enableDebug: true),
                        _syncRepository,
                        metrics,
                        project.SourceConnectionId!.Value,
                        cancellationToken);

                    var result = await _scriptCompilationService.ExecutePostProcessingScriptAsync(script, context, cancellationToken);

                    await RecordScriptExecutionAsync(stepRun.Id, script, ScriptTypes.PostProcessing, result, cancellationToken);

                    if (!result.Success)
                    {
                        _logger.LogWarning("Post-processing script '{ScriptName}' failed: {Error}",
                            script.Name, result.ErrorMessage);
                    }
                    else
                    {
                        _logger.LogInformation("Post-processing script '{ScriptName}' completed in {Duration}ms - {Summary}",
                            script.Name, result.DurationMs, result.Metrics.GetSummary());

                        if (result.Metrics.IdentitiesCreated > 0)
                        {
                            Interlocked.Add(ref _personsCreatedCount, result.Metrics.IdentitiesCreated);
                        }

                        stepRun.PersonsCreated += result.Metrics.IdentitiesCreated;
                        stepRun.PersonsMatched += result.Metrics.IdentitiesUpdated;
                    }
                }

                await _syncRepository.UpdateStepRunPersonMetricsAsync(stepRun.Id, stepRun.PersonsCreated, stepRun.PersonsMatched, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing post-processing scripts for step '{StepName}'", step.Name);
            }
        }

        // ============================================================
        // THREAD-SAFE HELPER METHODS FOR PARALLEL WORKFLOW EXECUTION
        // ============================================================

        private async Task UpdateRunCurrentStepAsync(Guid runId, string currentStep, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.ExecuteAsync(
                    "UPDATE SyncProjectRuns SET CurrentStep = @CurrentStep WHERE Id = @RunId",
                    new { CurrentStep = currentStep, RunId = runId });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update current step for run {RunId}", runId);
            }
        }

        private async Task UpdateRunProgressAsync(Guid runId, int completedSteps, int progressPercentage, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.ExecuteAsync(
                    @"UPDATE SyncProjectRuns
                      SET CompletedSteps = @CompletedSteps,
                          ProgressPercentage = @ProgressPercentage
                      WHERE Id = @RunId",
                    new { CompletedSteps = completedSteps, ProgressPercentage = progressPercentage, RunId = runId });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update progress for run {RunId}", runId);
            }
        }

        private async Task RecordScriptExecutionAsync(
            Guid stepRunId,
            SyncProcessingScript script,
            string executionPhase,
            ScriptExecutionResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                var execution = new SyncScriptExecution
                {
                    Id = Guid.NewGuid(),
                    SyncStepRunId = stepRunId,
                    ScriptId = script.Id,
                    ExecutionPhase = executionPhase,
                    Status = result.Success ? ExecutionStatus.Success : ExecutionStatus.Error,
                    StartedAt = DateTime.UtcNow.AddMilliseconds(-result.DurationMs),
                    CompletedAt = DateTime.UtcNow,
                    DurationMs = result.DurationMs,
                    ObjectsProcessed = result.ObjectsProcessed,
                    ObjectsModified = result.Metrics.ObjectsModified,
                    IdentitiesCreated = result.Metrics.IdentitiesCreated,
                    ManagersResolved = result.Metrics.ManagersResolved,
                    ErrorMessage = result.ErrorMessage,
                    OutputLog = result.LogEntries.Any()
                        ? JsonSerializer.Serialize(result.LogEntries.Select(e => new
                        {
                            t = e.Timestamp.ToString("O"),
                            l = e.Level.ToString()[0],
                            m = e.Message
                        }))
                        : null
                };

                await _syncRepository.RecordScriptExecutionAsync(execution, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record script execution for '{ScriptName}'", script.Name);
            }
        }

        // ============================================================================
        // PRE-SYNC DATABASE OPTIMIZATION
        // ============================================================================

        private async Task ExecutePreSyncOptimizationAsync(
            SyncProjectRun run,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("PRE-SYNC OPTIMIZATION: Starting database index rebuild...");

            var preSyncTask = new PostSyncTask
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                TaskType = "DatabaseOptimization",
                TaskPhase = "PreSync",
                Status = "Running",
                Priority = 0,
                ObjectsTotal = _databaseOptimizationService!.GetTablesToOptimize().Length,
                ObjectsProcessed = 0,
                ObjectsSkipped = 0,
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            using (var connection = CreateConnection())
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO PostSyncTasks (Id, SyncProjectRunId, TaskType, TaskPhase, Status, Priority, ObjectsTotal, ObjectsProcessed, ObjectsSkipped, StartedAt, CreatedAt)
                      VALUES (@Id, @SyncProjectRunId, @TaskType, @TaskPhase, @Status, @Priority, @ObjectsTotal, @ObjectsProcessed, @ObjectsSkipped, @StartedAt, @CreatedAt)",
                    preSyncTask);
            }

            _ = _syncLogBuffer.AddLogAsync(run.Id, Microsoft.Extensions.Logging.LogLevel.Information,
                "Pre-sync database optimization starting...", "SyncProjectOrchestrator");

            try
            {
                var progress = new Progress<OptimizationProgress>(p =>
                {
                    _ = UpdatePreSyncTaskProgressAsync(preSyncTask.Id, p.TablesCompleted, p.CurrentTable);

                    _ = _syncLogBuffer.AddLogAsync(run.Id, Microsoft.Extensions.Logging.LogLevel.Debug,
                        $"Optimizing table: {p.CurrentTable} ({p.TablesCompleted}/{p.TotalTables})", "DatabaseOptimization");
                });

                var result = await _databaseOptimizationService.RunOptimizationAsync(progress, cancellationToken);

                preSyncTask.Status = result.Success ? "Completed" : "Failed";
                preSyncTask.ErrorMessage = result.ErrorMessage;
                preSyncTask.CompletedAt = DateTime.UtcNow;
                preSyncTask.DurationSeconds = result.DurationSeconds;
                preSyncTask.ObjectsProcessed = result.TablesOptimized;

                using (var connection = CreateConnection())
                {
                    await connection.ExecuteAsync(
                        @"UPDATE PostSyncTasks SET Status = @Status, ErrorMessage = @ErrorMessage, CompletedAt = @CompletedAt, DurationSeconds = @DurationSeconds, ObjectsProcessed = @ObjectsProcessed WHERE Id = @Id",
                        preSyncTask);
                }

                if (result.Success)
                {
                    _logger.LogInformation("PRE-SYNC OPTIMIZATION: Completed in {Duration}s - {Tables} tables optimized",
                        result.DurationSeconds, result.TablesOptimized);

                    _ = _syncLogBuffer.AddLogAsync(run.Id, Microsoft.Extensions.Logging.LogLevel.Information,
                        $"Pre-sync optimization completed in {result.DurationSeconds}s - {result.TablesOptimized} tables optimized", "SyncProjectOrchestrator");
                }
                else
                {
                    _logger.LogWarning("PRE-SYNC OPTIMIZATION: Failed - {Error}", result.ErrorMessage);

                    _ = _syncLogBuffer.AddLogAsync(run.Id, Microsoft.Extensions.Logging.LogLevel.Warning,
                        $"Pre-sync optimization failed: {result.ErrorMessage}", "SyncProjectOrchestrator");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PRE-SYNC OPTIMIZATION: Failed with exception");

                preSyncTask.Status = "Failed";
                preSyncTask.ErrorMessage = ex.Message;
                preSyncTask.CompletedAt = DateTime.UtcNow;

                using (var connection = CreateConnection())
                {
                    await connection.ExecuteAsync(
                        @"UPDATE PostSyncTasks SET Status = @Status, ErrorMessage = @ErrorMessage, CompletedAt = @CompletedAt WHERE Id = @Id",
                        preSyncTask);
                }

                _ = _syncLogBuffer.AddLogAsync(run.Id, Microsoft.Extensions.Logging.LogLevel.Error,
                    $"Pre-sync optimization exception: {ex.Message}", "SyncProjectOrchestrator");
            }
        }

        private async Task UpdatePreSyncTaskProgressAsync(Guid taskId, int tablesCompleted, string currentTable)
        {
            try
            {
                using var connection = CreateConnection();
                await connection.ExecuteAsync(
                    @"UPDATE PostSyncTasks SET ObjectsProcessed = @ObjectsProcessed WHERE Id = @Id",
                    new { Id = taskId, ObjectsProcessed = tablesCompleted });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update pre-sync task progress");
            }
        }

        // ============================================================================
        // PROJECT CHAINING
        // ============================================================================

        private async Task ExecuteProjectChainsAsync(
            Guid completedProjectId,
            string status,
            CancellationToken cancellationToken)
        {
            try
            {
                List<dynamic> chains;
                using (var connection = CreateConnection())
                {
                    chains = (await connection.QueryAsync(
                        @"SELECT c.*, tp.Name as TargetProjectName
                          FROM SyncProjectChains c
                          LEFT JOIN SyncProjects tp ON c.TargetProjectId = tp.Id
                          WHERE c.SourceProjectId = @SourceProjectId AND c.IsEnabled = 1
                          ORDER BY c.ExecutionOrder",
                        new { SourceProjectId = completedProjectId })).ToList();
                }

                if (!chains.Any())
                {
                    _logger.LogDebug("No enabled chains found for project {ProjectId}", completedProjectId);
                    return;
                }

                _logger.LogInformation("Found {Count} chain(s) to evaluate for project {ProjectId}",
                    chains.Count, completedProjectId);

                foreach (var chain in chains)
                {
                    string triggerCondition = (string)chain.TriggerCondition;
                    string targetProjectName = (string?)chain.TargetProjectName ?? ((Guid)chain.TargetProjectId).ToString();
                    int delaySeconds = (int)chain.DelaySeconds;
                    Guid targetProjectId = (Guid)chain.TargetProjectId;

                    if (!ShouldTriggerChain(triggerCondition, status))
                    {
                        _logger.LogDebug("Skipping chain to {TargetProject} - condition {Condition} not met for status {Status}",
                            targetProjectName, triggerCondition, status);
                        continue;
                    }

                    if (delaySeconds > 0)
                    {
                        _logger.LogInformation("Waiting {Delay}s before triggering {TargetProject}",
                            delaySeconds, targetProjectName);
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    }

                    _logger.LogInformation("Triggering chained project: {TargetProject} (condition: {Condition})",
                        targetProjectName, triggerCondition);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ExecuteSyncProjectAsync(
                                targetProjectId,
                                "Chained",
                                $"Chained from project {completedProjectId}",
                                CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to execute chained project {ProjectId}", targetProjectId);
                        }
                    }, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error executing project chains for {ProjectId}", completedProjectId);
            }
        }

        private static bool ShouldTriggerChain(string condition, string status)
        {
            return condition switch
            {
                "OnSuccess" => status == "Completed",
                "OnFailure" => status == "Failed",
                "OnCompletion" => true,
                _ => false
            };
        }

        private async Task PostSyncNotificationAsync(SyncProject project, SyncProjectRun run, CancellationToken cancellationToken)
        {
            try
            {
                var isSuccess = run.Status == "Completed";
                var severity = isSuccess ? (run.TotalErrors > 0 ? "Medium" : "Low") : "High";
                var notificationType = isSuccess ? "SyncComplete" : "SyncError";

                var title = isSuccess
                    ? $"Sync Completed: {project.Name}"
                    : $"Sync Failed: {project.Name}";

                var message = isSuccess
                    ? $"**Objects:** {run.TotalObjectsCreated} created, {run.TotalObjectsUpdated} updated, {run.TotalObjectsDeleted} deleted\n" +
                      $"**Persons:** {run.TotalPersonsCreated} created\n" +
                      $"**Duration:** {run.DurationSeconds}s" +
                      (run.TotalErrors > 0 ? $"\n**Errors:** {run.TotalErrors}" : "")
                    : $"Sync failed with status: {run.Status}\n**Errors:** {run.TotalErrors}";

                if (_adminNotificationService != null)
                {
                    // Use the service for both DB persistence AND SignalR broadcast
                    var notification = new AdminNotification
                    {
                        Id = Guid.NewGuid(),
                        NotificationType = notificationType,
                        Category = "Sync",
                        Severity = severity,
                        Title = title,
                        Message = message,
                        ActionUrl = $"/admin/sync-projects/{project.Id}/runs/{run.Id}",
                        ActionText = "View Run",
                        RelatedEntityId = run.Id,
                        RelatedEntityType = "SyncProjectRun",
                        Source = "Sync Engine",
                        CreatedAt = DateTime.UtcNow
                    };

                    await _adminNotificationService.SendNotificationAsync(notification);
                }
                else
                {
                    // Fallback: direct DB insert (no real-time broadcast)
                    using var connection = CreateConnection();
                    await connection.ExecuteAsync(@"
                        INSERT INTO AdminNotifications (Id, NotificationType, Category, Severity, Title, Message,
                            ActionUrl, ActionText, RelatedEntityId, RelatedEntityType, Source, CreatedAt, IsRead, IsDismissed)
                        VALUES (@Id, @NotificationType, @Category, @Severity, @Title, @Message,
                            @ActionUrl, @ActionText, @RelatedEntityId, @RelatedEntityType, @Source, GETUTCDATE(), 0, 0)",
                        new {
                            Id = Guid.NewGuid(),
                            NotificationType = notificationType,
                            Category = "Sync",
                            Severity = severity,
                            Title = title,
                            Message = message,
                            ActionUrl = $"/admin/sync-projects/{project.Id}/runs/{run.Id}",
                            ActionText = "View Run",
                            RelatedEntityId = run.Id,
                            RelatedEntityType = "SyncProjectRun",
                            Source = "Sync Engine"
                        });
                }

                _logger.LogDebug("Posted admin notification for sync completion: {ProjectName}", project.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to post admin notification for sync completion: {ProjectName}", project.Name);
            }
        }
    }
}
