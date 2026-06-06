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
/// Repository for manager/owner resolution, person matching, and identity manager operations.
/// </summary>
public class SyncRelationshipRepository : DapperRepositoryBase, ISyncRelationshipRepository
{
    public SyncRelationshipRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public async Task<Identity?> FindIdentityByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(FindIdentityByEmailAsync), new { email });

        // Parameter validation before retry wrapper
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Email cannot be null or whitespace");
            return null;
        }

        try
        {
            return await SyncRepositoryHelpers.ExecuteWithRetryAsync(async () =>
            {
                using var tracker = new SyncRepositoryHelpers.PerformanceTracker(_logger, nameof(FindIdentityByEmailAsync),
                    new { email }, slowThresholdMs: 500); // 500ms for single lookup

                var normalizedEmail = email.ToLower().Trim();
                _logger.LogDebug("Finding active identity by email: {Email}", normalizedEmail);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var command = new CommandDefinition(
                    @"SELECT TOP 1 *
                      FROM Identities
                      WHERE IsActive = 1
                        AND PrimaryEmail IS NOT NULL
                        AND LOWER(PrimaryEmail) = @Email",
                    new { Email = normalizedEmail },
                    cancellationToken: cancellationToken,
                    commandTimeout: 30);

                var identity = await connection.QueryFirstOrDefaultAsync<Identity>(command);

                if (identity != null)
                {
                    _logger.LogDebug("Found identity {IdentityId} by email {Email} in {ElapsedMs}ms",
                        identity.Id, normalizedEmail, tracker.ElapsedMs);
                }

                return identity;
            }, nameof(FindIdentityByEmailAsync), _logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // Always re-throw cancellation
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error finding identity by email: {Email}", email);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(FindIdentityByEmailAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(FindIdentityByEmailAsync));
        }
    }

    public async Task<List<Identity>> FindIdentitiesByNameAsync(
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(FindIdentitiesByNameAsync), new { firstName, lastName });

        try
        {
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                _logger.LogWarning("FirstName and LastName cannot be null or whitespace");
                return new List<Identity>();
            }

            var normalizedFirstName = firstName.ToLower().Trim();
            var normalizedLastName = lastName.ToLower().Trim();
            _logger.LogDebug("Finding active identities by name: {FirstName} {LastName}", normalizedFirstName, normalizedLastName);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                @"SELECT *
                  FROM Identities
                  WHERE IsActive = 1
                    AND FirstName IS NOT NULL
                    AND LastName IS NOT NULL
                    AND LOWER(FirstName) = @FirstName
                    AND LOWER(LastName) = @LastName",
                new { FirstName = normalizedFirstName, LastName = normalizedLastName },
                cancellationToken: cancellationToken,
                commandTimeout: 30);

            var identities = (await connection.QueryAsync<Identity>(command)).ToList();

            _logger.LogInformation("Found {IdentityCount} identities matching {FirstName} {LastName}",
                identities.Count, normalizedFirstName, normalizedLastName);

            return identities;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error finding identities by name: {FirstName} {LastName}", firstName, lastName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(FindIdentitiesByNameAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(FindIdentitiesByNameAsync));
        }
    }

    public async Task<Identity?> FindIdentityByIdAsync(
        Guid identityId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(FindIdentityByIdAsync), new { identityId });

        try
        {
            if (identityId == Guid.Empty)
            {
                _logger.LogWarning("IdentityId cannot be empty");
                return null;
            }

            _logger.LogDebug("Finding identity by ID: {IdentityId}", identityId);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                "SELECT * FROM Identities WHERE Id = @IdentityId",
                new { IdentityId = identityId },
                cancellationToken: cancellationToken,
                commandTimeout: 30);

            var identity = await connection.QueryFirstOrDefaultAsync<Identity>(command);

            if (identity != null)
            {
                _logger.LogInformation("Found identity {IdentityId}", identityId);
            }
            else
            {
                _logger.LogDebug("No identity found for ID {IdentityId}", identityId);
            }

            return identity;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error finding identity by ID: {IdentityId}", identityId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(FindIdentityByIdAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(FindIdentityByIdAsync));
        }
    }

    /// <inheritdoc />
    public async Task<Identity?> FindIdentityByEmployeeIdAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        _logger.LogDebug("Finding identity by EmployeeId: {EmployeeId}", employeeId);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Look up the identity via linked objects that have an EmployeeId attribute
            var command = new CommandDefinition(
                @"SELECT i.* FROM Identities i
                  INNER JOIN Objects o ON o.IdentityId = i.Id
                  WHERE o.EmployeeId = @EmployeeId AND i.IsActive = 1",
                new { EmployeeId = employeeId },
                cancellationToken: cancellationToken,
                commandTimeout: 30);

            return await connection.QueryFirstOrDefaultAsync<Identity>(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding identity by EmployeeId: {EmployeeId}", employeeId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Identity?> FindIdentityByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        _logger.LogDebug("Finding identity by Username: {Username}", username);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Look up the identity via linked objects that have a Username (sAMAccountName)
            var command = new CommandDefinition(
                @"SELECT i.* FROM Identities i
                  INNER JOIN Objects o ON o.IdentityId = i.Id
                  WHERE o.Username = @Username AND i.IsActive = 1",
                new { Username = username },
                cancellationToken: cancellationToken,
                commandTimeout: 30);

            return await connection.QueryFirstOrDefaultAsync<Identity>(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding identity by Username: {Username}", username);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Identity?> FindIdentityByUPNAsync(
        string upn,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(upn))
            return null;

        _logger.LogDebug("Finding identity by UPN: {UPN}", upn);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Look up the identity via linked objects that have a UserPrincipalName
            var command = new CommandDefinition(
                @"SELECT i.* FROM Identities i
                  INNER JOIN Objects o ON o.IdentityId = i.Id
                  WHERE o.UserPrincipalName = @UPN AND i.IsActive = 1",
                new { UPN = upn },
                cancellationToken: cancellationToken,
                commandTimeout: 30);

            return await connection.QueryFirstOrDefaultAsync<Identity>(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding identity by UPN: {UPN}", upn);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Identity?> FindIdentityByDisplayNameAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        _logger.LogDebug("Finding identity by DisplayName: {DisplayName}", displayName);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Direct lookup on Identity.DisplayName
            var command = new CommandDefinition(
                @"SELECT * FROM Identities
                  WHERE DisplayName = @DisplayName AND IsActive = 1",
                new { DisplayName = displayName },
                cancellationToken: cancellationToken,
                commandTimeout: 30);

            return await connection.QueryFirstOrDefaultAsync<Identity>(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding identity by DisplayName: {DisplayName}", displayName);
            return null;
        }
    }

    public async Task<List<ObjectWithAttributes>> GetObjectsWithManagerAttributeAsync(
        Guid syncProjectRunId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetObjectsWithManagerAttributeAsync), new { syncProjectRunId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Query for objects with manager attribute that haven't been resolved yet
            var sql = @"
                SELECT DISTINCT o.Id, o.SourceConnectionId, o.SourceUniqueId, o.SourceType,
                       o.DisplayName, o.IdentityId, o.IsActive
                FROM Objects o
                INNER JOIN ObjectAttributes oa ON o.Id = oa.ObjectId
                INNER JOIN SyncAuditLogs sal ON o.Id = sal.ObjectId
                INNER JOIN SyncStepRuns ssr ON sal.SyncStepRunId = ssr.Id
                WHERE ssr.SyncProjectRunId = @SyncProjectRunId
                  AND oa.AttributeName = 'manager'
                  AND oa.AttributeValue IS NOT NULL
                  AND o.ManagerObjectId IS NULL
                  AND o.IsActive = 1
                ORDER BY o.DisplayName;
            ";

            var objects = (await connection.QueryAsync<IdentityObject>(sql,
                new { SyncProjectRunId = syncProjectRunId },
                commandTimeout: 120)).ToList();

            _logger.LogInformation("Found {Count} objects with manager attribute for run {RunId}",
                objects.Count, syncProjectRunId);

            // Load all attributes for these objects in a single query
            var objectIds = objects.Select(o => o.Id).ToList();
            var attributesSql = @"
                SELECT oa.*
                FROM ObjectAttributes oa
                WHERE oa.ObjectId IN @ObjectIds
                ORDER BY oa.ObjectId, oa.AttributeName;
            ";

            var allAttributes = (await connection.QueryAsync<ObjectAttribute>(attributesSql,
                new { ObjectIds = objectIds },
                commandTimeout: 30)).ToList();

            // Build result with attributes grouped by object
            var result = new List<ObjectWithAttributes>();
            foreach (var obj in objects)
            {
                var attributes = allAttributes.Where(a => a.ObjectId == obj.Id).ToList();
                result.Add(new ObjectWithAttributes
                {
                    Object = obj,
                    Attributes = attributes
                });
            }

            return result;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetObjectsWithManagerAttributeAsync));
        }
    }

    /// <summary>
    /// Update an object's ManagerObjectId after manager resolution.
    /// Sets the ManagerObjectId foreign key to the manager's Object record.
    /// </summary>
    public async Task UpdateObjectManagerIdAsync(
        Guid objectId,
        Guid managerObjectId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateObjectManagerIdAsync), new { objectId, managerObjectId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE Objects
                SET ManagerObjectId = @ManagerObjectId,
                    LastSyncedAt = GETUTCDATE()
                WHERE Id = @ObjectId;
            ";

            await connection.ExecuteAsync(sql,
                new { ObjectId = objectId, ManagerObjectId = managerObjectId },
                commandTimeout: 30);

            _logger.LogDebug("Updated object {ObjectId} with ManagerObjectId {ManagerObjectId}",
                objectId, managerObjectId);
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateObjectManagerIdAsync));
        }
    }

    /// <summary>
    /// Bulk update ManagerObjectId for multiple objects in a single database operation.
    /// Uses a temp table + UPDATE JOIN pattern for maximum performance.
    /// </summary>
    public async Task<int> BulkUpdateManagerIdsAsync(
        List<(Guid ObjectId, Guid ManagerObjectId)> updates,
        CancellationToken cancellationToken = default)
    {
        if (!updates.Any()) return 0;

        _logger.LogInformation("BULK UPDATE: Updating {Count} manager relationships...", updates.Count);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Create temp table
            await connection.ExecuteAsync(@"
                CREATE TABLE #ManagerUpdates (
                    ObjectId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    ManagerObjectId UNIQUEIDENTIFIER NOT NULL
                )", transaction: transaction);

            // Batch insert updates (80 per batch to stay under 2100 param limit: 2 columns x 80 = 160)
            const int batchSize = 500; // Safe with 2 columns
            var batches = updates
                .Select((u, i) => new { u, i })
                .GroupBy(x => x.i / batchSize)
                .Select(g => g.Select(x => x.u).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                var insertSql = "INSERT INTO #ManagerUpdates (ObjectId, ManagerObjectId) VALUES ";
                var valuesClauses = new List<string>();
                var parameters = new DynamicParameters();

                for (int i = 0; i < batch.Count; i++)
                {
                    var (objId, mgrId) = batch[i];
                    valuesClauses.Add($"(@o{i}, @m{i})");
                    parameters.Add($"o{i}", objId);
                    parameters.Add($"m{i}", mgrId);
                }

                await connection.ExecuteAsync(
                    insertSql + string.Join(",", valuesClauses),
                    parameters, transaction, commandTimeout: 120);
            }

            // Single UPDATE JOIN for all records
            var rowsAffected = await connection.ExecuteAsync(@"
                UPDATE o
                SET o.ManagerObjectId = u.ManagerObjectId,
                    o.LastSyncedAt = GETUTCDATE()
                FROM Objects o
                INNER JOIN #ManagerUpdates u ON o.Id = u.ObjectId",
                transaction: transaction, commandTimeout: 120);

            await transaction.CommitAsync(cancellationToken);

            sw.Stop();
            _logger.LogInformation("BULK UPDATE COMPLETE: {Count} manager relationships in {Ms}ms",
                rowsAffected, sw.ElapsedMilliseconds);

            return rowsAffected;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "BULK UPDATE FAILED after {Ms}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }


    /// <summary>
    /// Resolve manager relationships for all objects in a connection by matching DN columns directly.
    /// Uses ManagerSourceId (DN of manager) to match against other objects' DN column.
    /// OPTIMIZED: Single UPDATE JOIN, no ObjectAttributes table, maximum performance.
    /// </summary>
    public async Task<int> ResolveManagerRelationshipsAsync(
        Guid sourceConnectionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MANAGER RESOLUTION: Starting for connection {ConnectionId}", sourceConnectionId);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // DIAGNOSTIC: Show sample ManagerSourceId values and DN values to debug mismatches
        try
        {
            var sampleSql = @"
                SELECT TOP 3 DisplayName, ManagerSourceId FROM Objects
                WHERE SourceConnectionId = @ConnectionId AND IsActive = 1
                AND ManagerSourceId IS NOT NULL AND ManagerSourceId != ''
                AND ManagerObjectId IS NULL;

                SELECT TOP 3 DisplayName, DN FROM Objects
                WHERE SourceConnectionId = @ConnectionId AND IsActive = 1
                AND DN IS NOT NULL AND DN != '';";

            using var multi = await connection.QueryMultipleAsync(sampleSql, new { ConnectionId = sourceConnectionId });
            var needingResolution = (await multi.ReadAsync<dynamic>()).ToList();
            var withDN = (await multi.ReadAsync<dynamic>()).ToList();

            _logger.LogWarning("MANAGER RESOLUTION DIAGNOSTIC - Objects needing resolution (ManagerSourceId values):");
            foreach (var obj in needingResolution)
            {
                _logger.LogWarning("  - {DisplayName}: ManagerSourceId = '{ManagerSourceId}'", obj.DisplayName, obj.ManagerSourceId);
            }
            _logger.LogWarning("MANAGER RESOLUTION DIAGNOSTIC - Potential managers (DN values):");
            foreach (var obj in withDN)
            {
                _logger.LogWarning("  - {DisplayName}: DN = '{DN}'", obj.DisplayName, obj.DN);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DIAGNOSTIC: Error getting sample values");
        }

        try
        {
            // OPTIMIZED: Use ManagerSourceId and DN columns directly (no ObjectAttributes joins)
            var sql = @"
                UPDATE o
                SET o.ManagerObjectId = mgr.Id,
                    o.LastSyncedAt = GETUTCDATE()
                FROM Objects o
                INNER JOIN Objects mgr ON
                    LOWER(o.ManagerSourceId) = LOWER(mgr.DN)
                    AND mgr.SourceConnectionId = @ConnectionId
                    AND mgr.IsActive = 1
                WHERE o.SourceConnectionId = @ConnectionId
                    AND o.IsActive = 1
                    AND o.ManagerSourceId IS NOT NULL
                    AND o.ManagerSourceId != ''
                    AND o.ManagerObjectId IS NULL
                    AND mgr.Id != o.Id";

            var rowsAffected = await connection.ExecuteAsync(sql,
                new { ConnectionId = sourceConnectionId },
                commandTimeout: 300);

            sw.Stop();
            _logger.LogInformation("MANAGER RESOLUTION: Updated {Count} objects in {Ms}ms",
                rowsAffected, sw.ElapsedMilliseconds);

            return rowsAffected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MANAGER RESOLUTION FAILED after {Ms}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Get manager resolution statistics for a connection.
    /// Returns counts needed for step run metrics without loading actual objects.
    /// </summary>
    public async Task<(int TotalWithManagerDN, int AlreadyResolved, int NeedingResolution)> GetManagerResolutionStatsAsync(
        Guid sourceConnectionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = @"
            SELECT
                (SELECT COUNT(*) FROM Objects
                 WHERE SourceConnectionId = @ConnectionId AND IsActive = 1
                 AND ManagerSourceId IS NOT NULL AND ManagerSourceId != '') AS TotalWithManagerDN,
                (SELECT COUNT(*) FROM Objects
                 WHERE SourceConnectionId = @ConnectionId AND IsActive = 1
                 AND ManagerObjectId IS NOT NULL) AS AlreadyResolved,
                (SELECT COUNT(*) FROM Objects
                 WHERE SourceConnectionId = @ConnectionId AND IsActive = 1
                 AND ManagerSourceId IS NOT NULL AND ManagerSourceId != ''
                 AND ManagerObjectId IS NULL) AS NeedingResolution";

        var result = await connection.QuerySingleAsync<(int TotalWithManagerDN, int AlreadyResolved, int NeedingResolution)>(
            sql, new { ConnectionId = sourceConnectionId }, commandTimeout: 30);

        return result;
    }

    /// <summary>
    /// Gets objects that had ManagerSourceId set, with their resolution status.
    /// Call this AFTER ResolveManagerRelationshipsAsync to see which were resolved vs skipped.
    /// </summary>
    public async Task<List<ManagerResolutionAuditItem>> GetManagerResolutionDetailsAsync(
        Guid sourceConnectionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Get all objects that have a ManagerSourceId (whether resolved or not)
        var sql = @"
            SELECT
                o.Id AS ObjectId,
                o.DisplayName,
                o.SourceUniqueId,
                o.Email,
                o.Username,
                o.UserPrincipalName,
                o.ManagerSourceId,
                o.ManagerObjectId,
                mgr.DisplayName AS ManagerDisplayName
            FROM Objects o
            LEFT JOIN Objects mgr ON o.ManagerObjectId = mgr.Id
            WHERE o.SourceConnectionId = @ConnectionId
                AND o.IsActive = 1
                AND o.ManagerSourceId IS NOT NULL
                AND o.ManagerSourceId != ''
            ORDER BY o.DisplayName";

        var results = await connection.QueryAsync<ManagerResolutionAuditItem>(
            sql, new { ConnectionId = sourceConnectionId }, commandTimeout: 120);

        return results.AsList();
    }

    /// <summary>
    /// Resolve group owner relationships for all groups in a connection.
    /// </summary>
    public async Task<int> ResolveGroupOwnerRelationshipsAsync(
        Guid sourceConnectionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GROUP OWNER RESOLUTION: Starting for connection {ConnectionId}", sourceConnectionId);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var sql = @"
                UPDATE o
                SET o.OwnerObjectId = owner.Id,
                    o.LastSyncedAt = GETUTCDATE()
                FROM Objects o
                INNER JOIN ObjectAttributes oa_owner ON oa_owner.ObjectId = o.Id
                    AND oa_owner.AttributeName = 'managedBy'
                INNER JOIN ObjectAttributes oa_dn ON oa_dn.AttributeName = 'distinguishedName'
                    AND oa_dn.AttributeValue = oa_owner.AttributeValue
                INNER JOIN Objects owner ON owner.Id = oa_dn.ObjectId
                WHERE o.SourceConnectionId = @ConnectionId
                    AND o.ObjectClass = 'group'
                    AND o.OwnerObjectId IS NULL";

            var rowsAffected = await connection.ExecuteAsync(sql,
                new { ConnectionId = sourceConnectionId },
                commandTimeout: 300);

            sw.Stop();
            _logger.LogInformation("GROUP OWNER RESOLUTION: Updated {Count} groups in {Ms}ms",
                rowsAffected, sw.ElapsedMilliseconds);

            return rowsAffected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GROUP OWNER RESOLUTION FAILED after {Ms}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Find an object by Distinguished Name (DN) from the DN attribute.
    /// Used for resolving manager and owner relationships from Active Directory.
    /// </summary>
    public async Task<ObjectWithAttributes?> FindObjectByDNAsync(
        Guid sourceConnectionId,
        string distinguishedName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(FindObjectByDNAsync),
            new { sourceConnectionId, distinguishedName });

        // Parameter validation before retry wrapper
        if (sourceConnectionId == Guid.Empty)
        {
            _logger.LogWarning("SourceConnectionId cannot be empty");
            throw new ArgumentException("SourceConnectionId cannot be empty", nameof(sourceConnectionId));
        }

        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            _logger.LogWarning("DN cannot be null or whitespace");
            throw new ArgumentException("DN cannot be null or whitespace", nameof(distinguishedName));
        }

        try
        {
            return await SyncRepositoryHelpers.ExecuteWithRetryAsync(async () =>
            {
                using var tracker = new SyncRepositoryHelpers.PerformanceTracker(_logger, nameof(FindObjectByDNAsync),
                    new { sourceConnectionId }, slowThresholdMs: 500); // 500ms for single lookup

                _logger.LogDebug("Finding object by DN: {SourceConnectionId} / {DN}",
                    sourceConnectionId, distinguishedName);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Look up object by DN attribute (case-insensitive)
                var sql = @"
                    SELECT o.*
                    FROM Objects o
                    WHERE o.SourceConnectionId = @SourceConnectionId
                      AND o.DN = @DN
                      AND o.IsActive = 1;
                ";

                var identityObject = await connection.QueryFirstOrDefaultAsync<IdentityObject>(sql,
                    new { SourceConnectionId = sourceConnectionId, DN = distinguishedName });

                if (identityObject == null)
                {
                    _logger.LogDebug("Object not found for DN: {DN}", distinguishedName);
                    return null;
                }

                // Load attributes for this object
                var attributesSql = @"
                    SELECT *
                    FROM ObjectAttributes
                    WHERE ObjectId = @ObjectId;
                ";

                var attributes = (await connection.QueryAsync<ObjectAttribute>(attributesSql,
                    new { ObjectId = identityObject.Id })).ToList();

                _logger.LogDebug("Found object {ObjectId} ({DisplayName}) by DN with {AttributeCount} attributes in {ElapsedMs}ms",
                    identityObject.Id, identityObject.DisplayName, attributes.Count, tracker.ElapsedMs);

                return new ObjectWithAttributes
                {
                    Object = identityObject,
                    Attributes = attributes
                };
            }, nameof(FindObjectByDNAsync), _logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // Always re-throw cancellation
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error finding object by DN: {SourceConnectionId} / {DN}",
                sourceConnectionId, distinguishedName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(FindObjectByDNAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(FindObjectByDNAsync));
        }
    }

    /// <summary>
    /// Update a group's OwnerId after owner resolution.
    /// </summary>
    public async Task UpdateGroupOwnerIdAsync(
        Guid groupId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateGroupOwnerIdAsync), new { groupId, ownerId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE Groups
                SET OwnerId = @OwnerId,
                    LastSyncedAt = GETUTCDATE()
                WHERE Id = @GroupId;
            ";

            await connection.ExecuteAsync(sql,
                new { GroupId = groupId, OwnerId = ownerId },
                commandTimeout: 30);

            _logger.LogDebug("Updated group {GroupId} with OwnerId {OwnerId}",
                groupId, ownerId);
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateGroupOwnerIdAsync));
        }
    }

    /// <summary>
    /// Get all groups from a sync run that have an owner (managedBy) attribute.
    /// Used by PostSyncTaskService for GroupOwnerAssignment task.
    /// </summary>
    public async Task<List<GroupWithAttributes>> GetGroupsWithOwnerAttributeAsync(
        Guid syncProjectRunId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetGroupsWithOwnerAttributeAsync), new { syncProjectRunId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Query for groups with managedBy attribute
            // Note: Groups don't have audit log entries with GroupId, so we get all groups
            // with managedBy attribute and OwnerId = NULL (not yet resolved)
            var sql = @"
                SELECT DISTINCT g.Id, g.SourceConnectionId, g.SourceUniqueId,
                       g.Name, g.Description, g.IsActive
                FROM Groups g
                INNER JOIN GroupAttributes ga ON g.Id = ga.GroupId
                WHERE ga.AttributeName = 'managedBy'
                  AND ga.AttributeValue IS NOT NULL
                  AND g.OwnerId IS NULL
                  AND g.IsActive = 1
                ORDER BY g.Name;
            ";

            var groups = (await connection.QueryAsync<Models.Group>(sql,
                new { SyncProjectRunId = syncProjectRunId },
                commandTimeout: 120)).ToList();

            _logger.LogInformation("Found {Count} groups with managedBy attribute for run {RunId}",
                groups.Count, syncProjectRunId);

            // Load attributes for each group
            var result = new List<GroupWithAttributes>();
            foreach (var group in groups)
            {
                var attributesSql = @"
                    SELECT AttributeName, AttributeValue, CreatedAt, ModifiedAt
                    FROM GroupAttributes
                    WHERE GroupId = @GroupId
                    ORDER BY AttributeName;
                ";

                var attributes = (await connection.QueryAsync<Models.GroupAttribute>(attributesSql,
                    new { GroupId = group.Id },
                    commandTimeout: 30)).ToList();

                result.Add(new GroupWithAttributes
                {
                    Group = group,
                    Attributes = attributes
                });
            }

            return result;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetGroupsWithOwnerAttributeAsync));
        }
    }

    public async Task<List<IdentityManagerInfo>> GetIdentitiesNeedingManagerAssignmentAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetIdentitiesNeedingManagerAssignmentAsync));

        try
        {
            // Query logic:
            // 1. Get identities with an authoritative source object
            // 2. That authoritative object has a ManagerObjectId set
            // 3. That manager object is linked to an identity (has IdentityId)
            // 4. The identity's ManagerIdentityId is NULL or different from the expected value
            const string sql = @"
                SELECT
                    i.Id as IdentityId,
                    i.DisplayName as IdentityDisplayName,
                    i.AuthoritativeSourceId as AuthoritativeObjectId,
                    authObj.ManagerObjectId,
                    mgrObj.IdentityId as ManagerIdentityId
                FROM Identities i
                INNER JOIN Objects authObj ON i.AuthoritativeSourceId = authObj.Id
                LEFT JOIN Objects mgrObj ON authObj.ManagerObjectId = mgrObj.Id
                WHERE
                    i.IsActive = 1
                    AND authObj.ManagerObjectId IS NOT NULL
                    AND (
                        -- Case 1: Manager object exists and is linked, but identity doesn't have manager set
                        (mgrObj.IdentityId IS NOT NULL AND i.ManagerIdentityId IS NULL)
                        OR
                        -- Case 2: Manager changed - identity has wrong manager
                        (mgrObj.IdentityId IS NOT NULL AND i.ManagerIdentityId != mgrObj.IdentityId)
                        OR
                        -- Case 3: Manager was removed - authoritative object has no manager, but identity still has one
                        (authObj.ManagerObjectId IS NULL AND i.ManagerIdentityId IS NOT NULL)
                    )
                UNION
                -- Also include identities where manager should be cleared
                SELECT
                    i.Id as IdentityId,
                    i.DisplayName as IdentityDisplayName,
                    i.AuthoritativeSourceId as AuthoritativeObjectId,
                    NULL as ManagerObjectId,
                    NULL as ManagerIdentityId
                FROM Identities i
                INNER JOIN Objects authObj ON i.AuthoritativeSourceId = authObj.Id
                WHERE
                    i.IsActive = 1
                    AND authObj.ManagerObjectId IS NULL
                    AND i.ManagerIdentityId IS NOT NULL";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var results = (await connection.QueryAsync<IdentityManagerInfo>(
                new CommandDefinition(sql, cancellationToken: cancellationToken, commandTimeout: 300))).ToList();

            _logger.LogInformation("Found {Count} identities needing manager assignment", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetIdentitiesNeedingManagerAssignmentAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetIdentitiesNeedingManagerAssignmentAsync));
        }
    }

    /// <inheritdoc />
    public async Task UpdateIdentityManagerIdAsync(
        Guid identityId,
        Guid? managerIdentityId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateIdentityManagerIdAsync), new { identityId, managerIdentityId });

        try
        {
            const string sql = @"
                UPDATE Identities
                SET ManagerIdentityId = @ManagerIdentityId,
                    ModifiedAt = GETUTCDATE()
                WHERE Id = @IdentityId";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { IdentityId = identityId, ManagerIdentityId = managerIdentityId },
                cancellationToken: cancellationToken,
                commandTimeout: 60));

            _logger.LogDebug("Updated ManagerIdentityId for identity {IdentityId} to {ManagerIdentityId}",
                identityId, managerIdentityId?.ToString() ?? "NULL");
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateIdentityManagerIdAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateIdentityManagerIdAsync));
        }
    }

    /// <inheritdoc />
    public async Task<int> BulkUpdateIdentityManagerIdsAsync(
        List<(Guid IdentityId, Guid ManagerIdentityId)> updates,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkUpdateIdentityManagerIdsAsync), new { updateCount = updates.Count });

        if (updates == null || !updates.Any())
        {
            return 0;
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Use a transaction for bulk operation
            using var transaction = connection.BeginTransaction();

            int totalUpdated = 0;
            const string sql = @"
                UPDATE Identities
                SET ManagerIdentityId = @ManagerIdentityId,
                    ModifiedAt = GETUTCDATE()
                WHERE Id = @IdentityId";

            // Process in batches of 100 for efficiency
            const int batchSize = 100;
            for (int i = 0; i < updates.Count; i += batchSize)
            {
                var batch = updates.Skip(i).Take(batchSize);

                foreach (var (identityId, managerIdentityId) in batch)
                {
                    var affected = await connection.ExecuteAsync(
                        sql,
                        new { IdentityId = identityId, ManagerIdentityId = managerIdentityId },
                        transaction: transaction,
                        commandTimeout: 60);
                    totalUpdated += affected;
                }
            }

            transaction.Commit();

            _logger.LogInformation("Bulk updated ManagerIdentityId for {Count} identities", totalUpdated);
            return totalUpdated;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkUpdateIdentityManagerIdsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkUpdateIdentityManagerIdsAsync));
        }
    }
}
