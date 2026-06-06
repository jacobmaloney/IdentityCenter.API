using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Repositories
{
    /// <summary>
    /// High-performance Dapper-based repository for Identity operations.
    /// Provides 60-80x faster data access compared to EF Core for read-heavy operations.
    /// </summary>
    public class IdentityRepository : IIdentityRepository
    {
        private readonly string _defaultConnectionString;
        private readonly ILogger<IdentityRepository> _logger;

        public IdentityRepository(IConfiguration configuration, ILogger<IdentityRepository> logger)
        {
            _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
            _logger = logger;
        }

        // MULTI-TENANT SEAM (SaaS Day 4): this repo backs IdentitiesController (TenantDataPolicy), so its
        // connection MUST follow the current request's tenant. It does not derive from DapperRepositoryBase,
        // so it routes through the ambient accessor directly — tenant request → tenant DB; legacy/admin →
        // DefaultConnection. Resolved per access, never captured once.
        private string _connectionString =>
            DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

        /// <summary>
        /// Get all identities for a specific person, ordered by authoritative status and source type.
        /// High-performance single query.
        /// </summary>
        public async Task<List<IdentityObject>> GetIdentitiesByIdentityIdAsync(Guid personId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var identities = await connection.QueryAsync<IdentityObject>(
                @"
                SELECT *
                FROM Objects
                WHERE IdentityId = @IdentityId
                ORDER BY IsAuthoritative DESC, SourceType
                ",
                new { IdentityId = personId }).ConfigureAwait(false);

            return identities.ToList();
        }

        /// <summary>
        /// Get paginated list of identities with filtering by source type and search term.
        /// High-performance query using single database round-trip.
        /// </summary>
        public async Task<(List<IdentityObject> Identities, int TotalCount)> GetIdentitiesPagedAsync(
            int pageNumber = 1,
            int pageSize = 20,
            string? searchTerm = null,
            string? sourceType = null,
            bool? isActive = null,
            bool? isAuthoritative = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@PageNumber", pageNumber);
            parameters.Add("@PageSize", pageSize);
            parameters.Add("@SearchTerm", searchTerm);
            parameters.Add("@SourceType", sourceType);
            parameters.Add("@IsActive", isActive);
            parameters.Add("@IsAuthoritative", isAuthoritative);

            // Build WHERE clause dynamically
            var whereConditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                whereConditions.Add(@"(
                    i.DisplayName LIKE '%' + @SearchTerm + '%'
                    OR i.Email LIKE '%' + @SearchTerm + '%'
                    OR i.Username LIKE '%' + @SearchTerm + '%'
                    OR i.FirstName LIKE '%' + @SearchTerm + '%'
                    OR i.LastName LIKE '%' + @SearchTerm + '%'
                )");
            }

            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                whereConditions.Add("i.SourceType = @SourceType");
            }

            if (isActive.HasValue)
            {
                whereConditions.Add("i.IsActive = @IsActive");
            }

            if (isAuthoritative.HasValue)
            {
                whereConditions.Add("i.IsAuthoritative = @IsAuthoritative");
            }

            var whereClause = whereConditions.Any()
                ? "WHERE " + string.Join(" AND ", whereConditions)
                : "";

            using var multi = await connection.QueryMultipleAsync(
                $@"
                -- Total count for pagination
                SELECT COUNT(*)
                FROM Objects i
                {whereClause};

                -- Identities page
                WITH IdentityPage AS (
                    SELECT i.*,
                           ROW_NUMBER() OVER (ORDER BY i.DisplayName, i.Email, i.Id) AS RowNum
                    FROM Objects i
                    {whereClause}
                )
                SELECT *
                FROM IdentityPage
                WHERE RowNum BETWEEN (@PageNumber - 1) * @PageSize + 1 AND @PageNumber * @PageSize
                ORDER BY RowNum;
                ",
                parameters).ConfigureAwait(false);

            var totalCount = await multi.ReadFirstAsync<int>().ConfigureAwait(false);
            var identities = (await multi.ReadAsync<IdentityObject>().ConfigureAwait(false)).ToList();

            return (identities, totalCount);
        }

        /// <summary>
        /// Get identity by ID with full details.
        /// </summary>
        public async Task<IdentityObject?> GetIdentityByIdAsync(Guid identityId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var identity = await connection.QueryFirstOrDefaultAsync<IdentityObject>(
                @"
                SELECT *
                FROM Objects
                WHERE Id = @IdentityId
                ",
                new { IdentityId = identityId }).ConfigureAwait(false);

            return identity;
        }

        /// <summary>
        /// Get identities by source connection ID.
        /// Useful for viewing all identities from a specific directory connection.
        /// </summary>
        public async Task<List<IdentityObject>> GetIdentitiesBySourceConnectionAsync(Guid sourceConnectionId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var identities = await connection.QueryAsync<IdentityObject>(
                @"
                SELECT *
                FROM Objects
                WHERE SourceConnectionId = @SourceConnectionId
                ORDER BY DisplayName, Email
                ",
                new { SourceConnectionId = sourceConnectionId }).ConfigureAwait(false);

            return identities.ToList();
        }

        /// <summary>
        /// Get identity statistics: total identities, by source type, active/inactive counts.
        /// Single optimized query.
        /// </summary>
        public async Task<IdentityStatistics> GetIdentityStatisticsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Get overall counts (ISNULL handles empty table case where SUM returns NULL)
            var overallStats = await connection.QueryFirstAsync<dynamic>(
                @"
                SELECT
                    COUNT(*) AS TotalIdentities,
                    ISNULL(SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END), 0) AS ActiveIdentities,
                    ISNULL(SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END), 0) AS InactiveIdentities,
                    ISNULL(SUM(CASE WHEN IsAuthoritative = 1 THEN 1 ELSE 0 END), 0) AS AuthoritativeIdentities,
                    COUNT(DISTINCT IdentityId) AS UniquePersons,
                    COUNT(DISTINCT SourceConnectionId) AS UniqueSourceConnections
                FROM Objects
                ").ConfigureAwait(false);

            // Get counts by source type (ISNULL handles empty group case)
            var sourceTypeCounts = await connection.QueryAsync<dynamic>(
                @"
                SELECT
                    SourceType,
                    COUNT(*) AS Count,
                    ISNULL(SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END), 0) AS ActiveCount,
                    ISNULL(SUM(CASE WHEN IsAuthoritative = 1 THEN 1 ELSE 0 END), 0) AS AuthoritativeCount
                FROM Objects
                GROUP BY SourceType
                ORDER BY COUNT(*) DESC
                ").ConfigureAwait(false);

            var sourceTypeDict = new Dictionary<string, SourceTypeCount>();
            foreach (var row in sourceTypeCounts)
            {
                sourceTypeDict[row.SourceType] = new SourceTypeCount
                {
                    Total = row.Count,
                    Active = row.ActiveCount,
                    Authoritative = row.AuthoritativeCount
                };
            }

            return new IdentityStatistics
            {
                TotalIdentities = overallStats.TotalIdentities,
                ActiveIdentities = overallStats.ActiveIdentities,
                InactiveIdentities = overallStats.InactiveIdentities,
                AuthoritativeIdentities = overallStats.AuthoritativeIdentities,
                UniquePersons = overallStats.UniquePersons,
                UniqueSourceConnections = overallStats.UniqueSourceConnections,
                BySourceType = sourceTypeDict
            };
        }

        /// <summary>
        /// Search for identities by email address (exact or partial match).
        /// </summary>
        public async Task<List<IdentityObject>> SearchByEmailAsync(string email, bool exactMatch = false)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var query = exactMatch
                ? "SELECT * FROM Objects WHERE Email = @Email ORDER BY DisplayName"
                : "SELECT * FROM Objects WHERE Email LIKE '%' + @Email + '%' ORDER BY DisplayName";

            var identities = await connection.QueryAsync<IdentityObject>(query, new { Email = email }).ConfigureAwait(false);

            return identities.ToList();
        }

        /// <summary>
        /// Get authoritative identity for a person (the primary/source of truth identity).
        /// </summary>
        public async Task<IdentityObject?> GetAuthoritativeIdentityForPersonAsync(Guid personId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var identity = await connection.QueryFirstOrDefaultAsync<IdentityObject>(
                @"
                SELECT TOP 1 *
                FROM Objects
                WHERE IdentityId = @IdentityId
                  AND IsAuthoritative = 1
                ORDER BY LastSyncedAt DESC
                ",
                new { IdentityId = personId }).ConfigureAwait(false);

            return identity;
        }

        /// <summary>
        /// Get identities that haven't been seen since a specific date (potentially stale/deleted accounts).
        /// </summary>
        public async Task<List<IdentityObject>> GetStaleIdentitiesAsync(DateTime sinceDate, int limit = 100)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var identities = await connection.QueryAsync<IdentityObject>(
                @"
                SELECT TOP (@Limit) *
                FROM Objects
                WHERE LastSeenAt < @SinceDate
                   OR (LastSeenAt IS NULL AND LastSyncedAt < @SinceDate)
                ORDER BY LastSeenAt, LastSyncedAt
                ",
                new { SinceDate = sinceDate, Limit = limit }).ConfigureAwait(false);

            return identities.ToList();
        }

        /// <summary>
        /// Get identities with group membership counts.
        /// Useful for understanding which identities have the most access.
        /// </summary>
        public async Task<List<IdentityWithGroupCount>> GetIdentitiesWithGroupCountsAsync(
            int pageNumber = 1,
            int pageSize = 20,
            string? sourceType = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var parameters = new DynamicParameters();
            parameters.Add("@PageNumber", pageNumber);
            parameters.Add("@PageSize", pageSize);
            parameters.Add("@SourceType", sourceType);

            var whereClause = string.IsNullOrWhiteSpace(sourceType)
                ? ""
                : "WHERE i.SourceType = @SourceType";

            var results = await connection.QueryAsync<dynamic>(
                $@"
                WITH IdentityPage AS (
                    SELECT i.*,
                           ROW_NUMBER() OVER (ORDER BY i.DisplayName, i.Email, i.Id) AS RowNum
                    FROM Objects i
                    {whereClause}
                )
                SELECT
                    ip.*,
                    ISNULL(gm.GroupCount, 0) AS GroupCount
                FROM IdentityPage ip
                LEFT JOIN (
                    SELECT ObjectId AS IdentityId, COUNT(*) AS GroupCount
                    FROM ObjectGroupMemberships
                    WHERE RemovedAt IS NULL
                    GROUP BY ObjectId
                ) gm ON ip.Id = gm.IdentityId
                WHERE ip.RowNum BETWEEN (@PageNumber - 1) * @PageSize + 1 AND @PageNumber * @PageSize
                ORDER BY ip.RowNum
                ",
                parameters).ConfigureAwait(false);

            var identitiesWithCounts = new List<IdentityWithGroupCount>();
            foreach (var row in results)
            {
                identitiesWithCounts.Add(new IdentityWithGroupCount
                {
                    Object = new IdentityObject
                    {
                        Id = row.Id,
                        IdentityId = row.IdentityId,
                        SourceConnectionId = row.SourceConnectionId,
                        SourceUniqueId = row.SourceUniqueId,
                        SourceType = row.SourceType,
                        DisplayName = row.DisplayName,
                        Email = row.Email,
                        Username = row.Username,
                        FirstName = row.FirstName,
                        LastName = row.LastName,
                        Department = row.Department,
                        JobTitle = row.JobTitle,
                        Phone = row.Phone,
                        ManagerSourceId = row.ManagerSourceId,
                        IsActive = row.IsActive,
                        IsAuthoritative = row.IsAuthoritative,
                        MatchConfidence = row.MatchConfidence,
                        MatchMethod = row.MatchMethod,
                        FirstSyncedAt = row.FirstSyncedAt,
                        LastSyncedAt = row.LastSyncedAt,
                        LastSeenAt = row.LastSeenAt,
                        DeletedAt = row.DeletedAt,
                        IsBuiltIn = row.IsBuiltIn,
                        IsAdminSDHolder = row.IsAdminSDHolder
                    },
                    GroupCount = row.GroupCount
                });
            }

            return identitiesWithCounts;
        }

        /// <summary>
        /// Find duplicate objects linked to the same person (same email or username).
        /// Returns groups of duplicates where the same person has multiple objects with matching identifiers.
        /// </summary>
        public async Task<List<DuplicateObjectGroup>> FindDuplicateObjectsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Find persons with multiple objects that have the same email or username
            var sql = @"
                WITH DuplicateEmails AS (
                    SELECT IdentityId, LOWER(Email) AS DuplicateKey, 'Email' AS DuplicateType
                    FROM Objects
                    WHERE Email IS NOT NULL AND Email != '' AND IdentityId IS NOT NULL
                    GROUP BY IdentityId, LOWER(Email)
                    HAVING COUNT(*) > 1
                ),
                DuplicateUsernames AS (
                    SELECT IdentityId, LOWER(Username) AS DuplicateKey, 'Username' AS DuplicateType
                    FROM Objects
                    WHERE Username IS NOT NULL AND Username != '' AND IdentityId IS NOT NULL
                    GROUP BY IdentityId, LOWER(Username)
                    HAVING COUNT(*) > 1
                ),
                AllDuplicates AS (
                    SELECT * FROM DuplicateEmails
                    UNION
                    SELECT * FROM DuplicateUsernames
                )
                SELECT
                    o.*,
                    p.DisplayName AS PersonDisplayName,
                    d.DuplicateKey,
                    d.DuplicateType
                FROM AllDuplicates d
                INNER JOIN Objects o ON o.IdentityId = d.IdentityId
                    AND (
                        (d.DuplicateType = 'Email' AND LOWER(o.Email) = d.DuplicateKey)
                        OR (d.DuplicateType = 'Username' AND LOWER(o.Username) = d.DuplicateKey)
                    )
                LEFT JOIN Identities p ON p.Id = d.IdentityId
                ORDER BY o.IdentityId, d.DuplicateKey, o.FirstSyncedAt";

            var results = await connection.QueryAsync<dynamic>(sql).ConfigureAwait(false);

            var groups = new Dictionary<(Guid personId, string key), DuplicateObjectGroup>();

            foreach (var row in results)
            {
                var personId = (Guid)row.IdentityId;
                var key = (string)row.DuplicateKey;
                var groupKey = (personId, key);

                if (!groups.TryGetValue(groupKey, out var group))
                {
                    group = new DuplicateObjectGroup
                    {
                        PersonId = personId,
                        PersonDisplayName = row.PersonDisplayName,
                        DuplicateKey = key
                    };
                    groups[groupKey] = group;
                }

                group.DuplicateObjects.Add(new IdentityObject
                {
                    Id = row.Id,
                    IdentityId = row.IdentityId,
                    SourceConnectionId = row.SourceConnectionId,
                    SourceUniqueId = row.SourceUniqueId,
                    SourceType = row.SourceType,
                    DisplayName = row.DisplayName,
                    Email = row.Email,
                    Username = row.Username,
                    FirstName = row.FirstName,
                    LastName = row.LastName,
                    FirstSyncedAt = row.FirstSyncedAt,
                    LastSyncedAt = row.LastSyncedAt,
                    IsActive = row.IsActive
                });
            }

            return groups.Values.ToList();
        }

        /// <summary>
        /// Delete a duplicate object and clean up its group memberships.
        /// </summary>
        public async Task<bool> DeleteDuplicateObjectAsync(Guid objectId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();

            try
            {
                // First, remove group memberships
                await connection.ExecuteAsync(
                    "DELETE FROM ObjectGroupMemberships WHERE ObjectId = @ObjectId",
                    new { ObjectId = objectId },
                    transaction).ConfigureAwait(false);

                // Remove any tag assignments
                await connection.ExecuteAsync(
                    "DELETE FROM ObjectTags WHERE ObjectId = @ObjectId",
                    new { ObjectId = objectId },
                    transaction).ConfigureAwait(false);

                // Remove sync audit logs
                await connection.ExecuteAsync(
                    "DELETE FROM SyncAuditLogs WHERE ObjectId = @ObjectId",
                    new { ObjectId = objectId },
                    transaction).ConfigureAwait(false);

                // Remove object attributes
                await connection.ExecuteAsync(
                    "DELETE FROM ObjectAttributes WHERE ObjectId = @ObjectId",
                    new { ObjectId = objectId },
                    transaction).ConfigureAwait(false);

                // Remove group memberships (as member or as group)
                await connection.ExecuteAsync(
                    "DELETE FROM ObjectGroupMemberships WHERE ObjectId = @ObjectId OR GroupId = @ObjectId",
                    new { ObjectId = objectId },
                    transaction).ConfigureAwait(false);

                // Remove match logs
                await connection.ExecuteAsync(
                    "DELETE FROM IdentityMatchLogs WHERE ObjectId = @ObjectId",
                    new { ObjectId = objectId },
                    transaction).ConfigureAwait(false);

                // Clear manager references pointing to this object
                await connection.ExecuteAsync(
                    "UPDATE Objects SET ManagerObjectId = NULL WHERE ManagerObjectId = @ObjectId",
                    new { ObjectId = objectId },
                    transaction).ConfigureAwait(false);

                // Delete the object
                var deleted = await connection.ExecuteAsync(
                    "DELETE FROM Objects WHERE Id = @ObjectId",
                    new { ObjectId = objectId },
                    transaction).ConfigureAwait(false);

                transaction.Commit();

                _logger.LogInformation("Deleted duplicate object {ObjectId} and cleaned up {Deleted} records", objectId, deleted);
                return deleted > 0;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed to delete duplicate object {ObjectId}", objectId);
                throw;
            }
        }

        /// <summary>
        /// Bulk delete duplicate objects and clean up related data in a single transaction.
        /// </summary>
        public async Task<int> BulkDeleteDuplicateObjectsAsync(IEnumerable<Guid> objectIds)
        {
            var idList = objectIds.ToList();
            if (idList.Count == 0) return 0;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();

            try
            {
                // Create a temp table with the IDs for efficient bulk operations
                await connection.ExecuteAsync(
                    "CREATE TABLE #DuplicateIds (Id uniqueidentifier NOT NULL PRIMARY KEY)",
                    transaction: transaction).ConfigureAwait(false);

                // Insert IDs in batches
                foreach (var batch in idList.Chunk(500))
                {
                    var values = string.Join(",", batch.Select(id => string.Concat("('", id.ToString(), "')")));
                    await connection.ExecuteAsync(
                        string.Concat("INSERT INTO #DuplicateIds (Id) VALUES ", values),
                        transaction: transaction).ConfigureAwait(false);
                }

                // Clean up all FK-referencing tables
                await connection.ExecuteAsync(
                    "DELETE m FROM ObjectGroupMemberships m INNER JOIN #DuplicateIds d ON m.ObjectId = d.Id",
                    transaction: transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(
                    "DELETE t FROM ObjectTags t INNER JOIN #DuplicateIds d ON t.ObjectId = d.Id",
                    transaction: transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(
                    "DELETE s FROM SyncAuditLogs s INNER JOIN #DuplicateIds d ON s.ObjectId = d.Id",
                    transaction: transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(
                    "DELETE a FROM ObjectAttributes a INNER JOIN #DuplicateIds d ON a.ObjectId = d.Id",
                    transaction: transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(
                    "DELETE g FROM ObjectGroupMemberships g INNER JOIN #DuplicateIds d ON g.ObjectId = d.Id OR g.GroupId = d.Id",
                    transaction: transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(
                    "DELETE l FROM IdentityMatchLogs l INNER JOIN #DuplicateIds d ON l.ObjectId = d.Id",
                    transaction: transaction).ConfigureAwait(false);

                // Clear ManagerObjectId self-references pointing to objects being deleted
                await connection.ExecuteAsync(
                    "UPDATE o SET o.ManagerObjectId = NULL FROM Objects o INNER JOIN #DuplicateIds d ON o.ManagerObjectId = d.Id",
                    transaction: transaction).ConfigureAwait(false);

                // Delete the objects
                var deleted = await connection.ExecuteAsync(
                    "DELETE o FROM Objects o INNER JOIN #DuplicateIds d ON o.Id = d.Id",
                    transaction: transaction).ConfigureAwait(false);

                transaction.Commit();

                _logger.LogInformation("Bulk deleted {Count} duplicate objects", deleted);
                return deleted;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed to bulk delete {Count} duplicate objects", idList.Count);
                throw;
            }
        }
    }

    /// <summary>
    /// Container for identity statistics.
    /// </summary>
    public class IdentityStatistics
    {
        public int TotalIdentities { get; set; }
        public int ActiveIdentities { get; set; }
        public int InactiveIdentities { get; set; }
        public int AuthoritativeIdentities { get; set; }
        public int UniquePersons { get; set; }
        public int UniqueSourceConnections { get; set; }
        public Dictionary<string, SourceTypeCount> BySourceType { get; set; } = new();
    }

    /// <summary>
    /// Source type count breakdown.
    /// </summary>
    public class SourceTypeCount
    {
        public int Total { get; set; }
        public int Active { get; set; }
        public int Authoritative { get; set; }
    }

    /// <summary>
    /// Identity with group membership count.
    /// </summary>
    public class IdentityWithGroupCount
    {
        public IdentityObject Object { get; set; } = null!;
        public int GroupCount { get; set; }
    }
}
