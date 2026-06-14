using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Services;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Object/Group/Identity CRUD, bulk load, memberships, tags, and audit operations.
/// High-performance Dapper-based repository.
/// </summary>
public class SyncObjectRepository : DapperRepositoryBase, ISyncObjectRepository
{
    private static readonly SemaphoreSlim _sqlBulkCopySemaphore = new SemaphoreSlim(1, 1);
    private readonly IAuditLogService _auditLogService;

    public SyncObjectRepository(IConfiguration configuration, IGlobalLogger logger, IAuditLogService auditLogService)
        : base(configuration, logger)
    {
        _auditLogService = auditLogService;
    }

    public async Task<ObjectWithAttributes?> FindObjectBySourceUniqueIdAsync(
        Guid sourceConnectionId,
        string sourceUniqueId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(FindObjectBySourceUniqueIdAsync),
            new { sourceConnectionId, sourceUniqueId });

        try
        {
            if (sourceConnectionId == Guid.Empty)
            {
                _logger.LogWarning("SourceConnectionId cannot be empty");
                throw new ArgumentException("SourceConnectionId cannot be empty", nameof(sourceConnectionId));
            }

            if (string.IsNullOrWhiteSpace(sourceUniqueId))
            {
                _logger.LogWarning("SourceUniqueId cannot be null or whitespace");
                throw new ArgumentException("SourceUniqueId cannot be null or whitespace", nameof(sourceUniqueId));
            }

            _logger.LogDebug("Finding object by source unique ID: {SourceConnectionId} / {SourceUniqueId}",
                sourceConnectionId, sourceUniqueId);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                "usp_FindObjectBySourceUniqueId",
                new { SourceConnectionId = sourceConnectionId, SourceUniqueId = sourceUniqueId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken,
                commandTimeout: 300);

            using var multi = await connection.QueryMultipleAsync(command);

            var identityObject = await multi.ReadFirstOrDefaultAsync<IdentityObject>();
            if (identityObject == null)
            {
                _logger.LogDebug("Object not found for source unique ID: {SourceUniqueId}", sourceUniqueId);
                return null;
            }

            var attributes = (await multi.ReadAsync<ObjectAttribute>()).ToList();

            _logger.LogInformation("Successfully found object {ObjectId} with {AttributeCount} attributes",
                identityObject.Id, attributes.Count);

            return new ObjectWithAttributes
            {
                Object = identityObject,
                Attributes = attributes
            };
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error finding object by source unique ID: {SourceConnectionId} / {SourceUniqueId}",
                sourceConnectionId, sourceUniqueId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(FindObjectBySourceUniqueIdAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(FindObjectBySourceUniqueIdAsync));
        }
    }

    public async Task<Dictionary<string, ObjectWithAttributes>> BulkLoadExistingObjectsAsync(
        Guid sourceConnectionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkLoadExistingObjectsAsync),
            new { sourceConnectionId });

        if (sourceConnectionId == Guid.Empty)
        {
            _logger.LogWarning("SourceConnectionId cannot be empty");
            throw new ArgumentException("SourceConnectionId cannot be empty", nameof(sourceConnectionId));
        }

        try
        {
            return await SyncRepositoryHelpers.ExecuteWithRetryAsync(async () =>
            {
                using var tracker = new SyncRepositoryHelpers.PerformanceTracker(_logger, nameof(BulkLoadExistingObjectsAsync),
                    new { sourceConnectionId }, slowThresholdMs: 5000);

                _logger.LogDebug("Bulk loading ALL existing objects for connection: {SourceConnectionId}", sourceConnectionId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var command = new CommandDefinition(
                    @"-- Load all objects for this source connection
                      SELECT o.*
                      FROM Objects o
                      WHERE o.SourceConnectionId = @SourceConnectionId;

                      -- Load all attributes for these objects
                      SELECT oa.*
                      FROM ObjectAttributes oa
                      INNER JOIN Objects o ON oa.ObjectId = o.Id
                      WHERE o.SourceConnectionId = @SourceConnectionId;",
                    new { SourceConnectionId = sourceConnectionId },
                    cancellationToken: cancellationToken,
                    commandTimeout: 300);

                using var multi = await connection.QueryMultipleAsync(command);

                var objects = (await multi.ReadAsync<IdentityObject>()).ToList();
                tracker.LogIfSlow("Objects loaded");

                var attributes = (await multi.ReadAsync<ObjectAttribute>()).ToList();
                tracker.LogIfSlow("Attributes loaded");

                _logger.LogInformation("Bulk loaded {ObjectCount} objects with {AttributeCount} attributes in {ElapsedMs}ms",
                    objects.Count, attributes.Count, tracker.ElapsedMs);

                var result = new Dictionary<string, ObjectWithAttributes>(objects.Count, StringComparer.OrdinalIgnoreCase);

                var attributesByObjectId = attributes.ToLookup(a => a.ObjectId);

                foreach (var obj in objects)
                {
                    if (string.IsNullOrWhiteSpace(obj.SourceUniqueId))
                    {
                        _logger.LogWarning("Skipping object {ObjectId} with null/empty SourceUniqueId", obj.Id);
                        continue;
                    }

                    var objectWithAttrs = new ObjectWithAttributes
                    {
                        Object = obj,
                        Attributes = attributesByObjectId[obj.Id].ToList()
                    };

                    result[obj.SourceUniqueId] = objectWithAttrs;
                }

                _logger.LogInformation("PERFORMANCE BOOST: Created in-memory dictionary with {Count} objects for O(1) lookups in {ElapsedMs}ms",
                    result.Count, tracker.ElapsedMs);

                return result;
            }, nameof(BulkLoadExistingObjectsAsync), _logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error bulk loading objects for connection: {SourceConnectionId}", sourceConnectionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkLoadExistingObjectsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkLoadExistingObjectsAsync));
        }
    }

    public async Task<IdentityLookupCache> BulkLoadIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkLoadIdentitiesAsync));

        try
        {
            return await SyncRepositoryHelpers.ExecuteWithRetryAsync(async () =>
            {
                using var tracker = new SyncRepositoryHelpers.PerformanceTracker(_logger, nameof(BulkLoadIdentitiesAsync),
                    slowThresholdMs: 5000);

                _logger.LogDebug("Bulk loading ALL active identities for in-memory caching");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var command = new CommandDefinition(
                    @"SELECT *
                      FROM Identities
                      WHERE IsActive = 1
                      ORDER BY LastSeenAt DESC",
                    cancellationToken: cancellationToken,
                    commandTimeout: 300);

                var identities = (await connection.QueryAsync<Identity>(command)).ToList();
                tracker.LogIfSlow("Query completed");

                _logger.LogInformation("Bulk loaded {IdentityCount} active identities in {ElapsedMs}ms",
                    identities.Count, tracker.ElapsedMs);

                var emailLookup = new Dictionary<string, Identity>(identities.Count, StringComparer.OrdinalIgnoreCase);
                var nameLookup = new Dictionary<(string firstName, string lastName), List<Identity>>();

                foreach (var identity in identities)
                {
                    if (!string.IsNullOrWhiteSpace(identity.PrimaryEmail))
                    {
                        var emailKey = identity.PrimaryEmail.ToLower().Trim();
                        if (!emailLookup.ContainsKey(emailKey))
                        {
                            emailLookup[emailKey] = identity;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(identity.FirstName) && !string.IsNullOrWhiteSpace(identity.LastName))
                    {
                        var nameKey = (
                            identity.FirstName.ToLower().Trim(),
                            identity.LastName.ToLower().Trim()
                        );

                        if (!nameLookup.TryGetValue(nameKey, out var nameList))
                        {
                            nameList = new List<Identity>();
                            nameLookup[nameKey] = nameList;
                        }
                        nameList.Add(identity);
                    }
                }

                _logger.LogInformation("PERFORMANCE BOOST: Created Identity lookup caches in {ElapsedMs}ms - Email: {EmailCount} entries, Name: {NameCount} entries",
                    tracker.ElapsedMs, emailLookup.Count, nameLookup.Count);

                return new IdentityLookupCache
                {
                    ByEmail = emailLookup,
                    ByName = nameLookup
                };
            }, nameof(BulkLoadIdentitiesAsync), _logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error bulk loading identities");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkLoadIdentitiesAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkLoadIdentitiesAsync));
        }
    }

    public async Task<UpsertResult> UpsertObjectWithAttributesAsync(
        IdentityObject identityObject,
        List<ObjectAttribute> attributes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpsertObjectWithAttributesAsync),
            new { objectId = identityObject.Id, sourceUniqueId = identityObject.SourceUniqueId, attributeCount = attributes.Count });

        try
        {
            if (identityObject == null)
            {
                _logger.LogWarning("IdentityObject cannot be null");
                throw new ArgumentNullException(nameof(identityObject));
            }

            if (identityObject.SourceConnectionId == Guid.Empty)
            {
                _logger.LogWarning("IdentityObject.SourceConnectionId cannot be empty");
                throw new ArgumentException("SourceConnectionId cannot be empty", nameof(identityObject));
            }

            if (string.IsNullOrWhiteSpace(identityObject.SourceUniqueId))
            {
                _logger.LogWarning("IdentityObject.SourceUniqueId cannot be null or whitespace");
                throw new ArgumentException("SourceUniqueId cannot be null or whitespace", nameof(identityObject));
            }

            _logger.LogDebug("Upserting object {ObjectId} with {AttributeCount} attributes",
                identityObject.Id, attributes.Count);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var attributesJson = attributes.Any()
                ? JsonSerializer.Serialize(attributes.Select(a => new
                {
                    a.AttributeName,
                    a.AttributeValue,
                    a.DataType
                }))
                : null;

            _logger.LogDebug("Serialized {AttributeCount} attributes to JSON", attributes.Count);

            var command = new CommandDefinition(
                "usp_UpsertObjectWithAttributes",
                new
                {
                    identityObject.Id,
                    identityObject.SourceConnectionId,
                    identityObject.SourceUniqueId,
                    identityObject.SourceType,
                    identityObject.DisplayName,
                    identityObject.Email,
                    identityObject.Username,
                    identityObject.FirstName,
                    identityObject.LastName,
                    identityObject.Department,
                    identityObject.JobTitle,
                    identityObject.Phone,
                    identityObject.ManagerSourceId,
                    identityObject.IdentityId,
                    identityObject.IsActive,
                    identityObject.IsAuthoritative,
                    identityObject.MatchConfidence,
                    identityObject.MatchMethod,
                    identityObject.LastSyncedAt,
                    identityObject.LastSeenAt,
                    identityObject.IsBuiltIn,
                    identityObject.IsAdminSDHolder,
                    AttributesJson = attributesJson
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken,
                commandTimeout: 300);

            var result = await connection.QuerySingleAsync<UpsertResult>(command);

            _logger.LogInformation("Successfully upserted object {ObjectId}, IsNew: {IsNew}, AttributesInserted: {AttributesInserted}",
                result.Id, result.IsNew, result.AttributesInserted);

            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error upserting object {ObjectId}",
                identityObject?.Id ?? Guid.Empty);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpsertObjectWithAttributesAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpsertObjectWithAttributesAsync));
        }
    }

    public async Task<BulkUpsertResult> BulkUpsertObjectsAsync(
        List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkUpsertObjectsAsync),
            new { objectCount = objectsWithAttributes?.Count ?? 0 });

        SqlConnection connection = null;
        var startTime = DateTime.UtcNow;

        try
        {
            if (objectsWithAttributes == null)
            {
                _logger.LogWarning("ObjectsWithAttributes cannot be null");
                throw new ArgumentNullException(nameof(objectsWithAttributes));
            }

            if (!objectsWithAttributes.Any())
            {
                _logger.LogDebug("No objects to upsert");
                return new BulkUpsertResult { ObjectsProcessed = 0, ObjectsCreated = 0, ObjectsUpdated = 0, AttributesAffected = 0 };
            }

            _logger.LogInformation("BULK UPSERT: Processing {Count} objects in single database call", objectsWithAttributes.Count);

            connection = new SqlConnection(_connectionString);
            _logger.LogInformation("DB CONNECTION: Creating new connection (State: {State})", connection.State);

            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("DB CONNECTION: Connection opened successfully (State: {State}, Database: {Database})",
                connection.State, connection.Database);

            var objectsJson = JsonSerializer.Serialize(objectsWithAttributes.Select(item => new
            {
                Id = item.identityObject.Id,
                SourceConnectionId = item.identityObject.SourceConnectionId,
                SourceUniqueId = item.identityObject.SourceUniqueId,
                SourceType = item.identityObject.SourceType,
                ObjectClass = item.identityObject.ObjectClass,
                DisplayName = item.identityObject.DisplayName,
                Email = item.identityObject.Email,
                Username = item.identityObject.Username,
                FirstName = item.identityObject.FirstName,
                LastName = item.identityObject.LastName,
                Department = item.identityObject.Department,
                JobTitle = item.identityObject.JobTitle,
                Phone = item.identityObject.Phone,
                DN = item.identityObject.DN,
                CN = item.identityObject.CN,
                ManagerSourceId = item.identityObject.ManagerSourceId,
                IdentityId = item.identityObject.IdentityId,
                IsActive = item.identityObject.IsActive,
                IsAuthoritative = item.identityObject.IsAuthoritative,
                MatchConfidence = item.identityObject.MatchConfidence,
                MatchMethod = item.identityObject.MatchMethod,
                IsBuiltIn = item.identityObject.IsBuiltIn,
                IsAdminSDHolder = item.identityObject.IsAdminSDHolder,
                Attributes = item.attributes.Select(a => new
                {
                    a.AttributeName,
                    a.AttributeValue,
                    a.DataType
                })
            }));

            _logger.LogInformation("SERIALIZATION: Serialized {ObjectCount} objects with attributes to JSON ({JsonSize:N0} bytes)",
                objectsWithAttributes.Count, objectsJson.Length);

            var command = new CommandDefinition(
                "usp_BulkUpsertObjects",
                new { ObjectsJson = objectsJson },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken,
                commandTimeout: 120);

            _logger.LogInformation("EXECUTING STORED PROCEDURE: usp_BulkUpsertObjects (Timeout: 120s, Connection State: {State})",
                connection.State);

            var dbCallStart = DateTime.UtcNow;
            BulkUpsertResult result;

            try
            {
                result = await connection.QuerySingleAsync<BulkUpsertResult>(command);
                var dbCallDuration = (DateTime.UtcNow - dbCallStart).TotalSeconds;

                _logger.LogInformation("STORED PROCEDURE COMPLETED: Duration={Duration:F2}s, Connection State: {State}",
                    dbCallDuration, connection.State);
            }
            catch (SqlException sqlEx) when (sqlEx.Number == -2)
            {
                var dbCallDuration = (DateTime.UtcNow - dbCallStart).TotalSeconds;
                _logger.LogError("DATABASE TIMEOUT: Operation exceeded 120 second limit (Duration: {Duration:F2}s, Connection State: {State})",
                    dbCallDuration, connection.State);
                throw new TimeoutException($"Bulk upsert operation timed out after {dbCallDuration:F2} seconds. This may indicate a database performance issue or deadlock.", sqlEx);
            }
            catch (SqlException sqlEx)
            {
                var dbCallDuration = (DateTime.UtcNow - dbCallStart).TotalSeconds;
                _logger.LogError(sqlEx, "DATABASE ERROR: SQL Exception during bulk upsert (Duration: {Duration:F2}s, SQL Error: {ErrorNumber}, Connection State: {State})",
                    dbCallDuration, sqlEx.Number, connection.State);
                throw;
            }

            _logger.LogInformation("BULK UPSERT COMPLETE: Processed={Processed}, Created={Created}, Updated={Updated}, Attributes={Attributes}",
                result.ObjectsProcessed, result.ObjectsCreated, result.ObjectsUpdated, result.AttributesAffected);

            return result;
        }
        catch (TimeoutException ex)
        {
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "TIMEOUT: Bulk upsert operation timed out after {Duration:F2}s with {Count} objects. Connection State: {State}",
                totalDuration, objectsWithAttributes?.Count ?? 0, connection?.State.ToString() ?? "NULL");
            throw;
        }
        catch (SqlException ex)
        {
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "SQL ERROR: Database error bulk upserting {Count} objects (Duration: {Duration:F2}s, SQL Error: {ErrorNumber}, Connection State: {State})",
                objectsWithAttributes?.Count ?? 0, totalDuration, ex.Number, connection?.State.ToString() ?? "NULL");
            throw;
        }
        catch (Exception ex)
        {
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "UNEXPECTED ERROR: Bulk upsert failed with {Count} objects (Duration: {Duration:F2}s, Connection State: {State})",
                objectsWithAttributes?.Count ?? 0, totalDuration, connection?.State.ToString() ?? "NULL");
            throw;
        }
        finally
        {
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;

            if (connection != null)
            {
                var finalState = connection.State;
                _logger.LogInformation("DB CONNECTION CLEANUP: Disposing connection (State: {State}, Total Duration: {Duration:F2}s)",
                    finalState, totalDuration);

                try
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        connection.Close();
                        _logger.LogDebug("DB CONNECTION: Connection closed explicitly");
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "CONNECTION CLEANUP WARNING: Error while closing connection");
                }
                finally
                {
                    connection.Dispose();
                    _logger.LogDebug("DB CONNECTION: Connection disposed and returned to pool");
                }
            }

            _logger.LogMethodExit(nameof(BulkUpsertObjectsAsync));
        }
    }

    public async Task<BulkUpsertResult> BulkUpsertGroupsAsync(
        List<(Group group, List<GroupAttribute> attributes)> groupsWithAttributes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkUpsertGroupsAsync),
            new { groupCount = groupsWithAttributes?.Count ?? 0 });

        var startTime = DateTime.UtcNow;
        SqlConnection? connection = null;

        try
        {
            if (groupsWithAttributes == null || groupsWithAttributes.Count == 0)
            {
                _logger.LogWarning("No groups provided for bulk upsert - returning empty result");
                return new BulkUpsertResult();
            }

            _logger.LogInformation("DB CONNECTION: Opening new SQL connection for bulk group upsert");
            connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("DB CONNECTION OPENED: State={State}", connection.State);

            var groupsJson = System.Text.Json.JsonSerializer.Serialize(groupsWithAttributes.Select(g => new
            {
                Id = g.group.Id.ToString(),
                SourceConnectionId = g.group.SourceConnectionId.ToString(),
                SourceUniqueId = g.group.SourceUniqueId,
                SourceType = g.group.SourceType,
                Name = g.group.Name,
                Description = g.group.Description,
                DistinguishedName = g.group.DistinguishedName,
                GroupType = g.group.GroupType,
                Email = g.group.Email,
                IsMailEnabled = g.group.IsMailEnabled,
                OwnerId = g.group.OwnerId?.ToString(),
                ManagedBy = g.group.ManagedBy,
                IsActive = g.group.IsActive,
                Attributes = g.attributes.Select(a => new
                {
                    a.AttributeName,
                    a.AttributeValue,
                    a.DataType
                })
            }));

            _logger.LogInformation("SERIALIZATION: Serialized {GroupCount} groups with attributes to JSON ({JsonSize:N0} bytes)",
                groupsWithAttributes.Count, groupsJson.Length);

            var command = new CommandDefinition(
                "usp_BulkUpsertGroups",
                new { GroupsJson = groupsJson },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken,
                commandTimeout: 120);

            _logger.LogInformation("EXECUTING STORED PROCEDURE: usp_BulkUpsertGroups (Timeout: 120s, Connection State: {State})",
                connection.State);

            var dbCallStart = DateTime.UtcNow;
            BulkUpsertResult result;

            try
            {
                result = await connection.QuerySingleAsync<BulkUpsertResult>(command);
                var dbCallDuration = (DateTime.UtcNow - dbCallStart).TotalSeconds;

                _logger.LogInformation("STORED PROCEDURE COMPLETED: Duration={Duration:F2}s, Connection State: {State}",
                    dbCallDuration, connection.State);
            }
            catch (SqlException sqlEx) when (sqlEx.Number == -2)
            {
                var dbCallDuration = (DateTime.UtcNow - dbCallStart).TotalSeconds;
                _logger.LogError("DATABASE TIMEOUT: Operation exceeded 120 second limit (Duration: {Duration:F2}s, Connection State: {State})",
                    dbCallDuration, connection.State);
                throw new TimeoutException($"Bulk group upsert operation timed out after {dbCallDuration:F2} seconds.", sqlEx);
            }
            catch (SqlException sqlEx)
            {
                var dbCallDuration = (DateTime.UtcNow - dbCallStart).TotalSeconds;
                _logger.LogError(sqlEx, "DATABASE ERROR: SQL Exception during bulk group upsert (Duration: {Duration:F2}s, SQL Error: {ErrorNumber}, Connection State: {State})",
                    dbCallDuration, sqlEx.Number, connection.State);
                throw;
            }

            _logger.LogInformation("BULK GROUP UPSERT COMPLETE: Processed={Processed}, Created={Created}, Updated={Updated}, Attributes={Attributes}",
                result.ObjectsProcessed, result.ObjectsCreated, result.ObjectsUpdated, result.AttributesAffected);

            return result;
        }
        catch (TimeoutException ex)
        {
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "TIMEOUT: Bulk group upsert timed out after {Duration:F2}s with {Count} groups.",
                totalDuration, groupsWithAttributes?.Count ?? 0);
            throw;
        }
        catch (SqlException ex)
        {
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "SQL ERROR: Database error bulk upserting {Count} groups (Duration: {Duration:F2}s, SQL Error: {ErrorNumber})",
                groupsWithAttributes?.Count ?? 0, totalDuration, ex.Number);
            throw;
        }
        catch (Exception ex)
        {
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "UNEXPECTED ERROR: Bulk group upsert failed with {Count} groups (Duration: {Duration:F2}s)",
                groupsWithAttributes?.Count ?? 0, totalDuration);
            throw;
        }
        finally
        {
            if (connection != null)
            {
                connection.Dispose();
                _logger.LogDebug("DB CONNECTION: Connection disposed");
            }

            _logger.LogMethodExit(nameof(BulkUpsertGroupsAsync));
        }
    }

    public async Task<BulkUpsertResult> FastBulkUpsertObjectsAsync(
        List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes,
        CancellationToken cancellationToken = default,
        Func<int, int, Task>? onProgress = null)
    {
        _logger.LogMethodEntry(nameof(FastBulkUpsertObjectsAsync), new { objectCount = objectsWithAttributes?.Count ?? 0 });
        var startTime = DateTime.UtcNow;

        try
        {
            if (objectsWithAttributes == null || !objectsWithAttributes.Any())
                return new BulkUpsertResult();

            int total = objectsWithAttributes.Count;
            _logger.LogInformation("BATCH APPROACH: {Count} objects with MINIMAL round-trips", total);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.UtcNow;

            var lastProgressTime = DateTime.MinValue;
            const int progressThrottleSeconds = 3;
            var connId = objectsWithAttributes.First().identityObject.SourceConnectionId;

            _logger.LogInformation("Step 1: Fetching existing objects for comparison...");
            var allSourceIds = objectsWithAttributes.Select(o => o.identityObject.SourceUniqueId).ToList();

            var existingObjects = new Dictionary<string, (Guid Id, string? DisplayName, string? Email, string? Username,
                string? FirstName, string? LastName, string? Department, string? JobTitle, string? Phone,
                string? DN, string? CN, string? ManagerSourceId, bool IsActive, int? UserAccountControl)>(StringComparer.OrdinalIgnoreCase);

            const int chunkSize = 500;
            for (int i = 0; i < allSourceIds.Count; i += chunkSize)
            {
                var chunk = allSourceIds.Skip(i).Take(chunkSize).ToList();
                var fetchCmd = new CommandDefinition(
                    @"SELECT Id, SourceUniqueId, DisplayName, Email, Username, FirstName, LastName,
                      Department, JobTitle, Phone, DN, CN, ManagerSourceId, IsActive, UserAccountControl
                      FROM Objects WHERE SourceConnectionId=@C AND SourceUniqueId IN @Ids",
                    new { C = connId, Ids = chunk },
                    commandTimeout: 600,
                    cancellationToken: cancellationToken);
                var found = await connection.QueryAsync<(Guid Id, string SourceUniqueId, string? DisplayName, string? Email,
                    string? Username, string? FirstName, string? LastName, string? Department, string? JobTitle,
                    string? Phone, string? DN, string? CN, string? ManagerSourceId, bool IsActive, int? UserAccountControl)>(fetchCmd);
                foreach (var obj in found)
                    existingObjects[obj.SourceUniqueId] = (obj.Id, obj.DisplayName, obj.Email, obj.Username,
                        obj.FirstName, obj.LastName, obj.Department, obj.JobTitle, obj.Phone,
                        obj.DN, obj.CN, obj.ManagerSourceId, obj.IsActive, obj.UserAccountControl);
            }
            _logger.LogInformation("Fetched {Existing} existing objects for comparison", existingObjects.Count);

            var newObjects = new List<(IdentityObject identityObject, List<ObjectAttribute> attributes)>();
            var updateObjects = new List<(IdentityObject identityObject, List<ObjectAttribute> attributes)>();
            var skippedSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unchangedCount = 0;
            var auditEntries = new List<ChangeAuditEntry>();

            foreach (var item in objectsWithAttributes)
            {
                var obj = item.identityObject;
                var sourceId = obj.SourceUniqueId ?? "";

                if (!existingObjects.TryGetValue(sourceId, out var existing))
                {
                    newObjects.Add(item);
                }
                else
                {
                    // Track per-field changes for audit logging
                    void CheckField(string fieldName, string? oldVal, string? newVal)
                    {
                        if (oldVal != newVal)
                        {
                            auditEntries.Add(new ChangeAuditEntry
                            {
                                Timestamp = now,
                                OperationType = ChangeOperationType.Update,
                                EntityType = "Object",
                                EntityId = existing.Id,
                                EntityDisplayName = obj.DisplayName ?? existing.DisplayName,
                                PropertyName = fieldName,
                                OldValue = oldVal,
                                NewValue = newVal,
                                Source = "ADSync",
                                Success = true
                            });
                        }
                    }

                    bool hasChanges =
                        existing.DisplayName != obj.DisplayName ||
                        existing.Email != obj.Email ||
                        existing.Username != obj.Username ||
                        existing.FirstName != obj.FirstName ||
                        existing.LastName != obj.LastName ||
                        existing.Department != obj.Department ||
                        existing.JobTitle != obj.JobTitle ||
                        existing.Phone != obj.Phone ||
                        existing.DN != obj.DN ||
                        existing.CN != obj.CN ||
                        existing.ManagerSourceId != obj.ManagerSourceId ||
                        existing.IsActive != obj.IsActive ||
                        existing.UserAccountControl != obj.UserAccountControl;

                    if (hasChanges)
                    {
                        updateObjects.Add(item);
                        CheckField("DisplayName", existing.DisplayName, obj.DisplayName);
                        CheckField("Email", existing.Email, obj.Email);
                        CheckField("Username", existing.Username, obj.Username);
                        CheckField("FirstName", existing.FirstName, obj.FirstName);
                        CheckField("LastName", existing.LastName, obj.LastName);
                        CheckField("Department", existing.Department, obj.Department);
                        CheckField("JobTitle", existing.JobTitle, obj.JobTitle);
                        CheckField("Phone", existing.Phone, obj.Phone);
                        CheckField("DN", existing.DN, obj.DN);
                        CheckField("CN", existing.CN, obj.CN);
                        CheckField("ManagerSourceId", existing.ManagerSourceId, obj.ManagerSourceId);
                        CheckField("IsActive", existing.IsActive.ToString(), obj.IsActive.ToString());
                        CheckField("UserAccountControl", existing.UserAccountControl?.ToString(), obj.UserAccountControl?.ToString());
                    }
                    else
                    {
                        unchangedCount++;
                        skippedSourceIds.Add(sourceId);
                    }
                }
            }
            _logger.LogInformation("Comparison: {New} new, {Changed} changed, {Unchanged} unchanged (skipped!)",
                newObjects.Count, updateObjects.Count, unchangedCount);

            int created = 0, updated = 0;

            if (newObjects.Any())
            {
                _logger.LogInformation("Step 2: Inserting {Count} new objects...", newObjects.Count);
                const int insertBatch = 50;
                for (int i = 0; i < newObjects.Count; i += insertBatch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = newObjects.Skip(i).Take(insertBatch).ToList();
                    var insertParams = new DynamicParameters();
                    insertParams.Add("@Now", now);
                    var valueRows = new List<string>();

                    for (int j = 0; j < batch.Count; j++)
                    {
                        var obj = batch[j].identityObject;
                        var p = $"p{j}";
                        insertParams.Add($"@{p}_Id", obj.Id);
                        insertParams.Add($"@{p}_ConnId", obj.SourceConnectionId);
                        insertParams.Add($"@{p}_Uid", obj.SourceUniqueId);
                        insertParams.Add($"@{p}_Type", obj.SourceType);
                        insertParams.Add($"@{p}_Class", obj.ObjectClass);
                        insertParams.Add($"@{p}_Disp", obj.DisplayName);
                        insertParams.Add($"@{p}_Email", obj.Email);
                        insertParams.Add($"@{p}_User", obj.Username);
                        insertParams.Add($"@{p}_UPN", obj.UserPrincipalName);
                        insertParams.Add($"@{p}_First", obj.FirstName);
                        insertParams.Add($"@{p}_Last", obj.LastName);
                        insertParams.Add($"@{p}_Dept", obj.Department);
                        insertParams.Add($"@{p}_Job", obj.JobTitle);
                        insertParams.Add($"@{p}_Phone", obj.Phone);
                        insertParams.Add($"@{p}_Div", obj.Division);
                        insertParams.Add($"@{p}_Comp", obj.Company);
                        insertParams.Add($"@{p}_Ofc", obj.Office);
                        insertParams.Add($"@{p}_CC", obj.CostCenter);
                        insertParams.Add($"@{p}_Mobile", obj.MobilePhone);
                        insertParams.Add($"@{p}_EmpId", obj.EmployeeId);
                        insertParams.Add($"@{p}_DN", obj.DN);
                        insertParams.Add($"@{p}_CN", obj.CN);
                        insertParams.Add($"@{p}_Mgr", obj.ManagerSourceId);
                        insertParams.Add($"@{p}_IdId", obj.IdentityId);
                        insertParams.Add($"@{p}_Active", obj.IsActive);
                        insertParams.Add($"@{p}_Auth", obj.IsAuthoritative);
                        insertParams.Add($"@{p}_Conf", obj.MatchConfidence);
                        insertParams.Add($"@{p}_Meth", obj.MatchMethod);
                        insertParams.Add($"@{p}_Built", obj.IsBuiltIn);
                        insertParams.Add($"@{p}_Admin", obj.IsAdminSDHolder);
                        insertParams.Add($"@{p}_UAC", obj.UserAccountControl);
                        insertParams.Add($"@{p}_PwdLast", obj.PasswordLastSet);
                        insertParams.Add($"@{p}_PwdNever", obj.PasswordNeverExpires);
                        valueRows.Add($"(@{p}_Id,@{p}_ConnId,@{p}_Uid,@{p}_Type,@{p}_Class,@{p}_Disp,@{p}_Email,@{p}_User,@{p}_UPN,@{p}_First,@{p}_Last,@{p}_Dept,@{p}_Job,@{p}_Phone,@{p}_DN,@{p}_CN,@{p}_Mgr,@{p}_IdId,@{p}_Active,@{p}_Auth,@{p}_Conf,@{p}_Meth,@{p}_Built,@{p}_Admin,@{p}_UAC,@{p}_PwdLast,@{p}_PwdNever,@Now,@Now,@Now)");
                    }

                    // Use MERGE to prevent duplicate key violations when the same SourceUniqueId
                    // already exists (e.g., from overlapping sync projects or stale caches)
                    var mergeStatements = new List<string>();
                    for (int j = 0; j < batch.Count; j++)
                    {
                        var p = $"p{j}";
                        mergeStatements.Add($@"
IF NOT EXISTS (SELECT 1 FROM Objects WHERE SourceConnectionId=@{p}_ConnId AND SourceUniqueId=@{p}_Uid)
    INSERT INTO Objects (Id,SourceConnectionId,SourceUniqueId,SourceType,ObjectClass,DisplayName,Email,Username,UserPrincipalName,FirstName,LastName,Department,Division,Company,Office,CostCenter,JobTitle,Phone,MobilePhone,EmployeeId,DN,CN,ManagerSourceId,IdentityId,IsActive,LifecycleState,IsAuthoritative,MatchConfidence,MatchMethod,IsBuiltIn,IsAdminSDHolder,UserAccountControl,PasswordLastSet,PasswordNeverExpires,FirstSyncedAt,LastSyncedAt,LastSeenAt)
    VALUES (@{p}_Id,@{p}_ConnId,@{p}_Uid,@{p}_Type,@{p}_Class,@{p}_Disp,@{p}_Email,@{p}_User,@{p}_UPN,@{p}_First,@{p}_Last,@{p}_Dept,@{p}_Div,@{p}_Comp,@{p}_Ofc,@{p}_CC,@{p}_Job,@{p}_Phone,@{p}_Mobile,@{p}_EmpId,@{p}_DN,@{p}_CN,@{p}_Mgr,@{p}_IdId,@{p}_Active,CASE WHEN @{p}_Active=1 THEN 0 ELSE 1 END,@{p}_Auth,@{p}_Conf,@{p}_Meth,@{p}_Built,@{p}_Admin,@{p}_UAC,@{p}_PwdLast,@{p}_PwdNever,@Now,@Now,@Now);");
                    }
                    var insertSql = string.Join("\n", mergeStatements);
                    var insertCmd = new CommandDefinition(insertSql, insertParams, commandTimeout: 600, cancellationToken: cancellationToken);
                    created += await connection.ExecuteAsync(insertCmd);

                    bool isFinalInsertBatch = (i + insertBatch) >= newObjects.Count;
                    if (onProgress != null && (isFinalInsertBatch || (DateTime.UtcNow - lastProgressTime).TotalSeconds >= progressThrottleSeconds))
                    {
                        await onProgress(created + updated, total);
                        lastProgressTime = DateTime.UtcNow;
                    }
                    _logger.LogInformation("Inserted {Count}/{Total} objects...", created, newObjects.Count);
                }
            }

            if (updateObjects.Any())
            {
                _logger.LogInformation("Step 3: Updating {Count} existing objects via BATCH...", updateObjects.Count);

                const int updateBatch = 50;
                for (int i = 0; i < updateObjects.Count; i += updateBatch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = updateObjects.Skip(i).Take(updateBatch).ToList();
                    var updateParams = new DynamicParameters();
                    updateParams.Add("@Now", now);
                    updateParams.Add("@ConnId", connId);

                    var valueRows = new List<string>();
                    for (int j = 0; j < batch.Count; j++)
                    {
                        var obj = batch[j].identityObject;
                        var p = $"u{j}";
                        updateParams.Add($"@{p}_Uid", obj.SourceUniqueId);
                        updateParams.Add($"@{p}_Disp", obj.DisplayName);
                        updateParams.Add($"@{p}_Email", obj.Email);
                        updateParams.Add($"@{p}_User", obj.Username);
                        updateParams.Add($"@{p}_UPN", obj.UserPrincipalName);
                        updateParams.Add($"@{p}_First", obj.FirstName);
                        updateParams.Add($"@{p}_Last", obj.LastName);
                        updateParams.Add($"@{p}_Dept", obj.Department);
                        updateParams.Add($"@{p}_Job", obj.JobTitle);
                        updateParams.Add($"@{p}_Phone", obj.Phone);
                        updateParams.Add($"@{p}_DN", obj.DN);
                        updateParams.Add($"@{p}_CN", obj.CN);
                        updateParams.Add($"@{p}_Mgr", obj.ManagerSourceId);
                        updateParams.Add($"@{p}_Active", obj.IsActive);
                        updateParams.Add($"@{p}_Auth", obj.IsAuthoritative);
                        updateParams.Add($"@{p}_Built", obj.IsBuiltIn);
                        updateParams.Add($"@{p}_Admin", obj.IsAdminSDHolder);
                        updateParams.Add($"@{p}_UAC", obj.UserAccountControl);
                        updateParams.Add($"@{p}_PwdLast", obj.PasswordLastSet);
                        updateParams.Add($"@{p}_PwdNever", obj.PasswordNeverExpires);
                        updateParams.Add($"@{p}_Class", obj.ObjectClass);
                        updateParams.Add($"@{p}_IdId", obj.IdentityId);
                        updateParams.Add($"@{p}_Conf", obj.MatchConfidence);
                        updateParams.Add($"@{p}_Meth", obj.MatchMethod);
                        valueRows.Add($"(@{p}_Uid,@{p}_Disp,@{p}_Email,@{p}_User,@{p}_UPN,@{p}_First,@{p}_Last,@{p}_Dept,@{p}_Job,@{p}_Phone,@{p}_DN,@{p}_CN,@{p}_Mgr,@{p}_Active,@{p}_Auth,@{p}_Built,@{p}_Admin,@{p}_UAC,@{p}_PwdLast,@{p}_PwdNever,@{p}_Class,@{p}_IdId,@{p}_Conf,@{p}_Meth)");
                    }

                    var batchUpdateSql = $@"
                        ;WITH UpdateData AS (
                            SELECT * FROM (VALUES {string.Join(",", valueRows)})
                            AS t(Uid,Disp,Email,Usr,UPN,First,Last,Dept,Job,Phone,DN,CN,Mgr,Active,Auth,Built,Admin,UAC,PwdLast,PwdNever,Class,IdId,Conf,Meth)
                        )
                        UPDATE o SET
                            o.DisplayName=u.Disp, o.Email=u.Email, o.Username=u.Usr, o.UserPrincipalName=u.UPN,
                            o.FirstName=u.First, o.LastName=u.Last,
                            o.Department=u.Dept, o.JobTitle=u.Job, o.Phone=u.Phone, o.DN=u.DN, o.CN=u.CN, o.ManagerSourceId=u.Mgr,
                            o.IsActive=u.Active, o.IsAuthoritative=u.Auth, o.IsBuiltIn=u.Built, o.IsAdminSDHolder=u.Admin,
                            -- ARS 3-state: AD sync owns Active<->Disabled (0<->1) from the source
                            -- enable bit. NEVER reclassify a Deprovisioned(2) row -- tombstone/revive
                            -- own state 2. Preserve 2; otherwise enabled->0, disabled->1.
                            o.LifecycleState=CASE WHEN o.LifecycleState=2 THEN 2 WHEN u.Active=1 THEN 0 ELSE 1 END,
                            o.UserAccountControl=u.UAC, o.PasswordLastSet=u.PwdLast, o.PasswordNeverExpires=u.PwdNever,
                            o.ObjectClass=u.Class, o.IdentityId=COALESCE(u.IdId,o.IdentityId),
                            o.MatchConfidence=CASE WHEN u.IdId IS NOT NULL THEN u.Conf ELSE o.MatchConfidence END,
                            o.MatchMethod=CASE WHEN u.IdId IS NOT NULL THEN u.Meth ELSE o.MatchMethod END,
                            o.LastSyncedAt=@Now, o.LastSeenAt=@Now
                        FROM Objects o
                        INNER JOIN UpdateData u ON o.SourceUniqueId=u.Uid AND o.SourceConnectionId=@ConnId";

                    var updateCmd = new CommandDefinition(batchUpdateSql, updateParams, commandTimeout: 600, cancellationToken: cancellationToken);
                    updated += await connection.ExecuteAsync(updateCmd);

                    bool isFinalUpdateBatch = (i + updateBatch) >= updateObjects.Count;
                    if (onProgress != null && (isFinalUpdateBatch || (DateTime.UtcNow - lastProgressTime).TotalSeconds >= progressThrottleSeconds))
                    {
                        await onProgress(created + updated, total);
                        lastProgressTime = DateTime.UtcNow;
                    }
                    _logger.LogInformation("Updated {Count}/{Total} objects...", updated, updateObjects.Count);
                }
            }

            if (unchangedCount > 0)
            {
                _logger.LogInformation("Step 4: Updating LastSeenAt for {Count} unchanged objects...", unchangedCount);
                var updateSourceIdSet = new HashSet<string>(
                    updateObjects.Select(u => u.identityObject.SourceUniqueId ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                var unchangedSourceIds = objectsWithAttributes
                    .Where(o => existingObjects.ContainsKey(o.identityObject.SourceUniqueId ?? "") &&
                                !updateSourceIdSet.Contains(o.identityObject.SourceUniqueId ?? ""))
                    .Select(o => o.identityObject.SourceUniqueId)
                    .ToList();

                for (int i = 0; i < unchangedSourceIds.Count; i += chunkSize)
                {
                    var chunk = unchangedSourceIds.Skip(i).Take(chunkSize).ToList();
                    var lastSeenCmd = new CommandDefinition(
                        "UPDATE Objects SET LastSeenAt=@Now WHERE SourceConnectionId=@C AND SourceUniqueId IN @Ids",
                        new { Now = now, C = connId, Ids = chunk },
                        commandTimeout: 600,
                        cancellationToken: cancellationToken);
                    await connection.ExecuteAsync(lastSeenCmd);
                }
            }

            _logger.LogInformation("Step 5: Building ID lookup for attributes...");
            var idLookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in existingObjects)
                idLookup[kvp.Key] = kvp.Value.Id;

            if (newObjects.Any())
            {
                var newSourceIds = newObjects.Select(o => o.identityObject.SourceUniqueId).ToList();
                for (int i = 0; i < newSourceIds.Count; i += chunkSize)
                {
                    var chunk = newSourceIds.Skip(i).Take(chunkSize).ToList();
                    var idCmd = new CommandDefinition(
                        "SELECT Id, SourceUniqueId FROM Objects WHERE SourceConnectionId=@C AND SourceUniqueId IN @Ids",
                        new { C = connId, Ids = chunk },
                        commandTimeout: 600,
                        cancellationToken: cancellationToken);
                    var ids = await connection.QueryAsync<(Guid Id, string SourceUniqueId)>(idCmd);
                    foreach (var (id, uid) in ids) idLookup[uid] = id;
                }
            }

            // Create audit entries for new objects (now that we have their IDs)
            foreach (var item in newObjects)
            {
                var sourceId = item.identityObject.SourceUniqueId ?? "";
                if (idLookup.TryGetValue(sourceId, out var newObjId))
                {
                    auditEntries.Add(new ChangeAuditEntry
                    {
                        Timestamp = now,
                        OperationType = ChangeOperationType.Create,
                        EntityType = "Object",
                        EntityId = newObjId,
                        EntityDisplayName = item.identityObject.DisplayName,
                        Source = "ADSync",
                        Success = true
                    });
                }
            }

            // Flush audit entries (non-fatal)
            try
            {
                if (auditEntries.Count > 0)
                    await _auditLogService.LogChangesAsync(auditEntries);
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(auditEx, "Failed to log {Count} audit entries for AD sync (non-fatal)", auditEntries.Count);
            }

            // Only process attributes for new/changed objects to avoid expensive MERGE on unchanged data.
            // When all objects are unchanged, skip the MERGE entirely (saves ~7s per batch).
            var objectsNeedingAttrUpdate = newObjects.Count > 0 || updateObjects.Count > 0
                ? objectsWithAttributes  // If any objects changed, process all to catch new attribute mappings
                : new List<(IdentityObject identityObject, List<ObjectAttribute> attributes)>(); // Skip entirely
            _logger.LogInformation("Step 6: MERGE attributes for {Count} objects ({New} new, {Changed} changed, {Unchanged} unchanged{Skip})...",
                objectsNeedingAttrUpdate.Count, newObjects.Count, updateObjects.Count, unchangedCount,
                objectsNeedingAttrUpdate.Count == 0 ? " - SKIPPED (no changes)" : "");

            await connection.ExecuteAsync(@"
                IF OBJECT_ID('tempdb..#AttrStaging') IS NOT NULL DROP TABLE #AttrStaging;
                CREATE TABLE #AttrStaging (
                    ObjectId UNIQUEIDENTIFIER NOT NULL,
                    AttributeName NVARCHAR(255) NOT NULL,
                    AttributeValue NVARCHAR(MAX),
                    DataType NVARCHAR(50),
                    LastSyncedAt DATETIME2 NOT NULL
                )", commandTimeout: 30);

            var attrDataTable = new DataTable();
            attrDataTable.Columns.Add("ObjectId", typeof(Guid));
            attrDataTable.Columns.Add("AttributeName", typeof(string));
            attrDataTable.Columns.Add("AttributeValue", typeof(string));
            attrDataTable.Columns.Add("DataType", typeof(string));
            attrDataTable.Columns.Add("LastSyncedAt", typeof(DateTime));

            foreach (var (obj, attrList) in objectsNeedingAttrUpdate)
            {
                if (idLookup.TryGetValue(obj.SourceUniqueId ?? "", out var oid))
                {
                    foreach (var a in attrList)
                    {
                        attrDataTable.Rows.Add(oid, a.AttributeName, a.AttributeValue, a.DataType, now);
                    }
                }
            }

            int attrs = attrDataTable.Rows.Count;
            if (attrs > 0)
            {
                using var bulkCopy = new SqlBulkCopy(connection)
                {
                    DestinationTableName = "#AttrStaging",
                    BatchSize = 5000,
                    BulkCopyTimeout = 300
                };
                bulkCopy.ColumnMappings.Add("ObjectId", "ObjectId");
                bulkCopy.ColumnMappings.Add("AttributeName", "AttributeName");
                bulkCopy.ColumnMappings.Add("AttributeValue", "AttributeValue");
                bulkCopy.ColumnMappings.Add("DataType", "DataType");
                bulkCopy.ColumnMappings.Add("LastSyncedAt", "LastSyncedAt");
                await bulkCopy.WriteToServerAsync(attrDataTable, cancellationToken);

                var mergeCmd = new CommandDefinition(@"
                    MERGE ObjectAttributes AS target
                    USING #AttrStaging AS source
                    ON target.ObjectId = source.ObjectId AND target.AttributeName = source.AttributeName
                    WHEN MATCHED AND (target.AttributeValue != source.AttributeValue OR target.DataType != source.DataType) THEN
                        UPDATE SET
                            AttributeValue = source.AttributeValue,
                            DataType = source.DataType,
                            LastSyncedAt = source.LastSyncedAt
                    WHEN NOT MATCHED BY TARGET THEN
                        INSERT (Id, ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt)
                        VALUES (NEWID(), source.ObjectId, source.AttributeName, source.AttributeValue, source.DataType, source.LastSyncedAt);",
                    commandTimeout: 600,
                    cancellationToken: cancellationToken);
                var mergeResult = await connection.ExecuteAsync(mergeCmd);

                _logger.LogInformation("MERGE completed: {Count} attributes processed (only changed/new rows touched)", attrs);
            }

            await connection.ExecuteAsync("DROP TABLE IF EXISTS #AttrStaging", commandTimeout: 30);

            _logger.LogInformation("BATCH COMPLETE: {Sec:F1}s - {Created} created, {Updated} updated, {Skipped} skipped, {Attrs} attrs",
                (DateTime.UtcNow - startTime).TotalSeconds, created, updated, unchangedCount, attrs);

            return new BulkUpsertResult
            {
                ObjectsProcessed = objectsWithAttributes.Count,
                ObjectsCreated = created,
                ObjectsUpdated = updated,
                ObjectsSkipped = unchangedCount,
                AttributesAffected = attrs,
                SkippedSourceIds = skippedSourceIds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BATCH APPROACH FAILED");
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(FastBulkUpsertObjectsAsync));
        }
    }

    public async Task<BulkUpsertResult> TrueBulkUpsertObjectsAsync(
        List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(TrueBulkUpsertObjectsAsync),
            new { objectCount = objectsWithAttributes?.Count ?? 0 });

        if (objectsWithAttributes == null)
        {
            _logger.LogWarning("ObjectsWithAttributes cannot be null");
            throw new ArgumentNullException(nameof(objectsWithAttributes));
        }

        if (!objectsWithAttributes.Any())
        {
            _logger.LogDebug("No objects to upsert");
            return new BulkUpsertResult { ObjectsProcessed = 0, ObjectsCreated = 0, ObjectsUpdated = 0, AttributesAffected = 0 };
        }

        SqlConnection? connection = null;

        await _sqlBulkCopySemaphore.WaitAsync(cancellationToken);
        try
        {
            using var tracker = new SyncRepositoryHelpers.PerformanceTracker(_logger, nameof(TrueBulkUpsertObjectsAsync),
                new { objectCount = objectsWithAttributes.Count }, slowThresholdMs: 30000);
            _logger.LogInformation("TRUE BULK UPSERT: Processing {Count} objects with TEMP TABLES (MAXIMUM PERFORMANCE)", objectsWithAttributes.Count);

            var connectionBuilder = new SqlConnectionStringBuilder(_connectionString)
            {
                ConnectTimeout = 30,
                PacketSize = 16384,
            };
            connection = new SqlConnection(connectionBuilder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("DB CONNECTION: Opened (State: {State})", connection.State);

            await connection.ExecuteAsync("SET LOCK_TIMEOUT 60000;");
            _logger.LogDebug("Lock timeout set to 60 seconds");

            int objectsCreated = 0;
            int objectsUpdated = 0;
            int attributesAffected = 0;

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var now = DateTime.UtcNow;

                    _logger.LogInformation("STEP 1/5: Creating temp tables...");

                    await connection.ExecuteAsync(@"
                        CREATE TABLE #ObjectsToUpsert (
                            Id UNIQUEIDENTIFIER,
                            SourceConnectionId UNIQUEIDENTIFIER,
                            SourceUniqueId NVARCHAR(450),
                            SourceType NVARCHAR(100),
                            ObjectClass NVARCHAR(100),
                            DisplayName NVARCHAR(500),
                            Email NVARCHAR(500),
                            Username NVARCHAR(500),
                            FirstName NVARCHAR(200),
                            LastName NVARCHAR(200),
                            Department NVARCHAR(200),
                            Division NVARCHAR(500),
                            Company NVARCHAR(500),
                            Office NVARCHAR(200),
                            CostCenter NVARCHAR(100),
                            JobTitle NVARCHAR(200),
                            Phone NVARCHAR(100),
                            MobilePhone NVARCHAR(100),
                            EmployeeId NVARCHAR(100),
                            DN NVARCHAR(MAX),
                            CN NVARCHAR(500),
                            ManagerSourceId NVARCHAR(500),
                            IdentityId UNIQUEIDENTIFIER,
                            IsActive BIT,
                            IsAuthoritative BIT,
                            MatchConfidence INT,
                            MatchMethod NVARCHAR(100),
                            IsBuiltIn BIT,
                            IsAdminSDHolder BIT,
                            UserAccountControl INT,
                            PasswordLastSet DATETIME2,
                            PasswordNeverExpires BIT
                        );

                        CREATE TABLE #AttributesToUpsert (
                            ObjectSourceConnectionId UNIQUEIDENTIFIER,
                            ObjectSourceUniqueId NVARCHAR(450),
                            AttributeName NVARCHAR(200),
                            AttributeValue NVARCHAR(MAX),
                            DataType NVARCHAR(50)
                        );
                    ", transaction: transaction);

                    _logger.LogInformation("STEP 1 COMPLETE: Temp tables created in {ElapsedMs}ms", tracker.ElapsedMs);
                    tracker.LogIfSlow("Step 1 - Temp tables");

                    _logger.LogInformation("STEP 2/5: Bulk loading {Count} objects into temp table via SqlBulkCopy...", objectsWithAttributes.Count);

                    var objectsTable = new DataTable();
                    objectsTable.Columns.Add("Id", typeof(Guid));
                    objectsTable.Columns.Add("SourceConnectionId", typeof(Guid));
                    objectsTable.Columns.Add("SourceUniqueId", typeof(string));
                    objectsTable.Columns.Add("SourceType", typeof(string));
                    objectsTable.Columns.Add("ObjectClass", typeof(string));
                    objectsTable.Columns.Add("DisplayName", typeof(string));
                    objectsTable.Columns.Add("Email", typeof(string));
                    objectsTable.Columns.Add("Username", typeof(string));
                    objectsTable.Columns.Add("FirstName", typeof(string));
                    objectsTable.Columns.Add("LastName", typeof(string));
                    objectsTable.Columns.Add("Department", typeof(string));
                    objectsTable.Columns.Add("Division", typeof(string));
                    objectsTable.Columns.Add("Company", typeof(string));
                    objectsTable.Columns.Add("Office", typeof(string));
                    objectsTable.Columns.Add("CostCenter", typeof(string));
                    objectsTable.Columns.Add("JobTitle", typeof(string));
                    objectsTable.Columns.Add("Phone", typeof(string));
                    objectsTable.Columns.Add("MobilePhone", typeof(string));
                    objectsTable.Columns.Add("EmployeeId", typeof(string));
                    objectsTable.Columns.Add("DN", typeof(string));
                    objectsTable.Columns.Add("CN", typeof(string));
                    objectsTable.Columns.Add("ManagerSourceId", typeof(string));
                    objectsTable.Columns.Add("IdentityId", typeof(Guid));
                    objectsTable.Columns.Add("IsActive", typeof(bool));
                    objectsTable.Columns.Add("IsAuthoritative", typeof(bool));
                    objectsTable.Columns.Add("MatchConfidence", typeof(int));
                    objectsTable.Columns.Add("MatchMethod", typeof(string));
                    objectsTable.Columns.Add("IsBuiltIn", typeof(bool));
                    objectsTable.Columns.Add("IsAdminSDHolder", typeof(bool));
                    objectsTable.Columns.Add("UserAccountControl", typeof(int));
                    objectsTable.Columns.Add("PasswordLastSet", typeof(DateTime));
                    objectsTable.Columns.Add("PasswordNeverExpires", typeof(bool));

                    foreach (var item in objectsWithAttributes)
                    {
                        var obj = item.identityObject;
                        objectsTable.Rows.Add(
                            obj.Id,
                            obj.SourceConnectionId,
                            obj.SourceUniqueId,
                            obj.SourceType ?? (object)DBNull.Value,
                            obj.ObjectClass ?? (object)DBNull.Value,
                            obj.DisplayName ?? (object)DBNull.Value,
                            obj.Email ?? (object)DBNull.Value,
                            obj.Username ?? (object)DBNull.Value,
                            obj.FirstName ?? (object)DBNull.Value,
                            obj.LastName ?? (object)DBNull.Value,
                            obj.Department ?? (object)DBNull.Value,
                            obj.Division ?? (object)DBNull.Value,
                            obj.Company ?? (object)DBNull.Value,
                            obj.Office ?? (object)DBNull.Value,
                            obj.CostCenter ?? (object)DBNull.Value,
                            obj.JobTitle ?? (object)DBNull.Value,
                            obj.Phone ?? (object)DBNull.Value,
                            obj.MobilePhone ?? (object)DBNull.Value,
                            obj.EmployeeId ?? (object)DBNull.Value,
                            obj.DN ?? (object)DBNull.Value,
                            obj.CN ?? (object)DBNull.Value,
                            obj.ManagerSourceId ?? (object)DBNull.Value,
                            obj.IdentityId.HasValue ? obj.IdentityId.Value : (object)DBNull.Value,
                            obj.IsActive,
                            obj.IsAuthoritative,
                            obj.MatchConfidence,
                            obj.MatchMethod ?? (object)DBNull.Value,
                            obj.IsBuiltIn,
                            obj.IsAdminSDHolder,
                            obj.UserAccountControl ?? (object)DBNull.Value,
                            obj.PasswordLastSet.HasValue ? obj.PasswordLastSet.Value : (object)DBNull.Value,
                            obj.PasswordNeverExpires
                        );
                    }

                    using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, transaction))
                    {
                        bulkCopy.DestinationTableName = "#ObjectsToUpsert";
                        bulkCopy.BatchSize = 100;
                        bulkCopy.BulkCopyTimeout = 300;

                        for (int i = 0; i < objectsTable.Columns.Count; i++)
                        {
                            bulkCopy.ColumnMappings.Add(objectsTable.Columns[i].ColumnName, objectsTable.Columns[i].ColumnName);
                        }

                        bulkCopy.WriteToServer(objectsTable);
                    }

                    _logger.LogInformation("STEP 2A COMPLETE: {Count} objects loaded via SqlBulkCopy in {ElapsedMs}ms",
                        objectsWithAttributes.Count, tracker.ElapsedMs);
                    tracker.LogIfSlow("Step 2A - Objects SqlBulkCopy");

                    var attributesTable = new DataTable();
                    attributesTable.Columns.Add("ObjectSourceConnectionId", typeof(Guid));
                    attributesTable.Columns.Add("ObjectSourceUniqueId", typeof(string));
                    attributesTable.Columns.Add("AttributeName", typeof(string));
                    attributesTable.Columns.Add("AttributeValue", typeof(string));
                    attributesTable.Columns.Add("DataType", typeof(string));

                    foreach (var item in objectsWithAttributes)
                    {
                        foreach (var attr in item.attributes)
                        {
                            attributesTable.Rows.Add(
                                item.identityObject.SourceConnectionId,
                                item.identityObject.SourceUniqueId ?? (object)DBNull.Value,
                                attr.AttributeName ?? (object)DBNull.Value,
                                attr.AttributeValue ?? (object)DBNull.Value,
                                attr.DataType ?? (object)DBNull.Value
                            );
                        }
                    }

                    var totalAttributes = attributesTable.Rows.Count;

                    if (totalAttributes > 0)
                    {
                        _logger.LogInformation("STEP 2B: Bulk loading {Count} attributes into temp table via SqlBulkCopy...", totalAttributes);

                        using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, transaction))
                        {
                            bulkCopy.DestinationTableName = "#AttributesToUpsert";
                            bulkCopy.BatchSize = 500;
                            bulkCopy.BulkCopyTimeout = 300;

                            for (int i = 0; i < attributesTable.Columns.Count; i++)
                            {
                                bulkCopy.ColumnMappings.Add(attributesTable.Columns[i].ColumnName, attributesTable.Columns[i].ColumnName);
                            }

                            bulkCopy.WriteToServer(attributesTable);
                        }

                        _logger.LogInformation("STEP 2B COMPLETE: {Count} attributes loaded via SqlBulkCopy in {ElapsedMs}ms",
                            totalAttributes, tracker.ElapsedMs);
                        tracker.LogIfSlow("Step 2B - Attributes SqlBulkCopy");
                    }

                    await connection.ExecuteAsync(@"
                        CREATE INDEX IX_ObjectsToUpsert_Key ON #ObjectsToUpsert(SourceConnectionId, SourceUniqueId);
                        CREATE INDEX IX_AttributesToUpsert_Key ON #AttributesToUpsert(ObjectSourceConnectionId, ObjectSourceUniqueId);
                    ", transaction: transaction);
                    _logger.LogInformation("STEP 2C COMPLETE: Indexes created on temp tables in {ElapsedMs}ms", tracker.ElapsedMs);
                    tracker.LogIfSlow("Step 2 - Bulk load complete");

                    _logger.LogInformation("STEP 3/5: Executing bulk MERGE for {Count} objects...", objectsWithAttributes.Count);

                    var mergeResults = await connection.QueryAsync<string>(@"
                        MERGE INTO Objects WITH (ROWLOCK) AS target
                        USING #ObjectsToUpsert AS source
                        ON target.SourceConnectionId = source.SourceConnectionId
                           AND target.SourceUniqueId = source.SourceUniqueId
                        WHEN MATCHED THEN
                            UPDATE SET
                                DisplayName = source.DisplayName,
                                Email = source.Email,
                                Username = source.Username,
                                FirstName = source.FirstName,
                                LastName = source.LastName,
                                Department = source.Department,
                                Division = source.Division,
                                Company = source.Company,
                                Office = source.Office,
                                CostCenter = source.CostCenter,
                                JobTitle = source.JobTitle,
                                Phone = source.Phone,
                                MobilePhone = source.MobilePhone,
                                EmployeeId = source.EmployeeId,
                                DN = source.DN,
                                CN = source.CN,
                                ManagerSourceId = source.ManagerSourceId,
                                IsActive = source.IsActive,
                                -- ARS 3-state lifecycle (0=Active, 1=Disabled, 2=Deprovisioned).
                                -- The AD sync owns the Active<->Disabled (0<->1) transition from
                                -- the source enable/disable bit (IsActive). It must NEVER touch a
                                -- Deprovisioned(2) row: tombstone/revive (the Conduit ingest path
                                -- and the evaluation job) own state 2. So a row already at 2 is
                                -- preserved; otherwise it tracks IsActive: enabled->0, disabled->1.
                                LifecycleState = CASE WHEN target.LifecycleState = 2 THEN 2
                                                      WHEN source.IsActive = 1 THEN 0 ELSE 1 END,
                                IsAuthoritative = source.IsAuthoritative,
                                IsBuiltIn = source.IsBuiltIn,
                                IsAdminSDHolder = source.IsAdminSDHolder,
                                UserAccountControl = source.UserAccountControl,
                                PasswordLastSet = source.PasswordLastSet,
                                PasswordNeverExpires = source.PasswordNeverExpires,
                                ObjectClass = source.ObjectClass,
                                IdentityId = COALESCE(source.IdentityId, target.IdentityId),
                                MatchConfidence = CASE WHEN source.IdentityId IS NOT NULL THEN source.MatchConfidence ELSE target.MatchConfidence END,
                                MatchMethod = CASE WHEN source.IdentityId IS NOT NULL THEN source.MatchMethod ELSE target.MatchMethod END,
                                LastSyncedAt = @Now,
                                LastSeenAt = @Now
                        WHEN NOT MATCHED THEN
                            INSERT (Id, SourceConnectionId, SourceUniqueId, SourceType, ObjectClass, DisplayName, Email, Username,
                                    FirstName, LastName, Department, JobTitle, Phone, DN, CN, ManagerSourceId, IdentityId, IsActive, LifecycleState,
                                    IsAuthoritative, MatchConfidence, MatchMethod, IsBuiltIn, IsAdminSDHolder,
                                    UserAccountControl, PasswordLastSet, PasswordNeverExpires,
                                    FirstSyncedAt, LastSyncedAt, LastSeenAt)
                            VALUES (source.Id, source.SourceConnectionId, source.SourceUniqueId, source.SourceType, source.ObjectClass,
                                    source.DisplayName, source.Email, source.Username, source.FirstName, source.LastName,
                                    source.Department, source.JobTitle, source.Phone, source.DN, source.CN, source.ManagerSourceId,
                                    source.IdentityId, source.IsActive,
                                    -- New row from a sync is never a tombstone: Active(0) if enabled, Disabled(1) if disabled.
                                    CASE WHEN source.IsActive = 1 THEN 0 ELSE 1 END,
                                    source.IsAuthoritative, source.MatchConfidence, source.MatchMethod,
                                    source.IsBuiltIn, source.IsAdminSDHolder, source.UserAccountControl, source.PasswordLastSet, source.PasswordNeverExpires,
                                    @Now, @Now, @Now)
                        OUTPUT $action;",
                        new { Now = now }, transaction);

                    objectsCreated = mergeResults.Count(a => a == "INSERT");
                    objectsUpdated = mergeResults.Count(a => a == "UPDATE");

                    _logger.LogInformation("STEP 3 COMPLETE: {Created} created, {Updated} updated in {ElapsedMs}ms", objectsCreated, objectsUpdated, tracker.ElapsedMs);
                    tracker.LogIfSlow("Step 3 - MERGE");

                    _logger.LogInformation("STEP 4/5: Bulk deleting old attributes...");

                    await connection.ExecuteAsync(@"
                        DELETE oa
                        FROM ObjectAttributes oa WITH (ROWLOCK)
                        INNER JOIN Objects o ON oa.ObjectId = o.Id
                        INNER JOIN #ObjectsToUpsert t ON o.SourceConnectionId = t.SourceConnectionId
                                                      AND o.SourceUniqueId = t.SourceUniqueId;",
                        transaction: transaction);

                    _logger.LogInformation("STEP 4 COMPLETE: Old attributes deleted in {ElapsedMs}ms", tracker.ElapsedMs);
                    tracker.LogIfSlow("Step 4 - Delete attributes");

                    if (totalAttributes > 0)
                    {
                        _logger.LogInformation("STEP 5/5: Bulk inserting {Count} new attributes...", totalAttributes);

                        attributesAffected = await connection.ExecuteAsync(@"
                            INSERT INTO ObjectAttributes (Id, ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt)
                            SELECT
                                NEWID(),
                                o.Id,
                                t.AttributeName,
                                t.AttributeValue,
                                t.DataType,
                                @Now
                            FROM #AttributesToUpsert t
                            INNER JOIN Objects o ON t.ObjectSourceConnectionId = o.SourceConnectionId
                                                 AND t.ObjectSourceUniqueId = o.SourceUniqueId;",
                            new { Now = now }, transaction);

                        _logger.LogInformation("STEP 5 COMPLETE: {Count} attributes inserted in {ElapsedMs}ms", attributesAffected, tracker.ElapsedMs);
                        tracker.LogIfSlow("Step 5 - Insert attributes");
                    }

                    transaction.Commit();
                    _logger.LogInformation("TRANSACTION COMMITTED: {Count} objects + attributes saved in {ElapsedMs}ms total", objectsWithAttributes.Count, tracker.ElapsedMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TRANSACTION FAILED: Rolling back all changes");
                    try
                    {
                        if (transaction.Connection != null)
                        {
                            transaction.Rollback();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        _logger.LogWarning("Transaction already completed, rollback skipped");
                    }
                    throw;
                }
            }

            _logger.LogInformation("TRUE BULK UPSERT COMPLETE: Duration={Duration}ms, Created={Created}, Updated={Updated}, Attributes={Attributes}",
                tracker.ElapsedMs, objectsCreated, objectsUpdated, attributesAffected);

            return new BulkUpsertResult
            {
                ObjectsProcessed = objectsWithAttributes.Count,
                ObjectsCreated = objectsCreated,
                ObjectsUpdated = objectsUpdated,
                AttributesAffected = attributesAffected
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TRUE BULK UPSERT FAILED: Count={Count}",
                objectsWithAttributes?.Count ?? 0);
            throw;
        }
        finally
        {
            _sqlBulkCopySemaphore.Release();

            if (connection != null)
            {
                connection.Dispose();
                _logger.LogDebug("DB CONNECTION: Disposed");
            }
            _logger.LogMethodExit(nameof(TrueBulkUpsertObjectsAsync));
        }
    }

    public async Task<int> BulkInsertAuditLogsAsync(
        List<SyncAuditLog> auditLogs,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkInsertAuditLogsAsync),
            new { auditLogCount = auditLogs?.Count ?? 0 });

        if (auditLogs == null)
        {
            _logger.LogWarning("AuditLogs cannot be null");
            throw new ArgumentNullException(nameof(auditLogs));
        }

        if (!auditLogs.Any())
        {
            _logger.LogDebug("No audit logs to insert");
            return 0;
        }

        try
        {
            return await SyncRepositoryHelpers.ExecuteWithRetryAsync(async () =>
            {
                using var tracker = new SyncRepositoryHelpers.PerformanceTracker(_logger, nameof(BulkInsertAuditLogsAsync),
                    new { auditLogCount = auditLogs.Count }, slowThresholdMs: 5000);

                _logger.LogDebug("Bulk inserting {AuditLogCount} audit logs using Dapper SqlBulkCopy", auditLogs.Count);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var dataTable = new DataTable();
                dataTable.Columns.Add("Id", typeof(Guid));
                dataTable.Columns.Add("SyncStepRunId", typeof(Guid));
                dataTable.Columns.Add("ObjectId", typeof(Guid));
                dataTable.Columns.Add("OperationType", typeof(string));
                dataTable.Columns.Add("ObjectDisplayName", typeof(string));
                dataTable.Columns.Add("SourceUniqueId", typeof(string));
                dataTable.Columns.Add("Email", typeof(string));
                dataTable.Columns.Add("Username", typeof(string));
                dataTable.Columns.Add("UserPrincipalName", typeof(string));
                dataTable.Columns.Add("ChangeDetails", typeof(string));
                dataTable.Columns.Add("ChangeCount", typeof(int));
                dataTable.Columns.Add("ErrorMessage", typeof(string));
                dataTable.Columns.Add("ProcessingTimeMs", typeof(decimal));
                dataTable.Columns.Add("Timestamp", typeof(DateTime));

                var now = DateTime.UtcNow;
                foreach (var log in auditLogs)
                {
                    dataTable.Rows.Add(
                        Guid.NewGuid(),
                        log.SyncStepRunId,
                        log.ObjectId == Guid.Empty ? DBNull.Value : log.ObjectId,
                        log.OperationType ?? (object)DBNull.Value,
                        log.ObjectDisplayName ?? (object)DBNull.Value,
                        log.SourceUniqueId ?? (object)DBNull.Value,
                        log.Email ?? (object)DBNull.Value,
                        log.Username ?? (object)DBNull.Value,
                        log.UserPrincipalName ?? (object)DBNull.Value,
                        log.ChangeDetails ?? (object)DBNull.Value,
                        log.ChangeCount,
                        log.ErrorMessage ?? (object)DBNull.Value,
                        log.ProcessingTimeMs,
                        now
                    );
                }

                using var bulkCopy = new SqlBulkCopy(connection)
                {
                    DestinationTableName = "SyncAuditLogs",
                    BatchSize = 1000,
                    BulkCopyTimeout = 300
                };

                bulkCopy.ColumnMappings.Add("Id", "Id");
                bulkCopy.ColumnMappings.Add("SyncStepRunId", "SyncStepRunId");
                bulkCopy.ColumnMappings.Add("ObjectId", "ObjectId");
                bulkCopy.ColumnMappings.Add("OperationType", "OperationType");
                bulkCopy.ColumnMappings.Add("ObjectDisplayName", "ObjectDisplayName");
                bulkCopy.ColumnMappings.Add("SourceUniqueId", "SourceUniqueId");
                bulkCopy.ColumnMappings.Add("Email", "Email");
                bulkCopy.ColumnMappings.Add("Username", "Username");
                bulkCopy.ColumnMappings.Add("UserPrincipalName", "UserPrincipalName");
                bulkCopy.ColumnMappings.Add("ChangeDetails", "ChangeDetails");
                bulkCopy.ColumnMappings.Add("ChangeCount", "ChangeCount");
                bulkCopy.ColumnMappings.Add("ErrorMessage", "ErrorMessage");
                bulkCopy.ColumnMappings.Add("ProcessingTimeMs", "ProcessingTimeMs");
                bulkCopy.ColumnMappings.Add("Timestamp", "Timestamp");

                await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
                int insertedCount = auditLogs.Count;

                _logger.LogInformation("Successfully bulk inserted {InsertedCount} audit logs via SqlBulkCopy in {ElapsedMs}ms",
                    insertedCount, tracker.ElapsedMs);

                return insertedCount;
            }, nameof(BulkInsertAuditLogsAsync), _logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error bulk inserting {AuditLogCount} audit logs",
                auditLogs?.Count ?? 0);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkInsertAuditLogsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkInsertAuditLogsAsync));
        }
    }

    public async Task<IdentityObject?> FindObjectByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(FindObjectByEmailAsync), new { email });

        try
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("Email cannot be null or whitespace");
                return null;
            }

            var normalizedEmail = email.ToLower().Trim();
            _logger.LogDebug("Finding active object by email: {Email}", normalizedEmail);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                @"SELECT TOP 1 o.*
                  FROM Objects o
                  WHERE o.IsActive = 1
                    AND o.Email IS NOT NULL
                    AND LOWER(o.Email) = @Email
                    AND o.IdentityId IS NOT NULL",
                new { Email = normalizedEmail },
                cancellationToken: cancellationToken,
                commandTimeout: 30);

            var identityObject = await connection.QueryFirstOrDefaultAsync<IdentityObject>(command);

            if (identityObject != null)
            {
                _logger.LogInformation("Found object {ObjectId} with IdentityId {IdentityId} by email {Email}",
                    identityObject.Id, identityObject.IdentityId, normalizedEmail);
            }
            else
            {
                _logger.LogDebug("No object found for email {Email}", normalizedEmail);
            }

            return identityObject;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error finding object by email: {Email}", email);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(FindObjectByEmailAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(FindObjectByEmailAsync));
        }
    }

    public async Task<Guid> CreateIdentityAsync(
        Identity identity,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(CreateIdentityAsync), new { identity.DisplayName });

        try
        {
            if (identity.Id == Guid.Empty)
                identity.Id = Guid.NewGuid();

            const string sql = @"
                INSERT INTO Identities (
                    Id, CentralId, DisplayName, FirstName, LastName, MiddleName, Suffix, Salutation,
                    PreferredName, DateOfBirth, Gender, NationalId, PhotoUrl,
                    PrimaryEmail, SecondaryEmail, PrimaryPhone, MobilePhone, HomePhone, Fax,
                    StreetAddress, City, State, PostalCode, Country,
                    EmployeeId, JobTitle, Department, Division, Company, Office, Building, Floor, Room,
                    CostCenter, ProfitCenter, IdentityType, ContractType, HireDate, TerminationDate, LastWorkDay,
                    Description, ManagerIdentityId, ManagerEmployeeId,
                    Username, UserPrincipalName, Status, IsActive, SecurityClearance, RiskScore, RiskLevel,
                    AuthoritativeSourceId, PreferredLanguage, TimeZone, Locale,
                    CreatedAt, ModifiedAt, LastSeenAt, LastLoginAt, PasswordLastChangedAt, LastAccessReviewAt,
                    CreatedBy, ModifiedBy, CustomAttributes
                )
                VALUES (
                    @Id, @CentralId, @DisplayName, @FirstName, @LastName, @MiddleName, @Suffix, @Salutation,
                    @PreferredName, @DateOfBirth, @Gender, @NationalId, @PhotoUrl,
                    @PrimaryEmail, @SecondaryEmail, @PrimaryPhone, @MobilePhone, @HomePhone, @Fax,
                    @StreetAddress, @City, @State, @PostalCode, @Country,
                    @EmployeeId, @JobTitle, @Department, @Division, @Company, @Office, @Building, @Floor, @Room,
                    @CostCenter, @ProfitCenter, @IdentityType, @ContractType, @HireDate, @TerminationDate, @LastWorkDay,
                    @Description, @ManagerIdentityId, @ManagerEmployeeId,
                    @Username, @UserPrincipalName, @Status, @IsActive, @SecurityClearance, @RiskScore, @RiskLevel,
                    @AuthoritativeSourceId, @PreferredLanguage, @TimeZone, @Locale,
                    @CreatedAt, @ModifiedAt, @LastSeenAt, @LastLoginAt, @PasswordLastChangedAt, @LastAccessReviewAt,
                    @CreatedBy, @ModifiedBy, @CustomAttributes
                )";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(sql, identity, cancellationToken: cancellationToken));

            _logger.LogInformation("Created identity {IdentityId}: {DisplayName}", identity.Id, identity.DisplayName);
            return identity.Id;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreateIdentityAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreateIdentityAsync));
        }
    }

    public async Task UpdateObjectIdentityLinkAsync(
        Guid objectId,
        Guid identityId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateObjectIdentityLinkAsync), new { objectId, identityId });

        try
        {
            const string sql = @"
                UPDATE Objects
                SET IdentityId = @IdentityId, LastSyncedAt = GETUTCDATE()
                WHERE Id = @ObjectId";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { ObjectId = objectId, IdentityId = identityId },
                cancellationToken: cancellationToken));

            _logger.LogDebug("Linked object {ObjectId} to identity {IdentityId}", objectId, identityId);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateObjectIdentityLinkAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateObjectIdentityLinkAsync));
        }
    }

    public async Task<Dictionary<string, GroupWithAttributes>> BulkLoadExistingGroupsAsync(
        Guid sourceConnectionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkLoadExistingGroupsAsync),
            new { sourceConnectionId });

        try
        {
            if (sourceConnectionId == Guid.Empty)
            {
                _logger.LogWarning("SourceConnectionId cannot be empty");
                throw new ArgumentException("SourceConnectionId cannot be empty", nameof(sourceConnectionId));
            }

            _logger.LogDebug("Bulk loading ALL existing groups for connection: {SourceConnectionId}", sourceConnectionId);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                @"-- Load all groups for this source connection
                  SELECT g.*
                  FROM Groups g
                  WHERE g.SourceConnectionId = @SourceConnectionId;

                  -- Load all attributes for these groups
                  SELECT ga.*
                  FROM GroupAttributes ga
                  INNER JOIN Groups g ON ga.GroupId = g.Id
                  WHERE g.SourceConnectionId = @SourceConnectionId;",
                new { SourceConnectionId = sourceConnectionId },
                cancellationToken: cancellationToken,
                commandTimeout: 300);

            using var multi = await connection.QueryMultipleAsync(command);

            var groups = (await multi.ReadAsync<Group>()).ToList();
            var attributes = (await multi.ReadAsync<GroupAttribute>()).ToList();

            _logger.LogInformation("Bulk loaded {GroupCount} groups with {AttributeCount} attributes",
                groups.Count, attributes.Count);

            var result = new Dictionary<string, GroupWithAttributes>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                if (string.IsNullOrWhiteSpace(group.SourceUniqueId))
                {
                    _logger.LogWarning("Skipping group {GroupId} with null/empty SourceUniqueId", group.Id);
                    continue;
                }

                var groupWithAttrs = new GroupWithAttributes
                {
                    Group = group,
                    Attributes = attributes.Where(a => a.GroupId == group.Id).ToList()
                };

                result[group.SourceUniqueId] = groupWithAttrs;
            }

            _logger.LogInformation("PERFORMANCE BOOST: Created in-memory dictionary with {Count} groups for O(1) lookups", result.Count);

            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error bulk loading groups for connection: {SourceConnectionId}", sourceConnectionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkLoadExistingGroupsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkLoadExistingGroupsAsync));
        }
    }

    public async Task<UpsertResult> UpsertGroupWithAttributesAsync(
        Group group,
        List<GroupAttribute> attributes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpsertGroupWithAttributesAsync),
            new { groupId = group.Id, sourceUniqueId = group.SourceUniqueId, attributeCount = attributes.Count });

        try
        {
            if (group == null)
            {
                _logger.LogWarning("Group cannot be null");
                throw new ArgumentNullException(nameof(group));
            }

            if (group.SourceConnectionId == Guid.Empty)
            {
                _logger.LogWarning("Group.SourceConnectionId cannot be empty");
                throw new ArgumentException("SourceConnectionId cannot be empty", nameof(group));
            }

            if (string.IsNullOrWhiteSpace(group.SourceUniqueId))
            {
                _logger.LogWarning("Group.SourceUniqueId cannot be null or whitespace");
                throw new ArgumentException("SourceUniqueId cannot be null or whitespace", nameof(group));
            }

            _logger.LogDebug("Upserting group {GroupId} with {AttributeCount} attributes",
                group.Id, attributes.Count);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var attributesJson = attributes.Any()
                ? JsonSerializer.Serialize(attributes.Select(a => new
                {
                    a.AttributeName,
                    a.AttributeValue,
                    a.DataType
                }))
                : null;

            var mergeCommand = new CommandDefinition(
                @"MERGE Groups AS target
                  USING (SELECT @Id AS Id, @SourceConnectionId AS SourceConnectionId, @SourceUniqueId AS SourceUniqueId) AS source
                  ON (target.SourceConnectionId = source.SourceConnectionId AND target.SourceUniqueId = source.SourceUniqueId)
                  WHEN MATCHED THEN
                      UPDATE SET
                          Name = @Name,
                          Description = @Description,
                          DistinguishedName = @DistinguishedName,
                          GroupType = @GroupType,
                          Email = @Email,
                          IsMailEnabled = @IsMailEnabled,
                          IsActive = @IsActive,
                          LastSyncedAt = @LastSyncedAt,
                          LastSeenAt = @LastSeenAt
                  WHEN NOT MATCHED THEN
                      INSERT (Id, SourceConnectionId, SourceUniqueId, SourceType, Name, Description, DistinguishedName, GroupType, Email, IsMailEnabled, IsActive, FirstSyncedAt, LastSyncedAt, LastSeenAt)
                      VALUES (@Id, @SourceConnectionId, @SourceUniqueId, @SourceType, @Name, @Description, @DistinguishedName, @GroupType, @Email, @IsMailEnabled, @IsActive, @FirstSyncedAt, @LastSyncedAt, @LastSeenAt);

                  SELECT @Id AS Id, CASE WHEN @@ROWCOUNT > 0 AND NOT EXISTS(SELECT 1 FROM Groups WHERE Id = @Id AND FirstSyncedAt < DATEADD(SECOND, -1, GETUTCDATE())) THEN 1 ELSE 0 END AS IsNew;",
                new
                {
                    group.Id,
                    group.SourceConnectionId,
                    group.SourceUniqueId,
                    group.SourceType,
                    group.Name,
                    group.Description,
                    group.DistinguishedName,
                    group.GroupType,
                    group.Email,
                    group.IsMailEnabled,
                    group.IsActive,
                    group.FirstSyncedAt,
                    group.LastSyncedAt,
                    group.LastSeenAt
                },
                cancellationToken: cancellationToken,
                commandTimeout: 300);

            var result = await connection.QuerySingleAsync<UpsertResult>(mergeCommand);

            var actualGroupId = await connection.QuerySingleAsync<Guid>(
                "SELECT Id FROM Groups WHERE SourceConnectionId = @SourceConnectionId AND SourceUniqueId = @SourceUniqueId",
                new { group.SourceConnectionId, group.SourceUniqueId },
                commandTimeout: 300);

            result.Id = actualGroupId;

            if (attributesJson != null)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM GroupAttributes WHERE GroupId = @GroupId",
                    new { GroupId = actualGroupId },
                    commandTimeout: 300);

                if (attributes.Any())
                {
                    var insertAttrCommand = @"INSERT INTO GroupAttributes (Id, GroupId, AttributeName, AttributeValue, DataType, LastSyncedAt)
                                              VALUES (@Id, @GroupId, @AttributeName, @AttributeValue, @DataType, @LastSyncedAt)";

                    await connection.ExecuteAsync(insertAttrCommand, attributes.Select(a => new
                    {
                        Id = Guid.NewGuid(),
                        GroupId = actualGroupId,
                        a.AttributeName,
                        a.AttributeValue,
                        a.DataType,
                        LastSyncedAt = DateTime.UtcNow
                    }), commandTimeout: 300);
                }
            }

            result.AttributesInserted = attributes.Count;

            _logger.LogInformation("Successfully upserted group {GroupId}, IsNew: {IsNew}, AttributesInserted: {AttributesInserted}",
                result.Id, result.IsNew, result.AttributesInserted);

            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error upserting group {GroupId}",
                group?.Id ?? Guid.Empty);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpsertGroupWithAttributesAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpsertGroupWithAttributesAsync));
        }
    }

    public async Task<GroupWithAttributes?> FindGroupBySourceUniqueIdAsync(
        Guid sourceConnectionId,
        string sourceUniqueId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(FindGroupBySourceUniqueIdAsync),
            new { sourceConnectionId, sourceUniqueId });

        try
        {
            if (sourceConnectionId == Guid.Empty)
            {
                _logger.LogWarning("SourceConnectionId cannot be empty");
                throw new ArgumentException("SourceConnectionId cannot be empty", nameof(sourceConnectionId));
            }

            if (string.IsNullOrWhiteSpace(sourceUniqueId))
            {
                _logger.LogWarning("SourceUniqueId cannot be null or whitespace");
                throw new ArgumentException("SourceUniqueId cannot be null or whitespace", nameof(sourceUniqueId));
            }

            _logger.LogDebug("Finding group by source unique ID: {SourceConnectionId} / {SourceUniqueId}",
                sourceConnectionId, sourceUniqueId);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                @"SELECT * FROM Groups WHERE SourceConnectionId = @SourceConnectionId AND SourceUniqueId = @SourceUniqueId;
                  SELECT ga.* FROM GroupAttributes ga
                  INNER JOIN Groups g ON ga.GroupId = g.Id
                  WHERE g.SourceConnectionId = @SourceConnectionId AND g.SourceUniqueId = @SourceUniqueId;",
                new { SourceConnectionId = sourceConnectionId, SourceUniqueId = sourceUniqueId },
                cancellationToken: cancellationToken,
                commandTimeout: 300);

            using var multi = await connection.QueryMultipleAsync(command);

            var groupResult = await multi.ReadFirstOrDefaultAsync<Group>();
            if (groupResult == null)
            {
                _logger.LogDebug("Group not found for source unique ID: {SourceUniqueId}", sourceUniqueId);
                return null;
            }

            var groupAttributes = (await multi.ReadAsync<GroupAttribute>()).ToList();

            _logger.LogInformation("Successfully found group {GroupId} with {AttributeCount} attributes",
                groupResult.Id, groupAttributes.Count);

            return new GroupWithAttributes
            {
                Group = groupResult,
                Attributes = groupAttributes
            };
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error finding group by source unique ID: {SourceConnectionId} / {SourceUniqueId}",
                sourceConnectionId, sourceUniqueId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(FindGroupBySourceUniqueIdAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(FindGroupBySourceUniqueIdAsync));
        }
    }

    public async Task<List<ObjectWithAttributes>> GetUnmatchedObjectsFromRunAsync(
        Guid syncProjectRunId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetUnmatchedObjectsFromRunAsync), new { syncProjectRunId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT DISTINCT o.Id, o.SourceConnectionId, o.SourceUniqueId, o.SourceType,
                       o.DisplayName, o.IsActive, o.FirstSyncedAt
                FROM Objects o
                INNER JOIN SyncAuditLogs sal ON o.Id = sal.ObjectId
                INNER JOIN SyncStepRuns ssr ON sal.SyncStepRunId = ssr.Id
                WHERE ssr.SyncProjectRunId = @SyncProjectRunId
                  AND o.IdentityId IS NULL
                  AND sal.OperationType = 'Created'
                ORDER BY o.FirstSyncedAt;
            ";

            var objects = (await connection.QueryAsync<IdentityObject>(sql,
                new { SyncProjectRunId = syncProjectRunId },
                commandTimeout: 120)).ToList();

            _logger.LogInformation("Found {Count} unmatched objects for run {RunId}",
                objects.Count, syncProjectRunId);

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

            var result = new List<ObjectWithAttributes>();
            foreach (var obj in objects)
            {
                var objAttributes = allAttributes.Where(a => a.ObjectId == obj.Id).ToList();
                result.Add(new ObjectWithAttributes
                {
                    Object = obj,
                    Attributes = objAttributes
                });
            }

            return result;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetUnmatchedObjectsFromRunAsync));
        }
    }

    public async Task<(int TotalSynced, int AlreadyMatched, int NeedingMatch)> GetUserObjectCountsFromRunAsync(
        Guid syncProjectRunId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetUserObjectCountsFromRunAsync), new { syncProjectRunId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    COUNT(DISTINCT o.Id) AS TotalSynced,
                    COUNT(DISTINCT CASE WHEN o.IdentityId IS NOT NULL THEN o.Id END) AS AlreadyMatched,
                    COUNT(DISTINCT CASE WHEN o.IdentityId IS NULL THEN o.Id END) AS NeedingMatch
                FROM Objects o
                INNER JOIN SyncAuditLogs sal ON o.Id = sal.ObjectId
                INNER JOIN SyncStepRuns ssr ON sal.SyncStepRunId = ssr.Id
                WHERE ssr.SyncProjectRunId = @SyncProjectRunId
                  AND o.ObjectClass IN ('user', 'contact')
                  AND (o.IsBuiltIn = 0 OR o.IsBuiltIn IS NULL);
            ";

            var result = await connection.QuerySingleAsync<(int TotalSynced, int AlreadyMatched, int NeedingMatch)>(sql,
                new { SyncProjectRunId = syncProjectRunId },
                commandTimeout: 120);

            _logger.LogInformation("User/contact counts for run {RunId}: Total={Total}, AlreadyMatched={Matched}, NeedingMatch={Needing}",
                syncProjectRunId, result.TotalSynced, result.AlreadyMatched, result.NeedingMatch);

            return result;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetUserObjectCountsFromRunAsync));
        }
    }

    public async Task<Dictionary<string, Guid>> GetObjectIdsBySourceUniqueIdsAsync(
        Guid sourceConnectionId,
        List<string> sourceUniqueIds,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetObjectIdsBySourceUniqueIdsAsync),
            new { sourceConnectionId, count = sourceUniqueIds?.Count ?? 0 });

        try
        {
            if (sourceUniqueIds == null || !sourceUniqueIds.Any())
            {
                return new Dictionary<string, Guid>();
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT SourceUniqueId, Id
                FROM Objects
                WHERE SourceConnectionId = @SourceConnectionId
                  AND SourceUniqueId IN @SourceUniqueIds
            ";

            var results = await connection.QueryAsync<(string SourceUniqueId, Guid Id)>(sql,
                new { SourceConnectionId = sourceConnectionId, SourceUniqueIds = sourceUniqueIds },
                commandTimeout: 120);

            return results.ToDictionary(r => r.SourceUniqueId, r => r.Id, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetObjectIdsBySourceUniqueIdsAsync));
        }
    }

    public async Task<Dictionary<string, Guid>> GetObjectIdsByDistinguishedNamesAsync(
        Guid sourceConnectionId,
        List<string> distinguishedNames,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetObjectIdsByDistinguishedNamesAsync),
            new { sourceConnectionId, count = distinguishedNames?.Count ?? 0 });

        try
        {
            if (distinguishedNames == null || !distinguishedNames.Any())
            {
                return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT DN AS DistinguishedName, Id
                FROM Objects
                WHERE SourceConnectionId = @SourceConnectionId
                  AND DN IN @DistinguishedNames
            ";

            var results = await connection.QueryAsync<(string DistinguishedName, Guid Id)>(sql,
                new { SourceConnectionId = sourceConnectionId, DistinguishedNames = distinguishedNames },
                commandTimeout: 120);

            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results)
            {
                if (!string.IsNullOrWhiteSpace(r.DistinguishedName))
                {
                    map[r.DistinguishedName] = r.Id;
                }
            }
            return map;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetObjectIdsByDistinguishedNamesAsync));
        }
    }

    public async Task<List<ObjectWithAttributes>> GetAllUnmatchedUserObjectsAsync(
        Guid sourceConnectionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetAllUnmatchedUserObjectsAsync), new { sourceConnectionId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT o.Id, o.SourceConnectionId, o.SourceUniqueId, o.SourceType,
                       o.DisplayName, o.IsActive, o.FirstSyncedAt, o.ObjectClass, o.IsBuiltIn
                FROM Objects o
                WHERE o.SourceConnectionId = @SourceConnectionId
                  AND o.IdentityId IS NULL
                  AND o.ObjectClass IN ('user', 'contact')
                  AND (o.IsBuiltIn = 0 OR o.IsBuiltIn IS NULL)
                ORDER BY o.FirstSyncedAt;
            ";

            var objects = (await connection.QueryAsync<IdentityObject>(sql,
                new { SourceConnectionId = sourceConnectionId },
                commandTimeout: 300)).ToList();

            _logger.LogInformation("Found {Count} unmatched user/contact objects for connection {ConnectionId}",
                objects.Count, sourceConnectionId);

            if (objects.Count == 0)
            {
                return new List<ObjectWithAttributes>();
            }

            var objectIds = objects.Select(o => o.Id).ToList();
            var attributesSql = @"
                SELECT oa.*
                FROM ObjectAttributes oa
                WHERE oa.ObjectId IN @ObjectIds
                  AND oa.AttributeName IN ('mail', 'givenName', 'sn', 'sAMAccountName', 'department', 'title')
                ORDER BY oa.ObjectId;
            ";

            var allAttributes = (await connection.QueryAsync<ObjectAttribute>(attributesSql,
                new { ObjectIds = objectIds },
                commandTimeout: 300)).ToList();

            var attributesByObjectId = allAttributes.GroupBy(a => a.ObjectId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = objects.Select(obj => new ObjectWithAttributes
            {
                Object = obj,
                Attributes = attributesByObjectId.TryGetValue(obj.Id, out var attrs)
                    ? attrs
                    : new List<ObjectAttribute>()
            }).ToList();

            _logger.LogInformation("Loaded {Count} unmatched objects with attributes", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all unmatched user objects: {Message}", ex.Message);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAllUnmatchedUserObjectsAsync));
        }
    }

    public async Task UpdateObjectIdentityIdAsync(
        Guid objectId,
        Guid identityId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateObjectIdentityIdAsync), new { objectId, identityId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE Objects
                SET IdentityId = @IdentityId,
                    LastSyncedAt = GETUTCDATE()
                WHERE Id = @ObjectId;
            ";

            await connection.ExecuteAsync(sql,
                new { ObjectId = objectId, IdentityId = identityId },
                commandTimeout: 30);

            _logger.LogDebug("Updated object {ObjectId} with IdentityId {IdentityId}",
                objectId, identityId);
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateObjectIdentityIdAsync));
        }
    }

    public async Task<int> BulkUpsertObjectGroupMembershipsAsync(
        List<(Guid ObjectId, Guid GroupId, bool IsDirect, bool IsPrimary)> memberships,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkUpsertObjectGroupMembershipsAsync));

        if (!memberships.Any())
        {
            _logger.LogInformation("No memberships to upsert");
            return 0;
        }

        await _sqlBulkCopySemaphore.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(@"
                CREATE TABLE #TempMemberships (
                    ObjectId UNIQUEIDENTIFIER NOT NULL,
                    GroupId UNIQUEIDENTIFIER NOT NULL,
                    IsDirect BIT NOT NULL,
                    IsPrimary BIT NOT NULL
                )", commandTimeout: 30);

            var dataTable = new DataTable();
            dataTable.Columns.Add("ObjectId", typeof(Guid));
            dataTable.Columns.Add("GroupId", typeof(Guid));
            dataTable.Columns.Add("IsDirect", typeof(bool));
            dataTable.Columns.Add("IsPrimary", typeof(bool));

            foreach (var m in memberships)
            {
                dataTable.Rows.Add(m.ObjectId, m.GroupId, m.IsDirect, m.IsPrimary);
            }

            using (var bulkCopy = new SqlBulkCopy(connection))
            {
                bulkCopy.DestinationTableName = "#TempMemberships";
                bulkCopy.BulkCopyTimeout = 120;
                await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
            }

            _logger.LogInformation("Bulk copied {Count} memberships to temp table", memberships.Count);

            var affected = await connection.ExecuteScalarAsync<int>(@"
                DECLARE @Affected INT = 0;

                MERGE ObjectGroupMemberships AS target
                USING #TempMemberships AS source
                ON (target.ObjectId = source.ObjectId AND target.GroupId = source.GroupId)
                WHEN MATCHED THEN
                    UPDATE SET
                        IsDirect = source.IsDirect,
                        IsPrimary = source.IsPrimary,
                        LastSyncedAt = GETUTCDATE(),
                        RemovedAt = NULL,
                        IsActive = 1
                WHEN NOT MATCHED THEN
                    INSERT (Id, ObjectId, GroupId, IsDirect, IsPrimary, AddedAt, LastSyncedAt, IsActive)
                    VALUES (NEWID(), source.ObjectId, source.GroupId, source.IsDirect, source.IsPrimary, GETUTCDATE(), GETUTCDATE(), 1);

                SET @Affected = @@ROWCOUNT;

                DROP TABLE #TempMemberships;

                SELECT @Affected;
            ", commandTimeout: 120);

            _logger.LogInformation("Bulk upserted {Count} memberships ({Affected} affected)",
                memberships.Count, affected);

            return affected;
        }
        finally
        {
            _sqlBulkCopySemaphore.Release();
            _logger.LogMethodExit(nameof(BulkUpsertObjectGroupMembershipsAsync));
        }
    }

    public async Task<int> MarkRemovedObjectGroupMembershipsAsync(
        Guid objectId,
        List<Guid> currentGroupIds,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(MarkRemovedObjectGroupMembershipsAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var json = System.Text.Json.JsonSerializer.Serialize(
                currentGroupIds.Select(id => id.ToString()));

            var result = await connection.QuerySingleAsync<dynamic>(
                "dbo.usp_MarkRemovedObjectGroupMemberships",
                new { ObjectId = objectId, CurrentGroupIdsJson = json },
                commandType: CommandType.StoredProcedure,
                commandTimeout: 30);

            _logger.LogInformation("Marked {Count} memberships as removed for object {ObjectId}",
                result.MembershipsRemoved, objectId);

            return (int)result.MembershipsRemoved;
        }
        finally
        {
            _logger.LogMethodExit(nameof(MarkRemovedObjectGroupMembershipsAsync));
        }
    }

    public async Task<int> BulkInsertIdentitiesAsync(
        List<Identity> identities,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkInsertIdentitiesAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var json = System.Text.Json.JsonSerializer.Serialize(
                identities.Select(i => new {
                    Id = i.Id.ToString(),
                    i.FirstName,
                    i.LastName,
                    PrimaryEmail = i.PrimaryEmail,
                    PrimaryPhone = i.PrimaryPhone,
                    i.Department,
                    i.JobTitle,
                    AuthoritativeSourceId = i.AuthoritativeSourceId?.ToString(),
                    i.IsActive
                }));

            var result = await connection.QuerySingleAsync<dynamic>(
                "dbo.usp_BulkInsertIdentities",
                new { IdentitiesJson = json },
                commandType: CommandType.StoredProcedure,
                commandTimeout: 300);

            _logger.LogInformation("Bulk inserted {Count} identities in single operation",
                result.IdentitiesInserted);

            return (int)result.IdentitiesInserted;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkInsertIdentitiesAsync));
        }
    }

    public async Task<int> BulkAssignTagToObjectsAsync(
        Guid tagId,
        List<Guid> objectIds,
        bool isInherited = true,
        CancellationToken cancellationToken = default)
    {
        if (objectIds == null || objectIds.Count == 0)
            return 0;

        _logger.LogMethodEntry(nameof(BulkAssignTagToObjectsAsync), new { tagId, objectCount = objectIds.Count, isInherited });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            int totalInserted = 0;

            const int batchSize = 10000;
            for (int i = 0; i < objectIds.Count; i += batchSize)
            {
                var batch = objectIds.Skip(i).Take(batchSize).ToList();

                var jsonArray = System.Text.Json.JsonSerializer.Serialize(batch.Select(id => id.ToString()));

                var sql = @"
                    MERGE INTO ObjectTags AS target
                    USING (
                        SELECT TRY_CAST(value AS UNIQUEIDENTIFIER) AS ObjectId, @TagId AS TagId
                        FROM OPENJSON(@ObjectIdsJson)
                        WHERE TRY_CAST(value AS UNIQUEIDENTIFIER) IS NOT NULL
                          AND EXISTS (SELECT 1 FROM Objects WHERE Id = TRY_CAST(value AS UNIQUEIDENTIFIER))
                    ) AS source
                    ON target.ObjectId = source.ObjectId AND target.TagId = source.TagId
                    WHEN NOT MATCHED THEN
                        INSERT (Id, ObjectId, TagId, IsInherited, CreatedAt)
                        VALUES (NEWID(), source.ObjectId, source.TagId, @IsInherited, GETUTCDATE());

                    SELECT @@ROWCOUNT;
                ";

                var command = new CommandDefinition(
                    sql,
                    new { TagId = tagId, ObjectIdsJson = jsonArray, IsInherited = isInherited },
                    commandTimeout: 60,
                    cancellationToken: cancellationToken);

                var result = await connection.QuerySingleAsync<int>(command);
                totalInserted += result;

                _logger.LogDebug("Assigned tag to batch {BatchNum}: {Inserted} objects", (i / batchSize) + 1, result);
            }

            _logger.LogInformation("Assigned tag {TagId} to {TotalInserted} objects (out of {TotalRequested})",
                tagId, totalInserted, objectIds.Count);

            return totalInserted;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkAssignTagToObjectsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkAssignTagToObjectsAsync));
        }
    }

    public async Task<int> BulkAssignTagToObjectsBySourceAsync(
        Guid tagId,
        Guid sourceConnectionId,
        List<string> sourceUniqueIds,
        bool isInherited = true,
        CancellationToken cancellationToken = default)
    {
        if (sourceUniqueIds == null || sourceUniqueIds.Count == 0)
            return 0;

        _logger.LogMethodEntry(nameof(BulkAssignTagToObjectsBySourceAsync),
            new { tagId, sourceConnectionId, sourceUniqueIdCount = sourceUniqueIds.Count, isInherited });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("TAG DEBUG: Sample SourceUniqueIds to match: [{InputSamples}]",
                string.Join(", ", sourceUniqueIds.Take(3)));

            int totalInserted = 0;

            const int batchSize = 10000;
            for (int i = 0; i < sourceUniqueIds.Count; i += batchSize)
            {
                var batch = sourceUniqueIds.Skip(i).Take(batchSize).ToList();

                var jsonArray = System.Text.Json.JsonSerializer.Serialize(batch);

                var sql = @"
                    INSERT INTO ObjectTags (Id, ObjectId, TagId, IsInherited, CreatedAt)
                    SELECT NEWID(), o.Id, @TagId, @IsInherited, GETUTCDATE()
                    FROM OPENJSON(@SourceUniqueIdsJson) AS j
                    INNER JOIN Objects o ON o.SourceUniqueId = j.value
                        AND o.SourceConnectionId = @SourceConnectionId
                    WHERE NOT EXISTS (
                        SELECT 1 FROM ObjectTags ot
                        WHERE ot.ObjectId = o.Id AND ot.TagId = @TagId
                    )
                ";

                var command = new CommandDefinition(
                    sql,
                    new { TagId = tagId, SourceConnectionId = sourceConnectionId, SourceUniqueIdsJson = jsonArray, IsInherited = isInherited },
                    commandTimeout: 120,
                    cancellationToken: cancellationToken);

                var result = await connection.ExecuteAsync(command);
                totalInserted += result;

                _logger.LogDebug("Assigned tag to batch {BatchNum}: {Inserted} objects", (i / batchSize) + 1, result);
            }

            _logger.LogDebug("Assigned tag {TagId} to {TotalInserted} objects by SourceUniqueId (out of {TotalRequested})",
                tagId, totalInserted, sourceUniqueIds.Count);

            return totalInserted;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkAssignTagToObjectsBySourceAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkAssignTagToObjectsBySourceAsync));
        }
    }

    public async Task<List<ObjectWithAttributes>> GetUnlinkedObjectsAsync(
        string objectClass,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            -- Get sample unlinked objects with attributes
            SELECT TOP (@Limit) o.*
            FROM Objects o
            WHERE o.ObjectClass = @ObjectClass
            AND o.IdentityId IS NULL
            AND o.IsActive = 1
            ORDER BY o.LastSyncedAt DESC;

            -- Get all attributes for these objects
            SELECT oa.*
            FROM ObjectAttributes oa
            INNER JOIN Objects o ON oa.ObjectId = o.Id
            WHERE o.ObjectClass = @ObjectClass
            AND o.IdentityId IS NULL
            AND o.IsActive = 1
            AND o.Id IN (SELECT TOP (@Limit) Id FROM Objects WHERE ObjectClass = @ObjectClass AND IdentityId IS NULL AND IsActive = 1 ORDER BY LastSyncedAt DESC);";

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
                sql, new { ObjectClass = objectClass, Limit = limit },
                cancellationToken: cancellationToken));

            var objects = (await multi.ReadAsync<IdentityObject>()).ToList();
            var attributes = (await multi.ReadAsync<ObjectAttribute>()).ToList();

            var result = new List<ObjectWithAttributes>();
            foreach (var obj in objects)
            {
                result.Add(new ObjectWithAttributes
                {
                    Object = obj,
                    Attributes = attributes.Where(a => a.ObjectId == obj.Id).ToList()
                });
            }

            _logger.LogInformation("Retrieved {Count} unlinked {ObjectClass} objects for preview", result.Count, objectClass);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unlinked objects for class {ObjectClass}", objectClass);
            throw;
        }
    }

    public async Task<List<ObjectWithAttributes>> GetObjectsByIdsAsync(
        List<Guid> objectIds,
        CancellationToken cancellationToken = default)
    {
        if (objectIds == null || !objectIds.Any())
        {
            return new List<ObjectWithAttributes>();
        }

        const string sql = @"
            SELECT o.* FROM Objects o WHERE o.Id IN @ObjectIds;
            SELECT oa.* FROM ObjectAttributes oa WHERE oa.ObjectId IN @ObjectIds;";

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
                sql, new { ObjectIds = objectIds },
                cancellationToken: cancellationToken));

            var objects = (await multi.ReadAsync<IdentityObject>()).ToList();
            var attributes = (await multi.ReadAsync<ObjectAttribute>()).ToList();

            var result = new List<ObjectWithAttributes>();
            foreach (var obj in objects)
            {
                result.Add(new ObjectWithAttributes
                {
                    Object = obj,
                    Attributes = attributes.Where(a => a.ObjectId == obj.Id).ToList()
                });
            }

            _logger.LogInformation("Retrieved {Count} objects by ID for simulation", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting objects by IDs");
            throw;
        }
    }

    public async Task<int> GetCountAsync(
        string tableName,
        string? whereClause = null,
        CancellationToken cancellationToken = default)
    {
        var allowedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Objects", "Identities", "Groups", "ObjectGroupMemberships", "IdentityGroupMemberships",
            "ObjectAttributes", "GroupAttributes", "ObjectTags", "IdentityTags"
        };

        if (!allowedTables.Contains(tableName))
        {
            throw new ArgumentException($"Table '{tableName}' is not allowed for count queries", nameof(tableName));
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"SELECT COUNT(*) FROM {tableName}";
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sql += $" WHERE {whereClause}";
            }

            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                sql,
                cancellationToken: cancellationToken));

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting count from {Table} with where: {Where}", tableName, whereClause);
            return 0;
        }
    }

    public async Task<DataStatisticsResult> GetDataStatisticsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = @"
            SELECT
                (SELECT COUNT(*) FROM Objects) AS ObjectCount,
                (SELECT COUNT(*) FROM Identities) AS IdentityCount,
                (SELECT COUNT(*) FROM Groups) AS GroupCount,
                (SELECT COUNT(*) FROM IdentityGroupMemberships) AS MembershipCount";

        var result = await connection.QuerySingleAsync<DataStatisticsResult>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return result;
    }
}
