using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Manages named credential profiles for SQL Server scanning.
/// Profiles hold encrypted credentials that can be shared across multiple servers.
/// </summary>
public interface ISqlCredentialRepository
{
    Task<List<SqlServerCredential>> GetAllAsync(bool includeInactive = false);
    Task<SqlServerCredential?> GetByIdAsync(Guid id);
    Task<SqlServerCredential?> GetDefaultAsync();
    Task<Guid> CreateAsync(SqlServerCredential credential);
    Task UpdateAsync(SqlServerCredential credential);
    Task DeleteAsync(Guid id);
    Task SetDefaultAsync(Guid id);
    Task MarkUsedAsync(Guid id);

    /// <summary>Returns the count of SqlServerInventory rows using this credential.</summary>
    Task<int> GetUsageCountAsync(Guid id);
}
