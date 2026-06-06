using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IConfigurationRepository
{
    // System Configuration
    Task<SystemConfiguration?> GetSystemConfigurationAsync();
    Task UpsertSystemConfigurationAsync(SystemConfiguration config);

    // Settings
    Task<List<Setting>> GetSettingsAsync();
    Task<Setting?> GetSettingAsync(string key);
    Task<Setting?> GetSettingByCategoryAndKeyAsync(string category, string key);
    Task UpsertSettingAsync(string category, string key, string value, string? dataType = null, bool isEncrypted = false);

    // Maintenance Settings
    Task<MaintenanceSettings?> GetMaintenanceSettingsAsync();
    Task UpsertMaintenanceSettingsAsync(MaintenanceSettings settings);
}
