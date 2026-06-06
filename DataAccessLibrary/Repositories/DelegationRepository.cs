using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="IDelegationRepository"/>.
/// All queries use parameterized SQL. Schema managed by DatabaseMigrationService.
/// </summary>
public class DelegationRepository : IDelegationRepository
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public DelegationRepository(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    // =========================================================================
    // AccessTemplate CRUD
    // =========================================================================

    /// <inheritdoc/>
    public async Task<List<AccessTemplate>> GetAllTemplatesAsync()
    {
        const string sql = @"
            SELECT
                t.Id, t.Name, t.Description, t.IsSystem, t.IsActive,
                t.CreatedBy, t.CreatedAt, t.ModifiedBy, t.ModifiedAt,
                COUNT(a.Id) AS AssignmentCount
            FROM AccessTemplates t
            LEFT JOIN DelegationAssignments a ON a.AccessTemplateId = t.Id AND a.IsActive = 1
            GROUP BY
                t.Id, t.Name, t.Description, t.IsSystem, t.IsActive,
                t.CreatedBy, t.CreatedAt, t.ModifiedBy, t.ModifiedAt
            ORDER BY t.Name;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        var results = await connection.QueryAsync<AccessTemplate>(sql);
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task<AccessTemplate?> GetTemplateByIdAsync(Guid id)
    {
        const string sql = @"
            SELECT
                Id, Name, Description, IsSystem, IsActive,
                CreatedBy, CreatedAt, ModifiedBy, ModifiedAt
            FROM AccessTemplates
            WHERE Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<AccessTemplate>(sql, new { Id = id });
    }

    /// <inheritdoc/>
    public async Task<AccessTemplate?> GetTemplateWithPermissionsAsync(Guid id)
    {
        const string sql = @"
            SELECT
                Id, Name, Description, IsSystem, IsActive,
                CreatedBy, CreatedAt, ModifiedBy, ModifiedAt
            FROM AccessTemplates
            WHERE Id = @Id;

            SELECT
                Id, AccessTemplateId, PermissionType, ObjectClass, Target, AccessLevel, CreatedAt
            FROM TemplatePermissions
            WHERE AccessTemplateId = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();

        using var multi = await connection.QueryMultipleAsync(sql, new { Id = id });

        var template = await multi.ReadSingleOrDefaultAsync<AccessTemplate>();
        if (template == null) return null;

        var permissions = await multi.ReadAsync<TemplatePermission>();
        template.Permissions = permissions.ToList();

        return template;
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateTemplateAsync(AccessTemplate template)
    {
        const string sql = @"
            INSERT INTO AccessTemplates
                (Id, Name, Description, IsSystem, IsActive, CreatedBy, CreatedAt, ModifiedBy, ModifiedAt)
            VALUES
                (@Id, @Name, @Description, @IsSystem, @IsActive, @CreatedBy, @CreatedAt, @ModifiedBy, @ModifiedAt);";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, template);
        await LogAuditAsync(connection, "Created", "AccessTemplate", template.Id, template.Name, template.CreatedBy);
        return template.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateTemplateAsync(AccessTemplate template)
    {
        const string sql = @"
            UPDATE AccessTemplates
            SET
                Name        = @Name,
                Description = @Description,
                IsActive    = @IsActive,
                ModifiedBy  = @ModifiedBy,
                ModifiedAt  = @ModifiedAt
            WHERE Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, template);
        await LogAuditAsync(connection, "Updated", "AccessTemplate", template.Id, template.Name, template.ModifiedBy);
    }

    /// <inheritdoc/>
    public async Task DeleteTemplateAsync(Guid id)
    {
        const string sql = @"
            UPDATE AccessTemplates
            SET IsActive = 0
            WHERE Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, new { Id = id });
        await LogAuditAsync(connection, "Deleted", "AccessTemplate", id, null, null);
    }

    // =========================================================================
    // TemplatePermission CRUD
    // =========================================================================

    /// <inheritdoc/>
    public async Task<List<TemplatePermission>> GetPermissionsForTemplateAsync(Guid templateId)
    {
        const string sql = @"
            SELECT
                Id, AccessTemplateId, PermissionType, ObjectClass, Target, AccessLevel, CreatedAt
            FROM TemplatePermissions
            WHERE AccessTemplateId = @TemplateId;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        var results = await connection.QueryAsync<TemplatePermission>(sql, new { TemplateId = templateId });
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task SetPermissionsAsync(Guid templateId, List<TemplatePermission> permissions)
    {
        const string deleteSql = @"
            DELETE FROM TemplatePermissions
            WHERE AccessTemplateId = @TemplateId;";

        const string insertSql = @"
            INSERT INTO TemplatePermissions
                (Id, AccessTemplateId, PermissionType, ObjectClass, Target, AccessLevel, CreatedAt)
            VALUES
                (@Id, @AccessTemplateId, @PermissionType, @ObjectClass, @Target, @AccessLevel, @CreatedAt);";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(deleteSql, new { TemplateId = templateId }, transaction);

            if (permissions.Count > 0)
            {
                foreach (var p in permissions)
                {
                    p.AccessTemplateId = templateId;
                    if (p.Id == Guid.Empty) p.Id = Guid.NewGuid();
                }
                await connection.ExecuteAsync(insertSql, permissions, transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =========================================================================
    // ManagedScope CRUD
    // =========================================================================

    /// <inheritdoc/>
    public async Task<List<ManagedScope>> GetAllScopesAsync()
    {
        const string sql = @"
            SELECT
                s.Id, s.Name, s.Description, s.ScopeType, s.ScopeDefinition,
                s.IsActive, s.CreatedBy, s.CreatedAt, s.ModifiedBy, s.ModifiedAt,
                COUNT(c.Id) AS AssignmentCount
            FROM ManagedScopes s
            LEFT JOIN DelegationScopeComposites c ON c.ManagedScopeId = s.Id
            GROUP BY
                s.Id, s.Name, s.Description, s.ScopeType, s.ScopeDefinition,
                s.IsActive, s.CreatedBy, s.CreatedAt, s.ModifiedBy, s.ModifiedAt
            ORDER BY s.Name;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        var results = await connection.QueryAsync<ManagedScope>(sql);
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task<ManagedScope?> GetScopeByIdAsync(Guid id)
    {
        const string sql = @"
            SELECT
                Id, Name, Description, ScopeType, ScopeDefinition,
                IsActive, CreatedBy, CreatedAt, ModifiedBy, ModifiedAt
            FROM ManagedScopes
            WHERE Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<ManagedScope>(sql, new { Id = id });
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateScopeAsync(ManagedScope scope)
    {
        const string sql = @"
            INSERT INTO ManagedScopes
                (Id, Name, Description, ScopeType, ScopeDefinition, IsActive, CreatedBy, CreatedAt, ModifiedBy, ModifiedAt)
            VALUES
                (@Id, @Name, @Description, @ScopeType, @ScopeDefinition, @IsActive, @CreatedBy, @CreatedAt, @ModifiedBy, @ModifiedAt);";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, scope);
        return scope.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateScopeAsync(ManagedScope scope)
    {
        const string sql = @"
            UPDATE ManagedScopes
            SET
                Name            = @Name,
                Description     = @Description,
                ScopeType       = @ScopeType,
                ScopeDefinition = @ScopeDefinition,
                IsActive        = @IsActive,
                ModifiedBy      = @ModifiedBy,
                ModifiedAt      = @ModifiedAt
            WHERE Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, scope);
    }

    /// <inheritdoc/>
    public async Task DeleteScopeAsync(Guid id)
    {
        const string sql = @"
            UPDATE ManagedScopes
            SET IsActive = 0
            WHERE Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    // =========================================================================
    // DelegationAssignment CRUD
    // =========================================================================

    /// <inheritdoc/>
    public async Task<List<DelegationAssignment>> GetAllAssignmentsAsync()
    {
        const string sql = @"
            SELECT
                a.Id, a.AccessTemplateId, a.PrincipalType, a.PrincipalId,
                a.PrincipalName, a.ManagedScopeId, a.IsActive, a.ExpiresAt,
                a.CreatedBy, a.CreatedAt, a.ModifiedBy, a.ModifiedAt,
                t.Name AS TemplateName,
                s.Name AS ScopeName
            FROM DelegationAssignments a
            LEFT JOIN AccessTemplates t ON t.Id = a.AccessTemplateId
            LEFT JOIN ManagedScopes   s ON s.Id = a.ManagedScopeId
            ORDER BY a.CreatedAt DESC;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        var results = await connection.QueryAsync<DelegationAssignment>(sql);
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task<DelegationAssignment?> GetAssignmentByIdAsync(Guid id)
    {
        const string sql = @"
            SELECT
                a.Id, a.AccessTemplateId, a.PrincipalType, a.PrincipalId,
                a.PrincipalName, a.ManagedScopeId, a.IsActive, a.ExpiresAt,
                a.CreatedBy, a.CreatedAt, a.ModifiedBy, a.ModifiedAt,
                t.Name AS TemplateName,
                s.Name AS ScopeName
            FROM DelegationAssignments a
            LEFT JOIN AccessTemplates t ON t.Id = a.AccessTemplateId
            LEFT JOIN ManagedScopes   s ON s.Id = a.ManagedScopeId
            WHERE a.Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<DelegationAssignment>(sql, new { Id = id });
    }

    /// <inheritdoc/>
    public async Task<List<DelegationAssignment>> GetActiveAssignmentsForRolesAsync(IEnumerable<string> roleIds)
    {
        const string sql = @"
            SELECT
                a.Id, a.AccessTemplateId, a.PrincipalType, a.PrincipalId,
                a.PrincipalName, a.ManagedScopeId, a.IsActive, a.ExpiresAt,
                a.CreatedBy, a.CreatedAt, a.ModifiedBy, a.ModifiedAt,
                t.Name AS TemplateName,
                s.Name AS ScopeName
            FROM DelegationAssignments a
            LEFT JOIN AccessTemplates t ON t.Id = a.AccessTemplateId
            LEFT JOIN ManagedScopes   s ON s.Id = a.ManagedScopeId
            WHERE a.PrincipalType = 'Role'
              AND a.PrincipalId   IN @RoleIds
              AND a.IsActive      = 1
              AND (a.ExpiresAt IS NULL OR a.ExpiresAt > GETUTCDATE());";

        var roleList = roleIds?.ToList() ?? new List<string>();
        if (roleList.Count == 0) return new List<DelegationAssignment>();

        using var connection = CreateConnection();
        await connection.OpenAsync();
        var results = await connection.QueryAsync<DelegationAssignment>(sql, new { RoleIds = roleList });
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateAssignmentAsync(DelegationAssignment assignment)
    {
        // Duplicate check: prevent identical active assignments (same template + principal + scope)
        const string duplicateCheckSql = @"
            SELECT COUNT(*)
            FROM DelegationAssignments
            WHERE AccessTemplateId = @TemplateId
              AND PrincipalType    = @PType
              AND PrincipalId      = @PId
              AND ISNULL(CAST(ManagedScopeId AS NVARCHAR(36)), '') = ISNULL(CAST(@ScopeId AS NVARCHAR(36)), '')
              AND IsActive = 1;";

        using var connection = CreateConnection();
        await connection.OpenAsync();

        var existingCount = await connection.QuerySingleAsync<int>(duplicateCheckSql, new
        {
            TemplateId = assignment.AccessTemplateId,
            PType      = assignment.PrincipalType,
            PId        = assignment.PrincipalId,
            ScopeId    = assignment.ManagedScopeId
        });

        if (existingCount > 0)
            throw new InvalidOperationException(
                string.Concat(
                    "An active delegation assignment already exists for template '",
                    assignment.TemplateName ?? assignment.AccessTemplateId.ToString(),
                    "' and principal '",
                    assignment.PrincipalName ?? assignment.PrincipalId,
                    "' with the same scope."));

        const string sql = @"
            INSERT INTO DelegationAssignments
                (Id, AccessTemplateId, PrincipalType, PrincipalId, PrincipalName,
                 ManagedScopeId, IsActive, ExpiresAt, CreatedBy, CreatedAt, ModifiedBy, ModifiedAt)
            VALUES
                (@Id, @AccessTemplateId, @PrincipalType, @PrincipalId, @PrincipalName,
                 @ManagedScopeId, @IsActive, @ExpiresAt, @CreatedBy, @CreatedAt, @ModifiedBy, @ModifiedAt);";

        await connection.ExecuteAsync(sql, assignment);
        var details = string.Concat(assignment.TemplateName ?? assignment.AccessTemplateId.ToString(), " -> ", assignment.PrincipalName ?? assignment.PrincipalId);
        await LogAuditAsync(connection, "Created", "DelegationAssignment", assignment.Id, details, assignment.CreatedBy);
        return assignment.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateAssignmentAsync(DelegationAssignment assignment)
    {
        const string sql = @"
            UPDATE DelegationAssignments
            SET
                AccessTemplateId = @AccessTemplateId,
                PrincipalType    = @PrincipalType,
                PrincipalId      = @PrincipalId,
                PrincipalName    = @PrincipalName,
                ManagedScopeId   = @ManagedScopeId,
                IsActive         = @IsActive,
                ExpiresAt        = @ExpiresAt,
                ModifiedBy       = @ModifiedBy,
                ModifiedAt       = @ModifiedAt
            WHERE Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, assignment);
    }

    /// <inheritdoc/>
    public async Task DeleteAssignmentAsync(Guid id)
    {
        const string sql = @"
            UPDATE DelegationAssignments
            SET IsActive = 0
            WHERE Id = @Id;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, new { Id = id });
        await LogAuditAsync(connection, "Deleted", "DelegationAssignment", id, null, null);
    }

    /// <inheritdoc/>
    public async Task<int> DeactivateExpiredAssignmentsAsync(CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE DelegationAssignments
            SET IsActive = 0
            WHERE IsActive = 1
              AND ExpiresAt IS NOT NULL
              AND ExpiresAt < GETUTCDATE();";

        using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    // =========================================================================
    // Group-based principal queries
    // =========================================================================

    /// <inheritdoc/>
    public async Task<List<DelegationAssignment>> GetActiveAssignmentsForGroupsAsync(List<string> groupIds, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                a.Id, a.AccessTemplateId, a.PrincipalType, a.PrincipalId,
                a.PrincipalName, a.ManagedScopeId, a.IsActive, a.ExpiresAt,
                a.CreatedBy, a.CreatedAt, a.ModifiedBy, a.ModifiedAt,
                t.Name AS TemplateName,
                s.Name AS ScopeName
            FROM DelegationAssignments a
            LEFT JOIN AccessTemplates t ON t.Id = a.AccessTemplateId
            LEFT JOIN ManagedScopes   s ON s.Id = a.ManagedScopeId
            WHERE a.PrincipalType = 'Group'
              AND a.PrincipalId   IN @GroupIds
              AND a.IsActive      = 1
              AND (a.ExpiresAt IS NULL OR a.ExpiresAt > GETUTCDATE());";

        if (groupIds == null || groupIds.Count == 0) return new List<DelegationAssignment>();

        using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        var results = await connection.QueryAsync<DelegationAssignment>(
            new CommandDefinition(sql, new { GroupIds = groupIds }, cancellationToken: ct));
        return results.ToList();
    }

    // =========================================================================
    // DelegationScopeComposite CRUD
    // =========================================================================

    /// <inheritdoc/>
    public async Task<List<DelegationScopeComposite>> GetCompositesForAssignmentAsync(Guid assignmentId)
    {
        const string sql = @"
            SELECT Id, DelegationAssignmentId, ManagedScopeId
            FROM DelegationScopeComposites
            WHERE DelegationAssignmentId = @AssignmentId;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        var results = await connection.QueryAsync<DelegationScopeComposite>(sql, new { AssignmentId = assignmentId });
        return results.ToList();
    }

    /// <inheritdoc/>
    public async Task SetCompositesAsync(Guid assignmentId, List<Guid> scopeIds)
    {
        const string deleteSql = @"
            DELETE FROM DelegationScopeComposites
            WHERE DelegationAssignmentId = @AssignmentId;";

        const string insertSql = @"
            INSERT INTO DelegationScopeComposites (Id, DelegationAssignmentId, ManagedScopeId)
            VALUES (@Id, @DelegationAssignmentId, @ManagedScopeId);";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(deleteSql, new { AssignmentId = assignmentId }, transaction);

            if (scopeIds.Count > 0)
            {
                var rows = scopeIds.Select(sid => new DelegationScopeComposite
                {
                    Id = Guid.NewGuid(),
                    DelegationAssignmentId = assignmentId,
                    ManagedScopeId = sid
                }).ToList();

                await connection.ExecuteAsync(insertSql, rows, transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =========================================================================
    // Batch queries
    // =========================================================================

    /// <inheritdoc/>
    public async Task<List<AccessTemplate>> GetTemplatesWithPermissionsBatchAsync(List<Guid> templateIds, CancellationToken ct = default)
    {
        if (templateIds == null || templateIds.Count == 0)
            return new List<AccessTemplate>();

        const string sql = @"
            SELECT
                Id, Name, Description, IsSystem, IsActive,
                CreatedBy, CreatedAt, ModifiedBy, ModifiedAt
            FROM AccessTemplates
            WHERE Id IN @Ids;

            SELECT
                Id, AccessTemplateId, PermissionType, ObjectClass, Target, AccessLevel, CreatedAt
            FROM TemplatePermissions
            WHERE AccessTemplateId IN @Ids;";

        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { Ids = templateIds }, cancellationToken: ct));

        var templates = (await multi.ReadAsync<AccessTemplate>()).ToList();
        var permissions = (await multi.ReadAsync<TemplatePermission>()).ToList();

        // Group permissions by template ID and attach
        var permsByTemplate = permissions.GroupBy(p => p.AccessTemplateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var t in templates)
            t.Permissions = permsByTemplate.TryGetValue(t.Id, out var perms) ? perms : new List<TemplatePermission>();

        return templates;
    }

    /// <inheritdoc/>
    public async Task<List<ManagedScope>> GetScopesBatchAsync(List<Guid> scopeIds, CancellationToken ct = default)
    {
        if (scopeIds == null || scopeIds.Count == 0)
            return new List<ManagedScope>();

        const string sql = @"
            SELECT
                Id, Name, Description, ScopeType, ScopeDefinition,
                IsActive, CreatedBy, CreatedAt, ModifiedBy, ModifiedAt
            FROM ManagedScopes
            WHERE Id IN @Ids;";

        using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        var results = await connection.QueryAsync<ManagedScope>(
            new CommandDefinition(sql, new { Ids = scopeIds }, cancellationToken: ct));
        return results.ToList();
    }

    // =========================================================================
    // System-level queries
    // =========================================================================

    /// <inheritdoc/>
    public async Task<bool> AnyAssignmentsExistAsync()
    {
        const string sql = @"
            SELECT TOP 1 1
            FROM DelegationAssignments
            WHERE IsActive = 1;";

        using var connection = CreateConnection();
        await connection.OpenAsync();
        var result = await connection.QuerySingleOrDefaultAsync<int?>(sql);
        return result.HasValue;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Inserts a row into ChangeAuditLogs for delegation mutations.
    /// Failures are swallowed and logged so they never block the main operation.
    /// </summary>
    private async Task LogAuditAsync(SqlConnection conn, string action, string entityType, Guid entityId, string? details, string? userId)
    {
        try
        {
            // Remapped to the REAL ChangeAuditLogs columns. The previous insert
            // wrote Action/NewValues (which do not exist) and a NEWID() into the
            // bigint identity Id, so every delegation audit row was silently
            // dropped. Delegation mutations map to OperationType=Update(1); the
            // specific verb (e.g. "PreviewedDelegation") is preserved in Reason,
            // and the detail payload in NewValue — consistent with the working EF
            // audit path (ChangeAuditLog.FromEntry).
            const string sql = @"
                INSERT INTO ChangeAuditLogs (Timestamp, UserId, OperationType, EntityType, EntityId, Reason, NewValue, Source, Success)
                VALUES (GETUTCDATE(), @UserId, 1, @EntityType, @EntityId, @Action, @Details, 'Delegation', 1)";

            await conn.ExecuteAsync(sql, new
            {
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("DelegationRepository.LogAuditAsync failed (non-fatal): {Message}", ex.Message);
        }
    }
}
