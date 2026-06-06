using Dapper;
using DataAccessLibrary.Models;
using Common.Encryption;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// High-performance repository for SMTP configuration management
/// Encrypts Server, Username, and Password before database storage
/// Based on proven IdentityServer implementation pattern
/// </summary>
public class SMTPRepository : ISMTPRepository
{
    private readonly string _connectionString;
    private readonly IEncryptionService _encryptionService;
    private readonly IGlobalLogger _logger;

    public SMTPRepository(
        IConfiguration configuration,
        IEncryptionService encryptionService,
        IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<List<SMTPConfiguration>> GetAllAsync()
    {
        _logger.LogMethodEntry(nameof(GetAllAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT * FROM SMTPConfiguration
                ORDER BY IsDefault DESC, DisplayName";

            var configs = (await connection.QueryAsync<SMTPConfiguration>(sql).ConfigureAwait(false)).ToList();

            // Decrypt sensitive fields
            foreach (var config in configs)
            {
                await DecryptConfigurationAsync(config).ConfigureAwait(false);
            }

            _logger.LogInformation("Retrieved {Count} SMTP configurations", configs.Count);
            return configs;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetAllAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAllAsync));
        }
    }

    public async Task<SMTPConfiguration?> GetDefaultAsync()
    {
        _logger.LogMethodEntry(nameof(GetDefaultAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT TOP 1 * FROM SMTPConfiguration
                WHERE IsDefault = 1 AND IsActive = 1
                ORDER BY CreatedAt DESC";

            var config = await connection.QueryFirstOrDefaultAsync<SMTPConfiguration>(sql).ConfigureAwait(false);

            if (config != null)
            {
                await DecryptConfigurationAsync(config).ConfigureAwait(false);
                _logger.LogInformation("Retrieved default SMTP configuration: {DisplayName}", config.DisplayName);
            }
            else
            {
                _logger.LogWarning("No default SMTP configuration found");
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetDefaultAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetDefaultAsync));
        }
    }

    public async Task<SMTPConfiguration?> GetByIdAsync(Guid id)
    {
        _logger.LogMethodEntry(nameof(GetByIdAsync), new { id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "SELECT * FROM SMTPConfiguration WHERE Id = @Id";

            var config = await connection.QueryFirstOrDefaultAsync<SMTPConfiguration>(sql, new { Id = id }).ConfigureAwait(false);

            if (config != null)
            {
                await DecryptConfigurationAsync(config).ConfigureAwait(false);
                _logger.LogInformation("Retrieved SMTP configuration: {DisplayName}", config.DisplayName);
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetByIdAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetByIdAsync));
        }
    }

    public async Task<SMTPConfiguration> InsertAsync(SMTPConfiguration config)
    {
        _logger.LogMethodEntry(nameof(InsertAsync), new { config.DisplayName });

        try
        {
            // Encrypt sensitive fields before storage
            var encryptedServer = await _encryptionService.EncryptAsync(config.Server ?? "").ConfigureAwait(false);
            var encryptedUsername = await _encryptionService.EncryptAsync(config.Username ?? "").ConfigureAwait(false);
            var encryptedPassword = await _encryptionService.EncryptAsync(config.Password ?? "").ConfigureAwait(false);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // If this is set as default, unset other defaults
            if (config.IsDefault)
            {
                await connection.ExecuteAsync(
                    "UPDATE SMTPConfiguration SET IsDefault = 0 WHERE IsDefault = 1").ConfigureAwait(false);
            }

            const string sql = @"
                INSERT INTO SMTPConfiguration
                (Id, DisplayName, Description, IsDefault, IsActive,
                 Server, Port, EnableSsl, Username, Password,
                 FromAddress, FromDisplayName, ReplyToAddress, ReplyToDisplayName,
                 CreatedAt, CreatedBy)
                VALUES
                (@Id, @DisplayName, @Description, @IsDefault, @IsActive,
                 @Server, @Port, @EnableSsl, @Username, @Password,
                 @FromAddress, @FromDisplayName, @ReplyToAddress, @ReplyToDisplayName,
                 GETUTCDATE(), @CreatedBy);

                SELECT * FROM SMTPConfiguration WHERE Id = @Id";

            var insertedConfig = await connection.QueryFirstAsync<SMTPConfiguration>(sql, new
            {
                config.Id,
                config.DisplayName,
                config.Description,
                config.IsDefault,
                config.IsActive,
                Server = encryptedServer,
                config.Port,
                config.EnableSsl,
                Username = encryptedUsername,
                Password = encryptedPassword,
                config.FromAddress,
                config.FromDisplayName,
                config.ReplyToAddress,
                config.ReplyToDisplayName,
                config.CreatedBy
            }).ConfigureAwait(false);

            // Decrypt for return
            await DecryptConfigurationAsync(insertedConfig).ConfigureAwait(false);

            _logger.LogInformation("Inserted SMTP configuration: {DisplayName}", config.DisplayName);
            return insertedConfig;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(InsertAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(InsertAsync));
        }
    }

    public async Task<SMTPConfiguration> UpdateAsync(SMTPConfiguration config)
    {
        _logger.LogMethodEntry(nameof(UpdateAsync), new { config.Id, config.DisplayName });

        try
        {
            // Encrypt sensitive fields before storage
            var encryptedServer = await _encryptionService.EncryptAsync(config.Server ?? "").ConfigureAwait(false);
            var encryptedUsername = await _encryptionService.EncryptAsync(config.Username ?? "").ConfigureAwait(false);
            var encryptedPassword = await _encryptionService.EncryptAsync(config.Password ?? "").ConfigureAwait(false);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // If this is set as default, unset other defaults
            if (config.IsDefault)
            {
                await connection.ExecuteAsync(
                    "UPDATE SMTPConfiguration SET IsDefault = 0 WHERE IsDefault = 1 AND Id != @Id",
                    new { config.Id }).ConfigureAwait(false);
            }

            const string sql = @"
                UPDATE SMTPConfiguration
                SET DisplayName = @DisplayName,
                    Description = @Description,
                    IsDefault = @IsDefault,
                    IsActive = @IsActive,
                    Server = @Server,
                    Port = @Port,
                    EnableSsl = @EnableSsl,
                    Username = @Username,
                    Password = @Password,
                    FromAddress = @FromAddress,
                    FromDisplayName = @FromDisplayName,
                    ReplyToAddress = @ReplyToAddress,
                    ReplyToDisplayName = @ReplyToDisplayName,
                    ModifiedAt = GETUTCDATE(),
                    ModifiedBy = @ModifiedBy
                WHERE Id = @Id;

                SELECT * FROM SMTPConfiguration WHERE Id = @Id";

            var updatedConfig = await connection.QueryFirstAsync<SMTPConfiguration>(sql, new
            {
                config.Id,
                config.DisplayName,
                config.Description,
                config.IsDefault,
                config.IsActive,
                Server = encryptedServer,
                config.Port,
                config.EnableSsl,
                Username = encryptedUsername,
                Password = encryptedPassword,
                config.FromAddress,
                config.FromDisplayName,
                config.ReplyToAddress,
                config.ReplyToDisplayName,
                config.ModifiedBy
            }).ConfigureAwait(false);

            // Decrypt for return
            await DecryptConfigurationAsync(updatedConfig).ConfigureAwait(false);

            _logger.LogInformation("Updated SMTP configuration: {DisplayName}", config.DisplayName);
            return updatedConfig;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateAsync));
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        _logger.LogMethodEntry(nameof(DeleteAsync), new { id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "DELETE FROM SMTPConfiguration WHERE Id = @Id";

            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id }).ConfigureAwait(false);

            _logger.LogInformation("Deleted SMTP configuration {Id}, rows affected: {RowsAffected}",
                id, rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeleteAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeleteAsync));
        }
    }

    public async Task UpdateTestResultAsync(Guid id, bool success, string result)
    {
        _logger.LogMethodEntry(nameof(UpdateTestResultAsync), new { id, success });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                UPDATE SMTPConfiguration
                SET LastTestDate = GETUTCDATE(),
                    LastTestSuccess = @Success,
                    LastTestResult = @Result
                WHERE Id = @Id";

            await connection.ExecuteAsync(sql, new
            {
                Id = id,
                Success = success,
                Result = result
            }).ConfigureAwait(false);

            _logger.LogInformation("Updated test result for SMTP configuration {Id}: {Success}",
                id, success);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateTestResultAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateTestResultAsync));
        }
    }

    /// <summary>
    /// Decrypts sensitive fields in the configuration object
    /// </summary>
    private async Task DecryptConfigurationAsync(SMTPConfiguration config)
    {
        try
        {
            if (!string.IsNullOrEmpty(config.Server))
            {
                config.Server = await _encryptionService.DecryptAsync(config.Server).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(config.Username))
            {
                config.Username = await _encryptionService.DecryptAsync(config.Username).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(config.Password))
            {
                config.Password = await _encryptionService.DecryptAsync(config.Password).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt SMTP configuration {Id}", config.Id);
            throw;
        }
    }
}
