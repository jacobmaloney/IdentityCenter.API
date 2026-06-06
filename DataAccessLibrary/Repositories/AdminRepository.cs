using Dapper;
using Microsoft.Data.SqlClient;
using DataAccessLibrary.Models;
using ChangeHistory.Models;
using ChangeHistory.Services;
using Logging;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// High-performance Dapper-based repository for Admin page data access.
/// Replaces EF Core DbContext usage for better performance.
/// </summary>
public class AdminRepository : DapperRepositoryBase, IAdminRepository
{
    private readonly IChangeHistoryService _changeHistory;

    public AdminRepository(IConfiguration configuration, IGlobalLogger logger, IChangeHistoryService changeHistory)
        : base(configuration, logger)
    {
        _changeHistory = changeHistory;
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    #region Directory Connections

    public async Task<List<DirectoryConnection>> GetDirectoryConnectionsAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<DirectoryConnection>(
            "SELECT * FROM DirectoryConnections ORDER BY Name").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<DirectoryConnection?> GetDirectoryConnectionAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<DirectoryConnection>(
            "SELECT * FROM DirectoryConnections WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<Guid> CreateDirectoryConnectionAsync(DirectoryConnection connection)
    {
        using var conn = CreateConnection();
        connection.Id = connection.Id == Guid.Empty ? Guid.NewGuid() : connection.Id;
        connection.CreatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO DirectoryConnections (Id, Name, ConnectionType, ConnectionString, Credentials, Configuration,
                IsActive, IsAuthoritative, LastSyncAt, CreatedAt, LastTestAt, LastTestResult)
            VALUES (@Id, @Name, @ConnectionType, @ConnectionString, @Credentials, @Configuration,
                @IsActive, @IsAuthoritative, @LastSyncAt, @CreatedAt, @LastTestAt, @LastTestResult)",
            connection).ConfigureAwait(false);

        return connection.Id;
    }

    public async Task UpdateDirectoryConnectionAsync(DirectoryConnection connection)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE DirectoryConnections
            SET Name = @Name, ConnectionType = @ConnectionType, ConnectionString = @ConnectionString,
                Credentials = @Credentials, Configuration = @Configuration,
                IsActive = @IsActive, IsAuthoritative = @IsAuthoritative,
                LastSyncAt = @LastSyncAt, LastTestAt = @LastTestAt, LastTestResult = @LastTestResult,
                ModifiedAt = @ModifiedAt
            WHERE Id = @Id",
            connection).ConfigureAwait(false);
    }

    public async Task DeleteDirectoryConnectionAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM DirectoryConnections WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    #endregion

    #region Sync Projects

    public async Task<List<SyncProject>> GetSyncProjectsAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<SyncProject>(
            "SELECT * FROM SyncProjects ORDER BY Name").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<SyncProject?> GetSyncProjectAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<SyncProject>(
            "SELECT * FROM SyncProjects WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task UpdateSyncProjectScheduleAsync(Guid id, string? cronSchedule)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE SyncProjects SET CronSchedule = @CronSchedule WHERE Id = @Id",
            new { Id = id, CronSchedule = cronSchedule }).ConfigureAwait(false);
    }

    public async Task<int> GetSyncProjectCountForConnectionAsync(Guid connectionId)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SyncProjects WHERE SourceConnectionId = @ConnectionId",
            new { ConnectionId = connectionId }).ConfigureAwait(false);
    }

    #endregion

    #region Objects

    public async Task<List<IdentityObject>> GetObjectsAsync(string? objectClass = null, int? limit = null, int? offset = null,
        string? scopeWhereClause = null, DynamicParameters? scopeParams = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT * FROM Objects o WHERE 1=1";
        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(objectClass))
        {
            sql += " AND o.ObjectClass = @ObjectClass";
            parameters.Add("ObjectClass", objectClass);
        }

        if (!string.IsNullOrEmpty(scopeWhereClause))
        {
            sql += " " + scopeWhereClause;
            parameters.AddDynamicParams(scopeParams);
        }

        sql += " ORDER BY o.DisplayName, o.CN";
        if (limit.HasValue)
        {
            sql += " OFFSET @PageOffset ROWS FETCH NEXT @PageLimit ROWS ONLY";
            parameters.Add("PageOffset", offset ?? 0);
            parameters.Add("PageLimit", limit.Value);
        }

        var result = await conn.QueryAsync<IdentityObject>(sql, parameters).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<IdentityObject?> GetObjectAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<IdentityObject>(
            "SELECT * FROM Objects WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<int> GetObjectCountAsync(string? objectClass = null,
        string? scopeWhereClause = null, DynamicParameters? scopeParams = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT COUNT(*) FROM Objects o WHERE 1=1";
        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(objectClass))
        {
            sql += " AND o.ObjectClass = @ObjectClass";
            parameters.Add("ObjectClass", objectClass);
        }

        if (!string.IsNullOrEmpty(scopeWhereClause))
        {
            sql += " " + scopeWhereClause;
            parameters.AddDynamicParams(scopeParams);
        }

        return await conn.ExecuteScalarAsync<int>(sql, parameters).ConfigureAwait(false);
    }

    public async Task UpdateObjectAsync(IdentityObject obj)
    {
        obj.ModifiedAt = DateTime.UtcNow;
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Objects
            SET IdentityId = @IdentityId,
                SourceConnectionId = @SourceConnectionId,
                SourceUniqueId = @SourceUniqueId,
                SourceType = @SourceType,
                ObjectClass = @ObjectClass,
                DisplayName = @DisplayName,
                Email = @Email,
                Username = @Username,
                FirstName = @FirstName,
                LastName = @LastName,
                Department = @Department,
                JobTitle = @JobTitle,
                MiddleName = @MiddleName,
                Phone = @Phone,
                MobilePhone = @MobilePhone,
                HomePhone = @HomePhone,
                Fax = @Fax,
                StreetAddress = @StreetAddress,
                City = @City,
                State = @State,
                PostalCode = @PostalCode,
                Country = @Country,
                Company = @Company,
                Division = @Division,
                Office = @Office,
                EmployeeId = @EmployeeId,
                EmployeeType = @EmployeeType,
                CostCenter = @CostCenter,
                UserPrincipalName = @UserPrincipalName,
                Description = @Description,
                DN = @DN,
                CN = @CN,
                ManagerSourceId = @ManagerSourceId,
                ManagerObjectId = @ManagerObjectId,
                ManagerId = @ManagerId,
                OwnerObjectId = @OwnerObjectId,
                OwnerIdentityId = @OwnerIdentityId,
                IsActive = @IsActive,
                IsAuthoritative = @IsAuthoritative,
                MatchConfidence = @MatchConfidence,
                MatchMethod = @MatchMethod,
                ModifiedAt = @ModifiedAt,
                LastSyncedAt = @LastSyncedAt,
                LastSeenAt = @LastSeenAt,
                DeletedAt = @DeletedAt,
                PasswordLastSet = @PasswordLastSet,
                IsBuiltIn = @IsBuiltIn,
                IsAdminSDHolder = @IsAdminSDHolder,
                PasswordNeverExpires = @PasswordNeverExpires,
                UserAccountControl = @UserAccountControl
            WHERE Id = @Id", obj).ConfigureAwait(false);

        await _changeHistory.RecordAsync(new ChangeRecord
        {
            OperationType = ChangeOperationType.Update,
            EntityType = "Object",
            EntityId = obj.Id,
            EntityDisplayName = obj.DisplayName ?? obj.CN,
            Source = "Admin"
        });
    }

    public async Task<List<IdentityObject>> SearchObjectsAsync(string searchQuery, int limit = 20,
        string? scopeWhereClause = null, DynamicParameters? scopeParams = null)
    {
        using var conn = CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("Query", $"%{searchQuery}%");
        parameters.Add("Limit", limit);

        var sql = @"SELECT TOP (@Limit) *
                    FROM Objects o
                    WHERE (o.DisplayName LIKE @Query OR o.CN LIKE @Query OR o.Email LIKE @Query)";

        if (!string.IsNullOrEmpty(scopeWhereClause))
        {
            sql += " " + scopeWhereClause;
            parameters.AddDynamicParams(scopeParams);
        }

        sql += " ORDER BY o.DisplayName";

        return (await conn.QueryAsync<IdentityObject>(sql, parameters).ConfigureAwait(false)).ToList();
    }

    public async Task<bool> ObjectExistsAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Objects WHERE Id = @Id", new { Id = id }).ConfigureAwait(false) > 0;
    }

    public async Task<IdentityObject?> FindLinkedUserObjectAsync(Guid identityId)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<IdentityObject>(@"
            SELECT TOP 1 * FROM Objects
            WHERE IdentityId = @IdentityId AND ObjectClass = 'user'
            ORDER BY CASE WHEN IsActive = 1 THEN 0 ELSE 1 END, FirstSyncedAt DESC",
            new { IdentityId = identityId }).ConfigureAwait(false);
    }

    public async Task<List<IdentityObject>> GetObjectsByIdentityIdAsync(Guid identityId)
    {
        using var conn = CreateConnection();
        var results = await conn.QueryAsync<IdentityObject>(
            "SELECT * FROM Objects WHERE IdentityId = @IdentityId ORDER BY IsActive DESC, ObjectClass, DisplayName",
            new { IdentityId = identityId }).ConfigureAwait(false);
        return results.ToList();
    }

    #endregion

    #region Object Attributes

    public async Task<List<ObjectAttribute>> GetObjectAttributesAsync(Guid objectId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<ObjectAttribute>(
            "SELECT * FROM ObjectAttributes WHERE ObjectId = @ObjectId ORDER BY AttributeName",
            new { ObjectId = objectId }).ConfigureAwait(false);
        return result.ToList();
    }

    /// <summary>
    /// Gets attributes from all Objects linked to a Person/Identity.
    /// Person (Identities table) -> Objects (via IdentityId) -> ObjectAttributes
    /// </summary>
    public async Task<List<ObjectAttribute>> GetPersonAttributesAsync(Guid personId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<ObjectAttribute>(@"
            SELECT oa.*
            FROM ObjectAttributes oa
            INNER JOIN Objects o ON oa.ObjectId = o.Id
            WHERE o.IdentityId = @PersonId
            ORDER BY oa.AttributeName",
            new { PersonId = personId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task UpsertObjectAttributeAsync(Guid objectId, string attributeName, string? attributeValue)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            MERGE ObjectAttributes AS target
            USING (SELECT @ObjectId AS ObjectId, @AttributeName AS AttributeName) AS source
            ON target.ObjectId = source.ObjectId AND target.AttributeName = source.AttributeName
            WHEN MATCHED THEN UPDATE SET AttributeValue = @AttributeValue, LastSyncedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN INSERT (Id, ObjectId, AttributeName, AttributeValue, LastSyncedAt)
                VALUES (NEWID(), @ObjectId, @AttributeName, @AttributeValue, GETUTCDATE());",
            new { ObjectId = objectId, AttributeName = attributeName, AttributeValue = attributeValue }).ConfigureAwait(false);
    }

    public async Task DeleteObjectAttributeAsync(Guid objectId, string attributeName)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM ObjectAttributes WHERE ObjectId = @ObjectId AND AttributeName = @AttributeName",
            new { ObjectId = objectId, AttributeName = attributeName }).ConfigureAwait(false);
    }

    public async Task DeleteAllObjectAttributesAsync(Guid objectId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM ObjectAttributes WHERE ObjectId = @ObjectId", new { ObjectId = objectId }).ConfigureAwait(false);
    }

    public async Task<DateTime?> GetLatestTimestampAttributeAsync(List<Guid> objectIds, string[] attributeNames)
    {
        if (objectIds == null || objectIds.Count == 0 || attributeNames == null || attributeNames.Length == 0)
            return null;

        using var conn = CreateConnection();
        var values = await conn.QueryAsync<string>(
            @"SELECT AttributeValue FROM ObjectAttributes
              WHERE ObjectId IN @ObjectIds AND AttributeName IN @AttributeNames
              AND AttributeValue IS NOT NULL AND AttributeValue != ''",
            new { ObjectIds = objectIds, AttributeNames = attributeNames }).ConfigureAwait(false);

        DateTime? latest = null;
        foreach (var value in values)
        {
            if (long.TryParse(value, out long fileTime) && fileTime > 0 && fileTime < long.MaxValue)
            {
                try
                {
                    var dt = DateTime.FromFileTimeUtc(fileTime);
                    if (latest == null || dt > latest)
                        latest = dt;
                }
                catch { /* skip invalid FILETIME values */ }
            }
        }
        return latest;
    }

    #endregion

    #region Identities

    public async Task<List<Identity>> GetIdentitiesAsync(string? objectClass = null, int? limit = null, int? offset = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT * FROM Identities WHERE 1=1";
        sql += " ORDER BY DisplayName, LastName, FirstName";
        DynamicParameters? parameters = null;
        if (limit.HasValue)
        {
            sql += " OFFSET @PageOffset ROWS FETCH NEXT @PageLimit ROWS ONLY";
            parameters = new DynamicParameters();
            parameters.Add("PageOffset", offset ?? 0);
            parameters.Add("PageLimit", limit.Value);
        }

        var result = await conn.QueryAsync<Identity>(sql, parameters).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<Identity?> GetIdentityAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Identity>(
            "SELECT * FROM Identities WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<int> GetIdentityCountAsync(string? objectClass = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT COUNT(*) FROM Identities";
        return await conn.ExecuteScalarAsync<int>(sql).ConfigureAwait(false);
    }

    public async Task<List<Identity>> SearchIdentitiesAsync(string searchTerm, int maxResults = 15, Guid? excludeId = null)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT TOP (@MaxResults) *
            FROM Identities
            WHERE IsActive = 1
              AND (@ExcludeId IS NULL OR Id != @ExcludeId)
              AND (DisplayName LIKE @Search
                   OR FirstName LIKE @Search
                   OR LastName LIKE @Search
                   OR PrimaryEmail LIKE @Search
                   OR Username LIKE @Search)
            ORDER BY DisplayName";

        var result = await conn.QueryAsync<Identity>(sql, new
        {
            MaxResults = maxResults,
            ExcludeId = excludeId,
            Search = $"%{searchTerm}%"
        }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<string>> GetDistinctIdentityFieldValuesAsync(string fieldName)
    {
        // Only allow known safe column names to prevent SQL injection
        var allowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Department", "Division", "Company", "Office", "Building",
            "IdentityType", "ContractType", "JobTitle", "Status",
            "CostCenter", "ProfitCenter", "Country", "City", "State"
        };
        if (!allowedFields.Contains(fieldName))
            throw new ArgumentException($"Field '{fieldName}' is not allowed for distinct value lookup.");

        using var conn = CreateConnection();
        var sql = $"SELECT DISTINCT [{fieldName}] FROM Identities WHERE [{fieldName}] IS NOT NULL AND [{fieldName}] != '' ORDER BY [{fieldName}]";
        var result = await conn.QueryAsync<string>(sql).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<Identity>> GetDirectReportIdentitiesAsync(Guid managerIdentityId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<Identity>(
            "SELECT * FROM Identities WHERE ManagerIdentityId = @ManagerIdentityId AND IsActive = 1 ORDER BY DisplayName",
            new { ManagerIdentityId = managerIdentityId }).ConfigureAwait(false);
        return result.ToList();
    }

    #region Field Lookup Values

    public async Task<List<FieldLookupValue>> GetFieldLookupValuesAsync(string fieldName)
    {
        using var conn = CreateConnection();
        const string sql = @"SELECT Id, FieldName, Value, SortOrder, IsActive, CreatedAt, ModifiedAt
                             FROM FieldLookupValues
                             WHERE FieldName = @FieldName
                             ORDER BY SortOrder, Value";
        var result = await conn.QueryAsync<FieldLookupValue>(sql, new { FieldName = fieldName }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<string>> GetFieldLookupFieldNamesAsync()
    {
        using var conn = CreateConnection();
        const string sql = "SELECT DISTINCT FieldName FROM FieldLookupValues ORDER BY FieldName";
        var result = await conn.QueryAsync<string>(sql).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<FieldLookupValue> CreateFieldLookupValueAsync(string fieldName, string value, int sortOrder = 0)
    {
        var item = new FieldLookupValue
        {
            Id = Guid.NewGuid(),
            FieldName = fieldName,
            Value = value,
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        using var conn = CreateConnection();
        const string sql = @"INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt)
                             VALUES (@Id, @FieldName, @Value, @SortOrder, @IsActive, @CreatedAt)";
        await conn.ExecuteAsync(sql, item).ConfigureAwait(false);

        await _changeHistory.RecordAsync(new ChangeRecord
        {
            OperationType = ChangeOperationType.Create,
            EntityType = "FieldLookupValue",
            EntityId = item.Id,
            EntityDisplayName = item.Value,
            PropertyName = item.FieldName,
            NewValue = item.Value,
            Source = "Admin"
        });

        return item;
    }

    public async Task UpdateFieldLookupValueAsync(FieldLookupValue item)
    {
        // Fetch old value for audit trail
        FieldLookupValue? oldItem = null;
        using (var readConn = CreateConnection())
        {
            oldItem = await readConn.QueryFirstOrDefaultAsync<FieldLookupValue>(
                "SELECT * FROM FieldLookupValues WHERE Id = @Id", new { item.Id }).ConfigureAwait(false);
        }

        item.ModifiedAt = DateTime.UtcNow;
        using var conn = CreateConnection();
        const string sql = @"UPDATE FieldLookupValues
                             SET Value = @Value, SortOrder = @SortOrder, IsActive = @IsActive, ModifiedAt = @ModifiedAt
                             WHERE Id = @Id";
        await conn.ExecuteAsync(sql, item).ConfigureAwait(false);

        await _changeHistory.RecordAsync(new ChangeRecord
        {
            OperationType = ChangeOperationType.Update,
            EntityType = "FieldLookupValue",
            EntityId = item.Id,
            EntityDisplayName = item.Value,
            PropertyName = item.FieldName,
            OldValue = oldItem?.Value,
            NewValue = item.Value,
            Source = "Admin"
        });
    }

    public async Task DeleteFieldLookupValueAsync(Guid id)
    {
        // Fetch for audit trail before deleting
        FieldLookupValue? oldItem = null;
        using (var readConn = CreateConnection())
        {
            oldItem = await readConn.QueryFirstOrDefaultAsync<FieldLookupValue>(
                "SELECT * FROM FieldLookupValues WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
        }

        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM FieldLookupValues WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);

        await _changeHistory.RecordAsync(new ChangeRecord
        {
            OperationType = ChangeOperationType.Delete,
            EntityType = "FieldLookupValue",
            EntityId = id,
            EntityDisplayName = oldItem?.Value,
            PropertyName = oldItem?.FieldName,
            OldValue = oldItem?.Value,
            Source = "Admin"
        });
    }

    #endregion

    public async Task UpdateIdentityAsync(Identity identity)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Identities
            SET CentralId = @CentralId,
                DisplayName = @DisplayName,
                FirstName = @FirstName,
                LastName = @LastName,
                MiddleName = @MiddleName,
                Suffix = @Suffix,
                Salutation = @Salutation,
                PreferredName = @PreferredName,
                DateOfBirth = @DateOfBirth,
                Gender = @Gender,
                NationalId = @NationalId,
                PhotoUrl = @PhotoUrl,
                PrimaryEmail = @PrimaryEmail,
                SecondaryEmail = @SecondaryEmail,
                PrimaryPhone = @PrimaryPhone,
                MobilePhone = @MobilePhone,
                HomePhone = @HomePhone,
                Fax = @Fax,
                StreetAddress = @StreetAddress,
                City = @City,
                State = @State,
                PostalCode = @PostalCode,
                Country = @Country,
                EmployeeId = @EmployeeId,
                JobTitle = @JobTitle,
                Department = @Department,
                Division = @Division,
                Company = @Company,
                Office = @Office,
                Building = @Building,
                Floor = @Floor,
                Room = @Room,
                CostCenter = @CostCenter,
                ProfitCenter = @ProfitCenter,
                IdentityType = @IdentityType,
                ContractType = @ContractType,
                HireDate = @HireDate,
                TerminationDate = @TerminationDate,
                LastWorkDay = @LastWorkDay,
                Description = @Description,
                ManagerIdentityId = @ManagerIdentityId,
                ManagerEmployeeId = @ManagerEmployeeId,
                Username = @Username,
                UserPrincipalName = @UserPrincipalName,
                Status = @Status,
                IsActive = @IsActive,
                SecurityClearance = @SecurityClearance,
                RiskScore = @RiskScore,
                RiskLevel = @RiskLevel,
                AuthoritativeSourceId = @AuthoritativeSourceId,
                PreferredLanguage = @PreferredLanguage,
                TimeZone = @TimeZone,
                Locale = @Locale,
                LastSeenAt = @LastSeenAt,
                LastLoginAt = @LastLoginAt,
                PasswordLastChangedAt = @PasswordLastChangedAt,
                LastAccessReviewAt = @LastAccessReviewAt,
                ModifiedBy = @ModifiedBy,
                CustomAttributes = @CustomAttributes,
                ModifiedAt = GETUTCDATE()
            WHERE Id = @Id", identity).ConfigureAwait(false);
    }

    public async Task DeletePersonWithObjectsAsync(Guid personId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        try
        {
            // 1. Find all linked objects
            var linkedObjectIds = (await conn.QueryAsync<Guid>(
                "SELECT Id FROM Objects WHERE IdentityId = @PersonId",
                new { PersonId = personId }, tx).ConfigureAwait(false)).ToList();

            // 2. Delete linked objects and their children
            if (linkedObjectIds.Any())
            {
                await conn.ExecuteAsync(
                    "DELETE FROM ObjectAttributes WHERE ObjectId IN @Ids",
                    new { Ids = linkedObjectIds }, tx).ConfigureAwait(false);
                await conn.ExecuteAsync(
                    "DELETE FROM ObjectGroupMemberships WHERE GroupId IN @Ids OR ObjectId IN @Ids",
                    new { Ids = linkedObjectIds }, tx).ConfigureAwait(false);
                await conn.ExecuteAsync(
                    "DELETE FROM ObjectTags WHERE ObjectId IN @Ids",
                    new { Ids = linkedObjectIds }, tx).ConfigureAwait(false);
                await conn.ExecuteAsync(
                    "DELETE FROM Objects WHERE Id IN @Ids",
                    new { Ids = linkedObjectIds }, tx).ConfigureAwait(false);
            }

            // 3. Clean up identity references (NO CASCADE FKs)
            await conn.ExecuteAsync(
                "DELETE FROM IdentityMatchLogs WHERE IdentityId = @Id",
                new { Id = personId }, tx).ConfigureAwait(false);
            await conn.ExecuteAsync(
                "DELETE FROM BusinessRoleMembers WHERE IdentityId = @Id",
                new { Id = personId }, tx).ConfigureAwait(false);
            await conn.ExecuteAsync(
                "DELETE FROM OrganizationalFolderMembers WHERE IdentityId = @Id",
                new { Id = personId }, tx).ConfigureAwait(false);
            await conn.ExecuteAsync(
                "DELETE FROM CompliancePolicyViolations WHERE EntityId = @Id",
                new { Id = personId }, tx).ConfigureAwait(false);
            // IdentityTags and IdentityGroupMemberships have ON DELETE CASCADE

            // 4. Null out manager references pointing to this person
            await conn.ExecuteAsync(
                "UPDATE Identities SET ManagerIdentityId = NULL WHERE ManagerIdentityId = @Id",
                new { Id = personId }, tx).ConfigureAwait(false);

            // 5. Delete the identity itself
            await conn.ExecuteAsync(
                "DELETE FROM Identities WHERE Id = @Id",
                new { Id = personId }, tx).ConfigureAwait(false);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<int> DeprovisionIdentityAsync(Guid identityId, string? reason = null)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);
        using var tx = conn.BeginTransaction();
        try
        {
            // Stamp DeletedAt (the retention clock) + mark Deprovisioned (ARS 3-state
            // value 2). Guarded so an already-deprovisioned identity is NOT re-stamped
            // (its clock keeps running from the original deprovision moment). A person
            // who is Active(0) OR Disabled(1)/suspended can BOTH become a leaver, so the
            // guard is "not already Deprovisioned" (state <> 2) rather than "state = 0":
            // a suspended worker who is later terminated must transition 1 -> 2.
            var affected = await conn.ExecuteAsync(
                @"UPDATE Identities
                     SET LifecycleState = 2,
                         DeletedAt = SYSUTCDATETIME(),
                         IsActive = 0,
                         ModifiedAt = SYSUTCDATETIME()
                   WHERE Id = @Id
                     AND LifecycleState <> 2",
                new { Id = identityId }, tx).ConfigureAwait(false);

            if (affected > 0)
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO ChangeAuditLogs (Timestamp, UserId, OperationType, EntityType, EntityId, Reason, Success)
                      VALUES (SYSUTCDATETIME(), 'System', 1, 'Identity', @Id, @Reason, 1)",
                    new { Id = identityId, Reason = reason ?? "Lifecycle deprovision (deferred deletion)" }, tx)
                    .ConfigureAwait(false);
            }

            tx.Commit();
            return affected;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<int> ReviveIdentityAsync(Guid identityId, string? reason = null)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);
        using var tx = conn.BeginTransaction();
        try
        {
            // Restore to Active "like nothing happened" -- only if still Deprovisioned
            // (ARS 3-state value 2; and, by implication, not yet purged: a purged row
            // no longer exists). Revive clears the retention clock and returns to
            // Active(0). A Disabled(1) row is NOT a revive target -- it was never
            // deprovisioned and has no clock to clear.
            var affected = await conn.ExecuteAsync(
                @"UPDATE Identities
                     SET LifecycleState = 0,
                         DeletedAt = NULL,
                         IsActive = 1,
                         ModifiedAt = SYSUTCDATETIME()
                   WHERE Id = @Id
                     AND LifecycleState = 2",
                new { Id = identityId }, tx).ConfigureAwait(false);

            if (affected > 0)
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO ChangeAuditLogs (Timestamp, UserId, OperationType, EntityType, EntityId, Reason, Success)
                      VALUES (SYSUTCDATETIME(), 'System', 1, 'Identity', @Id, @Reason, 1)",
                    new { Id = identityId, Reason = reason ?? "Lifecycle revive (within retention window)" }, tx)
                    .ConfigureAwait(false);
            }

            tx.Commit();
            return affected;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<int> SetIdentityDisabledAsync(Guid identityId, bool disabled, string? reason = null)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);
        using var tx = conn.BeginTransaction();
        try
        {
            // ARS 3-state Active(0) <-> Disabled(1). Disabled is the suspended-but-
            // present state: it is RETAINED INDEFINITELY and NEVER on the purge clock,
            // so this NEVER touches DeletedAt. The state guard is the safety invariant:
            //   * disable: only 0 -> 1 (a Deprovisioned(2) leaver is NOT re-classified
            //     as merely disabled -- its retention clock must keep running);
            //   * enable:  only 1 -> 0 (a Deprovisioned(2) row is revived via
            //     ReviveIdentityAsync, not here).
            // IsActive is kept in lockstep with the state.
            var affected = disabled
                ? await conn.ExecuteAsync(
                    @"UPDATE Identities
                         SET LifecycleState = 1,
                             IsActive = 0,
                             ModifiedAt = SYSUTCDATETIME()
                       WHERE Id = @Id
                         AND LifecycleState = 0",
                    new { Id = identityId }, tx).ConfigureAwait(false)
                : await conn.ExecuteAsync(
                    @"UPDATE Identities
                         SET LifecycleState = 0,
                             IsActive = 1,
                             ModifiedAt = SYSUTCDATETIME()
                       WHERE Id = @Id
                         AND LifecycleState = 1",
                    new { Id = identityId }, tx).ConfigureAwait(false);

            if (affected > 0)
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO ChangeAuditLogs (Timestamp, UserId, OperationType, EntityType, EntityId, Reason, Success)
                      VALUES (SYSUTCDATETIME(), 'System', 1, 'Identity', @Id, @Reason, 1)",
                    new
                    {
                        Id = identityId,
                        Reason = reason ?? (disabled
                            ? "Lifecycle disable (suspended -- retained indefinitely, not on purge clock)"
                            : "Lifecycle enable (suspension lifted -- back to Active)")
                    }, tx).ConfigureAwait(false);
            }

            tx.Commit();
            return affected;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteObjectWithCleanupAsync(Guid objectId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        try
        {
            // Fetch object info before deletion for audit trail
            var obj = await conn.QueryFirstOrDefaultAsync<IdentityObject>(
                "SELECT Id, DisplayName, CN, ObjectClass FROM Objects WHERE Id = @Id",
                new { Id = objectId }, tx).ConfigureAwait(false);

            await conn.ExecuteAsync(
                "DELETE FROM ObjectAttributes WHERE ObjectId = @Id",
                new { Id = objectId }, tx).ConfigureAwait(false);
            await conn.ExecuteAsync(
                "DELETE FROM ObjectGroupMemberships WHERE GroupId = @Id OR ObjectId = @Id",
                new { Id = objectId }, tx).ConfigureAwait(false);
            await conn.ExecuteAsync(
                "DELETE FROM ObjectTags WHERE ObjectId = @Id",
                new { Id = objectId }, tx).ConfigureAwait(false);
            await conn.ExecuteAsync(
                "DELETE FROM IdentityMatchLogs WHERE ObjectId = @Id",
                new { Id = objectId }, tx).ConfigureAwait(false);
            await conn.ExecuteAsync(
                "DELETE FROM Objects WHERE Id = @Id",
                new { Id = objectId }, tx).ConfigureAwait(false);

            tx.Commit();

            if (obj != null)
            {
                await _changeHistory.RecordAsync(new ChangeRecord
                {
                    OperationType = ChangeOperationType.Delete,
                    EntityType = "Object",
                    EntityId = objectId,
                    EntityDisplayName = obj.DisplayName ?? obj.CN,
                    Source = "Admin"
                });
            }
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    #endregion

    #region Group Memberships

    public async Task<List<ObjectGroupMembership>> GetObjectGroupMembershipsAsync(Guid? groupId = null, Guid? memberId = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT * FROM ObjectGroupMemberships WHERE IsActive = 1";
        if (groupId.HasValue) sql += " AND GroupId = @GroupId";
        if (memberId.HasValue) sql += " AND ObjectId = @MemberId";

        var result = await conn.QueryAsync<ObjectGroupMembership>(sql, new { GroupId = groupId, MemberId = memberId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<HashSet<Guid>> GetGroupIdsWithActiveMembersAsync()
    {
        using var conn = CreateConnection();
        var ids = await conn.QueryAsync<Guid>(
            "SELECT DISTINCT GroupId FROM ObjectGroupMemberships WHERE IsActive = 1").ConfigureAwait(false);
        return ids.ToHashSet();
    }

    public async Task<List<ObjectGroupMembership>> GetObjectGroupMembershipsWithGroupAsync(Guid objectId)
    {
        using var conn = CreateConnection();
        // Join to Objects table (unified model: groups are stored with ObjectClass='group')
        // FK: ObjectGroupMemberships.GroupId -> Objects.Id
        var sql = @"
            SELECT m.*,
                   1 as GroupSplit,
                   o.Id,
                   COALESCE(o.CN, o.DisplayName, cnAttr.AttributeValue, nameAttr.AttributeValue, 'Unknown') as Name,
                   o.DN as DistinguishedName,
                   o.SourceUniqueId,
                   'ActiveDirectory' as SourceType
            FROM ObjectGroupMemberships m
            INNER JOIN Objects o ON m.GroupId = o.Id
            LEFT JOIN ObjectAttributes cnAttr ON cnAttr.ObjectId = o.Id AND cnAttr.AttributeName = 'cn'
            LEFT JOIN ObjectAttributes nameAttr ON nameAttr.ObjectId = o.Id AND nameAttr.AttributeName = 'name'
            WHERE m.ObjectId = @ObjectId AND m.RemovedAt IS NULL";

        var result = await conn.QueryAsync<ObjectGroupMembership, Group, ObjectGroupMembership>(
            sql,
            (membership, group) =>
            {
                membership.Group = group;
                return membership;
            },
            new { ObjectId = objectId },
            splitOn: "GroupSplit").ConfigureAwait(false);

        return result.ToList();
    }

    public async Task<List<IdentityGroupMembership>> GetIdentityGroupMembershipsAsync(Guid? groupId = null, Guid? memberId = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT * FROM IdentityGroupMemberships WHERE 1=1";
        if (groupId.HasValue) sql += " AND GroupId = @GroupId";
        if (memberId.HasValue) sql += " AND IdentityId = @MemberId";

        var result = await conn.QueryAsync<IdentityGroupMembership>(sql, new { GroupId = groupId, MemberId = memberId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task AddObjectGroupMembershipAsync(Guid groupId, Guid memberId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            IF NOT EXISTS (SELECT 1 FROM ObjectGroupMemberships WHERE GroupId = @GroupId AND ObjectId = @MemberId AND RemovedAt IS NULL)
            INSERT INTO ObjectGroupMemberships (Id, GroupId, ObjectId, IsDirect, IsPrimary, IsActive, AddedAt, LastSyncedAt)
            VALUES (NEWID(), @GroupId, @MemberId, 1, 0, 1, GETUTCDATE(), GETUTCDATE())",
            new { GroupId = groupId, MemberId = memberId }).ConfigureAwait(false);
    }

    public async Task RemoveObjectGroupMembershipAsync(Guid groupId, Guid memberId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE ObjectGroupMemberships SET RemovedAt = GETUTCDATE() WHERE GroupId = @GroupId AND ObjectId = @MemberId AND RemovedAt IS NULL",
            new { GroupId = groupId, MemberId = memberId }).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all members of a group with their Object details
    /// </summary>
    public async Task<List<IdentityObject>> GetGroupMembersAsync(Guid groupId)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT o.*
            FROM ObjectGroupMemberships m WITH (NOLOCK)
            INNER JOIN Objects o WITH (NOLOCK) ON m.ObjectId = o.Id
            WHERE m.GroupId = @GroupId
              AND m.RemovedAt IS NULL
            ORDER BY COALESCE(o.CN, o.DisplayName, o.Username)";

        var result = await conn.QueryAsync<IdentityObject>(sql, new { GroupId = groupId }).ConfigureAwait(false);
        return result.ToList();
    }

    /// <summary>
    /// Gets all groups that an object is a member of with their Group details
    /// Groups are stored in Objects table with ObjectClass='group' (unified model)
    /// FK: ObjectGroupMemberships.GroupId -> Objects.Id
    /// </summary>
    public async Task<List<Group>> GetMemberOfGroupsAsync(Guid objectId)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT o.Id,
                   o.SourceConnectionId,
                   o.SourceUniqueId,
                   'ActiveDirectory' as SourceType,
                   COALESCE(o.CN, o.DisplayName, cnAttr.AttributeValue, nameAttr.AttributeValue, 'Unknown') as Name,
                   o.DN as DistinguishedName
            FROM ObjectGroupMemberships m WITH (NOLOCK)
            INNER JOIN Objects o WITH (NOLOCK) ON m.GroupId = o.Id
            LEFT JOIN ObjectAttributes cnAttr WITH (NOLOCK) ON cnAttr.ObjectId = o.Id AND cnAttr.AttributeName = 'cn'
            LEFT JOIN ObjectAttributes nameAttr WITH (NOLOCK) ON nameAttr.ObjectId = o.Id AND nameAttr.AttributeName = 'name'
            WHERE m.ObjectId = @ObjectId
              AND m.RemovedAt IS NULL
            ORDER BY COALESCE(o.CN, o.DisplayName, cnAttr.AttributeValue, nameAttr.AttributeValue)";

        var result = await conn.QueryAsync<Group>(sql, new { ObjectId = objectId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<Group>> SearchGroupsAsync(string searchTerm, int limit = 20)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT TOP (@Limit) o.Id,
                   o.SourceConnectionId, o.SourceUniqueId,
                   'ActiveDirectory' as SourceType,
                   COALESCE(o.CN, o.DisplayName, 'Unknown') as Name,
                   o.DN as DistinguishedName
            FROM Objects o WITH (NOLOCK)
            WHERE o.ObjectClass = 'group'
              AND (o.CN LIKE @Query OR o.DisplayName LIKE @Query OR o.Email LIKE @Query)
            ORDER BY COALESCE(o.CN, o.DisplayName)";
        var result = await conn.QueryAsync<Group>(sql, new { Query = $"%{searchTerm}%", Limit = limit }).ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Tags

    public async Task<List<Tag>> GetTagsAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<Tag>("SELECT * FROM Tags ORDER BY Category, Name").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<Tag?> GetTagAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Tag>("SELECT * FROM Tags WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<Tag?> GetTagByNameAsync(string name, Guid? excludeId = null)
    {
        using var conn = CreateConnection();
        var sql = excludeId.HasValue
            ? "SELECT * FROM Tags WHERE Name = @Name AND Id != @ExcludeId"
            : "SELECT * FROM Tags WHERE Name = @Name";
        return await conn.QueryFirstOrDefaultAsync<Tag>(sql, new { Name = name, ExcludeId = excludeId }).ConfigureAwait(false);
    }

    public async Task<Guid> CreateTagAsync(Tag tag)
    {
        using var conn = CreateConnection();
        tag.Id = tag.Id == Guid.Empty ? Guid.NewGuid() : tag.Id;
        tag.CreatedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(@"
            INSERT INTO Tags (Id, Name, Description, Color, Icon, Category, IsSystem, CreatedAt, CreatedBy)
            VALUES (@Id, @Name, @Description, @Color, @Icon, @Category, @IsSystem, @CreatedAt, @CreatedBy)", tag).ConfigureAwait(false);
        return tag.Id;
    }

    public async Task UpdateTagAsync(Tag tag)
    {
        using var conn = CreateConnection();
        tag.ModifiedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(@"
            UPDATE Tags SET Name = @Name, Description = @Description, Color = @Color, Icon = @Icon,
                Category = @Category, IsSystem = @IsSystem, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
            WHERE Id = @Id", tag).ConfigureAwait(false);
    }

    public async Task DeleteTagAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Tags WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<(int ObjectCount, int IdentityCount)> GetTagUsageCountsAsync(Guid tagId)
    {
        using var conn = CreateConnection();
        var objectCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ObjectTags WHERE TagId = @TagId", new { TagId = tagId }).ConfigureAwait(false);
        var identityCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM IdentityTags WHERE TagId = @TagId", new { TagId = tagId }).ConfigureAwait(false);
        return (objectCount, identityCount);
    }

    public async Task<List<Tag>> GetAllTagsAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<Tag>("SELECT * FROM Tags ORDER BY Category, Name").ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Object Tags

    public async Task<List<ObjectTag>> GetObjectTagsAsync(Guid objectId)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT ot.*, t.*
            FROM ObjectTags ot
            INNER JOIN Tags t ON ot.TagId = t.Id
            WHERE ot.ObjectId = @ObjectId
            ORDER BY t.Category, t.Name";

        var result = await conn.QueryAsync<ObjectTag, Tag, ObjectTag>(
            sql,
            (objectTag, tag) =>
            {
                objectTag.Tag = tag;
                return objectTag;
            },
            new { ObjectId = objectId },
            splitOn: "Id"
        ).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task AddTagToObjectAsync(Guid objectId, Guid tagId, string? createdBy = null)
    {
        using var conn = CreateConnection();
        // Check if already exists
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ObjectTags WHERE ObjectId = @ObjectId AND TagId = @TagId",
            new { ObjectId = objectId, TagId = tagId }).ConfigureAwait(false);

        if (exists == 0)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO ObjectTags (Id, ObjectId, TagId, IsInherited, CreatedAt, CreatedBy)
                VALUES (@Id, @ObjectId, @TagId, 0, @CreatedAt, @CreatedBy)",
                new
                {
                    Id = Guid.NewGuid(),
                    ObjectId = objectId,
                    TagId = tagId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy ?? "System"
                }).ConfigureAwait(false);
        }
    }

    public async Task RemoveTagFromObjectAsync(Guid objectTagId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM ObjectTags WHERE Id = @Id", new { Id = objectTagId }).ConfigureAwait(false);
    }

    public async Task RemoveTagFromObjectByIdsAsync(Guid objectId, Guid tagId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM ObjectTags WHERE ObjectId = @ObjectId AND TagId = @TagId",
            new { ObjectId = objectId, TagId = tagId }).ConfigureAwait(false);
    }

    #endregion

    #region Identity Tags

    public async Task<HashSet<Guid>> GetIdentityIdsByTagAsync(Guid tagId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<Guid>(
            "SELECT IdentityId FROM IdentityTags WHERE TagId = @TagId",
            new { TagId = tagId }).ConfigureAwait(false);
        return result.ToHashSet();
    }

    public async Task<List<IdentityTag>> GetIdentityTagsAsync(Guid identityId)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT it.*, t.*
            FROM IdentityTags it
            INNER JOIN Tags t ON it.TagId = t.Id
            WHERE it.IdentityId = @IdentityId
            ORDER BY t.Category, t.Name";

        var result = await conn.QueryAsync<IdentityTag, Tag, IdentityTag>(
            sql,
            (identityTag, tag) =>
            {
                identityTag.Tag = tag;
                return identityTag;
            },
            new { IdentityId = identityId },
            splitOn: "Id"
        ).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task AddTagToIdentityAsync(Guid identityId, Guid tagId, string? createdBy = null)
    {
        using var conn = CreateConnection();
        // Check if already exists
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM IdentityTags WHERE IdentityId = @IdentityId AND TagId = @TagId",
            new { IdentityId = identityId, TagId = tagId }).ConfigureAwait(false);

        if (exists == 0)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO IdentityTags (Id, IdentityId, TagId, IsInherited, CreatedAt, CreatedBy)
                VALUES (@Id, @IdentityId, @TagId, 0, @CreatedAt, @CreatedBy)",
                new
                {
                    Id = Guid.NewGuid(),
                    IdentityId = identityId,
                    TagId = tagId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy ?? "System"
                }).ConfigureAwait(false);
        }
    }

    public async Task RemoveTagFromIdentityAsync(Guid identityTagId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM IdentityTags WHERE Id = @Id", new { Id = identityTagId }).ConfigureAwait(false);
    }

    #endregion

    #region Identity Providers

    public async Task<List<IdentityProvider>> GetIdentityProvidersAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<IdentityProvider>("SELECT * FROM IdentityProviders ORDER BY Name").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<IdentityProvider>> GetEnabledIdentityProvidersAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<IdentityProvider>(
            "SELECT * FROM IdentityProviders WHERE IsEnabled = 1 ORDER BY IsPrimary DESC, Name").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<IdentityProvider?> GetIdentityProviderByNameAsync(string name)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<IdentityProvider>(
            "SELECT TOP 1 * FROM IdentityProviders WHERE Name = @Name", new { Name = name }).ConfigureAwait(false);
    }

    public async Task<IdentityProvider?> GetIdentityProviderAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<IdentityProvider>(
            "SELECT * FROM IdentityProviders WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<Guid> CreateIdentityProviderAsync(IdentityProvider provider)
    {
        using var conn = CreateConnection();
        provider.Id = provider.Id == Guid.Empty ? Guid.NewGuid() : provider.Id;
        await conn.ExecuteAsync(@"
            INSERT INTO IdentityProviders (Id, Name, Type, IsEnabled, IsPrimary, Configuration, Metadata, CreatedAt, CreatedBy)
            VALUES (@Id, @Name, @Type, @IsEnabled, @IsPrimary, @Configuration, @Metadata, GETUTCDATE(), @CreatedBy)", provider).ConfigureAwait(false);
        return provider.Id;
    }

    public async Task UpdateIdentityProviderAsync(IdentityProvider provider)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE IdentityProviders
            SET Name = @Name, Type = @Type, IsEnabled = @IsEnabled,
                IsPrimary = @IsPrimary, Configuration = @Configuration, Metadata = @Metadata,
                ModifiedAt = GETUTCDATE(), ModifiedBy = @ModifiedBy
            WHERE Id = @Id", provider).ConfigureAwait(false);
    }

    public async Task DeleteIdentityProviderAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM IdentityProviders WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    #endregion

    #region Schedule Templates

    public async Task<List<ScheduleTemplate>> GetScheduleTemplatesAsync(bool activeOnly = true)
    {
        using var conn = CreateConnection();
        var sql = @"SELECT * FROM ScheduleTemplates";
        if (activeOnly) sql += " WHERE IsActive = 1";
        sql += @" ORDER BY
            CASE Category WHEN 'Hourly' THEN 1 WHEN 'Daily' THEN 2 WHEN 'Weekly' THEN 3
                 WHEN 'Monthly' THEN 4 WHEN 'Quarterly' THEN 5 WHEN 'Yearly' THEN 6 ELSE 99 END,
            SortOrder, Name";
        var result = await conn.QueryAsync<ScheduleTemplate>(sql).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<ScheduleTemplate?> GetScheduleTemplateAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ScheduleTemplate>(
            "SELECT * FROM ScheduleTemplates WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<Guid> CreateScheduleTemplateAsync(ScheduleTemplate template)
    {
        using var conn = CreateConnection();
        template.Id = template.Id == Guid.Empty ? Guid.NewGuid() : template.Id;
        template.CreatedAt = DateTime.UtcNow;

        var maxSort = await conn.ExecuteScalarAsync<int?>(
            "SELECT MAX(SortOrder) FROM ScheduleTemplates WHERE Category = @Category",
            new { template.Category }).ConfigureAwait(false) ?? 0;
        template.SortOrder = maxSort + 1;

        await conn.ExecuteAsync(@"
            INSERT INTO ScheduleTemplates (Id, Name, Description, Category, CronExpression, SortOrder,
                IsSystem, IsActive, IconClass, Color, CreatedAt)
            VALUES (@Id, @Name, @Description, @Category, @CronExpression, @SortOrder,
                @IsSystem, @IsActive, @IconClass, @Color, @CreatedAt)", template).ConfigureAwait(false);
        return template.Id;
    }

    public async Task UpdateScheduleTemplateAsync(ScheduleTemplate template)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE ScheduleTemplates
            SET Name = @Name, Description = @Description, Category = @Category,
                CronExpression = @CronExpression, IconClass = @IconClass, Color = @Color
            WHERE Id = @Id", template).ConfigureAwait(false);
    }

    public async Task DeleteScheduleTemplateAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM ScheduleTemplates WHERE Id = @Id AND IsSystem = 0", new { Id = id }).ConfigureAwait(false);
    }

    #endregion

    #region Sync Audit Logs

    public async Task<List<SyncAuditLog>> GetSyncAuditLogsAsync(Guid? syncRunId = null, int? limit = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT TOP (@Limit) * FROM SyncAuditLogs";
        if (syncRunId.HasValue) sql += " WHERE SyncRunId = @SyncRunId";
        sql += " ORDER BY Timestamp DESC";

        var result = await conn.QueryAsync<SyncAuditLog>(sql,
            new { SyncRunId = syncRunId, Limit = limit ?? 1000 }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<SyncAuditLog>> GetSyncAuditLogsByStepRunAsync(Guid stepRunId)
    {
        using var conn = CreateConnection();
        var sql = "SELECT * FROM SyncAuditLogs WHERE SyncStepRunId = @StepRunId ORDER BY Timestamp";
        var result = await conn.QueryAsync<SyncAuditLog>(sql, new { StepRunId = stepRunId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<SyncStepRun?> GetSyncStepRunAsync(Guid stepRunId)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<SyncStepRun>(
            "SELECT * FROM SyncStepRuns WHERE Id = @Id", new { Id = stepRunId }).ConfigureAwait(false);
    }

    #endregion

    #region Sync Project Runs

    public async Task<List<SyncProjectRun>> GetSyncProjectRunsAsync(Guid? projectId = null, int? limit = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT TOP (@Limit) * FROM SyncProjectRuns";
        if (projectId.HasValue) sql += " WHERE SyncProjectId = @ProjectId";
        sql += " ORDER BY StartedAt DESC";

        var result = await conn.QueryAsync<SyncProjectRun>(sql,
            new { ProjectId = projectId, Limit = limit ?? 100 }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<int> ResetStuckSyncProjectAsync(Guid projectId)
    {
        using var conn = CreateConnection();
        var rowsAffected = 0;

        // Reset the sync project's IsRunning flag
        rowsAffected += await conn.ExecuteAsync(@"
            UPDATE SyncProjects
            SET IsRunning = 0
            WHERE Id = @ProjectId AND IsRunning = 1",
            new { ProjectId = projectId }).ConfigureAwait(false);

        // Mark any stuck runs as failed
        rowsAffected += await conn.ExecuteAsync(@"
            UPDATE SyncProjectRuns
            SET Status = 'Failed',
                CompletedAt = GETUTCDATE(),
                ErrorMessage = 'Reset by administrator - sync was stuck'
            WHERE SyncProjectId = @ProjectId AND Status = 'Running'",
            new { ProjectId = projectId }).ConfigureAwait(false);

        _logger.LogInformation("Reset stuck sync project {ProjectId}, affected {Rows} rows", projectId, rowsAffected);
        return rowsAffected;
    }

    public async Task<int> ResetAllStuckSyncProjectsAsync()
    {
        using var conn = CreateConnection();
        var rowsAffected = 0;

        // Reset all stuck sync projects
        rowsAffected += await conn.ExecuteAsync(@"
            UPDATE SyncProjects SET IsRunning = 0 WHERE IsRunning = 1").ConfigureAwait(false);

        // Mark all stuck runs as failed
        rowsAffected += await conn.ExecuteAsync(@"
            UPDATE SyncProjectRuns
            SET Status = 'Failed',
                CompletedAt = GETUTCDATE(),
                ErrorMessage = 'Reset by administrator - sync was stuck'
            WHERE Status = 'Running'").ConfigureAwait(false);

        _logger.LogInformation("Reset all stuck sync projects, affected {Rows} rows", rowsAffected);
        return rowsAffected;
    }

    public async Task<List<PostSyncTask>> GetPostSyncTasksForRunsAsync(List<Guid> runIds)
    {
        if (!runIds.Any()) return new List<PostSyncTask>();

        using var conn = CreateConnection();
        var result = await conn.QueryAsync<PostSyncTask>(@"
            SELECT * FROM PostSyncTasks
            WHERE SyncProjectRunId IN @RunIds
            ORDER BY Priority",
            new { RunIds = runIds }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task CancelSyncRunAsync(Guid runId)
    {
        using var conn = CreateConnection();

        // Update the run
        await conn.ExecuteAsync(@"
            UPDATE SyncProjectRuns
            SET Status = 'Cancelled',
                CompletedAt = GETUTCDATE(),
                DurationSeconds = DATEDIFF(SECOND, StartedAt, GETUTCDATE())
            WHERE Id = @RunId AND Status = 'Running'",
            new { RunId = runId }).ConfigureAwait(false);

        // Also reset the project's IsRunning flag
        await conn.ExecuteAsync(@"
            UPDATE SyncProjects
            SET IsRunning = 0
            WHERE Id = (SELECT SyncProjectId FROM SyncProjectRuns WHERE Id = @RunId)",
            new { RunId = runId }).ConfigureAwait(false);

        _logger.LogInformation("Cancelled sync run {RunId}", runId);
    }

    #endregion

    #region Advanced Search

    public async Task<(List<IdentityObject> Items, int TotalCount)> AdvancedSearchObjectsAsync(
        string? objectClass = null, string? source = null, string? displayName = null,
        string? email = null, string? dn = null, bool? isActive = null,
        List<Guid>? tagIds = null, Guid? connectionId = null,
        int page = 1, int pageSize = 50,
        string? scopeWhereClause = null, DynamicParameters? scopeParams = null)
    {
        using var conn = CreateConnection();
        var where = "WHERE 1=1";
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(objectClass)) { where += " AND o.ObjectClass = @ObjectClass"; p.Add("ObjectClass", objectClass); }
        if (!string.IsNullOrEmpty(source)) { where += " AND o.SourceConnectionId = @Source"; p.Add("Source", Guid.Parse(source)); }
        if (!string.IsNullOrEmpty(displayName)) { where += " AND (o.DisplayName LIKE @DisplayName OR o.CN LIKE @DisplayName)"; p.Add("DisplayName", $"%{displayName}%"); }
        if (!string.IsNullOrEmpty(email)) { where += " AND o.Email LIKE @Email"; p.Add("Email", $"%{email}%"); }
        if (!string.IsNullOrEmpty(dn)) { where += " AND o.DN LIKE @DN"; p.Add("DN", $"%{dn}%"); }
        if (isActive.HasValue) { where += " AND o.IsActive = @IsActive"; p.Add("IsActive", isActive.Value); }
        if (connectionId.HasValue) { where += " AND o.SourceConnectionId = @ConnectionId"; p.Add("ConnectionId", connectionId.Value); }

        var joinClause = "";
        if (tagIds != null && tagIds.Any())
        {
            joinClause = " INNER JOIN ObjectTags ot ON o.Id = ot.ObjectId";
            where += " AND ot.TagId IN @TagIds";
            p.Add("TagIds", tagIds);
        }

        if (!string.IsNullOrEmpty(scopeWhereClause))
        {
            where += " " + scopeWhereClause;
            p.AddDynamicParams(scopeParams);
        }

        var countSql = $"SELECT COUNT(DISTINCT o.Id) FROM Objects o{joinClause} {where}";
        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);
        var dataSql = $@"SELECT DISTINCT o.* FROM Objects o{joinClause} {where}
            ORDER BY o.DisplayName, o.CN
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        var items = (await conn.QueryAsync<IdentityObject>(dataSql, p).ConfigureAwait(false)).ToList();

        return (items, totalCount);
    }

    #endregion

    #region Active Directory Connections

    public async Task<List<DirectoryConnection>> GetActiveDirectoryConnectionsAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<DirectoryConnection>(
            "SELECT * FROM DirectoryConnections WHERE IsActive = 1 ORDER BY Name").ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Tags Extended

    public async Task<Tag?> GetTagWithWorkflowCountAsync(Guid tagId)
    {
        using var conn = CreateConnection();
        var tag = await conn.QueryFirstOrDefaultAsync<Tag>(
            "SELECT * FROM Tags WHERE Id = @Id", new { Id = tagId }).ConfigureAwait(false);
        return tag;
    }

    public async Task<List<Guid>> GetObjectIdsByTagIdsAsync(List<Guid> tagIds)
    {
        if (!tagIds.Any()) return new List<Guid>();
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<Guid>(
            "SELECT DISTINCT ObjectId FROM ObjectTags WHERE TagId IN @TagIds",
            new { TagIds = tagIds }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<Guid>> GetObjectTagIdsAsync(List<Guid> objectIds)
    {
        if (!objectIds.Any()) return new List<Guid>();
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<Guid>(
            "SELECT DISTINCT TagId FROM ObjectTags WHERE ObjectId IN @ObjectIds",
            new { ObjectIds = objectIds }).ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Sync Step Tags

    public async Task<Dictionary<Guid, List<Tag>>> GetSyncStepTagsAsync(List<Guid> stepIds)
    {
        if (!stepIds.Any()) return new Dictionary<Guid, List<Tag>>();
        using var conn = CreateConnection();
        var results = await conn.QueryAsync<dynamic>(@"
            SELECT st.SyncStepId, t.Id, t.Name, t.Category, t.Color, t.Description, t.CreatedAt
            FROM SyncStepTags st
            INNER JOIN Tags t ON st.TagId = t.Id
            WHERE st.SyncStepId IN @StepIds",
            new { StepIds = stepIds }).ConfigureAwait(false);

        var dict = new Dictionary<Guid, List<Tag>>();
        foreach (var row in results)
        {
            var stepId = (Guid)row.SyncStepId;
            if (!dict.ContainsKey(stepId))
                dict[stepId] = new List<Tag>();
            dict[stepId].Add(new Tag
            {
                Id = row.Id,
                Name = row.Name,
                Category = row.Category,
                Color = row.Color,
                Description = row.Description,
                CreatedAt = row.CreatedAt
            });
        }
        return dict;
    }

    #endregion

    #region Sync Project Counts

    public async Task<int> GetSyncProjectRunCountAsync(Guid? projectId = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT COUNT(*) FROM SyncProjectRuns WHERE Status = 'Running'";
        if (projectId.HasValue)
            sql += " AND SyncProjectId = @ProjectId";
        return await conn.ExecuteScalarAsync<int>(sql, new { ProjectId = projectId }).ConfigureAwait(false);
    }

    #endregion

    #region System Settings - Data Clearing

    public async Task<int> GetViolationCountAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CompliancePolicyViolations").ConfigureAwait(false);
    }

    public async Task<int> GetCampaignCountAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Campaigns").ConfigureAwait(false);
    }

    public async Task<int> GetAssignmentCountAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AccessReviewAssignments").ConfigureAwait(false);
    }

    public async Task<(int AssignmentsDeleted, int CampaignsDeleted, int ViolationsDeleted)> ClearAllViolationsWithCampaignsAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        var assignmentsDeleted = await conn.ExecuteAsync(@"
            DELETE a FROM AccessReviewAssignments a
            INNER JOIN Campaigns c ON a.CampaignId = c.Id
            WHERE c.CampaignType IN ('PolicyViolation', 'PolicyViolationReview')", transaction: tx).ConfigureAwait(false);

        var campaignsDeleted = await conn.ExecuteAsync(@"
            DELETE FROM Campaigns
            WHERE CampaignType IN ('PolicyViolation', 'PolicyViolationReview')", transaction: tx).ConfigureAwait(false);

        var violationsDeleted = await conn.ExecuteAsync(
            "DELETE FROM CompliancePolicyViolations", transaction: tx).ConfigureAwait(false);

        tx.Commit();
        return (assignmentsDeleted, campaignsDeleted, violationsDeleted);
    }

    public async Task ClearAllAccessReviewDataAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        try { await conn.ExecuteAsync("DELETE FROM RemediationActions", transaction: tx).ConfigureAwait(false); } catch (Exception ex) { _logger.LogWarning(ex, "Non-critical cleanup operation failed: DELETE FROM RemediationActions"); }
        try { await conn.ExecuteAsync("DELETE FROM ReviewDecisionHistory", transaction: tx).ConfigureAwait(false); } catch (Exception ex) { _logger.LogWarning(ex, "Non-critical cleanup operation failed: DELETE FROM ReviewDecisionHistory"); }
        await conn.ExecuteAsync("DELETE FROM AccessReviewAssignments", transaction: tx).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM Campaigns", transaction: tx).ConfigureAwait(false);

        tx.Commit();
    }

    #endregion

    #region Field Lookup with Usage (Organization Center)

    public async Task<List<FieldValueWithUsage>> GetFieldValuesWithUsageAsync(string fieldName)
    {
        var allowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Department", "Division", "Company", "Office", "Building",
            "IdentityType", "ContractType", "JobTitle", "Status",
            "CostCenter", "ProfitCenter", "Country", "City", "State"
        };
        if (!allowedFields.Contains(fieldName))
            throw new ArgumentException($"Field '{fieldName}' is not allowed.");

        using var conn = CreateConnection();

        // Get managed values from FieldLookupValues
        var managed = await conn.QueryAsync<FieldValueWithUsage>($@"
            SELECT
                flv.Id AS LookupId,
                flv.FieldName,
                flv.Value,
                flv.SortOrder,
                flv.IsActive,
                CAST(1 AS BIT) AS IsManaged,
                ISNULL(ic.Cnt, 0) AS IdentityCount
            FROM FieldLookupValues flv
            LEFT JOIN (
                SELECT [{fieldName}] AS Val, COUNT(*) AS Cnt
                FROM Identities
                WHERE [{fieldName}] IS NOT NULL AND [{fieldName}] != ''
                GROUP BY [{fieldName}]
            ) ic ON ic.Val = flv.Value
            WHERE flv.FieldName = @FieldName
            ORDER BY flv.SortOrder, flv.Value",
            new { FieldName = fieldName }).ConfigureAwait(false);

        var managedSet = new HashSet<string>(managed.Select(m => m.Value), StringComparer.OrdinalIgnoreCase);

        // Get discovered-only values (in Identities but not in FieldLookupValues)
        var discovered = await conn.QueryAsync<FieldValueWithUsage>($@"
            SELECT
                NULL AS LookupId,
                @FieldName AS FieldName,
                [{fieldName}] AS Value,
                0 AS SortOrder,
                CAST(1 AS BIT) AS IsActive,
                CAST(0 AS BIT) AS IsManaged,
                COUNT(*) AS IdentityCount
            FROM Identities
            WHERE [{fieldName}] IS NOT NULL AND [{fieldName}] != ''
            GROUP BY [{fieldName}]
            ORDER BY [{fieldName}]",
            new { FieldName = fieldName }).ConfigureAwait(false);

        var result = managed.ToList();
        foreach (var d in discovered)
        {
            if (!managedSet.Contains(d.Value))
                result.Add(d);
        }

        return result;
    }

    public async Task<Dictionary<string, int>> GetFieldLookupCountsAsync()
    {
        using var conn = CreateConnection();
        // Count distinct values actually in use across Identities for each field
        // This includes both managed (FieldLookupValues) and discovered values
        var sql = @"
            SELECT 'Department' AS FieldName, COUNT(DISTINCT Department) AS Count FROM Identities WHERE Department IS NOT NULL AND Department != ''
            UNION ALL
            SELECT 'Division', COUNT(DISTINCT Division) FROM Identities WHERE Division IS NOT NULL AND Division != ''
            UNION ALL
            SELECT 'Company', COUNT(DISTINCT Company) FROM Identities WHERE Company IS NOT NULL AND Company != ''
            UNION ALL
            SELECT 'Office', COUNT(DISTINCT Office) FROM Identities WHERE Office IS NOT NULL AND Office != ''
            UNION ALL
            SELECT 'Building', COUNT(DISTINCT Building) FROM Identities WHERE Building IS NOT NULL AND Building != ''
            UNION ALL
            SELECT 'IdentityType', COUNT(DISTINCT IdentityType) FROM Identities WHERE IdentityType IS NOT NULL AND IdentityType != ''
            UNION ALL
            SELECT 'ContractType', COUNT(DISTINCT ContractType) FROM Identities WHERE ContractType IS NOT NULL AND ContractType != ''
            UNION ALL
            SELECT 'JobTitle', COUNT(DISTINCT JobTitle) FROM Identities WHERE JobTitle IS NOT NULL AND JobTitle != ''
            UNION ALL
            SELECT 'Country', COUNT(DISTINCT Country) FROM Identities WHERE Country IS NOT NULL AND Country != ''
            UNION ALL
            SELECT 'City', COUNT(DISTINCT City) FROM Identities WHERE City IS NOT NULL AND City != ''
            UNION ALL
            SELECT 'State', COUNT(DISTINCT [State]) FROM Identities WHERE [State] IS NOT NULL AND [State] != ''
            UNION ALL
            SELECT 'CostCenter', COUNT(DISTINCT CostCenter) FROM Identities WHERE CostCenter IS NOT NULL AND CostCenter != ''";

        var rows = await conn.QueryAsync<(string FieldName, int Count)>(sql).ConfigureAwait(false);

        return rows.ToDictionary(r => r.FieldName, r => r.Count);
    }

    public async Task<(List<Identity> Items, int TotalCount)> GetIdentitiesByFieldValueAsync(string fieldName, string value, int page = 1, int pageSize = 50)
    {
        var allowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Department", "Division", "Company", "Office", "Building",
            "IdentityType", "ContractType", "JobTitle", "Status",
            "CostCenter", "ProfitCenter", "Country", "City", "State"
        };
        if (!allowedFields.Contains(fieldName))
            throw new ArgumentException($"Field '{fieldName}' is not allowed.");

        using var conn = CreateConnection();

        var totalCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM Identities WHERE [{fieldName}] = @Value",
            new { Value = value }).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        var items = (await conn.QueryAsync<Identity>(
            $@"SELECT * FROM Identities
               WHERE [{fieldName}] = @Value
               ORDER BY DisplayName, LastName, FirstName
               OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            new { Value = value, Offset = offset, PageSize = pageSize }).ConfigureAwait(false)).ToList();

        return (items, totalCount);
    }

    #endregion

    #region Dashboard

    public async Task<Dictionary<string, int>> GetIdentityTypeBreakdownAsync()
    {
        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<(string IdentityType, int Count)>(@"
            SELECT COALESCE(IdentityType, '(Unclassified)') AS IdentityType, COUNT(*) AS [Count]
            FROM Identities WITH (NOLOCK)
            GROUP BY IdentityType
            ORDER BY [Count] DESC").ConfigureAwait(false);
        return rows.ToDictionary(r => r.IdentityType, r => r.Count);
    }

    #endregion

    #region Organizational Folder Policies

    public async Task<List<OrganizationalFolder>> GetFoldersForPolicyAsync(Guid policyId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<OrganizationalFolder>(@"
            SELECT f.* FROM OrganizationalFolders f
            INNER JOIN OrganizationalFolderPolicies fp ON f.Id = fp.FolderId
            WHERE fp.PolicyId = @PolicyId AND fp.IsActive = 1
            ORDER BY f.FolderType, f.Name",
            new { PolicyId = policyId }).ConfigureAwait(false);
        return result.ToList();
    }

    #endregion
}
