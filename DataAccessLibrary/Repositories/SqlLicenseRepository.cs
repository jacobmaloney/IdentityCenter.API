using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="ISqlLicenseRepository"/>.
/// Schema managed by V078/V079/V080 migrations.
/// </summary>
public class SqlLicenseRepository : ISqlLicenseRepository
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public SqlLicenseRepository(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    // ─────────────────────────────────────────────────────────────────────────
    // Entitlements
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<SqlLicenseEntitlement>> GetEntitlementsAsync()
    {
        const string sql = @"
            SELECT Id, LicenseType, Edition, Quantity, QuantityUnit, CostPerUnit, TotalCost,
                   VendorAgreementNumber, PurchaseDate, ExpiryDate, SoftwareAssurance,
                   IsActive, Notes, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
            FROM SqlLicenseEntitlements
            WHERE IsActive = 1
            ORDER BY Edition, LicenseType;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<SqlLicenseEntitlement>(sql);
        return rows.ToList();
    }

    public async Task<SqlLicenseEntitlement?> GetEntitlementAsync(Guid id)
    {
        const string sql = @"
            SELECT Id, LicenseType, Edition, Quantity, QuantityUnit, CostPerUnit, TotalCost,
                   VendorAgreementNumber, PurchaseDate, ExpiryDate, SoftwareAssurance,
                   IsActive, Notes, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
            FROM SqlLicenseEntitlements
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<SqlLicenseEntitlement>(sql, new { Id = id });
    }

    public async Task<Guid> CreateEntitlementAsync(SqlLicenseEntitlement entitlement)
    {
        const string sql = @"
            INSERT INTO SqlLicenseEntitlements
                (Id, LicenseType, Edition, Quantity, QuantityUnit, CostPerUnit,
                 VendorAgreementNumber, PurchaseDate, ExpiryDate, SoftwareAssurance,
                 IsActive, Notes, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            VALUES
                (@Id, @LicenseType, @Edition, @Quantity, @QuantityUnit, @CostPerUnit,
                 @VendorAgreementNumber, @PurchaseDate, @ExpiryDate, @SoftwareAssurance,
                 @IsActive, @Notes, GETUTCDATE(), @CreatedBy, GETUTCDATE(), @UpdatedBy);";

        if (entitlement.Id == Guid.Empty) entitlement.Id = Guid.NewGuid();

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, entitlement);
        _logger.LogInformation("SqlLicenseRepository.CreateEntitlementAsync: Created entitlement {Id} ({Edition} {LicenseType})",
            entitlement.Id, entitlement.Edition, entitlement.LicenseType);
        return entitlement.Id;
    }

    public async Task UpdateEntitlementAsync(SqlLicenseEntitlement entitlement)
    {
        const string sql = @"
            UPDATE SqlLicenseEntitlements
            SET LicenseType           = @LicenseType,
                Edition               = @Edition,
                Quantity              = @Quantity,
                QuantityUnit          = @QuantityUnit,
                CostPerUnit           = @CostPerUnit,
                VendorAgreementNumber = @VendorAgreementNumber,
                PurchaseDate          = @PurchaseDate,
                ExpiryDate            = @ExpiryDate,
                SoftwareAssurance     = @SoftwareAssurance,
                IsActive              = @IsActive,
                Notes                 = @Notes,
                UpdatedAt             = GETUTCDATE(),
                UpdatedBy             = @UpdatedBy
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, entitlement);
        _logger.LogInformation("SqlLicenseRepository.UpdateEntitlementAsync: Updated entitlement {Id}", entitlement.Id);
    }

    public async Task DeleteEntitlementAsync(Guid id)
    {
        const string sql = @"
            UPDATE SqlLicenseEntitlements
            SET IsActive = 0, UpdatedAt = GETUTCDATE()
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { Id = id });
        _logger.LogInformation("SqlLicenseRepository.DeleteEntitlementAsync: Soft-deleted entitlement {Id}", id);
    }

    public async Task<int> GetTotalOwnedCoresAsync(string edition)
    {
        const string sql = @"
            SELECT ISNULL(SUM(Quantity), 0)
            FROM SqlLicenseEntitlements
            WHERE IsActive = 1 AND Edition = @Edition AND QuantityUnit = 'Cores';";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(sql, new { Edition = edition });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inventory
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<SqlServerInventory>> GetInventoryAsync()
    {
        const string sql = @"
            SELECT
                s.Id, s.ObjectId, s.DiscoveryMethod, s.ServerName, s.Fqdn, s.IpAddress,
                s.Port, s.InstanceName, s.SqlEdition, s.SqlVersion, s.SqlVersionMajor,
                s.CpuCores, s.MemoryGb, s.OsName, s.OsVersion, s.IsOnline, s.IsProduction,
                s.OwnerId, s.OwnerAssignedAt, s.OwnerAssignedBy,
                s.ComplianceStatus, s.ComplianceCheckedAt,
                s.LastDiscoveredAt, s.LastAgentContactAt, s.CreatedAt, s.UpdatedAt,
                s.EncryptedConnectionString, s.CredentialId, s.LastScanStatus, s.LastScanMessage, s.LastScanDurationMs,
                o.DisplayName AS OwnerDisplayName
            FROM SqlServerInventory s
            LEFT JOIN Objects o ON o.Id = s.OwnerId AND o.DeletedAt IS NULL
            ORDER BY s.ServerName;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        var servers = (await conn.QueryAsync<SqlServerInventory>(sql)).ToList();

        // Load assignments for each server that has an ObjectId
        if (servers.Any())
        {
            const string assignmentSql = @"
                SELECT a.Id, a.EntitlementId, a.ObjectId, a.AssignedCores, a.AssignedBy, a.AssignedAt, a.IsActive, a.Notes,
                       e.Edition, e.Quantity
                FROM SqlLicenseAssignments a
                INNER JOIN SqlLicenseEntitlements e ON e.Id = a.EntitlementId
                WHERE a.IsActive = 1;";

            var assignments = (await conn.QueryAsync<SqlLicenseAssignment>(assignmentSql)).ToList();
            foreach (var server in servers.Where(s => s.ObjectId != null))
            {
                server.LicenseAssignment = assignments.FirstOrDefault(a => a.ObjectId == server.ObjectId);
            }
        }

        return servers;
    }

    public async Task<SqlServerInventory?> GetServerAsync(Guid id)
    {
        const string sql = @"
            SELECT
                s.Id, s.ObjectId, s.DiscoveryMethod, s.ServerName, s.Fqdn, s.IpAddress,
                s.Port, s.InstanceName, s.SqlEdition, s.SqlVersion, s.SqlVersionMajor,
                s.CpuCores, s.MemoryGb, s.OsName, s.OsVersion, s.IsOnline, s.IsProduction,
                s.OwnerId, s.OwnerAssignedAt, s.OwnerAssignedBy,
                s.ComplianceStatus, s.ComplianceCheckedAt,
                s.LastDiscoveredAt, s.LastAgentContactAt, s.CreatedAt, s.UpdatedAt,
                o.DisplayName AS OwnerDisplayName
            FROM SqlServerInventory s
            LEFT JOIN Objects o ON o.Id = s.OwnerId AND o.DeletedAt IS NULL
            WHERE s.Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        var server = await conn.QuerySingleOrDefaultAsync<SqlServerInventory>(sql, new { Id = id });

        if (server?.ObjectId != null)
        {
            const string assignmentSql = @"
                SELECT a.Id, a.EntitlementId, a.ObjectId, a.AssignedCores, a.AssignedBy, a.AssignedAt, a.IsActive, a.Notes,
                       e.Edition, e.Quantity
                FROM SqlLicenseAssignments a
                INNER JOIN SqlLicenseEntitlements e ON e.Id = a.EntitlementId
                WHERE a.ObjectId = @ObjectId AND a.IsActive = 1;";

            server.LicenseAssignment = await conn.QuerySingleOrDefaultAsync<SqlLicenseAssignment>(
                assignmentSql, new { server.ObjectId });

            // Load active violations
            const string violationSql = @"
                SELECT Id, SqlServerInventoryId, ObjectId, ViolationType, Severity, Title, Detail,
                       IsResolved, DetectedAt, CertificationCampaignId
                FROM LicenseComplianceViolations
                WHERE SqlServerInventoryId = @ServerId AND IsResolved = 0;";

            server.ActiveViolations = (await conn.QueryAsync<LicenseComplianceViolation>(
                violationSql, new { ServerId = server.Id })).ToList();

            // Load databases
            server.Databases = (await GetDatabasesAsync(server.Id)).ToList();
        }

        return server;
    }

    public async Task<SqlServerInventory?> GetServerByNameAsync(string serverName, string? instanceName = null)
    {
        const string sql = @"
            SELECT Id, ObjectId, DiscoveryMethod, ServerName, Fqdn, IpAddress,
                   Port, InstanceName, SqlEdition, SqlVersion, SqlVersionMajor,
                   CpuCores, MemoryGb, OsName, OsVersion, IsOnline, IsProduction,
                   OwnerId, OwnerAssignedAt, OwnerAssignedBy,
                   ComplianceStatus, ComplianceCheckedAt,
                   LastDiscoveredAt, LastAgentContactAt, CreatedAt, UpdatedAt,
                   EncryptedConnectionString, CredentialId, LastScanStatus, LastScanMessage, LastScanDurationMs,
                   DiscoveryStatus
            FROM SqlServerInventory
            WHERE ServerName = @ServerName
              AND ((@InstanceName IS NULL AND InstanceName IS NULL) OR InstanceName = @InstanceName);";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<SqlServerInventory>(sql,
            new { ServerName = serverName, InstanceName = instanceName });
    }

    public async Task<SqlServerInventory?> GetServerByIpAsync(string ipAddress, string? instanceName = null)
    {
        const string sql = @"
            SELECT Id, ObjectId, DiscoveryMethod, ServerName, Fqdn, IpAddress,
                   Port, InstanceName, SqlEdition, SqlVersion, SqlVersionMajor,
                   CpuCores, MemoryGb, OsName, OsVersion, IsOnline, IsProduction,
                   OwnerId, OwnerAssignedAt, OwnerAssignedBy,
                   ComplianceStatus, ComplianceCheckedAt,
                   LastDiscoveredAt, LastAgentContactAt, CreatedAt, UpdatedAt,
                   EncryptedConnectionString, CredentialId, LastScanStatus, LastScanMessage, LastScanDurationMs,
                   DiscoveryStatus
            FROM SqlServerInventory
            WHERE IpAddress = @IpAddress
              AND ((@InstanceName IS NULL AND InstanceName IS NULL) OR InstanceName = @InstanceName);";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<SqlServerInventory>(sql,
            new { IpAddress = ipAddress, InstanceName = instanceName });
    }

    public async Task<List<SqlServerInventory>> GetAllServersAsync()
    {
        const string sql = @"
            SELECT Id, ObjectId, DiscoveryMethod, ServerName, Fqdn, IpAddress,
                   Port, InstanceName, SqlEdition, SqlVersion, SqlVersionMajor,
                   CpuCores, MemoryGb, OsName, OsVersion, IsOnline, IsProduction,
                   OwnerId, OwnerAssignedAt, OwnerAssignedBy,
                   ComplianceStatus, ComplianceCheckedAt,
                   LastDiscoveredAt, LastAgentContactAt, CreatedAt, UpdatedAt,
                   EncryptedConnectionString, CredentialId, LastScanStatus, LastScanMessage, LastScanDurationMs,
                   DiscoveryStatus
            FROM SqlServerInventory ORDER BY ServerName;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<SqlServerInventory>(sql)).ToList();
    }

    public async Task<Guid> RecordScanHistoryAsync(NetworkScanHistoryEntry entry)
    {
        if (entry.Id == Guid.Empty) entry.Id = Guid.NewGuid();

        using var conn = CreateConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            INSERT INTO NetworkScanHistory
                (Id, NetworkScanRangeId, CidrRange, StartedAt, CompletedAt, DurationSeconds,
                 Status, TotalScanned, FoundServers, NewServers, ExistingServers, NewObjectsCreated,
                 ErrorMessage, DiscoveredServersJson, TriggeredBy)
            VALUES
                (@Id, @NetworkScanRangeId, @CidrRange, @StartedAt, @CompletedAt, @DurationSeconds,
                 @Status, @TotalScanned, @FoundServers, @NewServers, @ExistingServers, @NewObjectsCreated,
                 @ErrorMessage, @DiscoveredServersJson, @TriggeredBy)", entry);

        return entry.Id;
    }

    public async Task<List<NetworkScanHistoryEntry>> GetScanHistoryAsync(Guid? rangeId = null, int limit = 50)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        var sql = @"
            SELECT TOP (@Limit) *
            FROM NetworkScanHistory
            WHERE (@RangeId IS NULL OR NetworkScanRangeId = @RangeId)
            ORDER BY StartedAt DESC";

        return (await conn.QueryAsync<NetworkScanHistoryEntry>(sql, new { RangeId = rangeId, Limit = limit })).ToList();
    }

    public async Task UpdateServerCredentialAsync(Guid serverId, Guid? credentialId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE SqlServerInventory SET CredentialId = @CredentialId, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = serverId, CredentialId = credentialId });
    }

    public async Task UpdateScanStatusAsync(Guid serverId, string status, string? message, int? durationMs)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE SqlServerInventory
            SET LastScanStatus = @Status,
                LastScanMessage = @Message,
                LastScanDurationMs = @DurationMs,
                UpdatedAt = GETUTCDATE()
            WHERE Id = @Id",
            new { Id = serverId, Status = status, Message = message, DurationMs = durationMs });
    }

    public async Task<SqlServerInventory?> GetServerByIdAsync(Guid serverId)
    {
        const string sql = @"
            SELECT Id, ObjectId, DiscoveryMethod, ServerName, Fqdn, IpAddress,
                   Port, InstanceName, SqlEdition, SqlVersion, SqlVersionMajor,
                   CpuCores, MemoryGb, OsName, OsVersion, IsOnline, IsProduction,
                   OwnerId, OwnerAssignedAt, OwnerAssignedBy,
                   ComplianceStatus, ComplianceCheckedAt,
                   LastDiscoveredAt, LastAgentContactAt, CreatedAt, UpdatedAt,
                   EncryptedConnectionString, CredentialId, LastScanStatus, LastScanMessage, LastScanDurationMs,
                   DiscoveryStatus
            FROM SqlServerInventory WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<SqlServerInventory>(sql, new { Id = serverId });
    }

    public async Task<Guid> UpsertServerAsync(SqlServerInventory server)
    {
        const string lookupSql = @"
            SELECT Id FROM SqlServerInventory
            WHERE ServerName = @ServerName
              AND ISNULL(Port, 1433) = ISNULL(@Port, 1433)
              AND ((@InstanceName IS NULL AND InstanceName IS NULL) OR InstanceName = @InstanceName);";

        const string insertSql = @"
            INSERT INTO SqlServerInventory
                (Id, ObjectId, DiscoveryMethod, ServerName, Fqdn, IpAddress, Port, InstanceName,
                 SqlEdition, SqlVersion, SqlVersionMajor, CpuCores, MemoryGb, OsName, OsVersion,
                 IsOnline, IsProduction, OwnerId, ComplianceStatus, EncryptedConnectionString, DiscoveryStatus,
                 LastDiscoveredAt, CreatedAt, UpdatedAt)
            VALUES
                (@Id, @ObjectId, @DiscoveryMethod, @ServerName, @Fqdn, @IpAddress, @Port, @InstanceName,
                 @SqlEdition, @SqlVersion, @SqlVersionMajor, @CpuCores, @MemoryGb, @OsName, @OsVersion,
                 @IsOnline, @IsProduction, @OwnerId, @ComplianceStatus, @EncryptedConnectionString, @DiscoveryStatus,
                 GETUTCDATE(), GETUTCDATE(), GETUTCDATE());";

        const string updateSql = @"
            UPDATE SqlServerInventory
            SET ObjectId          = COALESCE(@ObjectId, ObjectId),
                DiscoveryMethod   = @DiscoveryMethod,
                Fqdn              = COALESCE(@Fqdn, Fqdn),
                IpAddress         = COALESCE(@IpAddress, IpAddress),
                SqlEdition        = COALESCE(@SqlEdition, SqlEdition),
                SqlVersion        = COALESCE(@SqlVersion, SqlVersion),
                SqlVersionMajor   = COALESCE(@SqlVersionMajor, SqlVersionMajor),
                CpuCores          = COALESCE(@CpuCores, CpuCores),
                MemoryGb          = COALESCE(@MemoryGb, MemoryGb),
                OsName            = COALESCE(@OsName, OsName),
                OsVersion         = COALESCE(@OsVersion, OsVersion),
                IsOnline          = @IsOnline,
                IsProduction      = COALESCE(@IsProduction, IsProduction),
                EncryptedConnectionString = COALESCE(@EncryptedConnectionString, EncryptedConnectionString),
                LastDiscoveredAt  = GETUTCDATE(),
                UpdatedAt         = GETUTCDATE()
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();

        var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(lookupSql,
            new { server.ServerName, server.Port, server.InstanceName });

        if (existingId.HasValue)
        {
            server.Id = existingId.Value;
            await conn.ExecuteAsync(updateSql, server);
            _logger.LogInformation("SqlLicenseRepository.UpsertServerAsync: Updated server {Id} ({ServerName})",
                server.Id, server.ServerName);
        }
        else
        {
            if (server.Id == Guid.Empty) server.Id = Guid.NewGuid();
            await conn.ExecuteAsync(insertSql, server);
            _logger.LogInformation("SqlLicenseRepository.UpsertServerAsync: Inserted server {Id} ({ServerName})",
                server.Id, server.ServerName);
        }

        return server.Id;
    }

    public async Task UpdateServerOwnerAsync(Guid serverId, string ownerId, string assignedBy)
    {
        const string sql = @"
            UPDATE SqlServerInventory
            SET OwnerId        = @OwnerId,
                OwnerAssignedAt = GETUTCDATE(),
                OwnerAssignedBy = @AssignedBy,
                UpdatedAt       = GETUTCDATE()
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { Id = serverId, OwnerId = ownerId, AssignedBy = assignedBy });
        _logger.LogInformation("SqlLicenseRepository.UpdateServerOwnerAsync: Server {ServerId} owner set to {OwnerId}",
            serverId, ownerId);
    }

    public async Task UpdateServerComplianceStatusAsync(Guid serverId, string status)
    {
        const string sql = @"
            UPDATE SqlServerInventory
            SET ComplianceStatus    = @Status,
                ComplianceCheckedAt = GETUTCDATE(),
                UpdatedAt           = GETUTCDATE()
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { Id = serverId, Status = status });
    }

    public async Task<List<SqlDatabaseInventory>> GetDatabasesAsync(Guid serverId)
    {
        const string sql = @"
            SELECT Id, SqlServerInventoryId, DatabaseName, SizeGb, LogSizeGb,
                   RecoveryModel, CompatibilityLevel, IsSystemDb,
                   LastBackupAt, LastBackupType, State, CreatedAt, UpdatedAt
            FROM SqlDatabaseInventory
            WHERE SqlServerInventoryId = @ServerId
            ORDER BY IsSystemDb, DatabaseName;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<SqlDatabaseInventory>(sql, new { ServerId = serverId });
        return rows.ToList();
    }

    public async Task UpsertDatabasesAsync(Guid serverId, List<SqlDatabaseInventory> databases)
    {
        const string upsertSql = @"
            MERGE SqlDatabaseInventory AS target
            USING (SELECT @Id AS Id, @SqlServerInventoryId AS SqlServerInventoryId,
                          @DatabaseName AS DatabaseName) AS source
            ON target.SqlServerInventoryId = source.SqlServerInventoryId
               AND target.DatabaseName = source.DatabaseName
            WHEN MATCHED THEN UPDATE SET
                SizeGb             = @SizeGb,
                LogSizeGb          = @LogSizeGb,
                RecoveryModel      = @RecoveryModel,
                CompatibilityLevel = @CompatibilityLevel,
                IsSystemDb         = @IsSystemDb,
                LastBackupAt       = @LastBackupAt,
                LastBackupType     = @LastBackupType,
                State              = @State,
                UpdatedAt          = GETUTCDATE()
            WHEN NOT MATCHED THEN INSERT
                (Id, SqlServerInventoryId, DatabaseName, SizeGb, LogSizeGb,
                 RecoveryModel, CompatibilityLevel, IsSystemDb,
                 LastBackupAt, LastBackupType, State, CreatedAt, UpdatedAt)
            VALUES
                (@Id, @SqlServerInventoryId, @DatabaseName, @SizeGb, @LogSizeGb,
                 @RecoveryModel, @CompatibilityLevel, @IsSystemDb,
                 @LastBackupAt, @LastBackupType, @State, GETUTCDATE(), GETUTCDATE());";

        using var conn = CreateConnection();
        await conn.OpenAsync();

        foreach (var db in databases)
        {
            db.SqlServerInventoryId = serverId;
            if (db.Id == Guid.Empty) db.Id = Guid.NewGuid();
            await conn.ExecuteAsync(upsertSql, db);
        }

        _logger.LogInformation("SqlLicenseRepository.UpsertDatabasesAsync: Upserted {Count} databases for server {ServerId}",
            databases.Count, serverId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Assignments
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<SqlLicenseAssignment>> GetAssignmentsAsync(Guid? entitlementId = null)
    {
        const string baseSql = @"
            SELECT a.Id, a.EntitlementId, a.ObjectId, a.AssignedCores, a.AssignedBy, a.AssignedAt, a.IsActive, a.Notes,
                   s.ServerName, e.Edition, e.Quantity
            FROM SqlLicenseAssignments a
            INNER JOIN SqlLicenseEntitlements e ON e.Id = a.EntitlementId
            LEFT JOIN SqlServerInventory s ON s.ObjectId = a.ObjectId
            WHERE a.IsActive = 1";

        var sql = entitlementId.HasValue
            ? baseSql + " AND a.EntitlementId = @EntitlementId ORDER BY s.ServerName;"
            : baseSql + " ORDER BY s.ServerName;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<SqlLicenseAssignment>(sql,
            entitlementId.HasValue ? new { EntitlementId = entitlementId.Value } : null);
        return rows.ToList();
    }

    public async Task<SqlLicenseAssignment?> GetAssignmentForServerAsync(string objectId)
    {
        const string sql = @"
            SELECT a.Id, a.EntitlementId, a.ObjectId, a.AssignedCores, a.AssignedBy, a.AssignedAt, a.IsActive, a.Notes,
                   e.Edition, e.Quantity
            FROM SqlLicenseAssignments a
            INNER JOIN SqlLicenseEntitlements e ON e.Id = a.EntitlementId
            WHERE a.ObjectId = @ObjectId AND a.IsActive = 1;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<SqlLicenseAssignment>(sql, new { ObjectId = objectId });
    }

    public async Task<Guid> CreateAssignmentAsync(SqlLicenseAssignment assignment)
    {
        const string sql = @"
            INSERT INTO SqlLicenseAssignments
                (Id, EntitlementId, ObjectId, AssignedCores, AssignedBy, AssignedAt, IsActive, Notes)
            VALUES
                (@Id, @EntitlementId, @ObjectId, @AssignedCores, @AssignedBy, GETUTCDATE(), 1, @Notes);";

        if (assignment.Id == Guid.Empty) assignment.Id = Guid.NewGuid();

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, assignment);
        _logger.LogInformation("SqlLicenseRepository.CreateAssignmentAsync: Assigned entitlement {EntitlementId} to object {ObjectId}",
            assignment.EntitlementId, assignment.ObjectId);
        return assignment.Id;
    }

    public async Task RemoveAssignmentAsync(Guid id, string removedBy)
    {
        const string sql = @"
            UPDATE SqlLicenseAssignments
            SET IsActive = 0
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { Id = id });
        _logger.LogInformation("SqlLicenseRepository.RemoveAssignmentAsync: Deactivated assignment {Id} by {RemovedBy}",
            id, removedBy);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Network Scan Ranges
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<NetworkScanRange>> GetScanRangesAsync()
    {
        const string sql = @"
            SELECT Id, Name, CidrRange, Description, IsEnabled,
                   LastScannedAt, LastScanDurationSeconds, CreatedAt, CreatedBy
            FROM NetworkScanRanges
            ORDER BY Name;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<NetworkScanRange>(sql);
        return rows.ToList();
    }

    public async Task<Guid> CreateScanRangeAsync(NetworkScanRange range)
    {
        const string sql = @"
            INSERT INTO NetworkScanRanges
                (Id, Name, CidrRange, Description, IsEnabled, CreatedAt, CreatedBy)
            VALUES
                (@Id, @Name, @CidrRange, @Description, @IsEnabled, GETUTCDATE(), @CreatedBy);";

        if (range.Id == Guid.Empty) range.Id = Guid.NewGuid();

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, range);
        _logger.LogInformation("SqlLicenseRepository.CreateScanRangeAsync: Created scan range {Id} ({Name}: {CidrRange})",
            range.Id, range.Name, range.CidrRange);
        return range.Id;
    }

    public async Task DeleteScanRangeAsync(Guid id)
    {
        const string sql = "DELETE FROM NetworkScanRanges WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { Id = id });
        _logger.LogInformation("SqlLicenseRepository.DeleteScanRangeAsync: Deleted scan range {Id}", id);
    }

    public async Task UpdateScanRangeLastScanAsync(Guid id, DateTime scannedAt, int durationSeconds)
    {
        const string sql = @"
            UPDATE NetworkScanRanges
            SET LastScannedAt           = @ScannedAt,
                LastScanDurationSeconds = @DurationSeconds
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { Id = id, ScannedAt = scannedAt, DurationSeconds = durationSeconds });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Compliance
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<SqlLicenseComplianceSummary> GetComplianceSummaryAsync(bool excludeDemo = false)
    {
        var demoFilter = excludeDemo
            ? @" AND NOT EXISTS (SELECT 1 FROM Objects o
                 WHERE TRY_CAST(inv.ObjectId AS UNIQUEIDENTIFIER) = o.Id
                   AND o.SourceConnectionId IN ('D0000000-0000-0000-0000-000000000001','D0000000-0000-0000-0000-000000000002','D0000000-0000-0000-0000-000000000003','D0000000-0000-0000-0000-000000000004'))"
            : "";

        var serverSql = string.Concat(@"
            SELECT
                COUNT(*) AS TotalServers,
                SUM(CASE WHEN ComplianceStatus = 'Licensed' THEN 1 ELSE 0 END) AS LicensedServers,
                SUM(CASE WHEN ComplianceStatus = 'Unlicensed' THEN 1 ELSE 0 END) AS UnlicensedServers,
                SUM(CASE WHEN ComplianceStatus = 'Violation' THEN 1 ELSE 0 END) AS ViolationServers,
                SUM(CASE WHEN ComplianceStatus IS NULL OR ComplianceStatus = 'Unknown' THEN 1 ELSE 0 END) AS UnknownServers,
                SUM(CASE WHEN OwnerId IS NULL THEN 1 ELSE 0 END) AS NoOwnerServers,
                SUM(CASE WHEN SqlVersionMajor <= 11 THEN 1 ELSE 0 END) AS EndOfLifeServers,
                SUM(CASE WHEN SqlEdition LIKE '%Developer%' THEN 1 ELSE 0 END) AS DeveloperInProdServers,
                ISNULL(SUM(CpuCores), 0) AS TotalDiscoveredCores
            FROM SqlServerInventory inv WHERE 1=1", demoFilter);

        const string entitlementSql = @"
            SELECT
                ISNULL(SUM(CASE WHEN QuantityUnit = 'Cores' THEN Quantity ELSE 0 END), 0) AS TotalOwnedCores,
                ISNULL(SUM(TotalCost), 0) AS TotalEntitlementCost
            FROM SqlLicenseEntitlements
            WHERE IsActive = 1;";

        using var conn = CreateConnection();
        await conn.OpenAsync();

        var summary = await conn.QuerySingleAsync<SqlLicenseComplianceSummary>(serverSql);
        var entitlementData = await conn.QuerySingleAsync<dynamic>(entitlementSql);

        summary.TotalOwnedCores = (int)entitlementData.TotalOwnedCores;
        summary.TotalEntitlementCost = (decimal)entitlementData.TotalEntitlementCost;

        // Estimate exposure: unlicensed Enterprise cores * ~$7k/core, Standard * ~$4k/core
        // This is a rough estimate; real pricing depends on agreement
        summary.EstimatedExposureCost = summary.CoreDeficit > 0
            ? summary.CoreDeficit * 5500m // blended estimate
            : 0;

        return summary;
    }

    public async Task<List<LicenseComplianceViolation>> GetViolationsAsync(bool unresolvedOnly = true, string? sourceType = null)
    {
        var sql = @"
            SELECT v.Id, v.SqlServerInventoryId, v.ObjectId, v.ViolationType, v.Severity,
                   v.Title, v.Detail, v.IsResolved, v.ResolvedAt, v.ResolvedBy, v.ResolutionNote,
                   v.DetectedAt, v.CertificationCampaignId, v.SourceType, v.LicensePoolId,
                   s.ServerName, lp.FriendlyName AS PoolName
            FROM LicenseComplianceViolations v
            LEFT JOIN SqlServerInventory s ON s.Id = v.SqlServerInventoryId
            LEFT JOIN LicensePools lp ON lp.Id = v.LicensePoolId
            WHERE 1=1";
        if (unresolvedOnly) sql += " AND v.IsResolved = 0";
        if (sourceType != null) sql += " AND v.SourceType = @SourceType";
        sql += " ORDER BY v.DetectedAt DESC;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<LicenseComplianceViolation>(sql, new { SourceType = sourceType });
        return rows.ToList();
    }

    public async Task<Guid> CreateViolationAsync(LicenseComplianceViolation violation)
    {
        const string sql = @"
            INSERT INTO LicenseComplianceViolations
                (Id, SqlServerInventoryId, ObjectId, ViolationType, Severity,
                 Title, Detail, IsResolved, DetectedAt, CertificationCampaignId,
                 SourceType, LicensePoolId)
            VALUES
                (@Id, @SqlServerInventoryId, @ObjectId, @ViolationType, @Severity,
                 @Title, @Detail, 0, @DetectedAt, @CertificationCampaignId,
                 @SourceType, @LicensePoolId);";

        if (violation.Id == Guid.Empty) violation.Id = Guid.NewGuid();
        if (violation.DetectedAt == default) violation.DetectedAt = DateTime.UtcNow;

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, violation);
        _logger.LogWarning("SqlLicenseRepository.CreateViolationAsync: [{Source}] {ViolationType} violation: {Title}",
            violation.SourceType ?? "SQL", violation.ViolationType, violation.Title);
        return violation.Id;
    }

    public async Task ResolveViolationAsync(Guid id, string resolvedBy, string? note = null)
    {
        const string sql = @"
            UPDATE LicenseComplianceViolations
            SET IsResolved     = 1,
                ResolvedAt     = GETUTCDATE(),
                ResolvedBy     = @ResolvedBy,
                ResolutionNote = @Note
            WHERE Id = @Id;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { Id = id, ResolvedBy = resolvedBy, Note = note });
        _logger.LogInformation("SqlLicenseRepository.ResolveViolationAsync: Resolved violation {Id} by {ResolvedBy}",
            id, resolvedBy);
    }

    public async Task LinkViolationToCertificationAsync(Guid violationId, Guid campaignId)
    {
        const string sql = @"
            UPDATE LicenseComplianceViolations
            SET CertificationCampaignId = @CampaignId
            WHERE Id = @ViolationId;";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { ViolationId = violationId, CampaignId = campaignId });
    }

    // ── SQL Server Permissions ──────────────────────────────────────────────

    public async Task DeactivateServerPermissionsAsync(Guid serverId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE SqlServerPermissions SET IsActive = 0 WHERE SqlServerInventoryId = @ServerId",
            new { ServerId = serverId });
    }

    public async Task<(int inserted, int adMatched)> UpsertPermissionsAsync(Guid serverId, List<SqlServerPermission> permissions)
    {
        if (!permissions.Any()) return (0, 0);

        using var conn = CreateConnection();
        await conn.OpenAsync();

        // Build a lookup of AD Objects by Username for matching.
        // Username can be duplicated across connections (AD + Entra). Prefer ActiveDirectory, then first match.
        var adObjects = await conn.QueryAsync<(Guid Id, string Username, string? SourceType)>(
            @"SELECT Id, Username, SourceType FROM Objects
              WHERE DeletedAt IS NULL AND IsActive = 1 AND ObjectClass = 'user'
                AND Username IS NOT NULL AND LEN(Username) > 0");
        var adByUsername = adObjects
            .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.SourceType == "ActiveDirectory" ? 0 : 1).First().Id,
                StringComparer.OrdinalIgnoreCase);

        int inserted = 0;
        int adMatched = 0;

        foreach (var perm in permissions)
        {
            // Try to match Windows principals to AD Objects
            if (perm.ObjectId == null && (perm.PrincipalType == "WindowsLogin" || perm.PrincipalType == "WindowsGroup"))
            {
                // Extract sAMAccountName from DOMAIN\username
                var parts = perm.PrincipalName.Split('\\');
                var sam = parts.Length > 1 ? parts[1] : perm.PrincipalName;

                if (adByUsername.TryGetValue(sam, out var objectId))
                {
                    perm.ObjectId = objectId;
                    perm.MatchMethod = "Username";
                    adMatched++;
                }
            }

            // Upsert: match on server + principal + scope + database + permission
            var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                @"SELECT Id FROM SqlServerPermissions
                  WHERE SqlServerInventoryId = @SqlServerInventoryId
                    AND PrincipalName = @PrincipalName
                    AND PermissionScope = @PermissionScope
                    AND ISNULL(DatabaseName, '') = ISNULL(@DatabaseName, '')
                    AND PermissionName = @PermissionName",
                perm);

            if (existingId.HasValue)
            {
                perm.Id = existingId.Value;
                await conn.ExecuteAsync(
                    @"UPDATE SqlServerPermissions SET
                        PrincipalType = @PrincipalType, PrincipalSid = @PrincipalSid,
                        PermissionClass = @PermissionClass, GrantState = @GrantState,
                        ObjectId = @ObjectId, MatchMethod = @MatchMethod,
                        IsPrivileged = @IsPrivileged, RiskLevel = @RiskLevel,
                        LastSeenAt = @LastSeenAt, IsActive = 1, SourceAgentId = @SourceAgentId
                      WHERE Id = @Id", perm);
            }
            else
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO SqlServerPermissions
                        (Id, SqlServerInventoryId, PrincipalName, PrincipalType, PrincipalSid,
                         PermissionScope, DatabaseName, PermissionName, PermissionClass, GrantState,
                         ObjectId, MatchMethod, IsPrivileged, RiskLevel,
                         DiscoveredAt, LastSeenAt, IsActive, SourceAgentId)
                      VALUES
                        (@Id, @SqlServerInventoryId, @PrincipalName, @PrincipalType, @PrincipalSid,
                         @PermissionScope, @DatabaseName, @PermissionName, @PermissionClass, @GrantState,
                         @ObjectId, @MatchMethod, @IsPrivileged, @RiskLevel,
                         @DiscoveredAt, @LastSeenAt, @IsActive, @SourceAgentId)", perm);
                inserted++;
            }
        }

        return (inserted, adMatched);
    }

    public async Task<List<SqlServerPermission>> GetServerPermissionsAsync(Guid serverId, bool activeOnly = true)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        var sql = "SELECT * FROM SqlServerPermissions WHERE SqlServerInventoryId = @ServerId";
        if (activeOnly) sql += " AND IsActive = 1";
        sql += " ORDER BY IsPrivileged DESC, PermissionScope, DatabaseName, PrincipalName";
        return (await conn.QueryAsync<SqlServerPermission>(sql, new { ServerId = serverId })).ToList();
    }

    public async Task<List<SqlServerPermission>> GetPermissionsForObjectAsync(Guid objectId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<SqlServerPermission>(
            "SELECT * FROM SqlServerPermissions WHERE ObjectId = @ObjectId AND IsActive = 1 ORDER BY IsPrivileged DESC",
            new { ObjectId = objectId })).ToList();
    }

    public async Task<List<SqlServerPermission>> GetPrivilegedPermissionsAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<SqlServerPermission>(
            "SELECT * FROM SqlServerPermissions WHERE IsPrivileged = 1 AND IsActive = 1 ORDER BY RiskLevel, PrincipalName")).ToList();
    }
}
