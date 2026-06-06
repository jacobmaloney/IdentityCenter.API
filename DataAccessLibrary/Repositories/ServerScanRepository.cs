using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class ServerScanRepository : IServerScanRepository
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public ServerScanRepository(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    // ── Local Users ─────────────────────────────────────────────────────────

    public async Task<List<ServerLocalUser>> GetLocalUsersAsync(Guid serverId, bool activeOnly = true)
    {
        var sql = @"
            SELECT u.*, o.DisplayName AS ObjectDisplayName
            FROM ServerLocalUsers u
            LEFT JOIN Objects o ON o.Id = u.ObjectId AND o.DeletedAt IS NULL
            WHERE u.SqlServerInventoryId = @ServerId"
            + (activeOnly ? " AND u.IsActive = 1" : "")
            + " ORDER BY u.IsLocalAdmin DESC, u.AccountName";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<ServerLocalUser>(sql, new { ServerId = serverId })).ToList();
    }

    public async Task<List<ServerLocalUser>> GetLocalAdminsAsync(Guid serverId)
    {
        const string sql = @"
            SELECT u.*, o.DisplayName AS ObjectDisplayName
            FROM ServerLocalUsers u
            LEFT JOIN Objects o ON o.Id = u.ObjectId AND o.DeletedAt IS NULL
            WHERE u.SqlServerInventoryId = @ServerId
              AND u.IsLocalAdmin = 1
              AND u.IsActive = 1
            ORDER BY u.AccountType, u.AccountName";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<ServerLocalUser>(sql, new { ServerId = serverId })).ToList();
    }

    public async Task DeactivateLocalUsersAsync(Guid serverId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE ServerLocalUsers SET IsActive = 0 WHERE SqlServerInventoryId = @ServerId",
            new { ServerId = serverId });
    }

    public async Task<(int inserted, int adMatched)> UpsertLocalUsersAsync(Guid serverId, List<ServerLocalUser> users)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        // Deactivate old entries first
        await conn.ExecuteAsync(
            "UPDATE ServerLocalUsers SET IsActive = 0 WHERE SqlServerInventoryId = @ServerId",
            new { ServerId = serverId });

        int inserted = 0;
        int adMatched = 0;

        foreach (var user in users)
        {
            user.SqlServerInventoryId = serverId;
            user.LastSeenAt = DateTime.UtcNow;
            user.IsActive = true;

            // Try AD matching via SID
            if (!string.IsNullOrEmpty(user.SID))
            {
                var matchedObjectId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                    "SELECT TOP 1 Id FROM Objects WHERE ObjectSid = @SID AND DeletedAt IS NULL",
                    new { user.SID });

                if (matchedObjectId.HasValue)
                {
                    user.ObjectId = matchedObjectId.Value;
                    user.MatchMethod = "SID";
                    adMatched++;
                }
            }

            // Fallback: match by sAMAccountName for domain accounts
            if (user.ObjectId == null && user.AccountType != "LocalUser" && user.AccountName.Contains('\\'))
            {
                var samName = user.AccountName.Split('\\').Last();
                var matchedObjectId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                    "SELECT TOP 1 Id FROM Objects WHERE Username = @SAM AND DeletedAt IS NULL",
                    new { SAM = samName });

                if (matchedObjectId.HasValue)
                {
                    user.ObjectId = matchedObjectId.Value;
                    user.MatchMethod = "SAMAccountName";
                    adMatched++;
                }
            }

            // Check for existing row to update vs insert
            var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                @"SELECT Id FROM ServerLocalUsers
                  WHERE SqlServerInventoryId = @ServerId AND AccountName = @AccountName AND ISNULL(GroupName,'') = ISNULL(@GroupName,'')
                  ORDER BY DiscoveredAt",
                new { ServerId = serverId, user.AccountName, user.GroupName });

            if (existingId.HasValue)
            {
                await conn.ExecuteAsync(@"
                    UPDATE ServerLocalUsers
                    SET IsActive = 1, LastSeenAt = GETUTCDATE(), IsLocalAdmin = @IsLocalAdmin,
                        IsDisabled = @IsDisabled, AccountType = @AccountType, SID = @SID,
                        ObjectId = @ObjectId, MatchMethod = @MatchMethod
                    WHERE Id = @Id",
                    new { Id = existingId.Value, user.IsLocalAdmin, user.IsDisabled,
                          user.AccountType, user.SID, user.ObjectId, user.MatchMethod });
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO ServerLocalUsers
                        (Id, SqlServerInventoryId, AccountName, AccountType, GroupName,
                         IsLocalAdmin, IsDisabled, SID, ObjectId, MatchMethod,
                         DiscoveredAt, LastSeenAt, IsActive)
                    VALUES
                        (@Id, @SqlServerInventoryId, @AccountName, @AccountType, @GroupName,
                         @IsLocalAdmin, @IsDisabled, @SID, @ObjectId, @MatchMethod,
                         GETUTCDATE(), GETUTCDATE(), 1)", user);
                inserted++;
            }
        }

        _logger.LogInformation("ServerScanRepository.UpsertLocalUsersAsync: Server {ServerId} — {Inserted} inserted, {Matched} AD-matched",
            serverId, inserted, adMatched);
        return (inserted, adMatched);
    }

    // ── Installed Products ──────────────────────────────────────────────────

    public async Task<List<ServerInstalledProduct>> GetInstalledProductsAsync(Guid serverId, bool activeOnly = true)
    {
        var sql = @"
            SELECT * FROM ServerInstalledProducts
            WHERE SqlServerInventoryId = @ServerId"
            + (activeOnly ? " AND IsActive = 1" : "")
            + " ORDER BY ProductCategory, ProductName";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<ServerInstalledProduct>(sql, new { ServerId = serverId })).ToList();
    }

    public async Task<List<ServerInstalledProduct>> GetProductsByCategoryAsync(Guid serverId, string category)
    {
        const string sql = @"
            SELECT * FROM ServerInstalledProducts
            WHERE SqlServerInventoryId = @ServerId
              AND ProductCategory = @Category
              AND IsActive = 1
            ORDER BY ProductName";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<ServerInstalledProduct>(sql, new { ServerId = serverId, Category = category })).ToList();
    }

    public async Task DeactivateInstalledProductsAsync(Guid serverId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE ServerInstalledProducts SET IsActive = 0 WHERE SqlServerInventoryId = @ServerId",
            new { ServerId = serverId });
    }

    public async Task<int> UpsertInstalledProductsAsync(Guid serverId, List<ServerInstalledProduct> products)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        // Deactivate old entries
        await conn.ExecuteAsync(
            "UPDATE ServerInstalledProducts SET IsActive = 0 WHERE SqlServerInventoryId = @ServerId",
            new { ServerId = serverId });

        int inserted = 0;

        foreach (var product in products)
        {
            product.SqlServerInventoryId = serverId;
            product.LastSeenAt = DateTime.UtcNow;
            product.IsActive = true;

            // Check for existing by name+version
            var existingId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                @"SELECT Id FROM ServerInstalledProducts
                  WHERE SqlServerInventoryId = @ServerId AND ProductName = @ProductName
                    AND ISNULL(ProductVersion,'') = ISNULL(@ProductVersion,'')",
                new { ServerId = serverId, product.ProductName, product.ProductVersion });

            if (existingId.HasValue)
            {
                await conn.ExecuteAsync(@"
                    UPDATE ServerInstalledProducts
                    SET IsActive = 1, LastSeenAt = GETUTCDATE(),
                        ProductEdition = @ProductEdition, ProductCategory = @ProductCategory,
                        LicenseKey = @LicenseKey, InstallPath = @InstallPath, Publisher = @Publisher,
                        IsLicensable = @IsLicensable
                    WHERE Id = @Id",
                    new { Id = existingId.Value, product.ProductEdition, product.ProductCategory,
                          product.LicenseKey, product.InstallPath, product.Publisher, product.IsLicensable });
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO ServerInstalledProducts
                        (Id, SqlServerInventoryId, ProductName, ProductVersion, ProductEdition,
                         ProductCategory, LicenseKey, InstallDate, InstallPath, Publisher,
                         IsLicensable, DiscoveredAt, LastSeenAt, IsActive)
                    VALUES
                        (@Id, @SqlServerInventoryId, @ProductName, @ProductVersion, @ProductEdition,
                         @ProductCategory, @LicenseKey, @InstallDate, @InstallPath, @Publisher,
                         @IsLicensable, GETUTCDATE(), GETUTCDATE(), 1)", product);
                inserted++;
            }
        }

        _logger.LogInformation("ServerScanRepository.UpsertInstalledProductsAsync: Server {ServerId} — {Inserted} products inserted",
            serverId, inserted);
        return inserted;
    }

    // ── WinRM Scan Status ───────────────────────────────────────────────────

    public async Task UpdateWinRmScanStatusAsync(Guid serverId, string status, string? message, int? durationMs)
    {
        const string sql = @"
            UPDATE SqlServerInventory
            SET LastWinRmScanStatus = @Status,
                LastWinRmScanMessage = @Message,
                LastWinRmScanAt = GETUTCDATE(),
                LastWinRmScanDurationMs = @DurationMs
            WHERE Id = @ServerId";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql, new { ServerId = serverId, Status = status, Message = message, DurationMs = durationMs });
    }
}
