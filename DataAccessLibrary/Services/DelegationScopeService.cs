using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text.Json;

namespace DataAccessLibrary.Services;

/// <summary>
/// Scoped service (one per Blazor circuit) that resolves and caches the current
/// user's delegation context.  Implements <see cref="IDelegationScopeService"/>.
/// </summary>
public class DelegationScopeService : IDelegationScopeService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // TODO: Add delegation activity tracking. Log each scoped object access
    // to DelegationActivityLog table for compliance reporting.

    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IGlobalLogger _logger;

    /// <summary>Cached context for this Blazor circuit.</summary>
    private UserDelegationContext? _context;

    public DelegationScopeService(
        IServiceProvider serviceProvider,
        IHttpContextAccessor httpContextAccessor,
        IGlobalLogger logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // GetContextAsync
    // =========================================================================

    /// <inheritdoc/>
    public async Task<UserDelegationContext> GetContextAsync()
    {
        // NOTE: Cache TTL is 5 minutes. If a user's roles change, they may retain
        // old permissions for up to 5 minutes. Call RefreshAsync() on role change events
        // to invalidate immediately. TODO: Wire into role assignment/removal workflow.
        if (_context != null && DateTime.UtcNow - _context.ResolvedAt < CacheTtl)
            return _context;

        _context = await ResolveContextAsync();
        return _context;
    }

    // =========================================================================
    // BuildObjectScopeFilterAsync
    // =========================================================================

    /// <inheritdoc/>
    public async Task<(string WhereClause, DynamicParameters Parameters)> BuildObjectScopeFilterAsync(string? tableAlias = null)
    {
        var ctx = await GetContextAsync();

        // Admin or pass-through: no restriction
        if (ctx.IsAdmin || !ctx.DelegationSystemActive || !ctx.HasAnyDelegation)
            return (string.Empty, new DynamicParameters());

        var parameters = new DynamicParameters();
        var clauses = new List<string>();

        foreach (var delegation in ctx.Delegations)
        {
            if (!string.IsNullOrWhiteSpace(delegation.ScopeWhereClause))
            {
                // Each delegation's clause may reference its own parameters; merge them in
                clauses.Add(delegation.ScopeWhereClause);
                foreach (var kvp in delegation.ScopeParameters)
                    parameters.Add(kvp.Key, kvp.Value);
            }
        }

        if (clauses.Count == 0)
            return (string.Empty, new DynamicParameters());

        // Combine delegation clauses with OR (user satisfies any one delegation)
        var combined = clauses.Count == 1
            ? clauses[0]
            : "(" + string.Join(" OR ", clauses) + ")";

        return (" AND " + combined, parameters);
    }

    // =========================================================================
    // CanPerformActionAsync
    // =========================================================================

    /// <inheritdoc/>
    public async Task<bool> CanPerformActionAsync(string action, string objectClass)
    {
        var ctx = await GetContextAsync();

        // Admin or pass-through: always allowed
        if (ctx.IsAdmin || !ctx.DelegationSystemActive)
            return true;

        // Global deny wins
        var denyKey = BuildActionKey(action, objectClass);
        if (ctx.DeniedActions.Contains(denyKey) || ctx.DeniedActions.Contains(BuildActionKey(action, "*")))
            return false;

        // Check AllowedActions for this objectClass or wildcard
        if (ctx.AllowedActions.TryGetValue(objectClass, out var classActions) && classActions.Contains(action))
            return true;
        if (ctx.AllowedActions.TryGetValue("*", out var globalActions) && globalActions.Contains(action))
            return true;

        return false;
    }

    // =========================================================================
    // CanAccessPageAsync
    // =========================================================================

    /// <inheritdoc/>
    public async Task<bool> CanAccessPageAsync(string pagePath)
    {
        var ctx = await GetContextAsync();

        // Admin always passes
        if (ctx.IsAdmin) return true;

        // Delegation system inactive: pass-through
        if (!ctx.DelegationSystemActive) return true;

        if (!ctx.HasAnyDelegation) return false;

        // Exact match
        if (ctx.AllowedPages.Contains(pagePath)) return true;

        // Wildcard: if any allowed page is "*" the user can see everything
        if (ctx.AllowedPages.Contains("*")) return true;

        // Prefix match: a permission for "/admin/objects" covers "/admin/objects/details"
        foreach (var allowed in ctx.AllowedPages)
        {
            if (pagePath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // =========================================================================
    // GetWritableAttributesAsync
    // =========================================================================

    /// <inheritdoc/>
    public async Task<HashSet<string>?> GetWritableAttributesAsync(string objectClass)
    {
        var ctx = await GetContextAsync();

        // Admin or pass-through: null means "all writable"
        if (ctx.IsAdmin || !ctx.DelegationSystemActive)
            return null;

        if (!ctx.HasAnyDelegation)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Merge writable attributes for this objectClass and wildcard class
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (ctx.WritableAttributes.TryGetValue(objectClass, out var classAttrs))
            result.UnionWith(classAttrs);

        if (ctx.WritableAttributes.TryGetValue("*", out var globalAttrs))
            result.UnionWith(globalAttrs);

        // If any delegation grants Write on "*", return null (all writable)
        if (result.Contains("*"))
            return null;

        return result;
    }

    // =========================================================================
    // RefreshAsync
    // =========================================================================

    /// <inheritdoc/>
    public Task RefreshAsync()
    {
        _context = null; // Force re-resolution on next call
        _logger.LogInformation("DelegationScopeService.RefreshAsync called — cache cleared for this circuit.");
        return Task.CompletedTask;
    }

    // =========================================================================
    // PreviewDelegationAsync
    // =========================================================================

    /// <inheritdoc/>
    public async Task<UserDelegationContext?> PreviewDelegationAsync(Guid assignmentId, CancellationToken ct = default)
    {
        // Log preview for audit trail
        try
        {
            var currentUserId = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var auditScope = _serviceProvider.CreateScope();
            var auditConfig = auditScope.ServiceProvider.GetRequiredService<IConfiguration>();
            var auditConnStr = auditConfig.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(auditConnStr))
            {
                using var auditConn = new Microsoft.Data.SqlClient.SqlConnection(auditConnStr);
                // Remapped to the REAL ChangeAuditLogs columns (the previous insert
                // wrote nonexistent Action/NewValues columns and NEWID() into the
                // bigint identity Id, so the row was silently dropped). A preview is
                // a read-only audit event: OperationType=Update(1), verb in Reason,
                // detail in NewValue — consistent with ChangeAuditLog.FromEntry.
                await auditConn.ExecuteAsync(
                    @"INSERT INTO ChangeAuditLogs (Timestamp, UserId, OperationType, EntityType, EntityId, Reason, NewValue, Source, Success)
                      VALUES (GETUTCDATE(), @UserId, 1, 'DelegationAssignment', @AssignmentId, 'PreviewedDelegation', @Details, 'Delegation', 1)",
                    new { UserId = currentUserId, AssignmentId = assignmentId, Details = "Admin previewed delegation permissions" });
            }
        }
        catch { /* best effort — audit failure must not block the preview */ }

        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDelegationRepository>();

        var assignment = await repo.GetAssignmentByIdAsync(assignmentId);
        if (assignment == null) return null;

        var template = await repo.GetTemplateWithPermissionsAsync(assignment.AccessTemplateId);
        if (template == null) return null;

        // Load scopes for this assignment
        var scopes = new List<ManagedScope>();
        if (assignment.ManagedScopeId.HasValue)
        {
            var directScope = await repo.GetScopeByIdAsync(assignment.ManagedScopeId.Value);
            if (directScope != null) scopes.Add(directScope);
        }
        else
        {
            var composites = await repo.GetCompositesForAssignmentAsync(assignment.Id);
            foreach (var composite in composites)
            {
                var compositeScope = await repo.GetScopeByIdAsync(composite.ManagedScopeId);
                if (compositeScope != null) scopes.Add(compositeScope);
            }
        }

        var (whereClause, scopeParams) = BuildScopeWhereClause(scopes, assignment.Id, _logger);
        var resolved = new ResolvedDelegation
        {
            AssignmentId = assignment.Id,
            Template = template,
            Scopes = scopes,
            ScopeWhereClause = whereClause,
            ScopeParameters = scopeParams
        };

        // Pre-compute permission sets for this single delegation
        var allowedObjectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deniedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var writableAttributes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var allowedActions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var perm in template.Permissions)
        {
            var objClass = perm.ObjectClass ?? "*";
            switch (perm.PermissionType)
            {
                case "ObjectType":
                    allowedObjectTypes.Add(perm.Target);
                    break;
                case "Page":
                    allowedPages.Add(perm.Target);
                    break;
                case "Attribute":
                    if (perm.AccessLevel.Equals("Write", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!writableAttributes.TryGetValue(objClass, out var attrSet))
                        {
                            attrSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            writableAttributes[objClass] = attrSet;
                        }
                        attrSet.Add(perm.Target);
                    }
                    break;
                case "Action":
                    if (perm.AccessLevel.Equals("Deny", StringComparison.OrdinalIgnoreCase))
                    {
                        deniedActions.Add(BuildActionKey(perm.Target, objClass));
                    }
                    else
                    {
                        if (!allowedActions.TryGetValue(objClass, out var actionSet))
                        {
                            actionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            allowedActions[objClass] = actionSet;
                        }
                        actionSet.Add(perm.Target);
                    }
                    break;
            }
        }

        return new UserDelegationContext
        {
            UserId = string.Concat("preview:", assignmentId),
            AccessLevel = 1,
            IsAdmin = false,
            HasAnyDelegation = true,
            DelegationSystemActive = true,
            Delegations = new List<ResolvedDelegation> { resolved },
            AllowedObjectTypes = allowedObjectTypes,
            AllowedPages = allowedPages,
            DeniedActions = deniedActions,
            WritableAttributes = writableAttributes,
            AllowedActions = allowedActions,
            ResolvedAt = DateTime.UtcNow
        };
    }

    // =========================================================================
    // Private: context resolution
    // =========================================================================

    private async Task<UserDelegationContext> ResolveContextAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDelegationRepository>();

        // --- Step 1: get current user ---
        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        // --- Step 2: short-circuit if no assignments exist ---
        var anyExist = await repo.AnyAssignmentsExistAsync();
        if (!anyExist)
        {
            return new UserDelegationContext
            {
                UserId = userId,
                AccessLevel = 1,
                IsAdmin = false,
                HasAnyDelegation = false,
                DelegationSystemActive = false,
                ResolvedAt = DateTime.UtcNow
            };
        }

        // --- Step 3: get user's roles from claims ---
        var roleClaims = httpContext?.User?.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList() ?? new List<string>();

        // --- Step 4: determine highest AccessLevel from roles in DB ---
        int accessLevel = 1;
        bool isAdmin = false;

        if (roleClaims.Count > 0)
        {
            accessLevel = await GetHighestAccessLevelForRolesAsync(scope.ServiceProvider, roleClaims);
            isAdmin = accessLevel >= 4;
        }

        // --- Step 5: Admin bypass ---
        if (isAdmin)
        {
            return new UserDelegationContext
            {
                UserId = userId,
                AccessLevel = accessLevel,
                IsAdmin = true,
                HasAnyDelegation = true,
                DelegationSystemActive = true,
                ResolvedAt = DateTime.UtcNow
            };
        }

        // --- Step 6: load active assignments for user's roles ---
        var assignments = await repo.GetActiveAssignmentsForRolesAsync(roleClaims);

        // Also load assignments for any AD group principals the user belongs to.
        // Groups are stored in claims under "groups" or as GroupSid.
        var userGroupClaims = httpContext?.User?.Claims
            .Where(c => c.Type == "groups" || c.Type == ClaimTypes.GroupSid)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList() ?? new List<string>();

        if (userGroupClaims.Count > 0)
        {
            var groupAssignments = await repo.GetActiveAssignmentsForGroupsAsync(userGroupClaims);
            assignments.AddRange(groupAssignments);
        }

        if (assignments.Count == 0)
        {
            return new UserDelegationContext
            {
                UserId = userId,
                AccessLevel = accessLevel,
                IsAdmin = false,
                HasAnyDelegation = false,
                DelegationSystemActive = true,
                ResolvedAt = DateTime.UtcNow
            };
        }

        // --- Step 7: batch-load all templates and scopes to avoid N+1 queries ---

        // Collect all unique template IDs and load in one query
        var templateIds = assignments.Select(a => a.AccessTemplateId).Distinct().ToList();
        var templatesById = (await repo.GetTemplatesWithPermissionsBatchAsync(templateIds))
            .ToDictionary(t => t.Id);

        // Collect direct scope IDs from assignments that reference a single scope
        var directScopeIds = assignments
            .Where(a => a.ManagedScopeId.HasValue)
            .Select(a => a.ManagedScopeId!.Value)
            .Distinct()
            .ToList();

        // For assignments without a direct scope, load composite scope rows
        // (one query per assignment for composites, but scope objects themselves are batched below)
        var compositeScopeIds = new List<Guid>();
        var compositesByAssignment = new Dictionary<Guid, List<DelegationScopeComposite>>();
        foreach (var assignment in assignments.Where(a => !a.ManagedScopeId.HasValue))
        {
            var composites = await repo.GetCompositesForAssignmentAsync(assignment.Id);
            compositesByAssignment[assignment.Id] = composites;
            compositeScopeIds.AddRange(composites.Select(c => c.ManagedScopeId));
        }

        // Load all unique scopes in one batch query
        var allScopeIds = directScopeIds.Concat(compositeScopeIds).Distinct().ToList();
        var scopesById = (await repo.GetScopesBatchAsync(allScopeIds))
            .ToDictionary(s => s.Id);

        // Build ResolvedDelegation entries using only in-memory lookups
        var resolvedList = new List<ResolvedDelegation>();

        foreach (var assignment in assignments)
        {
            if (!templatesById.TryGetValue(assignment.AccessTemplateId, out var template))
                continue;

            // Resolve scopes from in-memory dictionaries — no per-assignment DB queries
            var scopes = new List<ManagedScope>();

            if (assignment.ManagedScopeId.HasValue)
            {
                if (scopesById.TryGetValue(assignment.ManagedScopeId.Value, out var directScope))
                    scopes.Add(directScope);
            }
            else if (compositesByAssignment.TryGetValue(assignment.Id, out var composites))
            {
                foreach (var composite in composites)
                {
                    if (scopesById.TryGetValue(composite.ManagedScopeId, out var compositeScope))
                        scopes.Add(compositeScope);
                }
            }

            // --- Step 8: build SQL WHERE fragment for this delegation's scopes ---
            var (whereClause, scopeParams) = BuildScopeWhereClause(scopes, assignment.Id, _logger);

            resolvedList.Add(new ResolvedDelegation
            {
                AssignmentId = assignment.Id,
                Template = template,
                Scopes = scopes,
                ScopeWhereClause = whereClause,
                ScopeParameters = scopeParams
            });
        }

        // --- Step 9: pre-compute permission sets ---
        var allowedObjectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deniedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var writableAttributes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var allowedActions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var resolved in resolvedList)
        {
            foreach (var perm in resolved.Template.Permissions)
            {
                var objClass = perm.ObjectClass ?? "*";

                switch (perm.PermissionType)
                {
                    case "ObjectType":
                        allowedObjectTypes.Add(perm.Target);
                        break;

                    case "Page":
                        allowedPages.Add(perm.Target);
                        break;

                    case "Attribute":
                        if (perm.AccessLevel.Equals("Write", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!writableAttributes.TryGetValue(objClass, out var attrSet))
                            {
                                attrSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                writableAttributes[objClass] = attrSet;
                            }
                            attrSet.Add(perm.Target);
                        }
                        break;

                    case "Action":
                        if (perm.AccessLevel.Equals("Deny", StringComparison.OrdinalIgnoreCase))
                        {
                            deniedActions.Add(BuildActionKey(perm.Target, objClass));
                        }
                        else
                        {
                            if (!allowedActions.TryGetValue(objClass, out var actionSet))
                            {
                                actionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                allowedActions[objClass] = actionSet;
                            }
                            actionSet.Add(perm.Target);
                        }
                        break;
                }
            }
        }

        return new UserDelegationContext
        {
            UserId = userId,
            AccessLevel = accessLevel,
            IsAdmin = false,
            HasAnyDelegation = true,
            DelegationSystemActive = true,
            Delegations = resolvedList,
            AllowedObjectTypes = allowedObjectTypes,
            AllowedPages = allowedPages,
            DeniedActions = deniedActions,
            WritableAttributes = writableAttributes,
            AllowedActions = allowedActions,
            ResolvedAt = DateTime.UtcNow
        };
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Queries AspNetRoles for the highest AccessLevel among the user's role names.
    /// </summary>
    private static async Task<int> GetHighestAccessLevelForRolesAsync(
        IServiceProvider scopedProvider,
        List<string> roleNames)
    {
        var config = scopedProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) return 1;

        const string sql = @"
            SELECT MAX(ISNULL(AccessLevel, 1))
            FROM AspNetRoles
            WHERE Name IN @RoleNames;";

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();
        var result = await connection.QuerySingleOrDefaultAsync<int?>(sql, new { RoleNames = roleNames });
        return result ?? 1;
    }

    /// <summary>
    /// Builds a SQL WHERE fragment that restricts object rows to those matching
    /// the given scopes.  Composite scopes within one delegation are ANDed
    /// (the object must satisfy all of them); however individual scope clauses
    /// within a single delegation are combined with AND because they represent
    /// intersecting restrictions on a single assignment.
    ///
    /// Returns (empty, empty) when the list is empty (no scope = global access).
    /// </summary>
    private static (string WhereClause, Dictionary<string, object> Parameters) BuildScopeWhereClause(
        List<ManagedScope> scopes, Guid assignmentId, IGlobalLogger? logger = null)
    {
        if (scopes.Count == 0)
            return (string.Empty, new Dictionary<string, object>());

        var parts = new List<string>();
        var parameters = new Dictionary<string, object>();
        int idx = 0;

        foreach (var scope in scopes)
        {
            var paramSuffix = $"_{assignmentId:N}_{idx++}";

            switch (scope.ScopeType)
            {
                case "OU":
                {
                    var def = DeserializeScope<OUScopeDefinition>(scope.ScopeDefinition);
                    if (def == null || string.IsNullOrWhiteSpace(def.DN)) break;

                    var paramName = $"@ScopeDn{paramSuffix}";
                    if (def.IncludeChildren)
                    {
                        parts.Add($"DN LIKE {paramName}");
                        parameters[paramName] = "%" + def.DN;
                    }
                    else
                    {
                        // Exact OU DN (direct children only: one level below the OU)
                        var directChildParam = $"@ScopeDnDirect{paramSuffix}";
                        parts.Add($"(DN LIKE {paramName} AND DN NOT LIKE {directChildParam})");
                        parameters[paramName] = "%," + def.DN;
                        parameters[directChildParam] = "%,%," + def.DN;
                    }
                    break;
                }

                case "Query":
                {
                    var def = DeserializeScope<QueryScopeDefinition>(scope.ScopeDefinition);
                    if (def == null || string.IsNullOrWhiteSpace(def.Field)) break;

                    var paramName = $"@ScopeVal{paramSuffix}";
                    var col = SanitizeColumnName(def.Field, logger);
                    if (string.IsNullOrWhiteSpace(col)) break;

                    switch (def.Operator)
                    {
                        case "Contains":
                            parts.Add($"{col} LIKE {paramName}");
                            parameters[paramName] = "%" + def.Value + "%";
                            break;
                        case "StartsWith":
                            parts.Add($"{col} LIKE {paramName}");
                            parameters[paramName] = def.Value + "%";
                            break;
                        case "In":
                        {
                            var values = def.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            if (values.Length == 0) break;
                            var inParams = new List<string>();
                            for (int v = 0; v < values.Length; v++)
                            {
                                var vp = $"@ScopeIn{paramSuffix}_{v}";
                                inParams.Add(vp);
                                parameters[vp] = values[v];
                            }
                            parts.Add($"{col} IN ({string.Join(", ", inParams)})");
                            break;
                        }
                        default: // Equals
                            parts.Add($"{col} = {paramName}");
                            parameters[paramName] = def.Value;
                            break;
                    }
                    break;
                }

                case "Connection":
                {
                    var def = DeserializeScope<ConnectionScopeDefinition>(scope.ScopeDefinition);
                    if (def == null || def.ConnectionId == Guid.Empty) break;

                    var paramName = $"@ScopeConn{paramSuffix}";
                    parts.Add($"SourceConnectionId = {paramName}");
                    parameters[paramName] = def.ConnectionId;
                    break;
                }

                case "ObjectType":
                {
                    var def = DeserializeScope<ObjectTypeScopeDefinition>(scope.ScopeDefinition);
                    if (def == null || string.IsNullOrWhiteSpace(def.ObjectClass)) break;

                    var paramName = $"@ScopeObjClass{paramSuffix}";
                    parts.Add($"ObjectClass = {paramName}");
                    parameters[paramName] = def.ObjectClass;
                    break;
                }

                case "All":
                    // Explicit "all objects" scope - no filter needed; return immediately
                    return (string.Empty, new Dictionary<string, object>());
            }
        }

        if (parts.Count == 0)
            return (string.Empty, new Dictionary<string, object>());

        // Multiple scope parts within one delegation are ANDed (intersecting restrictions)
        var clause = parts.Count == 1
            ? parts[0]
            : "(" + string.Join(" AND ", parts) + ")";

        return (clause, parameters);
    }

    /// <summary>
    /// Deserializes a JSON scope definition, returning null on failure.
    /// </summary>
    private static T? DeserializeScope<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the SQL column name for a known field name to prevent SQL injection.
    /// Only whitelisted column names from the Objects table are passed through.
    /// If an unrecognised field is supplied a warning is logged and empty string is returned
    /// so the calling code can skip the clause rather than silently bypass security.
    /// </summary>
    private static string SanitizeColumnName(string field, IGlobalLogger? logger = null)
    {
        var result = field switch
        {
            // Core identity fields
            "Department"         => "Department",
            "ObjectClass"        => "ObjectClass",
            "Company"            => "Company",
            "Title"              => "Title",
            "JobTitle"           => "JobTitle",
            "Office"             => "Office",
            "City"               => "City",
            "State"              => "State",
            "PostalCode"         => "PostalCode",
            "Country"            => "Country",
            "Division"           => "Division",
            "EmployeeType"       => "EmployeeType",
            "EmployeeId"         => "EmployeeId",
            // Directory fields
            "CN"                 => "CN",
            "DN"                 => "DN",
            "Username"           => "Username",
            "UserPrincipalName"  => "UserPrincipalName",
            "DisplayName"        => "DisplayName",
            "Email"              => "Email",
            "FirstName"          => "FirstName",
            "LastName"           => "LastName",
            "MiddleName"         => "MiddleName",
            "Phone"              => "Phone",
            "MobilePhone"        => "MobilePhone",
            "HomePhone"          => "HomePhone",
            "Fax"                => "Fax",
            "StreetAddress"      => "StreetAddress",
            "Description"        => "Description",
            "SourceType"         => "SourceType",
            // Flag fields
            "IsActive"           => "IsActive",
            "IsHighRisk"         => "IsHighRisk",
            "IsBuiltIn"          => "IsBuiltIn",
            "IsAdminSDHolder"    => "IsAdminSDHolder",
            "PasswordNeverExpires" => "PasswordNeverExpires",
            // Relationship fields
            "ManagerSourceId"    => "ManagerSourceId",
            _                    => string.Empty
        };

        if (string.IsNullOrEmpty(result))
            logger?.LogWarning("DelegationScopeService.SanitizeColumnName: unrecognised field '{Field}' rejected to prevent SQL injection.", field);

        return result;
    }

    /// <summary>Builds a deny-action lookup key of the form "Action:ObjectClass".</summary>
    private static string BuildActionKey(string action, string objectClass)
        => string.Concat(action, ":", objectClass);
}
