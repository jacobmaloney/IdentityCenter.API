using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using IdentityCenter.API.Models;
using IdentityCenter.API.Services;
using ISyncObjectRepository = DataAccessLibrary.Repositories.ISyncObjectRepository;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// Public API over the IC <c>Objects</c> table. Built to power the IdentityCenter
/// Conduit connector — i.e. Conduit calls <c>GET /api/objects/query</c> when IC
/// is wired as a sync source, and <c>POST /api/objects/bulk</c> when IC is the
/// sync sink. Lives alongside <see cref="IdentitiesController"/> and follows the
/// same raw-Dapper / DefaultConnection / X-API-Key auth pattern.
/// </summary>
/// <remarks>
/// <para>
/// <b>Write-back design decision (2026-05-23):</b> the bulk endpoint writes
/// <i>directly</i> to the Objects table via Dapper and DOES NOT route through
/// <c>IObjectWriteBackService</c>. That service resolves a write target from the
/// object's source connection and pushes to AD / Entra; if IC accepted Conduit's
/// AD sync push through it, every Conduit→IC write would echo back out to the
/// real directory IC is supposed to be modelling. Conduit owns the directory
/// write — IC's job here is just to absorb the projection. Audit is preserved
/// by stamping a <c>ChangeAuditLogs</c> row per upsert.
/// </para>
/// </remarks>
[ApiController]
[Route("api/objects")]
[Authorize(Policy = "TenantDataPolicy")]
public class ObjectsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IGlobalLogger _logger;
    private readonly string _defaultConnectionString;
    private readonly ISyncObjectRepository _syncObjectRepository;
    private readonly PostProcessQueue _postProcessQueue;

    /// <summary>
    /// Connection string for THIS request. Routes through the ambient <see cref="DataAccessLibrary.ControlPlane.TenantConnectionAccessor"/>
    /// so a tenant-scoped request hits ONLY its own DB; falls back to DefaultConnection for legacy/admin
    /// (no resolver installed). Resolved per access — never captured once — to match DapperRepositoryBase.
    /// </summary>
    private string _connectionString =>
        DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

    // Strict naming pattern for any caller-supplied Source string that may end
    // up seeded into DirectoryConnections.Name. Alphanumerics + dash + underscore
    // only; bounded length. Anything else is rejected at request entry — better
    // to fail loud than to seed garbage connection rows that need manual cleanup.
    private static readonly Regex SourceNamePattern = new(
        @"^[A-Za-z0-9_\-]{1,100}$",
        RegexOptions.Compiled);

    // Whitelist of Objects-table columns we accept on a bulk upsert. Keeps the
    // raw-Dapper path safe (no caller-controlled column names land in SQL) and
    // matches the AD-writable set in ObjectWriteBackService.WritableFields.
    private static readonly HashSet<string> WritableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "DisplayName", "Email", "Username", "UserPrincipalName",
        "FirstName", "LastName", "MiddleName",
        "Department", "JobTitle", "Phone", "MobilePhone", "HomePhone", "Fax",
        "StreetAddress", "City", "State", "PostalCode", "Country",
        "Company", "Division", "Office",
        "EmployeeId", "EmployeeType", "CostCenter",
        "Description", "CN", "DN",
        // Phase 2.2 Part B: capture the raw manager reference (a DN) so the
        // ingest post-processing's ResolveManagerRelationshipsAsync can map it to
        // ManagerObjectId. Without this, a Conduit push could never establish the
        // manager graph. ManagerSourceId is a typed Objects column (nvarchar 500).
        "ManagerSourceId",
        "IsActive"
    };

    public ObjectsController(
        IConfiguration configuration,
        IGlobalLogger logger,
        ISyncObjectRepository syncObjectRepository,
        PostProcessQueue postProcessQueue)
    {
        _configuration = configuration;
        _logger = logger;
        _syncObjectRepository = syncObjectRepository;
        _postProcessQueue = postProcessQueue;
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    /// <summary>
    /// Paged query over the Objects table. <c>objectClass</c> is required;
    /// <c>source</c> filters by <c>SourceType</c> (e.g. "ActiveDirectory");
    /// <c>modifiedSince</c> is the incremental cursor (ISO-8601 UTC).
    /// </summary>
    [HttpGet("query")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Query(
        [FromQuery] string? objectClass = null,
        [FromQuery] string? source = null,
        [FromQuery] DateTime? modifiedSince = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (string.IsNullOrWhiteSpace(objectClass))
            return BadRequest(new { error = "objectClass is required" });
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 100;
        if (pageSize > 1000) pageSize = 1000;

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var where = new List<string> { "o.DeletedAt IS NULL", "o.ObjectClass = @objectClass" };
            var parameters = new DynamicParameters();
            parameters.Add("objectClass", objectClass);

            if (!string.IsNullOrWhiteSpace(source))
            {
                where.Add("o.SourceType = @source");
                parameters.Add("source", source);
            }
            if (modifiedSince.HasValue)
            {
                where.Add("(o.ModifiedAt IS NOT NULL AND o.ModifiedAt > @modifiedSince)");
                parameters.Add("modifiedSince", modifiedSince.Value);
            }

            var whereSql = string.Join(" AND ", where);

            var total = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM Objects o WHERE {whereSql}", parameters);

            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);

            var rows = await conn.QueryAsync(
                $@"SELECT o.Id, o.ObjectClass, o.SourceType AS Source, o.SourceUniqueId,
                          o.CN, o.DN, o.Username, o.UserPrincipalName, o.DisplayName,
                          o.IsActive, o.ModifiedAt, o.CreatedAt
                   FROM Objects o
                   WHERE {whereSql}
                   ORDER BY ISNULL(o.ModifiedAt, o.CreatedAt) ASC, o.Id ASC
                   OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY", parameters);

            var items = new List<ObjectQueryItem>();
            var ids = new List<Guid>();
            foreach (var r in rows)
            {
                var id = (Guid)r.Id;
                ids.Add(id);
                items.Add(new ObjectQueryItem
                {
                    Id = id,
                    ObjectClass = (string?)r.ObjectClass ?? string.Empty,
                    Source = (string?)r.Source ?? string.Empty,
                    SourceUniqueId = (string?)r.SourceUniqueId ?? string.Empty,
                    CN = (string?)r.CN,
                    DN = (string?)r.DN,
                    Username = (string?)r.Username,
                    UserPrincipalName = (string?)r.UserPrincipalName,
                    DisplayName = (string?)r.DisplayName,
                    IsActive = (bool)r.IsActive,
                    ModifiedAt = (DateTime?)r.ModifiedAt ?? (DateTime)r.CreatedAt,
                    Attributes = new Dictionary<string, string?>()
                });
            }

            // Hydrate sparse ObjectAttributes for the page in one round-trip.
            if (ids.Count > 0)
            {
                var attrRows = await conn.QueryAsync<(Guid ObjectId, string AttributeName, string? AttributeValue)>(
                    @"SELECT ObjectId, AttributeName, AttributeValue
                      FROM ObjectAttributes
                      WHERE ObjectId IN @ids",
                    new { ids });

                var byObject = items.ToDictionary(i => i.Id, i => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
                foreach (var ar in attrRows)
                {
                    if (byObject.TryGetValue(ar.ObjectId, out var bag))
                        bag[ar.AttributeName] = ar.AttributeValue;
                }
                foreach (var item in items)
                {
                    item.Attributes = byObject[item.Id];
                }
            }

            return Ok(new ObjectQueryResponse
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                HasMore = page * pageSize < total
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Objects query failed (objectClass={ObjectClass}, source={Source})", objectClass, source);
            return StatusCode(500, new { error = "Objects query failed" });
        }
    }

    /// <summary>
    /// Bulk upsert into the Objects table. Lookup keyed on
    /// <c>(SourceConnectionId-by-Source, SourceUniqueId)</c>. Items containing
    /// unknown attribute keys are still persisted (unknown keys land in
    /// ObjectAttributes), but the typed columns we write are limited to
    /// <see cref="WritableColumns"/>. Writes go straight to IC's DB and DO NOT
    /// echo back to AD / Entra — Conduit owns that side of the wire.
    /// </summary>
    [HttpPost("bulk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkUpsert([FromBody] BulkUpsertRequest request)
    {
        if (request is null || request.Items is null || request.Items.Count == 0)
            return BadRequest(new { error = "Items is required and must be non-empty" });
        if (request.Items.Count > 1000)
            return BadRequest(new { error = "Maximum 1000 items per request" });

        // Validate every distinct Source string up front. Anything that fails the
        // pattern would land in DirectoryConnections.Name on auto-seed, so reject
        // the whole batch rather than partial-process garbage.
        var distinctSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Source)) continue;
            distinctSources.Add(item.Source);
        }
        foreach (var src in distinctSources)
        {
            if (!SourceNamePattern.IsMatch(src))
            {
                return BadRequest(new
                {
                    error = $"Invalid Source value '{src}'. Must match {SourceNamePattern} (alphanumerics, '-' and '_' only, 1-100 chars)."
                });
            }
        }

        var results = new List<BulkUpsertResult>(request.Items.Count);

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Auto-seed a DirectoryConnections row for any Source string we've
            // never seen before. Idempotent: WHERE NOT EXISTS inside the INSERT
            // guards against concurrent first-batch races between sync runs.
            // Seeded rows are tagged ConnectionType='Conduit' so operators can
            // see at a glance which connections were synthesized by the bulk
            // API vs. configured by hand in the IC admin UI.
            foreach (var src in distinctSources)
            {
                var existed = await conn.ExecuteScalarAsync<int?>(
                    "SELECT 1 FROM [DirectoryConnections] WHERE [Name] = @Name",
                    new { Name = src });
                if (existed == 1) continue;

                var inserted = await conn.ExecuteAsync(
                    @"INSERT INTO [DirectoryConnections]
                          ([Id], [Name], [ConnectionType], [ConnectionString], [Credentials],
                           [IsActive], [IsAuthoritative], [CreatedAt])
                      SELECT NEWID(), @Name, 'Conduit', '', '', 1, 0, SYSUTCDATETIME()
                      WHERE NOT EXISTS (
                          SELECT 1 FROM [DirectoryConnections] WHERE [Name] = @Name)",
                    new { Name = src });
                if (inserted > 0)
                {
                    _logger.LogInformation(
                        "API: auto-seeded DirectoryConnections row for Source='{Source}' (batch {BatchId})",
                        src, request.BatchId);
                }
            }

            // Resolve a SourceConnectionId per Source string once for the batch.
            // Most batches share one source; this is just a small cache.
            var sourceToConnection = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            // ── FULL SET-BASED REWRITE (2026-06-08, task #63) ────────────────
            // The Objects upsert is now ONE SqlBulkCopy (#StagingObjects) + ONE MERGE
            // with an OUTPUT clause, replacing the old per-row existence-SELECT +
            // INSERT/UPDATE loop (~23ms/item against the slow .56). Attributes + audit
            // were already set-based (fb64e0e2/49b6c80); this collapses the last
            // per-row path. A 500-object batch now does a handful of round-trips total
            // (bulk-copy objects → MERGE → bulk-copy attrs → MERGE attrs → audit insert)
            // regardless of batch size.
            //
            // Revive/lifecycle fidelity is preserved EXACTLY via the MERGE OUTPUT:
            //   $action='INSERT'                       → Created
            //   $action='UPDATE' AND deleted.DeletedAt → Revived (tombstone reappear)
            //   $action='UPDATE'                       → Updated
            // The per-item IsActive→LifecycleState mapping and the revive CASE logic are
            // computed PER ROW in C# and carried into staging columns, so the MERGE only
            // copies already-resolved values — no behavior moved from C# into ambiguous
            // SQL. Allow-listed columns are written via bracket-quoted identifiers built
            // from the fixed server-side WritableColumns set (never caller input).

            // Stage 1 — resolve each item in-memory (NO per-row Object SQL). Connection
            // resolution still caches per Source. Invalid / unresolved items short-circuit
            // to a result here exactly as before; only valid items enter staging.
            var prepared = new List<PreparedObject>(request.Items.Count);
            // Index into `prepared` by (connectionId, SourceUniqueId) so a within-batch
            // duplicate supersedes the earlier staged row in O(1) — the MERGE source must
            // be unique on the target key or SQL throws "attempted to UPDATE the same row
            // more than once".
            var preparedIndex = new Dictionary<(Guid, string), int>(request.Items.Count);
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.SourceUniqueId)
                    || string.IsNullOrWhiteSpace(item.ObjectClass)
                    || string.IsNullOrWhiteSpace(item.Source))
                {
                    results.Add(new BulkUpsertResult
                    {
                        SourceUniqueId = item.SourceUniqueId ?? "",
                        Outcome = "Failed",
                        ErrorMessage = "SourceUniqueId, ObjectClass and Source are all required"
                    });
                    continue;
                }

                if (!sourceToConnection.TryGetValue(item.Source, out var connectionId))
                {
                    // Match by Name first (auto-seed creates Name = source string).
                    // Fall back to ConnectionType for backward compat with pre-V126 IC
                    // instances where an operator may have already hand-created a
                    // connection of a given type.
                    connectionId = await conn.ExecuteScalarAsync<Guid?>(
                        @"SELECT TOP 1 Id FROM DirectoryConnections
                          WHERE [Name] = @Source AND IsActive = 1
                          ORDER BY CreatedAt ASC",
                        new { item.Source }) ?? Guid.Empty;
                    if (connectionId == Guid.Empty)
                    {
                        connectionId = await conn.ExecuteScalarAsync<Guid?>(
                            @"SELECT TOP 1 Id FROM DirectoryConnections
                              WHERE ConnectionType = @Source AND IsActive = 1
                              ORDER BY CreatedAt ASC",
                            new { item.Source }) ?? Guid.Empty;
                    }
                    sourceToConnection[item.Source] = connectionId;
                }

                if (connectionId == Guid.Empty)
                {
                    results.Add(new BulkUpsertResult
                    {
                        SourceUniqueId = item.SourceUniqueId,
                        Outcome = "Skipped",
                        ErrorMessage = $"No active DirectoryConnection of type '{item.Source}'"
                    });
                    continue;
                }

                // De-dup within the batch on (connectionId, SourceUniqueId): the MERGE
                // source MUST NOT contain two rows for the same target key (SQL throws
                // "an action of type ... attempted to UPDATE the same row more than
                // once"). Last write wins, mirroring the old loop where a later item
                // simply re-UPDATEd the same row. The earlier duplicate is reported as
                // a redundant Updated outcome (its data was overwritten before landing).
                var key = (connectionId, item.SourceUniqueId!);
                if (preparedIndex.TryGetValue(key, out var dupIndex))
                {
                    // Supersede the earlier staged row with this later one.
                    prepared[dupIndex] = new PreparedObject(connectionId, item);
                }
                else
                {
                    preparedIndex[key] = prepared.Count;
                    prepared.Add(new PreparedObject(connectionId, item));
                }
            }

            var attrRows = new List<(Guid ObjectId, string AttributeName, string? AttributeValue, string? DataType)>();
            var auditRows = new List<(Guid ObjectId, int OperationType, string NewValue)>();

            // Stage 2/3 — SqlBulkCopy + MERGE Objects, then read the OUTPUT back to
            // derive per-item outcome + the ObjectId for EVERY row (new and existing).
            if (prepared.Count > 0)
            {
                IReadOnlyList<MergeOutputRow> mergeOut;
                try
                {
                    mergeOut = await UpsertObjectsSetBasedAsync(conn, prepared);
                }
                catch (Exception mergeEx)
                {
                    // A whole-batch MERGE failure is fatal to the object writes — surface
                    // it the same way the outer catch would, but tag every prepared item
                    // so the caller sees per-item Failed rather than a silent partial.
                    _logger.LogError(mergeEx, "API: bulk upsert MERGE failed for batch {BatchId}", request.BatchId);
                    foreach (var p in prepared)
                    {
                        results.Add(new BulkUpsertResult
                        {
                            SourceUniqueId = p.Item.SourceUniqueId,
                            Outcome = "Failed",
                            ErrorMessage = "Object MERGE failed: " + mergeEx.Message
                        });
                    }
                    return StatusCode(500, new { error = "Bulk upsert failed", batchId = request.BatchId });
                }

                // Index the OUTPUT by (SourceConnectionId, SourceUniqueId) so we can map
                // each prepared item back to its resolved ObjectId + action.
                var outByKey = new Dictionary<(Guid, string), MergeOutputRow>(mergeOut.Count);
                foreach (var o in mergeOut)
                    outByKey[(o.SourceConnectionId, o.SourceUniqueId)] = o;

                foreach (var p in prepared)
                {
                    var item = p.Item;
                    if (!outByKey.TryGetValue((p.ConnectionId, item.SourceUniqueId!), out var outRow))
                    {
                        // Should never happen — the MERGE emits one OUTPUT row per source
                        // row. Treat a missing mapping as a failed item rather than a
                        // mismatched ObjectId.
                        results.Add(new BulkUpsertResult
                        {
                            SourceUniqueId = item.SourceUniqueId,
                            Outcome = "Failed",
                            ErrorMessage = "MERGE did not return an ObjectId for this item"
                        });
                        continue;
                    }

                    var objectId = outRow.ObjectId;
                    string outcome, auditAction;
                    if (outRow.IsInsert)
                    {
                        outcome = "Created";
                        auditAction = "Created";
                    }
                    else
                    {
                        // A revive is a meaningful state change — audited distinctly so a
                        // tombstone→reappear round-trip is visible in ChangeAuditLogs. The
                        // outcome to the sink stays "Updated" either way (a revive is an
                        // upsert that landed on an existing, if tombstoned, row).
                        outcome = "Updated";
                        auditAction = outRow.PriorDeletedAt is not null ? "Revived" : "Updated";
                    }

                    // Stage this object's non-typed attributes + its audit row for the
                    // batched set-based flush below. ObjectId comes from the MERGE OUTPUT,
                    // so NEW objects get their attributes too.
                    CollectAttributes(attrRows, objectId, item.Attributes);
                    auditRows.Add((objectId,
                        string.Equals(auditAction, "Created", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                        JsonSerializer.Serialize(new
                        {
                            item.SourceUniqueId,
                            item.ObjectClass,
                            item.Source,
                            action = auditAction,
                            attributeCount = item.Attributes?.Count ?? 0
                        })));

                    results.Add(new BulkUpsertResult
                    {
                        SourceUniqueId = item.SourceUniqueId,
                        Outcome = outcome
                    });
                }
            }

            // ── Set-based attribute flush: ONE SqlBulkCopy + ONE MERGE ───────
            // Mirrors the proven internal path (SyncObjectRepository.FastBulkUpsert):
            // stage into #StagingAttrs, then MERGE on (ObjectId, AttributeName).
            // Matches the REAL ObjectAttributes schema EXACTLY — Id=NEWID() on insert,
            // (ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt); there
            // is NO FirstSyncedAt column (the old code referenced it and omitted Id,
            // which failed 100% of rows with "Invalid column name 'FirstSyncedAt'").
            await FlushAttributesAsync(conn, attrRows);

            // ── Set-based audit flush: ONE INSERT for all Created/Updated/Revived rows.
            await FlushAuditAsync(conn, auditRows);

            _logger.LogInformation(
                "API: bulk upsert batch {BatchId} processed {Total} items ({Created} created, {Updated} updated, {Skipped} skipped, {Failed} failed)",
                request.BatchId,
                results.Count,
                results.Count(r => r.Outcome == "Created"),
                results.Count(r => r.Outcome == "Updated"),
                results.Count(r => r.Outcome == "Skipped"),
                results.Count(r => r.Outcome == "Failed"));

            // Phase 2.2 Part D: fire post-processing for every connection this
            // batch touched. Non-blocking — enqueue + return; the background
            // service runs person-match + manager resolution off the request
            // thread. Coalesced per connection, so many batches collapse to one
            // pass. Only enqueue connections that actually had a successful upsert.
            var touchedConnections = sourceToConnection.Values
                .Where(id => id != Guid.Empty)
                .Distinct();
            // Capture the CURRENT request's tenant connection (resolved on this thread) so the background
            // post-process pass runs against the same tenant DB. Null for legacy/admin → DefaultConnection.
            var tenantConn = DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve();
            foreach (var connId in touchedConnections)
            {
                _postProcessQueue.Enqueue(connId, runPersonMatch: true, runManagerResolution: true, tenantConn);
            }

            return Ok(new BulkUpsertResponse
            {
                BatchId = request.BatchId,
                Results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: bulk upsert batch {BatchId} failed", request.BatchId);
            return StatusCode(500, new { error = "Bulk upsert failed", batchId = request.BatchId });
        }
    }

    /// <summary>One prepared, connection-resolved item awaiting the set-based MERGE.</summary>
    private readonly record struct PreparedObject(Guid ConnectionId, BulkUpsertItem Item);

    /// <summary>One row from the MERGE OUTPUT clause: maps a source key back to its
    /// resolved ObjectId, the action taken, and the row's prior DeletedAt (so an
    /// UPDATE that landed on a tombstone is reported as a Revive).</summary>
    private readonly record struct MergeOutputRow(
        string Action, Guid ObjectId, Guid SourceConnectionId, string SourceUniqueId, DateTime? PriorDeletedAt)
    {
        public bool IsInsert => string.Equals(Action, "INSERT", StringComparison.OrdinalIgnoreCase);
    }

    // The Objects allow-list, ordered, as a stable list so the staging DataTable
    // columns and the MERGE column lists line up 1:1. Excludes IsActive — IsActive is
    // a typed driver (NOT-NULL bit) handled explicitly, not as a free-text column.
    private static readonly string[] WritableColumnList =
        WritableColumns.Where(c => !string.Equals(c, "IsActive", StringComparison.OrdinalIgnoreCase))
                       .ToArray();

    /// <summary>
    /// THE set-based Objects upsert: SqlBulkCopy the whole batch into #StagingObjects,
    /// then ONE MERGE on (SourceConnectionId, SourceUniqueId) with an OUTPUT clause.
    /// Replaces the per-row existence-SELECT + INSERT/UPDATE loop. Lifecycle /
    /// tombstone-revive semantics are preserved EXACTLY — the per-row inputs that the
    /// old C# branched on (explicit IsActive present?, its value, the insert default)
    /// are carried into staging columns, and the MERGE reproduces the identical CASE
    /// logic against tgt.DeletedAt / tgt.LifecycleState.
    ///
    /// Allow-listed columns are written via bracket-quoted identifiers taken from the
    /// fixed server-side <see cref="WritableColumnList"/> — never caller input. Values
    /// flow only through the typed staging table (SqlBulkCopy), never concatenated into
    /// SQL, so the dynamic column list is injection-safe.
    ///
    /// Returns one <see cref="MergeOutputRow"/> per source row so the caller can derive
    /// per-item Created/Updated/Revived and resolve the ObjectId for the attribute flush.
    /// </summary>
    private async Task<IReadOnlyList<MergeOutputRow>> UpsertObjectsSetBasedAsync(
        SqlConnection conn, List<PreparedObject> prepared)
    {
        // ── Build the #StagingObjects temp table. Key + driver columns are fixed;
        // the allow-listed writable columns are appended as NVARCHAR(MAX) (the typed
        // Objects columns we touch are all nvarchar except none — typed numerics are
        // not in the allow-list) plus the IsActive bit drivers.
        var writableColDdl = string.Join(",\n                ",
            WritableColumnList.Select(c => $"{QuoteName(c)} NVARCHAR(MAX) NULL"));

        await conn.ExecuteAsync($@"
            IF OBJECT_ID('tempdb..#StagingObjects') IS NOT NULL DROP TABLE #StagingObjects;
            CREATE TABLE #StagingObjects (
                SourceConnectionId UNIQUEIDENTIFIER NOT NULL,
                SourceUniqueId NVARCHAR(450) NOT NULL,
                SourceType NVARCHAR(200) NOT NULL,
                ObjectClass NVARCHAR(200) NULL,
                OriginalSource NVARCHAR(450) NULL,
                HasOriginalSource BIT NOT NULL,
                InsertIsActive BIT NOT NULL,
                InsertLifecycleState INT NOT NULL,
                HasExplicitIsActive BIT NOT NULL,
                ExplicitIsActive BIT NOT NULL,
                {writableColDdl}
            )");

        // ── Build the DataTable once from the allow-list and bulk-copy in one shot.
        using (var table = new DataTable())
        {
            table.Columns.Add("SourceConnectionId", typeof(Guid));
            table.Columns.Add("SourceUniqueId", typeof(string));
            table.Columns.Add("SourceType", typeof(string));
            table.Columns.Add("ObjectClass", typeof(string));
            table.Columns.Add("OriginalSource", typeof(string));
            table.Columns.Add("HasOriginalSource", typeof(bool));
            table.Columns.Add("InsertIsActive", typeof(bool));
            table.Columns.Add("InsertLifecycleState", typeof(int));
            table.Columns.Add("HasExplicitIsActive", typeof(bool));
            table.Columns.Add("ExplicitIsActive", typeof(bool));
            foreach (var c in WritableColumnList)
                table.Columns.Add(c, typeof(string));

            foreach (var p in prepared)
            {
                var item = p.Item;
                var row = table.NewRow();
                row["SourceConnectionId"] = p.ConnectionId;
                row["SourceUniqueId"] = item.SourceUniqueId!;
                row["SourceType"] = item.Source!;
                row["ObjectClass"] = (object?)item.ObjectClass ?? DBNull.Value;

                var hasOrig = !string.IsNullOrWhiteSpace(item.OriginalSource);
                row["OriginalSource"] = hasOrig ? item.OriginalSource! : (object)DBNull.Value;
                row["HasOriginalSource"] = hasOrig;

                // IsActive / lifecycle drivers, computed per-row exactly as the old
                // InsertObjectAsync / UpdateObjectAsync did.
                var explicitRaw = LookupAttr(item.Attributes, "IsActive");
                var hasExplicit = !string.IsNullOrWhiteSpace(explicitRaw);
                var explicitVal = ParseBool(explicitRaw, defaultValue: true);
                row["HasExplicitIsActive"] = hasExplicit;
                row["ExplicitIsActive"] = explicitVal;
                // Insert default: IsActive=true unless an explicit IsActive=false; a new
                // present-but-disabled object is Disabled(1), otherwise Active(0).
                var insertIsActive = hasExplicit ? explicitVal : true;
                row["InsertIsActive"] = insertIsActive;
                row["InsertLifecycleState"] = insertIsActive ? 0 : 1;

                // Allow-listed typed columns from the attribute payload (last write wins
                // per name; only whitelisted keys; IsActive excluded above).
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in item.Attributes)
                {
                    if (string.Equals(key, "IsActive", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!WritableColumns.Contains(key)) continue;
                    if (!seen.Add(key)) continue;
                    // Resolve the canonical allow-list casing for the column name.
                    var col = WritableColumnList.First(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase));
                    row[col] = (object?)value ?? DBNull.Value;
                }

                table.Rows.Add(row);
            }

            using var bulkCopy = new SqlBulkCopy(conn)
            {
                DestinationTableName = "#StagingObjects",
                BatchSize = 5000,
                BulkCopyTimeout = 300
            };
            foreach (DataColumn dc in table.Columns)
                bulkCopy.ColumnMappings.Add(dc.ColumnName, dc.ColumnName);
            await bulkCopy.WriteToServerAsync(table);
        }

        // ── ONE MERGE. The writable allow-list drives both the UPDATE SET list and the
        // INSERT column/value lists, bracket-quoted. Lifecycle CASE logic mirrors the
        // old per-row code exactly:
        //   MATCHED + tgt tombstoned (DeletedAt NOT NULL) → REVIVE:
        //       DeletedAt=NULL; LifecycleState = explicit? (val?0:1) : 0; IsActive = explicit? val : 1
        //   MATCHED + present + explicit IsActive supplied → align lifecycle (preserve 2):
        //       LifecycleState = CASE WHEN tgt=2 THEN 2 ELSE (val?0:1) END; IsActive = val
        //   MATCHED + present + no explicit → IsActive/LifecycleState untouched
        //   NOT MATCHED → INSERT with the computed insert drivers.
        var updateSet = string.Join(",\n                ",
            WritableColumnList.Select(c => $"tgt.{QuoteName(c)} = src.{QuoteName(c)}"));
        var insertCols = string.Join(", ", WritableColumnList.Select(QuoteName));
        var insertVals = string.Join(", ", WritableColumnList.Select(c => $"src.{QuoteName(c)}"));

        var mergeSql = $@"
            DECLARE @MergeOut TABLE (
                Action NVARCHAR(10),
                ObjectId UNIQUEIDENTIFIER,
                SourceConnectionId UNIQUEIDENTIFIER,
                SourceUniqueId NVARCHAR(450),
                PriorDeletedAt DATETIME2 NULL
            );

            MERGE Objects AS tgt
            USING #StagingObjects AS src
               ON tgt.SourceConnectionId = src.SourceConnectionId
              AND tgt.SourceUniqueId = src.SourceUniqueId
            WHEN MATCHED THEN
                UPDATE SET
                    {updateSet},
                    tgt.OriginalSource = CASE WHEN src.HasOriginalSource = 1 THEN src.OriginalSource ELSE tgt.OriginalSource END,
                    tgt.ModifiedAt = SYSUTCDATETIME(),
                    tgt.LastSyncedAt = SYSUTCDATETIME(),
                    tgt.LastSeenAt = SYSUTCDATETIME(),
                    tgt.DeletedAt = CASE WHEN tgt.DeletedAt IS NOT NULL THEN NULL ELSE tgt.DeletedAt END,
                    tgt.LifecycleState =
                        CASE
                            WHEN tgt.DeletedAt IS NOT NULL THEN
                                CASE WHEN src.HasExplicitIsActive = 1 AND src.ExplicitIsActive = 0 THEN 1 ELSE 0 END
                            WHEN src.HasExplicitIsActive = 1 THEN
                                CASE WHEN tgt.LifecycleState = 2 THEN 2
                                     ELSE CASE WHEN src.ExplicitIsActive = 1 THEN 0 ELSE 1 END END
                            ELSE tgt.LifecycleState
                        END,
                    tgt.IsActive =
                        CASE
                            WHEN tgt.DeletedAt IS NOT NULL THEN
                                CASE WHEN src.HasExplicitIsActive = 1 THEN src.ExplicitIsActive ELSE 1 END
                            WHEN src.HasExplicitIsActive = 1 THEN src.ExplicitIsActive
                            ELSE tgt.IsActive
                        END
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (Id, SourceConnectionId, SourceUniqueId, SourceType, ObjectClass,
                        IsActive, LifecycleState, IsAuthoritative, MatchConfidence,
                        IsAdminSDHolder, PasswordNeverExpires, IsBuiltIn,
                        CreatedAt, ModifiedAt, FirstSyncedAt, LastSyncedAt, LastSeenAt,
                        OriginalSource{(insertCols.Length > 0 ? ", " + insertCols : "")})
                VALUES (NEWID(), src.SourceConnectionId, src.SourceUniqueId, src.SourceType, src.ObjectClass,
                        src.InsertIsActive, src.InsertLifecycleState, 0, 100,
                        0, 0, 0,
                        SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME(),
                        CASE WHEN src.HasOriginalSource = 1 THEN src.OriginalSource ELSE NULL END{(insertVals.Length > 0 ? ", " + insertVals : "")})
            OUTPUT $action, inserted.Id, inserted.SourceConnectionId, inserted.SourceUniqueId, deleted.DeletedAt
                INTO @MergeOut (Action, ObjectId, SourceConnectionId, SourceUniqueId, PriorDeletedAt);

            SELECT Action, ObjectId, SourceConnectionId, SourceUniqueId, PriorDeletedAt FROM @MergeOut;";

        var outRows = (await conn.QueryAsync<MergeOutputRow>(mergeSql, commandTimeout: 600)).ToList();

        await conn.ExecuteAsync("DROP TABLE IF EXISTS #StagingObjects");
        return outRows;
    }

    /// <summary>
    /// Bracket-quote a SQL identifier (QUOTENAME equivalent). Identifiers passed here
    /// come ONLY from the fixed server-side <see cref="WritableColumns"/> allow-list,
    /// never from caller input; the quoting is defense-in-depth.
    /// </summary>
    private static string QuoteName(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";

    /// <summary>
    /// Collect one object's NON-typed-column attributes (the ones that don't map to
    /// a writable Objects column) into the batch-wide staging list. Pure / no I/O —
    /// the accumulated rows are flushed set-based in <see cref="FlushAttributesAsync"/>.
    /// De-duped by name within the object (last write wins, matching the old
    /// per-object MERGE where a later key overwrote an earlier one).
    /// </summary>
    private static void CollectAttributes(
        List<(Guid ObjectId, string AttributeName, string? AttributeValue, string? DataType)> sink,
        Guid objectId,
        IReadOnlyDictionary<string, string?> attrs)
    {
        if (attrs.Count == 0) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in attrs)
        {
            // Skip keys we already wrote as typed columns.
            if (WritableColumns.Contains(key)) continue;
            if (!seen.Add(key)) continue;
            sink.Add((objectId, key, value, null));
        }
    }

    /// <summary>
    /// Flush ALL staged attribute rows for the batch in ONE SqlBulkCopy + ONE MERGE.
    /// Mirrors the proven internal path (SyncObjectRepository.FastBulkUpsertObjectsAsync):
    /// bulk-load into a #StagingAttrs temp table, then MERGE on (ObjectId, AttributeName).
    ///
    /// Matches the REAL ObjectAttributes schema EXACTLY:
    ///   (Id uniqueidentifier NOT NULL — NEWID() on insert, NO default),
    ///   (ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt).
    /// There is NO FirstSyncedAt column. The previous code inserted FirstSyncedAt and
    /// omitted Id, which threw "Invalid column name 'FirstSyncedAt'" and failed 100%
    /// of rows. SqlBulkCopy + the typed temp table are inherently injection-safe — no
    /// attribute name/value is ever concatenated into SQL.
    /// </summary>
    private async Task FlushAttributesAsync(
        SqlConnection conn,
        List<(Guid ObjectId, string AttributeName, string? AttributeValue, string? DataType)> attrRows)
    {
        if (attrRows.Count == 0) return;

        var now = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            IF OBJECT_ID('tempdb..#StagingAttrs') IS NOT NULL DROP TABLE #StagingAttrs;
            CREATE TABLE #StagingAttrs (
                ObjectId UNIQUEIDENTIFIER NOT NULL,
                AttributeName NVARCHAR(200) NOT NULL,
                AttributeValue NVARCHAR(MAX) NULL,
                DataType NVARCHAR(50) NULL,
                LastSyncedAt DATETIME2 NOT NULL
            )");

        using (var table = new DataTable())
        {
            table.Columns.Add("ObjectId", typeof(Guid));
            table.Columns.Add("AttributeName", typeof(string));
            table.Columns.Add("AttributeValue", typeof(string));
            table.Columns.Add("DataType", typeof(string));
            table.Columns.Add("LastSyncedAt", typeof(DateTime));
            foreach (var (objectId, name, value, dataType) in attrRows)
            {
                table.Rows.Add(
                    objectId,
                    name,
                    (object?)value ?? DBNull.Value,
                    (object?)dataType ?? DBNull.Value,
                    now);
            }

            using var bulkCopy = new SqlBulkCopy(conn)
            {
                DestinationTableName = "#StagingAttrs",
                BatchSize = 5000,
                BulkCopyTimeout = 300
            };
            bulkCopy.ColumnMappings.Add("ObjectId", "ObjectId");
            bulkCopy.ColumnMappings.Add("AttributeName", "AttributeName");
            bulkCopy.ColumnMappings.Add("AttributeValue", "AttributeValue");
            bulkCopy.ColumnMappings.Add("DataType", "DataType");
            bulkCopy.ColumnMappings.Add("LastSyncedAt", "LastSyncedAt");
            await bulkCopy.WriteToServerAsync(table);
        }

        await conn.ExecuteAsync(@"
            MERGE ObjectAttributes AS tgt
            USING #StagingAttrs AS src
               ON tgt.ObjectId = src.ObjectId AND tgt.AttributeName = src.AttributeName
            WHEN MATCHED THEN
                UPDATE SET AttributeValue = src.AttributeValue, LastSyncedAt = src.LastSyncedAt
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (Id, ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt)
                VALUES (NEWID(), src.ObjectId, src.AttributeName, src.AttributeValue, src.DataType, src.LastSyncedAt);",
            commandTimeout: 600);

        await conn.ExecuteAsync("DROP TABLE IF EXISTS #StagingAttrs");
    }

    /// <summary>
    /// Flush ALL staged audit rows for the batch in ONE set-based INSERT (Dapper
    /// expands the row list into a multi-VALUES statement). Best-effort: a failed
    /// audit write never fails the upsert. Columns match the working EF audit path
    /// (ChangeAuditLog.FromEntry): Timestamp, UserId, OperationType, EntityType,
    /// EntityId, Source, NewValue, Success. OperationType: Create=0, Update=1
    /// (a Revive is recorded as Update with action='Revived' in NewValue).
    /// </summary>
    private async Task FlushAuditAsync(SqlConnection conn, List<(Guid ObjectId, int OperationType, string NewValue)> auditRows)
    {
        if (auditRows.Count == 0) return;
        try
        {
            var rows = auditRows.Select(r => new
            {
                EntityId = r.ObjectId,
                OperationType = r.OperationType,
                NewValue = r.NewValue
            });
            await conn.ExecuteAsync(
                @"INSERT INTO ChangeAuditLogs (Timestamp, UserId, OperationType, EntityType, EntityId, Source, NewValue, Success)
                  VALUES (SYSUTCDATETIME(), 'Conduit', @OperationType, 'Object', @EntityId, 'Conduit-Bulk-API', @NewValue, 1)",
                rows);
        }
        catch (Exception ex)
        {
            // Audit is best-effort — never fail the upsert because the audit rows
            // didn't land. Log at Warning so a future schema divergence is visible.
            _logger.LogWarning("Batched audit write failed for {Count} rows (best-effort): {Error}", auditRows.Count, ex.Message);
        }
    }

    private static string? LookupAttr(IReadOnlyDictionary<string, string?> attrs, string key)
    {
        foreach (var (k, v) in attrs)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
        }
        return null;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        if (bool.TryParse(value, out var b)) return b;
        if (value == "1" || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (value == "0" || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)) return false;
        return defaultValue;
    }

    // ── Phase 7: AssignGroupOwner endpoint ──────────────────────────────────
    //
    // Conduit's AssignGroupOwner workflow step lands here. The "group" id is a
    // SourceUniqueId on an Objects row (typically the AD objectGUID or Entra
    // group id) — we resolve it to an Objects.Id, then write the owner into
    // the ObjectAttributes table under the canonical `managedBy` key. This
    // matches what the AD sink writes directly into LDAP and keeps the IC view
    // consistent with the directory view without needing a typed column.
    //
    // No write-back-to-AD echoing — see the file-level remarks for the bulk
    // endpoint reasoning. Conduit owns the directory write; IC absorbs.

    /// <summary>
    /// Assign the owner / managedBy on a Group Object. <paramref name="id"/> is
    /// the Objects row id; <paramref name="ownerExternalId"/> is the owner's
    /// SourceUniqueId / UPN / email. Group must have ObjectClass='group'.
    /// </summary>
    [HttpPatch("groups/{id:guid}/owner")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignGroupOwner(Guid id, [FromBody] AssignGroupOwnerRequest request)
    {
        if (id == Guid.Empty) return BadRequest(new { error = "id is required" });
        if (request is null || (request.OwnerIdentityId is null && string.IsNullOrWhiteSpace(request.OwnerExternalId)))
            return BadRequest(new { error = "ownerIdentityId or ownerExternalId is required" });

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var group = await conn.QuerySingleOrDefaultAsync<(Guid Id, string ObjectClass)>(
                "SELECT TOP 1 Id, ObjectClass FROM Objects WHERE Id = @Id",
                new { Id = id });
            if (group.Id == Guid.Empty) return NotFound(new { error = "Group object not found" });
            if (!string.Equals(group.ObjectClass, "group", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = $"Object {id} is not a group (ObjectClass='{group.ObjectClass}')." });

            // Resolve owner — prefer the typed identity id, fall back to UPN/email lookup.
            string ownerValue;
            if (request.OwnerIdentityId is not null)
            {
                ownerValue = request.OwnerIdentityId.Value.ToString();
            }
            else
            {
                ownerValue = request.OwnerExternalId!;
            }

            // Upsert the ObjectAttributes row keyed on (ObjectId, AttributeName='managedBy').
            await conn.ExecuteAsync(@"
                MERGE ObjectAttributes AS tgt
                USING (SELECT @ObjectId AS ObjectId, 'managedBy' AS AttributeName) AS src
                   ON tgt.ObjectId = src.ObjectId AND tgt.AttributeName = src.AttributeName
                WHEN MATCHED THEN
                    UPDATE SET AttributeValue = @Value, ModifiedAt = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (Id, ObjectId, AttributeName, AttributeValue, CreatedAt, ModifiedAt)
                    VALUES (NEWID(), @ObjectId, 'managedBy', @Value, SYSUTCDATETIME(), SYSUTCDATETIME());",
                new { ObjectId = group.Id, Value = ownerValue });

            _logger.LogInformation("API: Group {GroupId} owner set to {Owner}", group.Id, ownerValue);
            return Ok(new { groupId = group.Id, ownerExternalId = ownerValue });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign group owner on {Id}", id);
            return StatusCode(500, new { error = "Failed to assign group owner" });
        }
    }

    // ── Phase 2.2 Part B: group-membership ingest ───────────────────────────

    /// <summary>
    /// Bulk-ingest group membership edges pushed by Conduit. Resolves group and
    /// member SourceUniqueIds to Objects rows for the connection, then persists
    /// through the SAME repo primitive the internal sync uses
    /// (<c>BulkUpsertObjectGroupMembershipsAsync</c>) — no raw membership SQL in
    /// the controller. Idempotent. Unresolved ids (object not synced yet) are
    /// counted and skipped, never error the batch.
    /// </summary>
    [HttpPost("group-memberships/bulk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkGroupMemberships([FromBody] GroupMembershipBulkRequest request)
    {
        if (request is null || request.Memberships is null || request.Memberships.Count == 0)
            return BadRequest(new { error = "Memberships is required and must be non-empty" });
        if (string.IsNullOrWhiteSpace(request.Source) || !SourceNamePattern.IsMatch(request.Source))
            return BadRequest(new { error = "A valid Source is required" });
        if (request.Memberships.Count > 1000)
            return BadRequest(new { error = "Maximum 1000 membership edges per request" });

        try
        {
            var connectionId = await ResolveConnectionIdAsync(request.Source);
            if (connectionId == Guid.Empty)
                return BadRequest(new { error = $"No active DirectoryConnection for Source '{request.Source}'" });

            // Collect all distinct external ids (groups + members) in one set so
            // we resolve them to Objects.Id in a single repo round-trip each.
            var groupIds = request.Memberships
                .Select(m => m.GroupSourceUniqueId)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var memberIds = request.Memberships
                .SelectMany(m => m.MemberSourceUniqueIds ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var groupMap = groupIds.Count > 0
                ? await _syncObjectRepository.GetObjectIdsBySourceUniqueIdsAsync(connectionId, groupIds)
                : new Dictionary<string, Guid>();
            var memberMap = memberIds.Count > 0
                ? await _syncObjectRepository.GetObjectIdsBySourceUniqueIdsAsync(connectionId, memberIds)
                : new Dictionary<string, Guid>();

            var edges = new List<(Guid ObjectId, Guid GroupId, bool IsDirect, bool IsPrimary)>();
            int groupsResolved = 0, groupsUnresolved = 0, membersResolved = 0, membersUnresolved = 0;

            foreach (var m in request.Memberships)
            {
                if (!groupMap.TryGetValue(m.GroupSourceUniqueId, out var gId))
                {
                    groupsUnresolved++;
                    continue;
                }
                groupsResolved++;

                foreach (var memberExtId in m.MemberSourceUniqueIds ?? Array.Empty<string>())
                {
                    if (memberMap.TryGetValue(memberExtId, out var oId))
                    {
                        edges.Add((ObjectId: oId, GroupId: gId, IsDirect: true, IsPrimary: false));
                        membersResolved++;
                    }
                    else
                    {
                        membersUnresolved++;
                    }
                }
            }

            // De-dup the same way the orchestrator does before upsert.
            var deduped = edges
                .GroupBy(e => (e.ObjectId, e.GroupId))
                .Select(g => g.First())
                .ToList();

            int persisted = 0;
            if (deduped.Count > 0)
                persisted = await _syncObjectRepository.BulkUpsertObjectGroupMembershipsAsync(deduped);

            _logger.LogInformation(
                "API: group-membership batch {BatchId} for '{Source}' — groups {GR}/{GU}, members {MR}/{MU}, persisted {P}",
                request.BatchId, request.Source, groupsResolved, groupsUnresolved, membersResolved, membersUnresolved, persisted);

            return Ok(new GroupMembershipBulkResponse
            {
                BatchId = request.BatchId,
                GroupsResolved = groupsResolved,
                GroupsUnresolved = groupsUnresolved,
                MembersResolved = membersResolved,
                MembersUnresolved = membersUnresolved,
                EdgesPersisted = persisted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: group-membership batch {BatchId} failed", request.BatchId);
            return StatusCode(500, new { error = "Group membership ingest failed", batchId = request.BatchId });
        }
    }

    // ── Phase 2.2 Part C: tombstone soft-delete (DESTRUCTIVE — guardrailed) ──

    /// <summary>
    /// Soft-delete (set DeletedAt) the Objects rows whose SourceUniqueIds Conduit
    /// detected as absent from a COMPLETE source read. NEVER hard-deletes;
    /// reversible (a later bulk upsert of the same id clears DeletedAt). Strictly
    /// scoped to the resolved connection — no cross-connection bleed.
    ///
    /// SAFETY CAP: if the batch would soft-delete more than 50% of the
    /// connection's currently-live objects, the delete portion is ABORTED and
    /// Aborted=true is returned, UNLESS Override=true. This stops a truncated /
    /// mis-computed Conduit delta from wiping a connection.
    /// </summary>
    [HttpPost("tombstones")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Tombstones([FromBody] TombstoneRequest request)
    {
        if (request is null || request.SourceUniqueIds is null || request.SourceUniqueIds.Count == 0)
            return BadRequest(new { error = "SourceUniqueIds is required and must be non-empty" });
        if (string.IsNullOrWhiteSpace(request.Source) || !SourceNamePattern.IsMatch(request.Source))
            return BadRequest(new { error = "A valid Source is required" });
        if (request.SourceUniqueIds.Count > 1000)
            return BadRequest(new { error = "Maximum 1000 tombstones per request" });

        try
        {
            var connectionId = await ResolveConnectionIdAsync(request.Source);
            if (connectionId == Guid.Empty)
                return BadRequest(new { error = $"No active DirectoryConnection for Source '{request.Source}'" });

            var distinctIds = request.SourceUniqueIds
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Count currently-live objects for THIS connection (the cap denominator).
            var liveBefore = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM Objects
                  WHERE SourceConnectionId = @connectionId AND DeletedAt IS NULL",
                new { connectionId });

            // How many of the requested ids actually match a live row for THIS
            // connection. The cap is measured on what we would REALLY delete, not
            // on the raw request count (so unknown ids don't inflate the ratio).
            var matched = (await conn.QueryAsync<Guid>(
                @"SELECT Id FROM Objects
                  WHERE SourceConnectionId = @connectionId
                    AND DeletedAt IS NULL
                    AND SourceUniqueId IN @ids",
                new { connectionId, ids = distinctIds })).ToList();

            // 50% cap. Use integer math carefully: abort when matched*2 > liveBefore.
            // (i.e. matched > 50% of live). Equality at exactly 50% is allowed.
            var capTripped = liveBefore > 0 && (long)matched.Count * 2 > liveBefore;

            if (capTripped && !request.Override)
            {
                _logger.LogWarning(
                    "API: tombstone batch {BatchId} for '{Source}' ABORTED by 50% cap — would delete {Matched} of {Live} live (Override required)",
                    request.BatchId, request.Source, matched.Count, liveBefore);

                return Ok(new TombstoneResponse
                {
                    BatchId = request.BatchId,
                    Aborted = true,
                    AbortReason = $"Would soft-delete {matched.Count} of {liveBefore} live objects (>50%). Set Override=true to proceed.",
                    LiveBefore = liveBefore,
                    Requested = distinctIds.Count,
                    Matched = matched.Count,
                    SoftDeleted = 0
                });
            }

            int softDeleted = 0;
            bool deprovisioned = false;
            if (matched.Count > 0)
            {
                // THE DEPROVISIONING POLICY GATE. A gone-from-source object is, by the
                // ARS model, a candidate for EITHER Disabled(1, retained indefinitely)
                // OR Deprovisioned(2, retention clock armed). Which one is a POLICY
                // decision (DeprovisioningPolicy, IC-only governance). Default (policy
                // OFF, or it does not cover Objects, or the gone-from-source criterion
                // is off): DISABLE-AND-RETAIN -- state 1, DeletedAt stays NULL, so the
                // purge job (which only ever targets state 2) can NEVER touch it.
                // Promote to Deprovisioned(2) + arm the clock ONLY when the policy is
                // enabled, covers Objects, and the gone-from-source criterion qualifies.
                var policy = await DataAccessLibrary.Lifecycle.DeprovisioningPolicy.LoadAsync(conn);
                deprovisioned = policy.CoversObjects && policy.GoneFromSourceQualifies;

                // The SourceConnectionId guard in the WHERE is the cross-connection
                // bleed defense — even if an id collided across connections, only this
                // connection's row is touched. Only matched, this-connection, not-
                // already-deleted rows are affected. A revive (bulk upsert of the same
                // id) resets DeletedAt=NULL and LifecycleState=0 before any purge.
                if (deprovisioned)
                {
                    // DEPROVISION: stamp DeletedAt (the retention clock = ARS
                    // edsvaDeprovisionDate) + LifecycleState=2. The daily
                    // ObjectDeprovisionPurgeJob hard-deletes rows still at state 2 once
                    // DeletedAt is older than the global retention window.
                    softDeleted = await conn.ExecuteAsync(
                        @"UPDATE Objects
                          SET DeletedAt = SYSUTCDATETIME(),
                              ModifiedAt = SYSUTCDATETIME(),
                              IsActive = 0,
                              LifecycleState = 2
                          WHERE SourceConnectionId = @connectionId
                            AND DeletedAt IS NULL
                            AND Id IN @ids",
                        new { connectionId, ids = matched });
                }
                else
                {
                    // DISABLE-AND-RETAIN: gone-from-source but the policy does not opt
                    // in to deprovision. LifecycleState=1 (Disabled), DeletedAt stays
                    // NULL -- retained indefinitely, NEVER on the purge clock. We still
                    // stamp DeletedAt? NO: a Disabled row must have DeletedAt NULL so it
                    // is excluded by BOTH the state filter AND the DeletedAt filter in
                    // the purge. IsActive=0 reflects that it is gone from source. Guard
                    // is "currently Active(0)" so an already-Disabled or already-
                    // Deprovisioned row is not reclassified.
                    softDeleted = await conn.ExecuteAsync(
                        @"UPDATE Objects
                          SET ModifiedAt = SYSUTCDATETIME(),
                              IsActive = 0,
                              LifecycleState = 1
                          WHERE SourceConnectionId = @connectionId
                            AND DeletedAt IS NULL
                            AND LifecycleState = 0
                            AND Id IN @ids",
                        new { connectionId, ids = matched });
                }

                // Audit every transition to ChangeAuditLogs (OperationType 2 = Delete).
                await WriteTombstoneAuditAsync(conn, matched, request.Source, deprovisioned);
            }

            _logger.LogInformation(
                "API: tombstone batch {BatchId} for '{Source}' — requested {Req}, matched {Matched}, transitioned {Del} to {State} (live before {Live}, override={Ovr})",
                request.BatchId, request.Source, distinctIds.Count, matched.Count, softDeleted,
                deprovisioned ? "Deprovisioned(2)+clock" : "Disabled(1)/retained", liveBefore, request.Override);

            return Ok(new TombstoneResponse
            {
                BatchId = request.BatchId,
                Aborted = false,
                LiveBefore = liveBefore,
                Requested = distinctIds.Count,
                Matched = matched.Count,
                SoftDeleted = softDeleted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: tombstone batch {BatchId} failed", request.BatchId);
            return StatusCode(500, new { error = "Tombstone processing failed", batchId = request.BatchId });
        }
    }

    // ── Phase 2.2 Part D: manual post-process trigger ───────────────────────

    /// <summary>
    /// Manually enqueue post-processing (person-match + manager resolution) for a
    /// connection. Bulk upsert auto-enqueues this already; this endpoint exists
    /// for operators / re-runs. Non-blocking — returns once enqueued.
    /// </summary>
    [HttpPost("post-process")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PostProcess([FromBody] PostProcessRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Source) || !SourceNamePattern.IsMatch(request.Source))
            return BadRequest(new { error = "A valid Source is required" });

        var connectionId = await ResolveConnectionIdAsync(request.Source);
        if (connectionId == Guid.Empty)
            return BadRequest(new { error = $"No active DirectoryConnection for Source '{request.Source}'" });

        var tenantConn = DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve();
        var enqueued = _postProcessQueue.Enqueue(connectionId, request.RunPersonMatch, request.RunManagerResolution, tenantConn);
        return Ok(new PostProcessResponse
        {
            Enqueued = enqueued,
            ConnectionId = connectionId,
            Message = enqueued
                ? "Post-processing enqueued."
                : "A post-processing pass for this connection is already pending; coalesced."
        });
    }

    // ── Shared helpers for the Phase 2.2 endpoints ──────────────────────────

    /// <summary>
    /// Resolve a Source string to a DirectoryConnection id. Same precedence as the
    /// bulk endpoint: match by Name first (auto-seed sets Name=source), fall back
    /// to ConnectionType. Returns Guid.Empty when nothing active matches.
    /// </summary>
    private async Task<Guid> ResolveConnectionIdAsync(string source)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var byName = await conn.ExecuteScalarAsync<Guid?>(
            @"SELECT TOP 1 Id FROM DirectoryConnections
              WHERE [Name] = @source AND IsActive = 1
              ORDER BY CreatedAt ASC",
            new { source });
        if (byName.HasValue && byName.Value != Guid.Empty) return byName.Value;

        var byType = await conn.ExecuteScalarAsync<Guid?>(
            @"SELECT TOP 1 Id FROM DirectoryConnections
              WHERE ConnectionType = @source AND IsActive = 1
              ORDER BY CreatedAt ASC",
            new { source });
        return byType ?? Guid.Empty;
    }

    /// <summary>Audit one ChangeAuditLogs Delete row per tombstoned object. Records
    /// whether the policy gate promoted the row to Deprovisioned(2)+clock or merely
    /// Disabled(1)/retained, so the policy-driven decision is auditable.</summary>
    private async Task WriteTombstoneAuditAsync(SqlConnection conn, List<Guid> objectIds, string source, bool deprovisioned)
    {
        try
        {
            // OperationType 2 = Delete (ChangeOperationType). Source-tagged so
            // tombstone deletes are filterable + reversible decisions are auditable.
            var action = deprovisioned ? "Deprovision" : "Disable";
            var reason = deprovisioned
                ? "Tombstone -> Deprovisioned(2) by deprovisioning policy"
                : "Tombstone -> Disabled(1)/retained (deprovisioning policy off/not covered)";
            var rows = objectIds.Select(id => new
            {
                EntityId = id,
                NewValue = JsonSerializer.Serialize(new { Action = action, Source = source, Reason = reason })
            });
            await conn.ExecuteAsync(
                @"INSERT INTO ChangeAuditLogs (Timestamp, UserId, OperationType, EntityType, EntityId, Source, NewValue, Success)
                  VALUES (SYSUTCDATETIME(), 'Conduit', 2, 'Object', @EntityId, 'Conduit-Tombstone', @NewValue, 1)",
                rows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Tombstone audit write failed (best-effort): {Error}", ex.Message);
        }
    }
}
