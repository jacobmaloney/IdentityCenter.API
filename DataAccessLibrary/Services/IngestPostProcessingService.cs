using System.Diagnostics;
using System.Text.Json;
using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Phase 2.2 (Part A + D). Reusable, connection-scoped post-processing that runs
/// the SAME governance steps the live <see cref="SyncProjectOrchestrator"/> runs —
/// Person match, manager (DN → ManagerObjectId) resolution, and group-membership
/// resolution — but callable OUTSIDE a SyncProject run, so a Conduit ingest batch
/// can trigger identical downstream linking.
///
/// DUAL-RUN SAFETY: this service does NOT replace or alter the orchestrator. It
/// delegates to the exact same repository primitives the orchestrator already
/// calls — <see cref="ISyncRelationshipRepository.ResolveManagerRelationshipsAsync"/>,
/// <see cref="ISyncObjectRepository.BulkUpsertObjectGroupMembershipsAsync"/>, and
/// <see cref="PersonMatchOrchestrator"/>. The existing internal-sync path is
/// therefore byte-identical; this is purely an additional entry point.
///
/// Membership note: the orchestrator's GroupMembership step QUERIES the source
/// directory (AD 'member' attribute + primaryGroupID SID reconstruction) to build
/// the membership set. A Conduit ingest does NOT have a live directory to query —
/// Conduit pushes memberships EXPLICITLY via the group-memberships ingest endpoint,
/// which persists through the same <c>BulkUpsertObjectGroupMembershipsAsync</c>
/// primitive. So this service does NOT re-query a directory for memberships; it
/// resolves only what ingest already landed. Manager + person-match ARE re-run
/// here because they operate purely on already-ingested IC rows.
/// </summary>
public class IngestPostProcessingService
{
    private readonly ISyncRepository _syncRepository;
    private readonly PersonMatchOrchestrator _personMatchOrchestrator;
    private readonly ILogger<IngestPostProcessingService> _logger;
    private readonly string _defaultConnectionString;

    public IngestPostProcessingService(
        ISyncRepository syncRepository,
        PersonMatchOrchestrator personMatchOrchestrator,
        IConfiguration configuration,
        ILogger<IngestPostProcessingService> logger)
    {
        _syncRepository = syncRepository;
        _personMatchOrchestrator = personMatchOrchestrator;
        _logger = logger;
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string not configured");
    }

    // MULTI-TENANT SEAM (SaaS Day 4): this runs on the post-process drainer, which installs a fixed
    // tenant resolver into the ambient accessor for the duration of a tenant's work item. Routing
    // _connectionString through the accessor makes this pass hit the correct tenant DB. Falls back to
    // DefaultConnection when no resolver is installed (legacy/single-tenant).
    private string _connectionString =>
        DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

    /// <summary>
    /// Run the full post-ingest governance chain for one source connection:
    /// 1. Object → Person match (link ingested user Objects to Identities).
    /// 2. Manager resolution (ManagerSourceId DN → ManagerObjectId Guid).
    /// 3. Manager-resolution audit rows (same shape the orchestrator writes).
    ///
    /// Idempotent: every step is a no-op when there is nothing left to resolve.
    /// Scoped strictly to <paramref name="sourceConnectionId"/> — no cross-connection
    /// reads or writes (the underlying repo SQL is connection-filtered on both
    /// sides of every join).
    /// </summary>
    public async Task<IngestPostProcessingResult> RunForConnectionAsync(
        Guid sourceConnectionId,
        bool runPersonMatch = true,
        bool runManagerResolution = true,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new IngestPostProcessingResult { SourceConnectionId = sourceConnectionId };

        _logger.LogInformation(
            "INGEST POST-PROCESS: starting for connection {ConnectionId} (personMatch={PersonMatch}, manager={Manager})",
            sourceConnectionId, runPersonMatch, runManagerResolution);

        // ── Step 1: Person match ────────────────────────────────────────────
        if (runPersonMatch)
        {
            try
            {
                var (project, run) = BuildEphemeralPersonMatchContext(sourceConnectionId);
                var matchResult = await _personMatchOrchestrator.ExecuteAsync(project, run, cancellationToken);
                result.PersonsMatched = matchResult.Matched;
                result.PersonsCreated = matchResult.Created;
                _logger.LogInformation(
                    "INGEST POST-PROCESS: person match complete — {Matched} matched, {Created} created",
                    matchResult.Matched, matchResult.Created);
            }
            catch (Exception ex)
            {
                result.PersonMatchError = ex.Message;
                _logger.LogError(ex, "INGEST POST-PROCESS: person match FAILED for connection {ConnectionId}", sourceConnectionId);
            }
        }

        // ── Step 2 + 3: Manager resolution + audit ──────────────────────────
        if (runManagerResolution)
        {
            try
            {
                var (_, alreadyResolved, needingResolution) =
                    await _syncRepository.GetManagerResolutionStatsAsync(sourceConnectionId, cancellationToken);

                if (needingResolution > 0)
                {
                    var resolved = await _syncRepository.ResolveManagerRelationshipsAsync(sourceConnectionId, cancellationToken);
                    result.ManagersResolved = resolved;

                    // Audit goes to ChangeAuditLogs, NOT SyncAuditLogs.
                    //
                    // WHY: SyncAuditLogs has a hard FK (FK_SyncAuditLogs_SyncStepRuns,
                    // ON DELETE CASCADE) to a SyncStepRun row. An ingest-triggered run
                    // has no SyncStepRun, so writing there would either violate the FK
                    // or force us to manufacture synthetic sync-run rows that would
                    // pollute run history + dual-run parity comparisons. ChangeAuditLogs
                    // has no such FK and is the same audit sink the bulk/tombstone
                    // endpoints already use (Source-tagged so it's filterable).
                    var auditDetails = await _syncRepository.GetManagerResolutionDetailsAsync(sourceConnectionId, cancellationToken);
                    var resolvedAudits = auditDetails.Where(d => d.WasResolved).ToList();
                    if (resolvedAudits.Count > 0)
                    {
                        await WriteManagerAuditAsync(resolvedAudits, cancellationToken);
                    }

                    _logger.LogInformation(
                        "INGEST POST-PROCESS: manager resolution complete — {Resolved} resolved of {Needing} needing (was {Already} already resolved)",
                        resolved, needingResolution, alreadyResolved);
                }
                else
                {
                    _logger.LogInformation(
                        "INGEST POST-PROCESS: manager resolution skipped — all {Already} already resolved",
                        alreadyResolved);
                }
            }
            catch (Exception ex)
            {
                result.ManagerResolutionError = ex.Message;
                _logger.LogError(ex, "INGEST POST-PROCESS: manager resolution FAILED for connection {ConnectionId}", sourceConnectionId);
            }
        }

        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;
        _logger.LogInformation(
            "INGEST POST-PROCESS: done for connection {ConnectionId} in {Ms}ms (matched={Matched}, createdPeople={Created}, managers={Managers})",
            sourceConnectionId, result.DurationMs, result.PersonsMatched, result.PersonsCreated, result.ManagersResolved);

        return result;
    }

    /// <summary>
    /// Builds an in-memory PersonMatch SyncProject + run scoped to one connection.
    /// PersonMatchOrchestrator keys all of its work off project.SourceConnectionId
    /// and project.ProjectType, so this reuses the exact same matching path the
    /// orchestrator uses for a real PersonMatch project — no behavior fork.
    /// The run is transient (never persisted) because ingest post-processing
    /// records its own audit; PersonMatchOrchestrator does not require the run to
    /// exist in the DB.
    /// </summary>
    private static (SyncProject Project, SyncProjectRun Run) BuildEphemeralPersonMatchContext(Guid sourceConnectionId)
    {
        var project = new SyncProject
        {
            Id = Guid.NewGuid(),
            Name = $"Ingest-PersonMatch-{sourceConnectionId:N}",
            ProjectType = "PersonMatch",
            SourceConnectionId = sourceConnectionId
        };
        var run = new SyncProjectRun
        {
            Id = Guid.NewGuid(),
            SyncProjectId = project.Id,
            StartedAt = DateTime.UtcNow,
            Status = "Running"
        };
        return (project, run);
    }

    /// <summary>
    /// Writes one ChangeAuditLogs row per newly-resolved manager relationship.
    /// Same column set the bulk/tombstone endpoints use: OperationType=1 (Update),
    /// EntityType='Object', Source tagged so these are filterable. Best-effort —
    /// audit must never fail the resolution that already committed.
    /// </summary>
    private async Task WriteManagerAuditAsync(
        List<ManagerResolutionAuditItem> resolved, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var rows = resolved.Select(item => new
            {
                EntityId = item.ObjectId,
                NewValue = JsonSerializer.Serialize(new
                {
                    Field = "ManagerObjectId",
                    item.ManagerObjectId,
                    ManagerName = item.ManagerDisplayName,
                    item.SourceUniqueId
                })
            });

            await conn.ExecuteAsync(
                @"INSERT INTO ChangeAuditLogs (Timestamp, UserId, OperationType, EntityType, EntityId, Source, NewValue, Success)
                  VALUES (SYSUTCDATETIME(), 'Conduit', 1, 'Object', @EntityId, 'Conduit-PostProcess-Manager', @NewValue, 1)",
                rows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("INGEST POST-PROCESS: manager audit write failed (best-effort): {Error}", ex.Message);
        }
    }
}

/// <summary>
/// Outcome of an ingest-triggered post-processing run. Truthful: per-step errors
/// are captured rather than swallowed, so a caller (or audit) can see partial
/// failure instead of a fake "success".
/// </summary>
public class IngestPostProcessingResult
{
    public Guid SourceConnectionId { get; set; }
    public int PersonsMatched { get; set; }
    public int PersonsCreated { get; set; }
    public int ManagersResolved { get; set; }
    public string? PersonMatchError { get; set; }
    public string? ManagerResolutionError { get; set; }
    public long DurationMs { get; set; }

    public bool HadError => PersonMatchError is not null || ManagerResolutionError is not null;
}
