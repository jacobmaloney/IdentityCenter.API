using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository interface for Teams bot configuration operations
/// </summary>
public interface ITeamsBotRepository
{
    /// <summary>
    /// Get the active Teams bot configuration
    /// </summary>
    Task<TeamsBotConfiguration?> GetActiveConfigurationAsync();

    /// <summary>
    /// Get Teams bot configuration by ID
    /// </summary>
    Task<TeamsBotConfiguration?> GetByIdAsync(Guid id);

    /// <summary>
    /// Get all Teams bot configurations
    /// </summary>
    Task<List<TeamsBotConfiguration>> GetAllAsync();

    /// <summary>
    /// Create a new Teams bot configuration
    /// </summary>
    Task<Guid> CreateAsync(TeamsBotConfiguration configuration, string? createdBy = null);

    /// <summary>
    /// Update an existing Teams bot configuration
    /// </summary>
    Task<bool> UpdateAsync(TeamsBotConfiguration configuration, string? modifiedBy = null);

    /// <summary>
    /// Delete a Teams bot configuration
    /// </summary>
    Task<bool> DeleteAsync(Guid id);

    /// <summary>
    /// Activate a specific configuration (deactivates all others)
    /// </summary>
    Task<bool> ActivateConfigurationAsync(Guid id);

    /// <summary>
    /// Update test results for a configuration
    /// </summary>
    Task UpdateTestResultAsync(Guid id, bool success, string message);
}
