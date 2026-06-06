namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for checking duplicate names across entity types.
/// </summary>
public interface IDuplicateNameRepository
{
    Task<bool> IsWorkflowNameDuplicateAsync(string name, Guid? excludeId = null);
    Task<bool> IsSyncProjectNameDuplicateAsync(string name, Guid? excludeId = null);
    Task<bool> IsPersonNameDuplicateAsync(string displayName, Guid? excludeId = null);
    Task<bool> IsTagNameDuplicateAsync(string name, Guid? excludeId = null);
    Task<bool> IsRoleNameDuplicateAsync(string name, string? excludeId = null);
    Task<bool> IsConnectionNameDuplicateAsync(string name, Guid? excludeId = null);
    Task<bool> IsNameDuplicateAsync(string tableName, string nameColumn, string name, Guid? excludeId = null);
}
