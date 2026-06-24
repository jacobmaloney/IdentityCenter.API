using Dapper;
using DataAccessLibrary.Models;
using IdentityCenter.API.Models;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// Public API for the Identities table — the people / HR-side identity records
/// that may have one or more <c>Objects</c> rows attached to them.
/// </summary>
[ApiController]
[Route("api/identities")]
[Authorize(Policy = "TenantDataPolicy")]
public class IdentitiesController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IGlobalLogger _logger;
    private readonly string _defaultConnectionString;

    public IdentitiesController(IConfiguration configuration, IGlobalLogger logger)
    {
        _configuration = configuration;
        _logger = logger;
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    /// <summary>
    /// Connection string for THIS request. Routes through the ambient
    /// <see cref="DataAccessLibrary.ControlPlane.TenantConnectionAccessor"/> so a tenant-scoped request
    /// hits ONLY its own DB; falls back to DefaultConnection for legacy/admin. Resolved per access.
    /// </summary>
    private string _connectionString =>
        DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

    /// <summary>
    /// Creates a new Identity record (a person, not a directory account).
    /// </summary>
    /// <remarks>
    /// firstName, lastName and email are required. Email must be unique.
    /// Returns 201 with the new identity ID. 400 with field errors on validation failure.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateIdentity([FromBody] CreateIdentityRequest request)
    {
        var errors = ValidateCreate(request);
        if (errors.Count > 0)
            return BadRequest(new { error = "Validation failed", fields = errors });

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Email uniqueness check.
            var existing = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Identities WHERE PrimaryEmail = @Email",
                new { Email = request.Email });
            if (existing > 0)
                return BadRequest(new
                {
                    error = "Validation failed",
                    fields = new Dictionary<string, string> { ["email"] = "An identity with this email already exists." }
                });

            var id = Guid.NewGuid();
            var displayName = string.Concat(request.FirstName, " ", request.LastName).Trim();

            await conn.ExecuteAsync(
                @"INSERT INTO Identities (Id, DisplayName, FirstName, LastName, PrimaryEmail,
                                          Department, JobTitle, EmployeeId, ManagerIdentityId, HireDate,
                                          Status, IsActive, CreatedAt, ModifiedAt)
                  VALUES (@Id, @DisplayName, @FirstName, @LastName, @Email,
                          @Department, @JobTitle, @EmployeeId, @ManagerId, @HireDate,
                          'Active', 1, SYSUTCDATETIME(), SYSUTCDATETIME())",
                new
                {
                    Id = id,
                    DisplayName = displayName,
                    request.FirstName,
                    request.LastName,
                    request.Email,
                    request.Department,
                    request.JobTitle,
                    request.EmployeeId,
                    request.ManagerId,
                    HireDate = request.StartDate
                });

            _logger.LogInformation("API: Identity created {Id} ({Email})", id, request.Email);

            return CreatedAtAction(
                nameof(GetIdentity),
                new { id },
                new { identityId = id, status = "Created" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create identity for {Email}", request.Email);
            return StatusCode(500, new { error = "Failed to create identity" });
        }
    }

    /// <summary>
    /// Partial update of an Identity. Only fields supplied in the body are written.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateIdentity(Guid id, [FromBody] UpdateIdentityRequest request)
    {
        if (id == Guid.Empty)
            return BadRequest(new { error = "id is required" });

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Identities WHERE Id = @Id", new { Id = id });
            if (exists == 0)
                return NotFound(new { error = "Identity not found" });

            // Build the SET clause from non-null fields only — partial update semantics.
            var sets = new List<string>();
            var args = new DynamicParameters();
            args.Add("@Id", id);

            void Add(string column, string property, object? value)
            {
                if (value == null) return;
                sets.Add($"{column} = @{property}");
                args.Add($"@{property}", value);
            }

            Add("FirstName", "FirstName", request.FirstName);
            Add("LastName", "LastName", request.LastName);
            Add("PrimaryEmail", "Email", request.Email);
            Add("Department", "Department", request.Department);
            Add("JobTitle", "JobTitle", request.JobTitle);
            Add("Status", "Status", request.Status);
            Add("ManagerIdentityId", "ManagerId", request.ManagerId);
            Add("MobilePhone", "MobilePhone", request.MobilePhone);
            Add("Office", "Office", request.Office);

            if (sets.Count == 0)
                return Ok(new { identityId = id, updatedFields = Array.Empty<string>() });

            // Refresh the denormalized DisplayName when name fields change.
            if (request.FirstName != null || request.LastName != null)
            {
                sets.Add(@"DisplayName = LTRIM(RTRIM(
                    CONCAT(COALESCE(@FirstName, FirstName), ' ', COALESCE(@LastName, LastName))))");
            }

            sets.Add("ModifiedAt = SYSUTCDATETIME()");

            var sql = $"UPDATE Identities SET {string.Join(", ", sets)} WHERE Id = @Id";
            await conn.ExecuteAsync(sql, args);

            var updatedFields = sets
                .Where(s => !s.StartsWith("ModifiedAt") && !s.StartsWith("DisplayName"))
                .Select(s => s.Split('=')[0].Trim())
                .ToArray();

            _logger.LogInformation("API: Identity {Id} updated ({Fields})", id, string.Join(",", updatedFields));

            return Ok(new { identityId = id, updatedFields });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update identity {Id}", id);
            return StatusCode(500, new { error = "Failed to update identity" });
        }
    }

    /// <summary>
    /// Marks an Identity as Inactive. Triggers downstream offboarding work via
    /// the existing lifecycle rules already wired into the Identity table.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateIdentity(Guid id, [FromBody] DeactivateIdentityRequest? request)
    {
        if (id == Guid.Empty)
            return BadRequest(new { error = "id is required" });

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Identities WHERE Id = @Id", new { Id = id });
            if (exists == 0)
                return NotFound(new { error = "Identity not found" });

            var effectiveDate = request?.EffectiveDate ?? DateTime.UtcNow;

            // No TerminationReason column on Identities — store the reason in
            // Description (a free-text field) so the deactivation justification is
            // not lost. Also flips IsActive to keep both new and legacy queries
            // (Status filter / IsActive filter) honest.
            var reason = request?.Reason;
            await conn.ExecuteAsync(
                @"UPDATE Identities
                  SET Status = 'Inactive',
                      IsActive = 0,
                      TerminationDate = @EffectiveDate,
                      Description = CASE WHEN @Reason IS NULL THEN Description
                                         ELSE CONCAT(COALESCE(Description + N' | ', N''), N'Deactivated: ', @Reason) END,
                      ModifiedAt = SYSUTCDATETIME()
                  WHERE Id = @Id",
                new { Id = id, EffectiveDate = effectiveDate, Reason = reason });

            _logger.LogInformation("API: Identity {Id} deactivated effective {Date}", id, effectiveDate);

            return Ok(new { status = "Deactivated", effectiveDate });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate identity {Id}", id);
            return StatusCode(500, new { error = "Failed to deactivate identity" });
        }
    }

    /// <summary>
    /// Single-identity lookup used by callers reading back from a POST response.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIdentity(Guid id)
    {
        using var conn = new SqlConnection(_connectionString);
        var identity = await conn.QuerySingleOrDefaultAsync<Identity>(
            "SELECT * FROM Identities WHERE Id = @Id", new { Id = id });
        if (identity == null) return NotFound(new { error = "Identity not found" });
        return Ok(identity);
    }

    /// <summary>
    /// Paged search of Identities. Supports filtering by department and status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListIdentities(
        [FromQuery] string? department = null,
        [FromQuery] string? status = "Active",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        using var conn = new SqlConnection(_connectionString);

        var where = new List<string>();
        var args = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(department))
        {
            where.Add("Department = @Department");
            args.Add("@Department", department);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            where.Add("Status = @Status");
            args.Add("@Status", status);
        }
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM Identities {whereClause}", args);

        args.Add("@Offset", (page - 1) * pageSize);
        args.Add("@PageSize", pageSize);

        var rows = await conn.QueryAsync<Identity>(
            $@"SELECT * FROM Identities {whereClause}
               ORDER BY DisplayName, LastName, FirstName
               OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", args);

        return Ok(new
        {
            data = rows,
            page,
            pageSize,
            total
        });
    }

    private static Dictionary<string, string> ValidateCreate(CreateIdentityRequest request)
    {
        var errors = new Dictionary<string, string>();
        if (request == null)
        {
            errors["body"] = "Request body is required.";
            return errors;
        }
        if (string.IsNullOrWhiteSpace(request.FirstName))
            errors["firstName"] = "firstName is required.";
        if (string.IsNullOrWhiteSpace(request.LastName))
            errors["lastName"] = "lastName is required.";
        if (string.IsNullOrWhiteSpace(request.Email))
            errors["email"] = "email is required.";
        else if (!request.Email.Contains('@'))
            errors["email"] = "email must contain '@'.";
        return errors;
    }

    // ── Phase 7 person-aware endpoints ──────────────────────────────────────
    //
    // These three endpoints exist solely to power Conduit's WorkflowStep router
    // when an IC tenant is wired as a sync sink. They do NOT echo to AD/Entra
    // (Conduit owns directory writes) — they mutate IC's Identities table only.
    //
    // Auth is the same X-API-Key + AdminPolicy gate the rest of this controller
    // sits behind. Bulk operations are intentionally NOT supported here; the
    // orchestrator loops per object because each match decision is independent
    // and worth one round trip for governance auditability.

    /// <summary>
    /// Probe for an existing Identity that matches the supplied candidate keys.
    /// Conduit's PersonMatch step calls this for every inbound object.
    /// </summary>
    [HttpPost("match")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MatchIdentity([FromBody] MatchIdentityRequest request)
    {
        if (request?.CandidateKeys is null)
            return BadRequest(new { error = "candidateKeys is required" });

        var keys = request.CandidateKeys;
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Strongest match first — exact, case-insensitive against typed columns.
            // Sql Server's default collation is CI by default; relying on that here.
            Guid? matchedId;
            string? matchedBy = null;
            double confidence = 0;

            // 0) Directory key bridge. Conduit's Lookup/PersonMatch steps hand over the
            //    object's own SourceUniqueId (AD objectGUID / Entra id) — a deterministic
            //    directory key, not a person attribute. It lives on the Objects row, which
            //    (once correlated) carries IdentityId. Bridge through it so an AD-sourced
            //    object whose only id is an objectGUID still resolves to its person. A DN
            //    is accepted on the same bridge (Objects.DN). DeletedAt IS NULL so a
            //    soft-deleted object never produces a phantom person match.
            if (!string.IsNullOrWhiteSpace(request.SourceUniqueId))
            {
                matchedId = await conn.ExecuteScalarAsync<Guid?>(
                    @"SELECT TOP 1 IdentityId FROM Objects
                      WHERE (SourceUniqueId = @Sid OR DN = @Sid)
                        AND IdentityId IS NOT NULL AND DeletedAt IS NULL",
                    new { Sid = request.SourceUniqueId });
                if (matchedId is not null) { matchedBy = "sourceUniqueId"; confidence = 1.0; goto done; }
            }

            if (!string.IsNullOrWhiteSpace(keys.Upn))
            {
                matchedId = await conn.ExecuteScalarAsync<Guid?>(
                    "SELECT TOP 1 Id FROM Identities WHERE PrimaryEmail = @Upn", new { Upn = keys.Upn });
                if (matchedId is not null) { matchedBy = "upn"; confidence = 1.0; goto done; }
            }
            if (!string.IsNullOrWhiteSpace(keys.Email))
            {
                matchedId = await conn.ExecuteScalarAsync<Guid?>(
                    "SELECT TOP 1 Id FROM Identities WHERE PrimaryEmail = @Email", new { Email = keys.Email });
                if (matchedId is not null) { matchedBy = "email"; confidence = 0.95; goto done; }
            }
            if (!string.IsNullOrWhiteSpace(keys.EmployeeId))
            {
                matchedId = await conn.ExecuteScalarAsync<Guid?>(
                    "SELECT TOP 1 Id FROM Identities WHERE EmployeeId = @EmployeeId", new { keys.EmployeeId });
                if (matchedId is not null) { matchedBy = "employeeId"; confidence = 0.9; goto done; }
            }
            if (!string.IsNullOrWhiteSpace(keys.Username))
            {
                matchedId = await conn.ExecuteScalarAsync<Guid?>(
                    "SELECT TOP 1 Id FROM Identities WHERE Username = @Username OR UserPrincipalName = @Username",
                    new { keys.Username });
                if (matchedId is not null) { matchedBy = "username"; confidence = 0.85; goto done; }
            }
            if (!string.IsNullOrWhiteSpace(keys.FirstName) && !string.IsNullOrWhiteSpace(keys.LastName))
            {
                matchedId = await conn.ExecuteScalarAsync<Guid?>(
                    "SELECT TOP 1 Id FROM Identities WHERE FirstName = @FirstName AND LastName = @LastName",
                    new { keys.FirstName, keys.LastName });
                if (matchedId is not null) { matchedBy = "name"; confidence = 0.5; goto done; }
            }

            return Ok(new MatchIdentityResponse { Matched = false });

        done:
            return Ok(new MatchIdentityResponse
            {
                Matched = true,
                IdentityId = matchedId,
                MatchedBy = matchedBy,
                Confidence = confidence
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Identity match probe failed");
            return StatusCode(500, new { error = "Match probe failed", detail = ex.Message });
        }
    }

    /// <summary>
    /// Patch the manager link on an existing Identity. The caller may identify
    /// the manager by IC Identity GUID OR by external id (UPN / email).
    /// </summary>
    [HttpPatch("{id:guid}/manager")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignManager(Guid id, [FromBody] AssignManagerRequest request)
    {
        if (id == Guid.Empty) return BadRequest(new { error = "id is required" });
        if (request is null || (request.ManagerIdentityId is null && string.IsNullOrWhiteSpace(request.ManagerExternalId)))
            return BadRequest(new { error = "managerIdentityId or managerExternalId is required" });

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Identities WHERE Id = @Id", new { Id = id });
            if (exists == 0) return NotFound(new { error = "Identity not found" });

            Guid? managerId = request.ManagerIdentityId;
            if (managerId is null)
            {
                managerId = await ResolveManagerIdentityIdAsync(conn, request.ManagerExternalId!);
                if (managerId is null)
                    return NotFound(new { error = $"Manager '{request.ManagerExternalId}' not found among Identities." });
            }

            await conn.ExecuteAsync(
                "UPDATE Identities SET ManagerIdentityId = @ManagerId, ModifiedAt = SYSUTCDATETIME() WHERE Id = @Id",
                new { Id = id, ManagerId = managerId });

            _logger.LogInformation("API: Identity {Id} manager assigned to {ManagerId}", id, managerId);
            return Ok(new { identityId = id, managerIdentityId = managerId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign manager for {Id}", id);
            return StatusCode(500, new { error = "Failed to assign manager" });
        }
    }

    /// <summary>
    /// Resolve a manager *external id* (as Conduit's Lookup step hands it over) to an
    /// IC Identity GUID. The reference shape depends on the source directory:
    /// <list type="bullet">
    ///   <item>Entra ID / email-keyed sources emit a UPN or SMTP address → matches
    ///   <c>Identities.PrimaryEmail</c> / <c>SecondaryEmail</c> / <c>UserPrincipalName</c>.</item>
    ///   <item>Active Directory emits the <c>manager</c> attribute as a DISTINGUISHED NAME.
    ///   A DN matches none of the Identities email/UPN columns, so we bridge through the
    ///   <c>Objects</c> table: <c>Objects.DN = &lt;dn&gt;</c> → <c>Objects.IdentityId</c>
    ///   (the manager object's already-correlated person). This is what makes AD manager
    ///   resolution work the same way cloud sources already do.</item>
    ///   <item>sAMAccountName (bare account name) → <c>Identities.Username</c>.</item>
    /// </list>
    /// Every probe is an exact, parameterized, case-insensitive (server collation)
    /// equality match — no caller string ever influences SQL structure. Returns null
    /// when nothing matches so the caller can answer 404 (a truthful "unresolved",
    /// never a silent wrong link). Probes run strongest-key-first and short-circuit.
    /// </summary>
    private static async Task<Guid?> ResolveManagerIdentityIdAsync(SqlConnection conn, string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return null;

        // 1) Email / UPN columns on Identities (covers Entra UPN + SMTP references).
        var byEmailOrUpn = await conn.ExecuteScalarAsync<Guid?>(
            @"SELECT TOP 1 Id FROM Identities
              WHERE PrimaryEmail = @Ext OR SecondaryEmail = @Ext OR UserPrincipalName = @Ext",
            new { Ext = externalId });
        if (byEmailOrUpn is not null) return byEmailOrUpn;

        // 2) DN bridge: AD `manager` is a DN. The manager's directory Object carries
        //    that DN and (once person-matched) an IdentityId. Resolve through it.
        //    DeletedAt IS NULL: never link a manager via a soft-deleted object.
        if (externalId.Contains('=') && externalId.Contains(','))
        {
            var byDn = await conn.ExecuteScalarAsync<Guid?>(
                @"SELECT TOP 1 IdentityId FROM Objects
                  WHERE DN = @Ext AND IdentityId IS NOT NULL AND DeletedAt IS NULL",
                new { Ext = externalId });
            if (byDn is not null) return byDn;
        }

        // 3) sAMAccountName / bare account name → Identities.Username.
        var byUsername = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT TOP 1 Id FROM Identities WHERE Username = @Ext",
            new { Ext = externalId });
        if (byUsername is not null) return byUsername;

        // 4) Last resort: the manager's directory Object matched by its own source
        //    unique id (objectGUID) — covers a Conduit projection that hands the
        //    manager's resolvable key instead of a raw DN. DeletedAt IS NULL.
        var bySourceUniqueId = await conn.ExecuteScalarAsync<Guid?>(
            @"SELECT TOP 1 IdentityId FROM Objects
              WHERE SourceUniqueId = @Ext AND IdentityId IS NOT NULL AND DeletedAt IS NULL",
            new { Ext = externalId });
        return bySourceUniqueId;
    }

    // ── Table-to-table connector endpoints (Conduit IdentityCenter, table=Identities) ──
    //
    // These two endpoints are the Identities-table mirror of ObjectsController's
    // /query + /bulk. They let the Conduit IdentityCenter connector SOURCE from
    // and SINK into the Identities (people) table — enabling HR→IC/Identities and
    // IC/Identities→IC/Objects table-to-table syncs.
    //
    // LOCKED BOUNDARY (do not cross): RAW, DETERMINISTIC, field-mapped movement
    // ONLY. The upsert finds an existing row by an EXACT equality match on ONE
    // allow-listed deterministic key column (EmployeeId / UserPrincipalName /
    // Username / PrimaryEmail) and writes the mapped typed columns. It NEVER calls
    // PersonMatchOrchestrator and performs NO fuzzy object↔person correlation —
    // that governance stays inside IC's internal sync. (The separate /match
    // endpoint above is the correlation helper; these endpoints never invoke it.)
    //
    // Auth + tenant isolation are inherited from the controller: [Authorize(Policy
    // = "TenantDataPolicy")] + the ambient _connectionString that routes a
    // tenant-scoped key to ONLY that tenant's DB. No write-back to AD/Entra —
    // Conduit owns the directory write; IC absorbs the projection.

    // Allow-list of deterministic key columns a caller may match on. Maps the
    // caller's lowercase KeyField token → the real Identities column name. This is
    // the ONLY place a caller-supplied "column" influences SQL, and it can never
    // be anything other than one of these four — no injection surface.
    private static readonly Dictionary<string, string> KeyFieldColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["employeeId"]        = "EmployeeId",
        ["userPrincipalName"] = "UserPrincipalName",
        ["username"]          = "Username",
        ["email"]             = "PrimaryEmail",
    };

    // Allow-list of Identities typed columns a bulk upsert may write. Keeps the
    // raw-Dapper path safe (no caller-controlled column names land in SQL). Only
    // the deterministic, field-mapped, non-governance columns — never IDs,
    // lifecycle/governance/risk state, audit, or correlation columns.
    private static readonly HashSet<string> IdentityWritableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "DisplayName", "FirstName", "LastName", "MiddleName", "PreferredName",
        "PrimaryEmail", "SecondaryEmail", "PrimaryPhone", "MobilePhone", "HomePhone", "Fax",
        "Department", "JobTitle", "JobCode", "JobFamily", "EmployeeId", "EmployeeType",
        "Division", "Company", "Office", "Building", "Floor", "Room",
        "CostCenter", "ProfitCenter", "Organization", "BusinessUnit", "LegalEntity",
        "Region", "Site", "ContractType", "WorkSchedule", "PayGrade",
        "StreetAddress", "City", "State", "PostalCode", "Country",
        "Username", "UserPrincipalName", "Status",
        "CentralId", "ManagerEmployeeId", "ManagerDisplayName",
        "Sponsor", "SponsorEmail", "VendorName",
        "HireDate", "TerminationDate", "StartDate", "EndDate",
        "Description", "Notes",
    };

    // Columns that are DATETIME2 in the schema — we parse string payload values to
    // DateTime so Dapper binds the right SQL type rather than failing on a cast.
    private static readonly HashSet<string> IdentityDateColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "HireDate", "TerminationDate", "StartDate", "EndDate",
    };

    /// <summary>
    /// Paged query over the Identities table for the Conduit source path.
    /// <c>keyField</c> selects which deterministic column is surfaced as the
    /// row's natural key (default employeeId); <c>status</c> and <c>department</c>
    /// filter; <c>modifiedSince</c> is the incremental cursor (ISO-8601 UTC).
    /// Soft-deleted rows (DeletedAt set) are excluded.
    /// </summary>
    [HttpGet("query")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Query(
        [FromQuery] string keyField = "employeeId",
        [FromQuery] string? status = null,
        [FromQuery] string? department = null,
        [FromQuery] DateTime? modifiedSince = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (!KeyFieldColumns.TryGetValue(keyField, out var keyColumn))
            return BadRequest(new { error = $"Invalid keyField '{keyField}'. Allowed: {string.Join(", ", KeyFieldColumns.Keys)}." });
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 100;
        if (pageSize > 1000) pageSize = 1000;

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // DeletedAt may not exist on very old schemas; guard the filter so the
            // query degrades gracefully rather than throwing on a missing column.
            var hasDeletedAt = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_NAME = 'Identities' AND COLUMN_NAME = 'DeletedAt'") > 0;

            var where = new List<string>();
            if (hasDeletedAt) where.Add("DeletedAt IS NULL");

            var parameters = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(status))
            {
                where.Add("Status = @status");
                parameters.Add("status", status);
            }
            if (!string.IsNullOrWhiteSpace(department))
            {
                where.Add("Department = @department");
                parameters.Add("department", department);
            }
            if (modifiedSince.HasValue)
            {
                where.Add("(ModifiedAt IS NOT NULL AND ModifiedAt > @modifiedSince)");
                parameters.Add("modifiedSince", modifiedSince.Value);
            }

            var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;

            var total = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM Identities {whereSql}", parameters);

            parameters.Add("offset", (page - 1) * pageSize);
            parameters.Add("pageSize", pageSize);

            // SELECT * so the full typed-column projection is available to flatten
            // into Attributes; the connector maps any column without a second call.
            var rows = await conn.QueryAsync(
                $@"SELECT * FROM Identities {whereSql}
                   ORDER BY ISNULL(ModifiedAt, CreatedAt) ASC, Id ASC
                   OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY", parameters);

            var items = new List<IdentityQueryItem>();
            foreach (var r in rows)
            {
                var row = (IDictionary<string, object?>)r;
                string? Get(string col) => row.TryGetValue(col, out var v) && v is not null ? v.ToString() : null;

                var attrs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in row)
                {
                    // Surface only the writable, field-mapped columns as attributes —
                    // never IDs, governance/risk/lifecycle/audit state.
                    if (IdentityWritableColumns.Contains(kv.Key))
                        attrs[kv.Key] = kv.Value?.ToString();
                }

                var keyValue = Get(keyColumn);
                items.Add(new IdentityQueryItem
                {
                    Id = row.TryGetValue("Id", out var idv) && idv is Guid g ? g : Guid.Empty,
                    KeyField = keyField,
                    KeyValue = keyValue,
                    DisplayName = Get("DisplayName") ?? string.Empty,
                    FirstName = Get("FirstName"),
                    LastName = Get("LastName"),
                    PrimaryEmail = Get("PrimaryEmail"),
                    UserPrincipalName = Get("UserPrincipalName"),
                    Username = Get("Username"),
                    EmployeeId = Get("EmployeeId"),
                    Department = Get("Department"),
                    JobTitle = Get("JobTitle"),
                    Status = Get("Status"),
                    IsActive = row.TryGetValue("IsActive", out var ia) && ia is bool b && b,
                    ModifiedAt = row.TryGetValue("ModifiedAt", out var ma) && ma is DateTime mdt
                        ? mdt
                        : (row.TryGetValue("CreatedAt", out var ca) && ca is DateTime cdt ? cdt : DateTime.MinValue),
                    Attributes = attrs,
                });
            }

            return Ok(new IdentityQueryResponse
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
            _logger.LogError(ex, "API: Identities query failed (keyField={KeyField}, status={Status})", keyField, status);
            return StatusCode(500, new { error = "Identities query failed" });
        }
    }

    /// <summary>
    /// Bulk idempotent upsert into the Identities table. Each item is matched on
    /// the batch's deterministic <see cref="IdentityBulkUpsertRequest.KeyField"/>
    /// (exact equality on one allow-listed column). Existing rows are UPDATEd,
    /// new rows INSERTed. RAW LANDING ONLY — no PersonMatch/correlation, no
    /// write-back to AD/Entra. Mirrors <c>ObjectsController.BulkUpsert</c>.
    /// </summary>
    [HttpPost("bulk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkUpsert([FromBody] IdentityBulkUpsertRequest request)
    {
        if (request is null || request.Items is null || request.Items.Count == 0)
            return BadRequest(new { error = "Items is required and must be non-empty" });
        if (request.Items.Count > 1000)
            return BadRequest(new { error = "Maximum 1000 items per request" });
        if (!KeyFieldColumns.TryGetValue(request.KeyField, out var keyColumn))
            return BadRequest(new { error = $"Invalid keyField '{request.KeyField}'. Allowed: {string.Join(", ", KeyFieldColumns.Keys)}." });

        var results = new List<IdentityBulkUpsertResult>(request.Items.Count);

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.KeyValue))
                {
                    results.Add(new IdentityBulkUpsertResult
                    {
                        KeyValue = item.KeyValue ?? string.Empty,
                        Outcome = "Failed",
                        ErrorMessage = "KeyValue is required"
                    });
                    continue;
                }

                try
                {
                    // keyColumn is from the server-side allow-list — never caller text.
                    var existingId = await conn.ExecuteScalarAsync<Guid?>(
                        $"SELECT TOP 1 Id FROM Identities WHERE [{keyColumn}] = @KeyValue ORDER BY CreatedAt ASC",
                        new { KeyValue = item.KeyValue });

                    var outcome = existingId.HasValue
                        ? await UpdateIdentityRowAsync(conn, existingId.Value, item, keyColumn)
                        : await InsertIdentityRowAsync(conn, item, keyColumn);

                    results.Add(new IdentityBulkUpsertResult { KeyValue = item.KeyValue, Outcome = outcome });
                }
                catch (Exception itemEx)
                {
                    _logger.LogWarning(itemEx, "Identity bulk upsert item failed for key {KeyValue}", item.KeyValue);
                    results.Add(new IdentityBulkUpsertResult
                    {
                        KeyValue = item.KeyValue,
                        Outcome = "Failed",
                        ErrorMessage = itemEx.Message
                    });
                }
            }

            _logger.LogInformation(
                "API: identity bulk upsert batch {BatchId} on key '{KeyField}' processed {Total} ({Created} created, {Updated} updated, {Failed} failed)",
                request.BatchId, request.KeyField, results.Count,
                results.Count(r => r.Outcome == "Created"),
                results.Count(r => r.Outcome == "Updated"),
                results.Count(r => r.Outcome == "Failed"));

            return Ok(new IdentityBulkUpsertResponse { BatchId = request.BatchId, Results = results });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: identity bulk upsert batch {BatchId} failed", request.BatchId);
            return StatusCode(500, new { error = "Identity bulk upsert failed", batchId = request.BatchId });
        }
    }

    private static async Task<string> InsertIdentityRowAsync(SqlConnection conn, IdentityBulkUpsertItem item, string keyColumn)
    {
        var id = Guid.NewGuid();
        var (setCols, parameters) = BuildIdentityProjection(item.Attributes);

        // Ensure the key column is always written so the row is addressable on the
        // next run even when the payload didn't repeat it in Attributes.
        if (!setCols.Any(c => string.Equals(c.Column, keyColumn, StringComparison.OrdinalIgnoreCase)))
        {
            var pn = "@k_key";
            setCols.Add((keyColumn, pn));
            parameters.Add("k_key", item.KeyValue);
        }

        var cols = new List<string> { "Id" };
        var vals = new List<string> { "@_Id" };
        parameters.Add("_Id", id);

        foreach (var (col, paramName) in setCols)
        {
            cols.Add($"[{col}]");
            vals.Add(paramName);
        }

        // Required NOT NULL columns + denormalized DisplayName fallback.
        cols.Add("IsActive"); vals.Add("@_IsActive");
        parameters.Add("_IsActive", ParseActive(item.Attributes));
        cols.Add("CreatedAt"); vals.Add("SYSUTCDATETIME()");
        cols.Add("ModifiedAt"); vals.Add("SYSUTCDATETIME()");

        // DisplayName is NOT NULL in the schema. If the payload didn't supply it,
        // derive from First/Last, else fall back to the key value, so the insert
        // never violates the constraint.
        if (!setCols.Any(c => string.Equals(c.Column, "DisplayName", StringComparison.OrdinalIgnoreCase)))
        {
            var fn = LookupAttr(item.Attributes, "FirstName");
            var ln = LookupAttr(item.Attributes, "LastName");
            var derived = string.Join(" ", new[] { fn, ln }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(derived)) derived = item.KeyValue;
            cols.Add("DisplayName"); vals.Add("@_DisplayName");
            parameters.Add("_DisplayName", derived);
        }

        var sql = $"INSERT INTO Identities ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})";
        await conn.ExecuteAsync(sql, parameters);
        return "Created";
    }

    private static async Task<string> UpdateIdentityRowAsync(SqlConnection conn, Guid existingId, IdentityBulkUpsertItem item, string keyColumn)
    {
        var (setCols, parameters) = BuildIdentityProjection(item.Attributes);
        if (setCols.Count == 0)
        {
            // Nothing mapped to write besides the key it already matched on.
            await conn.ExecuteAsync(
                "UPDATE Identities SET ModifiedAt = SYSUTCDATETIME() WHERE Id = @_Id",
                new { _Id = existingId });
            return "Updated";
        }

        var setClauses = setCols.Select(c => $"[{c.Column}] = {c.ParamName}").ToList();

        // Refresh DisplayName when name fields move and the payload didn't set it explicitly.
        var setsName = setCols.Any(c =>
            string.Equals(c.Column, "FirstName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Column, "LastName", StringComparison.OrdinalIgnoreCase));
        var setsDisplay = setCols.Any(c => string.Equals(c.Column, "DisplayName", StringComparison.OrdinalIgnoreCase));
        if (setsName && !setsDisplay)
        {
            setClauses.Add(@"DisplayName = LTRIM(RTRIM(CONCAT(COALESCE(@n_fn, FirstName), ' ', COALESCE(@n_ln, LastName))))");
            parameters.Add("n_fn", LookupAttr(item.Attributes, "FirstName"));
            parameters.Add("n_ln", LookupAttr(item.Attributes, "LastName"));
        }

        setClauses.Add("ModifiedAt = SYSUTCDATETIME()");
        parameters.Add("_Id", existingId);

        var sql = $"UPDATE Identities SET {string.Join(", ", setClauses)} WHERE Id = @_Id";
        await conn.ExecuteAsync(sql, parameters);
        return "Updated";
    }

    /// <summary>
    /// Splits the inbound attribute payload into (allow-listed typed-column writes,
    /// SqlParameters). Only whitelisted columns end up in the SQL — unknown keys
    /// are dropped (Identities has no sparse-attribute side table).
    /// </summary>
    private static (List<(string Column, string ParamName)> Columns, DynamicParameters Params)
        BuildIdentityProjection(IReadOnlyDictionary<string, string?> attrs)
    {
        var cols = new List<(string, string)>();
        var prms = new DynamicParameters();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        foreach (var (key, value) in attrs)
        {
            if (!IdentityWritableColumns.Contains(key)) continue;
            // Canonicalise to the allow-listed casing so [Column] is exact.
            var canonical = IdentityWritableColumns.First(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase));
            if (!seen.Add(canonical)) continue;
            var paramName = $"@c{i++}";
            cols.Add((canonical, paramName));
            if (IdentityDateColumns.Contains(canonical))
            {
                DateTime? dt = DateTime.TryParse(value, null,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsed) ? parsed : null;
                prms.Add(paramName.TrimStart('@'), dt);
            }
            else
            {
                prms.Add(paramName.TrimStart('@'), value);
            }
        }
        return (cols, prms);
    }

    private static bool ParseActive(IReadOnlyDictionary<string, string?> attrs)
    {
        var status = LookupAttr(attrs, "Status");
        if (!string.IsNullOrWhiteSpace(status))
            return !string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "Terminated", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static string? LookupAttr(IReadOnlyDictionary<string, string?> attrs, string key)
    {
        foreach (var (k, v) in attrs)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }
}
