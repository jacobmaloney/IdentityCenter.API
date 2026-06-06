using DataAccessLibrary.Models;
using Common.Encryption;
using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// High-performance Dapper repository for Teams bot configuration
/// Uses encrypted storage for sensitive credentials
/// </summary>
public class TeamsBotRepository : ITeamsBotRepository
{
    private readonly string _connectionString;
    private readonly IEncryptionService _encryptionService;
    private readonly IGlobalLogger _logger;

    public TeamsBotRepository(
        IConfiguration configuration,
        IEncryptionService encryptionService,
        IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<TeamsBotConfiguration?> GetActiveConfigurationAsync()
    {
        _logger.LogMethodEntry(nameof(GetActiveConfigurationAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var config = await connection.QueryFirstOrDefaultAsync<TeamsBotConfiguration>(
                "usp_GetActiveTeamsBotConfiguration",
                commandType: System.Data.CommandType.StoredProcedure);

            if (config != null)
            {
                // Decrypt password
                config.AppPassword = await _encryptionService.DecryptAsync(config.AppPassword);
                _logger.LogInformation("Retrieved active Teams bot configuration: {BotName}", config.BotName);
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetActiveConfigurationAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetActiveConfigurationAsync));
        }
    }

    public async Task<TeamsBotConfiguration?> GetByIdAsync(Guid id)
    {
        _logger.LogMethodEntry(nameof(GetByIdAsync), new { id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "SELECT * FROM TeamsBotConfigurations WHERE Id = @Id";
            var config = await connection.QueryFirstOrDefaultAsync<TeamsBotConfiguration>(sql, new { Id = id });

            if (config != null)
            {
                // Decrypt password
                config.AppPassword = await _encryptionService.DecryptAsync(config.AppPassword);
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

    public async Task<List<TeamsBotConfiguration>> GetAllAsync()
    {
        _logger.LogMethodEntry(nameof(GetAllAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT * FROM TeamsBotConfigurations
                ORDER BY IsActive DESC, CreatedAt DESC";

            var configs = (await connection.QueryAsync<TeamsBotConfiguration>(sql)).ToList();

            // Decrypt passwords
            foreach (var config in configs)
            {
                config.AppPassword = await _encryptionService.DecryptAsync(config.AppPassword);
            }

            _logger.LogInformation("Retrieved {Count} Teams bot configurations", configs.Count);
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

    public async Task<Guid> CreateAsync(TeamsBotConfiguration configuration, string? createdBy = null)
    {
        _logger.LogMethodEntry(nameof(CreateAsync), new { configuration.BotName });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Encrypt password before storing
            var encryptedPassword = await _encryptionService.EncryptAsync(configuration.AppPassword);

            var parameters = new
            {
                Id = configuration.Id == Guid.Empty ? Guid.NewGuid() : configuration.Id,
                configuration.TenantId,
                configuration.AppId,
                AppPassword = encryptedPassword,
                configuration.MessagingEndpoint,
                configuration.BotName,
                configuration.ShortDescription,
                configuration.FullDescription,
                configuration.AccentColor,
                configuration.DeveloperName,
                configuration.WebsiteUrl,
                configuration.PrivacyUrl,
                configuration.TermsOfUseUrl,
                configuration.IsActive,
                CreatedBy = createdBy
            };

            var result = await connection.QuerySingleAsync<Guid>(
                "usp_InsertTeamsBotConfiguration",
                parameters,
                commandType: System.Data.CommandType.StoredProcedure);

            _logger.LogInformation("Created Teams bot configuration: {BotName}, Id: {Id}",
                configuration.BotName, result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreateAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreateAsync));
        }
    }

    public async Task<bool> UpdateAsync(TeamsBotConfiguration configuration, string? modifiedBy = null)
    {
        _logger.LogMethodEntry(nameof(UpdateAsync), new { configuration.Id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Encrypt password before storing
            var encryptedPassword = await _encryptionService.EncryptAsync(configuration.AppPassword);

            const string sql = @"
                UPDATE TeamsBotConfigurations
                SET TenantId = @TenantId,
                    AppId = @AppId,
                    AppPassword = @AppPassword,
                    MessagingEndpoint = @MessagingEndpoint,
                    BotName = @BotName,
                    ShortDescription = @ShortDescription,
                    FullDescription = @FullDescription,
                    AccentColor = @AccentColor,
                    DeveloperName = @DeveloperName,
                    WebsiteUrl = @WebsiteUrl,
                    PrivacyUrl = @PrivacyUrl,
                    TermsOfUseUrl = @TermsOfUseUrl,
                    IsActive = @IsActive,
                    ModifiedAt = GETUTCDATE(),
                    ModifiedBy = @ModifiedBy
                WHERE Id = @Id";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                configuration.Id,
                configuration.TenantId,
                configuration.AppId,
                AppPassword = encryptedPassword,
                configuration.MessagingEndpoint,
                configuration.BotName,
                configuration.ShortDescription,
                configuration.FullDescription,
                configuration.AccentColor,
                configuration.DeveloperName,
                configuration.WebsiteUrl,
                configuration.PrivacyUrl,
                configuration.TermsOfUseUrl,
                configuration.IsActive,
                ModifiedBy = modifiedBy
            });

            _logger.LogInformation("Updated Teams bot configuration: {Id}, Rows affected: {Count}",
                configuration.Id, rowsAffected);

            return rowsAffected > 0;
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

    public async Task<bool> DeleteAsync(Guid id)
    {
        _logger.LogMethodEntry(nameof(DeleteAsync), new { id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "DELETE FROM TeamsBotConfigurations WHERE Id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });

            _logger.LogInformation("Deleted Teams bot configuration: {Id}, Rows affected: {Count}",
                id, rowsAffected);

            return rowsAffected > 0;
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

    public async Task<bool> ActivateConfigurationAsync(Guid id)
    {
        _logger.LogMethodEntry(nameof(ActivateConfigurationAsync), new { id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();

            try
            {
                // Deactivate all configurations
                const string deactivateSql = "UPDATE TeamsBotConfigurations SET IsActive = 0, ModifiedAt = GETUTCDATE()";
                await connection.ExecuteAsync(deactivateSql, transaction: transaction);

                // Activate the specified configuration
                const string activateSql = @"
                    UPDATE TeamsBotConfigurations
                    SET IsActive = 1, ModifiedAt = GETUTCDATE()
                    WHERE Id = @Id";
                var rowsAffected = await connection.ExecuteAsync(activateSql, new { Id = id }, transaction: transaction);

                transaction.Commit();

                _logger.LogInformation("Activated Teams bot configuration: {Id}", id);
                return rowsAffected > 0;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(ActivateConfigurationAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(ActivateConfigurationAsync));
        }
    }

    public async Task UpdateTestResultAsync(Guid id, bool success, string message)
    {
        _logger.LogMethodEntry(nameof(UpdateTestResultAsync), new { id, success });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await connection.ExecuteAsync(
                "usp_UpdateTeamsBotTestResult",
                new { Id = id, TestSuccess = success, TestResult = message },
                commandType: System.Data.CommandType.StoredProcedure);

            _logger.LogInformation("Updated test result for Teams bot configuration: {Id}, Success: {Success}",
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
}
