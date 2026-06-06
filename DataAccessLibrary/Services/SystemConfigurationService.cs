using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service for managing system-wide configuration settings.
    /// Pure Dapper implementation - no EF Core.
    /// </summary>
    public class SystemConfigurationService
    {
        private readonly string _connectionString;
        private readonly ILogger<SystemConfigurationService> _logger;

        public SystemConfigurationService(
            IConfiguration configuration,
            ILogger<SystemConfigurationService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        /// <summary>
        /// Gets the system configuration (singleton record with Id=1)
        /// </summary>
        public async Task<SystemConfiguration> GetConfigurationAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var config = await connection.QueryFirstOrDefaultAsync<SystemConfiguration>(
                "SELECT * FROM SystemConfigurations WHERE Id = 1");

            if (config == null)
            {
                _logger.LogWarning("System configuration not found, creating default configuration");
                config = new SystemConfiguration { Id = 1 };

                // Insert the full singleton row using the C# model defaults so the
                // first-boot configuration carries correct, queryable values rather
                // than relying solely on DB column defaults. (Previously this listed
                // only 4 columns and threw on any NOT NULL column without a default.)
                await connection.ExecuteAsync(@"
                    INSERT INTO SystemConfigurations (
                        Id, AllowSelfRegistration, RequireEmailConfirmation, AllowExternalLogins,
                        MinimumPasswordLength, RequireDigit, RequireLowercase, RequireUppercase, RequireNonAlphanumeric,
                        MaxFailedAccessAttempts, LockoutDurationMinutes, SessionTimeoutMinutes, SlidingExpiration,
                        EnableAuditLogging, AuditRetentionDays, PortalUrl, PortalDisplayName,
                        EnablePolicyNotifications, EnableSyncNotifications, EnableEscalationNotifications,
                        ChatLlmEnabled, ChatLlmProvider, ChatLlmEndpoint, ChatLlmModel,
                        ChatLlmMaxTokens, ChatLlmTemperature, ChatLlmTimeoutSeconds,
                        CreatedAt, ModifiedAt, ModifiedBy)
                    VALUES (
                        @Id, @AllowSelfRegistration, @RequireEmailConfirmation, @AllowExternalLogins,
                        @MinimumPasswordLength, @RequireDigit, @RequireLowercase, @RequireUppercase, @RequireNonAlphanumeric,
                        @MaxFailedAccessAttempts, @LockoutDurationMinutes, @SessionTimeoutMinutes, @SlidingExpiration,
                        @EnableAuditLogging, @AuditRetentionDays, @PortalUrl, @PortalDisplayName,
                        @EnablePolicyNotifications, @EnableSyncNotifications, @EnableEscalationNotifications,
                        @ChatLlmEnabled, @ChatLlmProvider, @ChatLlmEndpoint, @ChatLlmModel,
                        @ChatLlmMaxTokens, @ChatLlmTemperature, @ChatLlmTimeoutSeconds,
                        @CreatedAt, @ModifiedAt, @ModifiedBy)",
                    new
                    {
                        config.Id,
                        config.AllowSelfRegistration,
                        config.RequireEmailConfirmation,
                        config.AllowExternalLogins,
                        config.MinimumPasswordLength,
                        config.RequireDigit,
                        config.RequireLowercase,
                        config.RequireUppercase,
                        config.RequireNonAlphanumeric,
                        config.MaxFailedAccessAttempts,
                        config.LockoutDurationMinutes,
                        config.SessionTimeoutMinutes,
                        config.SlidingExpiration,
                        config.EnableAuditLogging,
                        config.AuditRetentionDays,
                        config.PortalUrl,
                        config.PortalDisplayName,
                        config.EnablePolicyNotifications,
                        config.EnableSyncNotifications,
                        config.EnableEscalationNotifications,
                        config.ChatLlmEnabled,
                        config.ChatLlmProvider,
                        config.ChatLlmEndpoint,
                        config.ChatLlmModel,
                        config.ChatLlmMaxTokens,
                        config.ChatLlmTemperature,
                        config.ChatLlmTimeoutSeconds,
                        CreatedAt = DateTime.UtcNow,
                        ModifiedAt = DateTime.UtcNow,
                        ModifiedBy = "System"
                    });
            }

            return config;
        }

        /// <summary>
        /// Updates the system configuration
        /// </summary>
        public async Task<SystemConfiguration> UpdateConfigurationAsync(SystemConfiguration configuration, string modifiedBy)
        {
            configuration.ModifiedAt = DateTime.UtcNow;
            configuration.ModifiedBy = modifiedBy;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
                UPDATE SystemConfigurations
                SET AllowSelfRegistration = @AllowSelfRegistration,
                    ChatLlmEnabled = @ChatLlmEnabled,
                    ChatLlmProvider = @ChatLlmProvider,
                    ChatLlmEndpoint = @ChatLlmEndpoint,
                    ChatLlmApiKey = @ChatLlmApiKey,
                    ChatLlmModel = @ChatLlmModel,
                    ChatLlmMaxTokens = @ChatLlmMaxTokens,
                    ChatLlmTemperature = @ChatLlmTemperature,
                    ChatLlmTimeoutSeconds = @ChatLlmTimeoutSeconds,
                    ModifiedAt = @ModifiedAt,
                    ModifiedBy = @ModifiedBy
                WHERE Id = @Id",
                new
                {
                    configuration.Id,
                    configuration.AllowSelfRegistration,
                    configuration.ChatLlmEnabled,
                    configuration.ChatLlmProvider,
                    configuration.ChatLlmEndpoint,
                    configuration.ChatLlmApiKey,
                    configuration.ChatLlmModel,
                    configuration.ChatLlmMaxTokens,
                    configuration.ChatLlmTemperature,
                    configuration.ChatLlmTimeoutSeconds,
                    configuration.ModifiedAt,
                    configuration.ModifiedBy
                });

            _logger.LogInformation("System configuration updated by {ModifiedBy}", modifiedBy);
            return configuration;
        }

        /// <summary>
        /// Gets the AllowSelfRegistration setting specifically
        /// </summary>
        public async Task<bool> GetAllowSelfRegistrationAsync()
        {
            var config = await GetConfigurationAsync();
            return config.AllowSelfRegistration;
        }

        /// <summary>
        /// Updates the AllowSelfRegistration setting specifically
        /// </summary>
        public async Task SetAllowSelfRegistrationAsync(bool allow, string modifiedBy)
        {
            var config = await GetConfigurationAsync();
            config.AllowSelfRegistration = allow;
            await UpdateConfigurationAsync(config, modifiedBy);

            _logger.LogInformation("Self-registration {Status} by {ModifiedBy}",
                allow ? "enabled" : "disabled", modifiedBy);
        }
    }
}
