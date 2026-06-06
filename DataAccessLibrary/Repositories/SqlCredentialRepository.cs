using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class SqlCredentialRepository : ISqlCredentialRepository
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public SqlCredentialRepository(IConfiguration config, IGlobalLogger logger)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing");
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public async Task<List<SqlServerCredential>> GetAllAsync(bool includeInactive = false)
    {
        var sql = includeInactive
            ? "SELECT * FROM SqlServerCredentials ORDER BY IsDefault DESC, Name"
            : "SELECT * FROM SqlServerCredentials WHERE IsActive = 1 ORDER BY IsDefault DESC, Name";

        using var conn = CreateConnection();
        await conn.OpenAsync();
        return (await conn.QueryAsync<SqlServerCredential>(sql)).ToList();
    }

    public async Task<SqlServerCredential?> GetByIdAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<SqlServerCredential>(
            "SELECT * FROM SqlServerCredentials WHERE Id = @Id", new { Id = id });
    }

    public async Task<SqlServerCredential?> GetDefaultAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<SqlServerCredential>(
            "SELECT TOP 1 * FROM SqlServerCredentials WHERE IsDefault = 1 AND IsActive = 1 ORDER BY CreatedAt DESC");
    }

    public async Task<Guid> CreateAsync(SqlServerCredential credential)
    {
        if (credential.Id == Guid.Empty) credential.Id = Guid.NewGuid();
        credential.CreatedAt = DateTime.UtcNow;

        using var conn = CreateConnection();
        await conn.OpenAsync();

        // If this is set as default, clear other defaults first
        if (credential.IsDefault)
        {
            await conn.ExecuteAsync("UPDATE SqlServerCredentials SET IsDefault = 0 WHERE IsDefault = 1");
        }

        await conn.ExecuteAsync(@"
            INSERT INTO SqlServerCredentials
                (Id, Name, Description, AuthType, Username, EncryptedPassword, IsDefault, IsActive, CreatedAt, CreatedBy)
            VALUES
                (@Id, @Name, @Description, @AuthType, @Username, @EncryptedPassword, @IsDefault, @IsActive, @CreatedAt, @CreatedBy)",
            credential);

        _logger.LogInformation("SqlCredentialRepository: created credential {Name} ({Id})", credential.Name, credential.Id);
        return credential.Id;
    }

    public async Task UpdateAsync(SqlServerCredential credential)
    {
        credential.ModifiedAt = DateTime.UtcNow;

        using var conn = CreateConnection();
        await conn.OpenAsync();

        if (credential.IsDefault)
        {
            await conn.ExecuteAsync(
                "UPDATE SqlServerCredentials SET IsDefault = 0 WHERE IsDefault = 1 AND Id != @Id",
                new { credential.Id });
        }

        await conn.ExecuteAsync(@"
            UPDATE SqlServerCredentials SET
                Name = @Name,
                Description = @Description,
                AuthType = @AuthType,
                Username = @Username,
                EncryptedPassword = COALESCE(@EncryptedPassword, EncryptedPassword),
                IsDefault = @IsDefault,
                IsActive = @IsActive,
                ModifiedAt = @ModifiedAt,
                ModifiedBy = @ModifiedBy
            WHERE Id = @Id", credential);
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();

        // Unlink from inventory rows first
        await conn.ExecuteAsync(
            "UPDATE SqlServerInventory SET CredentialId = NULL WHERE CredentialId = @Id",
            new { Id = id });

        await conn.ExecuteAsync(
            "DELETE FROM SqlServerCredentials WHERE Id = @Id",
            new { Id = id });
    }

    public async Task SetDefaultAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE SqlServerCredentials SET IsDefault = 0 WHERE IsDefault = 1");
        await conn.ExecuteAsync("UPDATE SqlServerCredentials SET IsDefault = 1 WHERE Id = @Id", new { Id = id });
    }

    public async Task MarkUsedAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE SqlServerCredentials SET LastUsedAt = @Now WHERE Id = @Id",
            new { Id = id, Now = DateTime.UtcNow });
    }

    public async Task<int> GetUsageCountAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SqlServerInventory WHERE CredentialId = @Id",
            new { Id = id });
    }
}
