using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository interface for SMTP configuration management
/// Handles encrypted storage of email server credentials
/// </summary>
public interface ISMTPRepository
{
    /// <summary>
    /// Retrieves all SMTP configurations (credentials are decrypted)
    /// </summary>
    Task<List<SMTPConfiguration>> GetAllAsync();

    /// <summary>
    /// Retrieves the default active SMTP configuration
    /// </summary>
    Task<SMTPConfiguration?> GetDefaultAsync();

    /// <summary>
    /// Retrieves a specific SMTP configuration by ID
    /// </summary>
    Task<SMTPConfiguration?> GetByIdAsync(Guid id);

    /// <summary>
    /// Inserts a new SMTP configuration (credentials are encrypted before storage)
    /// </summary>
    Task<SMTPConfiguration> InsertAsync(SMTPConfiguration config);

    /// <summary>
    /// Updates an existing SMTP configuration (credentials are encrypted before storage)
    /// </summary>
    Task<SMTPConfiguration> UpdateAsync(SMTPConfiguration config);

    /// <summary>
    /// Deletes an SMTP configuration
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// Updates the last test result for an SMTP configuration
    /// </summary>
    Task UpdateTestResultAsync(Guid id, bool success, string result);
}
