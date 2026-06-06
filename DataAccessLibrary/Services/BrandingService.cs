using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Manages white-label branding settings.
    /// Settings are persisted as JSON in the Settings table
    /// (Category = "Branding", Key = "BrandingJson") and cached in IMemoryCache.
    /// Registered as Singleton so the cache is shared across all requests.
    /// </summary>
    public class BrandingService : IBrandingService
    {
        private const string CacheKey = "BrandingSettings_v1";
        private const string SettingsCategory = "Branding";
        private const string SettingsKey = "BrandingJson";

        private readonly string _connectionString;
        private readonly IMemoryCache _cache;
        private readonly ILogger<BrandingService> _logger;

        public BrandingService(
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<BrandingService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _cache = cache;
            _logger = logger;
        }

        public async Task<BrandingSettings> GetBrandingAsync()
        {
            if (_cache.TryGetValue(CacheKey, out BrandingSettings? cached) && cached != null)
                return cached;

            var settings = await LoadFromDatabaseAsync();

            _cache.Set(CacheKey, settings, TimeSpan.FromHours(1));
            return settings;
        }

        public string ProductName
        {
            get
            {
                if (_cache.TryGetValue(CacheKey, out BrandingSettings? cached)
                    && !string.IsNullOrWhiteSpace(cached?.ProductName))
                {
                    return cached.ProductName;
                }
                // Cache cold (e.g., very first call before MainLayout hydration) — return the
                // app's default name. The next GetBrandingAsync call will populate the cache.
                return "Identity Center";
            }
        }

        public async Task SaveBrandingAsync(BrandingSettings settings)
        {
            var json = JsonSerializer.Serialize(settings);

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);

            await conn.ExecuteAsync(@"
                MERGE Settings AS target
                USING (SELECT @Category AS Category, @Key AS [Key]) AS source
                ON target.Category = source.Category AND target.[Key] = source.[Key]
                WHEN MATCHED THEN
                    UPDATE SET Value = @Value, DataType = 'json', IsEncrypted = 0, ModifiedAt = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT (Category, [Key], Value, DataType, IsEncrypted, ModifiedAt)
                    VALUES (@Category, @Key, @Value, 'json', 0, GETUTCDATE());",
                new { Category = SettingsCategory, Key = SettingsKey, Value = json })
                .ConfigureAwait(false);

            _cache.Remove(CacheKey);
            _logger.LogInformation("Branding settings saved and cache invalidated.");
        }

        public void InvalidateCache()
        {
            _cache.Remove(CacheKey);
        }

        private async Task<BrandingSettings> LoadFromDatabaseAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync().ConfigureAwait(false);

                var json = await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT Value FROM Settings WHERE Category = @Category AND [Key] = @Key",
                    new { Category = SettingsCategory, Key = SettingsKey })
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(json))
                {
                    var deserialized = JsonSerializer.Deserialize<BrandingSettings>(json);
                    if (deserialized != null)
                        return deserialized;
                }
            }
            catch (Exception ex)
            {
                // DB may not be ready yet (first-run scenario) — return defaults silently.
                _logger.LogWarning(ex, "Could not load branding settings from database; using defaults.");
            }

            return new BrandingSettings();
        }
    }
}
