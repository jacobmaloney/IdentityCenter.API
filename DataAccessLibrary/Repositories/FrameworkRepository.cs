using System.Data;
using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// High-performance Dapper repository for compliance framework data access
/// EF Core is ONLY for migrations - Dapper for ALL queries for blazing fast UI
/// Target: <5ms for framework list queries
/// </summary>
public class FrameworkRepository : DapperRepositoryBase, IFrameworkRepository
{
    public FrameworkRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<List<ComplianceFramework>> GetAllFrameworksAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetAllFrameworksAsync));

        try
        {
            _logger.LogDebug("Opening database connection for framework query");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Fast query - no joins, just framework data
            // Returns ALL frameworks (active and inactive) - UI shows toggle to enable/disable
            var sql = @"
                SELECT
                    Id, Name, Code, Description, Category, Authority, Jurisdiction,
                    Industry, Version, PublishedDate, IsActive, IsBuiltIn, ComplianceScore,
                    TotalRequirements, ImplementedControls, LastAssessmentDate,
                    Color, Icon, ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM ComplianceFrameworks
                ORDER BY Name";

            var frameworks = await connection.QueryAsync<ComplianceFramework>(
                new CommandDefinition(
                    sql,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = frameworks.ToList();

            _logger.LogInformation("Retrieved {Count} compliance frameworks in {Time}ms",
                list.Count, "performance_tracked");

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetAllFrameworksAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAllFrameworksAsync));
        }
    }

    public async Task<ComplianceFramework?> GetFrameworkByIdAsync(Guid frameworkId, CancellationToken cancellationToken = default)
    {
        if (frameworkId == Guid.Empty)
            throw new ArgumentException("Framework ID cannot be empty", nameof(frameworkId));

        _logger.LogMethodEntry(nameof(GetFrameworkByIdAsync), new { frameworkId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, Name, Code, Description, Category, Authority, Jurisdiction,
                    Industry, Version, PublishedDate, IsActive, IsBuiltIn, ComplianceScore,
                    TotalRequirements, ImplementedControls, LastAssessmentDate,
                    Color, Icon, ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM ComplianceFrameworks
                WHERE Id = @FrameworkId";

            var framework = await connection.QueryFirstOrDefaultAsync<ComplianceFramework>(
                new CommandDefinition(
                    sql,
                    new { FrameworkId = frameworkId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            if (framework != null)
            {
                _logger.LogDebug("Found framework {FrameworkId}: {FrameworkName}",
                    frameworkId, framework.Name);
            }

            return framework;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetFrameworkByIdAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetFrameworkByIdAsync));
        }
    }

    public async Task<List<ComplianceFrameworkPolicyMapping>> GetFrameworkMappingsAsync(Guid frameworkId, CancellationToken cancellationToken = default)
    {
        if (frameworkId == Guid.Empty)
            throw new ArgumentException("Framework ID cannot be empty", nameof(frameworkId));

        _logger.LogMethodEntry(nameof(GetFrameworkMappingsAsync), new { frameworkId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    m.Id, m.FrameworkId, m.CompliancePolicyId, m.RequirementId,
                    m.RequirementDescription, m.ComplianceStatus, m.CoveragePercentage,
                    m.Evidence, m.LastValidated, m.GapDescription, m.CreatedAt
                FROM ComplianceFrameworkPolicyMappings m
                WHERE m.FrameworkId = @FrameworkId
                ORDER BY m.RequirementId";

            var mappings = await connection.QueryAsync<ComplianceFrameworkPolicyMapping>(
                new CommandDefinition(
                    sql,
                    new { FrameworkId = frameworkId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = mappings.ToList();

            _logger.LogInformation("Retrieved {Count} policy mappings for framework {FrameworkId}",
                list.Count, frameworkId);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetFrameworkMappingsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetFrameworkMappingsAsync));
        }
    }

    public async Task<ComplianceFrameworkPolicyMapping> CreateMappingAsync(ComplianceFrameworkPolicyMapping mapping, CancellationToken cancellationToken = default)
    {
        if (mapping == null)
            throw new ArgumentNullException(nameof(mapping));

        _logger.LogMethodEntry(nameof(CreateMappingAsync),
            new { frameworkId = mapping.FrameworkId, policyId = mapping.CompliancePolicyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                INSERT INTO ComplianceFrameworkPolicyMappings
                (Id, FrameworkId, CompliancePolicyId, RequirementId, RequirementDescription,
                 ComplianceStatus, CoveragePercentage, Evidence, LastValidated, GapDescription, SortOrder, CreatedAt)
                VALUES
                (@Id, @FrameworkId, @CompliancePolicyId, @RequirementId, @RequirementDescription,
                 @ComplianceStatus, @CoveragePercentage, @Evidence, @LastValidated, @GapDescription, @SortOrder, @CreatedAt)";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    mapping,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Created framework-policy mapping {MappingId}", mapping.Id);

            return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreateMappingAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreateMappingAsync));
        }
    }

    public async Task DeleteMappingAsync(Guid mappingId, CancellationToken cancellationToken = default)
    {
        if (mappingId == Guid.Empty)
            throw new ArgumentException("Mapping ID cannot be empty", nameof(mappingId));

        _logger.LogMethodEntry(nameof(DeleteMappingAsync), new { mappingId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM ComplianceFrameworkPolicyMappings WHERE Id = @Id",
                    new { Id = mappingId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Deleted framework-policy mapping {MappingId}", mappingId);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeleteMappingAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeleteMappingAsync));
        }
    }

    public async Task<List<ComplianceFrameworkPolicyMapping>> GetAllMappingsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetAllMappingsAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, FrameworkId, CompliancePolicyId, RequirementId,
                    RequirementDescription, ComplianceStatus, CoveragePercentage,
                    Evidence, LastValidated, GapDescription, SortOrder, CreatedAt
                FROM ComplianceFrameworkPolicyMappings
                ORDER BY FrameworkId, SortOrder, RequirementId";

            var mappings = await connection.QueryAsync<ComplianceFrameworkPolicyMapping>(
                new CommandDefinition(
                    sql,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = mappings.ToList();
            _logger.LogInformation("Retrieved {Count} total framework-policy mappings", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetAllMappingsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAllMappingsAsync));
        }
    }

    public async Task<Dictionary<Guid, int>> GetFrameworkPolicyCountsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetFrameworkPolicyCountsAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT FrameworkId, COUNT(*) AS PolicyCount
                FROM ComplianceFrameworkPolicyMappings
                GROUP BY FrameworkId";

            var results = await connection.QueryAsync<(Guid FrameworkId, int PolicyCount)>(
                new CommandDefinition(
                    sql,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var dict = results.ToDictionary(r => r.FrameworkId, r => r.PolicyCount);
            _logger.LogInformation("Retrieved policy counts for {Count} frameworks", dict.Count);
            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetFrameworkPolicyCountsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetFrameworkPolicyCountsAsync));
        }
    }

    public async Task ReorderFrameworkMappingsAsync(Guid frameworkId, List<Guid> orderedMappingIds, CancellationToken cancellationToken = default)
    {
        if (frameworkId == Guid.Empty)
            throw new ArgumentException("Framework ID cannot be empty", nameof(frameworkId));
        if (orderedMappingIds == null || !orderedMappingIds.Any())
            return;

        _logger.LogMethodEntry(nameof(ReorderFrameworkMappingsAsync), new { frameworkId, count = orderedMappingIds.Count });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Batch UPDATE using CASE/WHEN - single round-trip instead of N queries
            var parameters = new DynamicParameters();
            parameters.Add("FrameworkId", frameworkId);

            var caseWhenClauses = new System.Text.StringBuilder();
            var inClauses = new System.Text.StringBuilder();

            for (int i = 0; i < orderedMappingIds.Count; i++)
            {
                var paramName = $"Id{i}";
                parameters.Add(paramName, orderedMappingIds[i]);
                caseWhenClauses.Append($" WHEN Id = @{paramName} THEN {i}");
                if (i > 0) inClauses.Append(", ");
                inClauses.Append($"@{paramName}");
            }

            var sql = $@"
                UPDATE ComplianceFrameworkPolicyMappings
                SET SortOrder = CASE{caseWhenClauses} END
                WHERE FrameworkId = @FrameworkId AND Id IN ({inClauses})";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    parameters,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Reordered {Count} mappings for framework {FrameworkId}", orderedMappingIds.Count, frameworkId);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(ReorderFrameworkMappingsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(ReorderFrameworkMappingsAsync));
        }
    }

    public async Task<ComplianceFramework> CopyFrameworkAsync(Guid sourceFrameworkId, string newName, string createdBy, CancellationToken cancellationToken = default)
    {
        if (sourceFrameworkId == Guid.Empty)
            throw new ArgumentException("Source framework ID cannot be empty", nameof(sourceFrameworkId));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("New name cannot be empty", nameof(newName));

        _logger.LogMethodEntry(nameof(CopyFrameworkAsync), new { sourceFrameworkId, newName });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get source framework
            var source = await connection.QueryFirstOrDefaultAsync<ComplianceFramework>(
                new CommandDefinition(
                    "SELECT * FROM ComplianceFrameworks WHERE Id = @Id",
                    new { Id = sourceFrameworkId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            if (source == null)
                throw new InvalidOperationException($"Source framework {sourceFrameworkId} not found");

            // Create copy
            var newId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            const string insertSql = @"
                INSERT INTO ComplianceFrameworks
                (Id, Name, Code, Description, Category, Authority, Jurisdiction, Industry, Version, PublishedDate,
                 IsActive, IsBuiltIn, ComplianceScore, TotalRequirements, ImplementedControls, LastAssessmentDate, Color, Icon,
                 CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                VALUES
                (@Id, @Name, @Code, @Description, @Category, @Authority, @Jurisdiction, @Industry, @Version, @PublishedDate,
                 @IsActive, 0, 0, @TotalRequirements, 0, NULL, @Color, @Icon,
                 @CreatedAt, @CreatedBy, NULL, NULL);

                SELECT * FROM ComplianceFrameworks WHERE Id = @Id;";

            var copy = await connection.QuerySingleAsync<ComplianceFramework>(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        Id = newId,
                        Name = newName,
                        Code = source.Code + "_COPY",
                        source.Description,
                        source.Category,
                        source.Authority,
                        source.Jurisdiction,
                        source.Industry,
                        source.Version,
                        source.PublishedDate,
                        IsActive = false,
                        source.TotalRequirements,
                        source.Color,
                        source.Icon,
                        CreatedAt = now,
                        CreatedBy = createdBy
                    },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            // Copy policy mappings
            var mappings = await connection.QueryAsync<ComplianceFrameworkPolicyMapping>(
                new CommandDefinition(
                    "SELECT * FROM ComplianceFrameworkPolicyMappings WHERE FrameworkId = @FrameworkId ORDER BY SortOrder",
                    new { FrameworkId = sourceFrameworkId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            foreach (var mapping in mappings)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        @"INSERT INTO ComplianceFrameworkPolicyMappings
                          (Id, FrameworkId, CompliancePolicyId, RequirementId, RequirementDescription,
                           ComplianceStatus, CoveragePercentage, Evidence, LastValidated, GapDescription, SortOrder, CreatedAt)
                          VALUES
                          (@Id, @FrameworkId, @CompliancePolicyId, @RequirementId, @RequirementDescription,
                           'NotAssessed', 0, NULL, NULL, NULL, @SortOrder, @CreatedAt)",
                        new
                        {
                            Id = Guid.NewGuid(),
                            FrameworkId = newId,
                            mapping.CompliancePolicyId,
                            mapping.RequirementId,
                            mapping.RequirementDescription,
                            mapping.SortOrder,
                            CreatedAt = now
                        },
                        commandTimeout: 10,
                        cancellationToken: cancellationToken));
            }

            _logger.LogInformation("Copied framework {SourceId} to {NewId} with {MappingCount} mappings",
                sourceFrameworkId, newId, mappings.Count());

            return copy;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CopyFrameworkAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CopyFrameworkAsync));
        }
    }

    public async Task<List<ComplianceFramework>> GetFrameworksByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category cannot be null or empty", nameof(category));

        _logger.LogMethodEntry(nameof(GetFrameworksByCategoryAsync), new { category });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, Name, Code, Description, Category, Authority, Jurisdiction,
                    Industry, Version, PublishedDate, IsActive, IsBuiltIn, ComplianceScore,
                    TotalRequirements, ImplementedControls, LastAssessmentDate,
                    Color, Icon, ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM ComplianceFrameworks
                WHERE Category = @Category AND IsActive = 1
                ORDER BY Name";

            var frameworks = await connection.QueryAsync<ComplianceFramework>(
                new CommandDefinition(
                    sql,
                    new { Category = category },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = frameworks.ToList();

            _logger.LogInformation("Retrieved {Count} frameworks in category {Category}",
                list.Count, category);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetFrameworksByCategoryAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetFrameworksByCategoryAsync));
        }
    }

    public async Task<ComplianceFramework> CreateFrameworkAsync(ComplianceFramework framework, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO ComplianceFrameworks
            (Id, Name, Code, Description, Category, Authority, Jurisdiction, Industry, Version, PublishedDate,
             IsActive, IsBuiltIn, ComplianceScore, TotalRequirements, ImplementedControls, LastAssessmentDate,
             Color, Icon, ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds,
             CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
            VALUES
            (@Id, @Name, @Code, @Description, @Category, @Authority, @Jurisdiction, @Industry, @Version, @PublishedDate,
             @IsActive, @IsBuiltIn, @ComplianceScore, @TotalRequirements, @ImplementedControls, @LastAssessmentDate,
             @Color, @Icon, @ScopeConnectionIds, @ScopeTags, @ScopeAttributeQuery, @ScopeGroupIds,
             @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);

            SELECT * FROM ComplianceFrameworks WHERE Id = @Id;";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var result = await connection.QuerySingleAsync<ComplianceFramework>(
            new CommandDefinition(sql, framework, commandTimeout: 30, cancellationToken: cancellationToken));
        return result;
    }

    public async Task<ComplianceFramework> UpdateFrameworkAsync(ComplianceFramework framework, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE ComplianceFrameworks
            SET Name = @Name,
                Code = @Code,
                Description = @Description,
                Category = @Category,
                Authority = @Authority,
                Jurisdiction = @Jurisdiction,
                Industry = @Industry,
                Version = @Version,
                PublishedDate = @PublishedDate,
                IsActive = @IsActive,
                Color = @Color,
                Icon = @Icon,
                ScopeConnectionIds = @ScopeConnectionIds,
                ScopeTags = @ScopeTags,
                ScopeAttributeQuery = @ScopeAttributeQuery,
                ScopeGroupIds = @ScopeGroupIds,
                ModifiedAt = @ModifiedAt,
                ModifiedBy = @ModifiedBy
            WHERE Id = @Id;

            SELECT * FROM ComplianceFrameworks WHERE Id = @Id;";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var result = await connection.QuerySingleAsync<ComplianceFramework>(
            new CommandDefinition(sql, framework, commandTimeout: 30, cancellationToken: cancellationToken));
        return result;
    }

    public async Task DeleteFrameworkAsync(Guid frameworkId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM ComplianceFrameworks WHERE Id = @FrameworkId;";
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(sql, new { FrameworkId = frameworkId }, commandTimeout: 30, cancellationToken: cancellationToken));
    }

    // ============================================
    // FRAMEWORK ASSIGNMENT OPERATIONS
    // ============================================

    public async Task<FrameworkAssignment> CreateAssignmentAsync(FrameworkAssignment assignment, CancellationToken cancellationToken = default)
    {
        if (assignment == null)
            throw new ArgumentNullException(nameof(assignment));

        _logger.LogMethodEntry(nameof(CreateAssignmentAsync),
            new { frameworkId = assignment.FrameworkId, connectionId = assignment.ConnectionId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                INSERT INTO FrameworkAssignments
                (Id, FrameworkId, ConnectionId, DepartmentId, ApplicationId, ScopeExpression,
                 ScopeInheritance, IsActive, ActivatedAt, CreatedAt, CreatedBy)
                VALUES
                (@Id, @FrameworkId, @ConnectionId, @DepartmentId, @ApplicationId, @ScopeExpression,
                 @ScopeInheritance, @IsActive, @ActivatedAt, @CreatedAt, @CreatedBy);

                SELECT * FROM FrameworkAssignments WHERE Id = @Id;";

            var result = await connection.QuerySingleAsync<FrameworkAssignment>(
                new CommandDefinition(
                    sql,
                    assignment,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Created framework assignment {AssignmentId} for framework {FrameworkId}",
                assignment.Id, assignment.FrameworkId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreateAssignmentAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreateAssignmentAsync));
        }
    }

    public async Task<FrameworkAssignment?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment ID cannot be empty", nameof(assignmentId));

        _logger.LogMethodEntry(nameof(GetAssignmentAsync), new { assignmentId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                SELECT
                    fa.Id, fa.FrameworkId, fa.ConnectionId, fa.DepartmentId, fa.ApplicationId,
                    fa.ScopeExpression, fa.ScopeInheritance, fa.IsActive, fa.ActivatedAt,
                    fa.DeactivatedAt, fa.DeactivationReason, fa.ComplianceScore, fa.LastEvaluatedAt,
                    fa.TotalPolicies, fa.PassingPolicies, fa.FailingPolicies, fa.TotalViolations,
                    fa.CriticalViolations, fa.CreatedAt, fa.CreatedBy, fa.ModifiedAt, fa.ModifiedBy
                FROM FrameworkAssignments fa
                WHERE fa.Id = @AssignmentId";

            var assignment = await connection.QueryFirstOrDefaultAsync<FrameworkAssignment>(
                new CommandDefinition(
                    sql,
                    new { AssignmentId = assignmentId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            return assignment;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetAssignmentAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAssignmentAsync));
        }
    }

    public async Task<List<FrameworkAssignment>> GetAssignmentsForFrameworkAsync(Guid frameworkId, CancellationToken cancellationToken = default)
    {
        if (frameworkId == Guid.Empty)
            throw new ArgumentException("Framework ID cannot be empty", nameof(frameworkId));

        _logger.LogMethodEntry(nameof(GetAssignmentsForFrameworkAsync), new { frameworkId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                SELECT
                    fa.Id, fa.FrameworkId, fa.ConnectionId, fa.DepartmentId, fa.ApplicationId,
                    fa.ScopeExpression, fa.ScopeInheritance, fa.IsActive, fa.ActivatedAt,
                    fa.DeactivatedAt, fa.DeactivationReason, fa.ComplianceScore, fa.LastEvaluatedAt,
                    fa.TotalPolicies, fa.PassingPolicies, fa.FailingPolicies, fa.TotalViolations,
                    fa.CriticalViolations, fa.CreatedAt, fa.CreatedBy, fa.ModifiedAt, fa.ModifiedBy
                FROM FrameworkAssignments fa
                WHERE fa.FrameworkId = @FrameworkId
                ORDER BY fa.CreatedAt DESC";

            var assignments = await connection.QueryAsync<FrameworkAssignment>(
                new CommandDefinition(
                    sql,
                    new { FrameworkId = frameworkId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = assignments.ToList();

            _logger.LogInformation("Retrieved {Count} assignments for framework {FrameworkId}",
                list.Count, frameworkId);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetAssignmentsForFrameworkAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAssignmentsForFrameworkAsync));
        }
    }

    public async Task<List<FrameworkAssignment>> GetAssignmentsForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection ID cannot be empty", nameof(connectionId));

        _logger.LogMethodEntry(nameof(GetAssignmentsForConnectionAsync), new { connectionId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                SELECT
                    fa.Id, fa.FrameworkId, fa.ConnectionId, fa.DepartmentId, fa.ApplicationId,
                    fa.ScopeExpression, fa.ScopeInheritance, fa.IsActive, fa.ActivatedAt,
                    fa.DeactivatedAt, fa.DeactivationReason, fa.ComplianceScore, fa.LastEvaluatedAt,
                    fa.TotalPolicies, fa.PassingPolicies, fa.FailingPolicies, fa.TotalViolations,
                    fa.CriticalViolations, fa.CreatedAt, fa.CreatedBy, fa.ModifiedAt, fa.ModifiedBy
                FROM FrameworkAssignments fa
                WHERE fa.ConnectionId = @ConnectionId AND fa.IsActive = 1
                ORDER BY fa.CreatedAt DESC";

            var assignments = await connection.QueryAsync<FrameworkAssignment>(
                new CommandDefinition(
                    sql,
                    new { ConnectionId = connectionId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = assignments.ToList();

            _logger.LogInformation("Retrieved {Count} active assignments for connection {ConnectionId}",
                list.Count, connectionId);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetAssignmentsForConnectionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAssignmentsForConnectionAsync));
        }
    }

    public async Task<List<FrameworkAssignment>> GetActiveAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetActiveAssignmentsAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                SELECT
                    fa.Id, fa.FrameworkId, fa.ConnectionId, fa.DepartmentId, fa.ApplicationId,
                    fa.ScopeExpression, fa.ScopeInheritance, fa.IsActive, fa.ActivatedAt,
                    fa.DeactivatedAt, fa.DeactivationReason, fa.ComplianceScore, fa.LastEvaluatedAt,
                    fa.TotalPolicies, fa.PassingPolicies, fa.FailingPolicies, fa.TotalViolations,
                    fa.CriticalViolations, fa.CreatedAt, fa.CreatedBy, fa.ModifiedAt, fa.ModifiedBy
                FROM FrameworkAssignments fa
                WHERE fa.IsActive = 1
                ORDER BY CASE WHEN fa.LastEvaluatedAt IS NULL THEN 0 ELSE 1 END, fa.LastEvaluatedAt ASC";

            var assignments = await connection.QueryAsync<FrameworkAssignment>(
                new CommandDefinition(
                    sql,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = assignments.ToList();

            _logger.LogInformation("Retrieved {Count} active framework assignments", list.Count);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetActiveAssignmentsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetActiveAssignmentsAsync));
        }
    }

    public async Task<FrameworkAssignment> UpdateAssignmentAsync(FrameworkAssignment assignment, CancellationToken cancellationToken = default)
    {
        if (assignment == null)
            throw new ArgumentNullException(nameof(assignment));

        _logger.LogMethodEntry(nameof(UpdateAssignmentAsync), new { assignmentId = assignment.Id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                UPDATE FrameworkAssignments
                SET ConnectionId = @ConnectionId,
                    DepartmentId = @DepartmentId,
                    ApplicationId = @ApplicationId,
                    ScopeExpression = @ScopeExpression,
                    ScopeInheritance = @ScopeInheritance,
                    IsActive = @IsActive,
                    ActivatedAt = @ActivatedAt,
                    DeactivatedAt = @DeactivatedAt,
                    DeactivationReason = @DeactivationReason,
                    ModifiedAt = @ModifiedAt,
                    ModifiedBy = @ModifiedBy
                WHERE Id = @Id;

                SELECT * FROM FrameworkAssignments WHERE Id = @Id;";

            var result = await connection.QuerySingleAsync<FrameworkAssignment>(
                new CommandDefinition(
                    sql,
                    assignment,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Updated framework assignment {AssignmentId}", assignment.Id);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateAssignmentAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateAssignmentAsync));
        }
    }

    public async Task DeactivateAssignmentAsync(Guid assignmentId, string reason, string userId, CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment ID cannot be empty", nameof(assignmentId));

        _logger.LogMethodEntry(nameof(DeactivateAssignmentAsync), new { assignmentId, userId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                UPDATE FrameworkAssignments
                SET IsActive = 0,
                    DeactivatedAt = GETUTCDATE(),
                    DeactivationReason = @Reason,
                    ModifiedAt = GETUTCDATE(),
                    ModifiedBy = @UserId
                WHERE Id = @AssignmentId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { AssignmentId = assignmentId, Reason = reason, UserId = userId },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Deactivated framework assignment {AssignmentId} by {UserId}: {Reason}",
                assignmentId, userId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeactivateAssignmentAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeactivateAssignmentAsync));
        }
    }

    public async Task DeleteAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment ID cannot be empty", nameof(assignmentId));

        _logger.LogMethodEntry(nameof(DeleteAssignmentAsync), new { assignmentId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = "DELETE FROM FrameworkAssignments WHERE Id = @AssignmentId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { AssignmentId = assignmentId },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Deleted framework assignment {AssignmentId}", assignmentId);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeleteAssignmentAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeleteAssignmentAsync));
        }
    }

    public async Task UpdateAssignmentComplianceAsync(Guid assignmentId, decimal complianceScore, int totalPolicies,
        int passingPolicies, int failingPolicies, int totalViolations, int criticalViolations,
        CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment ID cannot be empty", nameof(assignmentId));

        _logger.LogMethodEntry(nameof(UpdateAssignmentComplianceAsync),
            new { assignmentId, complianceScore, totalPolicies, passingPolicies, failingPolicies });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                UPDATE FrameworkAssignments
                SET ComplianceScore = @ComplianceScore,
                    TotalPolicies = @TotalPolicies,
                    PassingPolicies = @PassingPolicies,
                    FailingPolicies = @FailingPolicies,
                    TotalViolations = @TotalViolations,
                    CriticalViolations = @CriticalViolations,
                    LastEvaluatedAt = GETUTCDATE()
                WHERE Id = @AssignmentId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        AssignmentId = assignmentId,
                        ComplianceScore = complianceScore,
                        TotalPolicies = totalPolicies,
                        PassingPolicies = passingPolicies,
                        FailingPolicies = failingPolicies,
                        TotalViolations = totalViolations,
                        CriticalViolations = criticalViolations
                    },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Updated compliance metrics for assignment {AssignmentId}: Score={Score}%",
                assignmentId, complianceScore);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateAssignmentComplianceAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateAssignmentComplianceAsync));
        }
    }

    // ============================================
    // POLICY OVERRIDE OPERATIONS
    // ============================================

    public async Task<List<FrameworkAssignmentPolicyOverride>> GetPolicyOverridesAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment ID cannot be empty", nameof(assignmentId));

        _logger.LogMethodEntry(nameof(GetPolicyOverridesAsync), new { assignmentId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                SELECT
                    Id, AssignmentId, PolicyId, IsEnabled, EnforcementMode,
                    CustomParameters, Justification, ExpiresAt,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM FrameworkAssignmentPolicyOverrides
                WHERE AssignmentId = @AssignmentId
                ORDER BY CreatedAt";

            var overrides = await connection.QueryAsync<FrameworkAssignmentPolicyOverride>(
                new CommandDefinition(
                    sql,
                    new { AssignmentId = assignmentId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            return overrides.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetPolicyOverridesAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetPolicyOverridesAsync));
        }
    }

    public async Task<FrameworkAssignmentPolicyOverride> UpsertPolicyOverrideAsync(FrameworkAssignmentPolicyOverride policyOverride, CancellationToken cancellationToken = default)
    {
        if (policyOverride == null)
            throw new ArgumentNullException(nameof(policyOverride));

        _logger.LogMethodEntry(nameof(UpsertPolicyOverrideAsync),
            new { assignmentId = policyOverride.AssignmentId, policyId = policyOverride.PolicyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // MERGE for upsert behavior
            const string sql = @"
                MERGE INTO FrameworkAssignmentPolicyOverrides AS target
                USING (SELECT @AssignmentId AS AssignmentId, @PolicyId AS PolicyId) AS source
                ON target.AssignmentId = source.AssignmentId AND target.PolicyId = source.PolicyId
                WHEN MATCHED THEN
                    UPDATE SET
                        IsEnabled = @IsEnabled,
                        EnforcementMode = @EnforcementMode,
                        CustomParameters = @CustomParameters,
                        Justification = @Justification,
                        ExpiresAt = @ExpiresAt,
                        ModifiedAt = GETUTCDATE(),
                        ModifiedBy = @ModifiedBy
                WHEN NOT MATCHED THEN
                    INSERT (Id, AssignmentId, PolicyId, IsEnabled, EnforcementMode,
                            CustomParameters, Justification, ExpiresAt, CreatedAt, CreatedBy)
                    VALUES (@Id, @AssignmentId, @PolicyId, @IsEnabled, @EnforcementMode,
                            @CustomParameters, @Justification, @ExpiresAt, GETUTCDATE(), @CreatedBy);

                SELECT * FROM FrameworkAssignmentPolicyOverrides
                WHERE AssignmentId = @AssignmentId AND PolicyId = @PolicyId;";

            var result = await connection.QuerySingleAsync<FrameworkAssignmentPolicyOverride>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        policyOverride.Id,
                        policyOverride.AssignmentId,
                        policyOverride.PolicyId,
                        policyOverride.IsEnabled,
                        policyOverride.EnforcementMode,
                        policyOverride.CustomParameters,
                        policyOverride.Justification,
                        policyOverride.ExpiresAt,
                        policyOverride.CreatedBy,
                        ModifiedBy = policyOverride.ModifiedBy ?? policyOverride.CreatedBy
                    },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Upserted policy override for assignment {AssignmentId}, policy {PolicyId}",
                policyOverride.AssignmentId, policyOverride.PolicyId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpsertPolicyOverrideAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpsertPolicyOverrideAsync));
        }
    }

    public async Task DeletePolicyOverrideAsync(Guid overrideId, CancellationToken cancellationToken = default)
    {
        if (overrideId == Guid.Empty)
            throw new ArgumentException("Override ID cannot be empty", nameof(overrideId));

        _logger.LogMethodEntry(nameof(DeletePolicyOverrideAsync), new { overrideId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = "DELETE FROM FrameworkAssignmentPolicyOverrides WHERE Id = @OverrideId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { OverrideId = overrideId },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Deleted policy override {OverrideId}", overrideId);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeletePolicyOverrideAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeletePolicyOverrideAsync));
        }
    }

    // ============================================
    // COMPLIANCE SUMMARY QUERIES
    // ============================================

    public async Task<decimal> GetOverallComplianceScoreAsync(Guid? connectionId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetOverallComplianceScoreAsync), new { connectionId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT ISNULL(AVG(ComplianceScore), 0)
                FROM FrameworkAssignments
                WHERE IsActive = 1";

            if (connectionId.HasValue)
            {
                sql += " AND ConnectionId = @ConnectionId";
            }

            var score = await connection.QuerySingleAsync<decimal>(
                new CommandDefinition(
                    sql,
                    new { ConnectionId = connectionId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            _logger.LogDebug("Overall compliance score: {Score}%", score);

            return score;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetOverallComplianceScoreAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetOverallComplianceScoreAsync));
        }
    }

    public async Task<List<FrameworkComplianceSummary>> GetComplianceSummaryByFrameworkAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetComplianceSummaryByFrameworkAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                SELECT
                    cf.Id AS FrameworkId,
                    cf.Name AS FrameworkName,
                    cf.Code AS FrameworkCode,
                    cf.Category,
                    cf.Color,
                    COUNT(fa.Id) AS TotalAssignments,
                    SUM(CASE WHEN fa.IsActive = 1 THEN 1 ELSE 0 END) AS ActiveAssignments,
                    ISNULL(AVG(CASE WHEN fa.IsActive = 1 THEN fa.ComplianceScore END), 0) AS AverageComplianceScore,
                    ISNULL(SUM(CASE WHEN fa.IsActive = 1 THEN fa.TotalViolations ELSE 0 END), 0) AS TotalViolations,
                    ISNULL(SUM(CASE WHEN fa.IsActive = 1 THEN fa.CriticalViolations ELSE 0 END), 0) AS CriticalViolations,
                    MAX(fa.LastEvaluatedAt) AS LastEvaluatedAt
                FROM ComplianceFrameworks cf
                LEFT JOIN FrameworkAssignments fa ON cf.Id = fa.FrameworkId
                WHERE cf.IsActive = 1
                GROUP BY cf.Id, cf.Name, cf.Code, cf.Category, cf.Color
                ORDER BY cf.Name";

            var summaries = await connection.QueryAsync<FrameworkComplianceSummary>(
                new CommandDefinition(
                    sql,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = summaries.ToList();

            _logger.LogInformation("Retrieved compliance summary for {Count} frameworks", list.Count);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetComplianceSummaryByFrameworkAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetComplianceSummaryByFrameworkAsync));
        }
    }

    public async Task<bool> IsFrameworkAssignedToConnectionAsync(Guid frameworkId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        if (frameworkId == Guid.Empty)
            throw new ArgumentException("Framework ID cannot be empty", nameof(frameworkId));
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection ID cannot be empty", nameof(connectionId));

        _logger.LogMethodEntry(nameof(IsFrameworkAssignedToConnectionAsync), new { frameworkId, connectionId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM FrameworkAssignments
                    WHERE FrameworkId = @FrameworkId
                      AND ConnectionId = @ConnectionId
                      AND IsActive = 1
                ) THEN 1 ELSE 0 END";

            var exists = await connection.QuerySingleAsync<bool>(
                new CommandDefinition(
                    sql,
                    new { FrameworkId = frameworkId, ConnectionId = connectionId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(IsFrameworkAssignedToConnectionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(IsFrameworkAssignedToConnectionAsync));
        }
    }
}
