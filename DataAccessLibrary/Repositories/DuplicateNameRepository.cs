using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for checking duplicate names across entity types.
/// Prevents users from creating duplicate workflow names, sync project names, etc.
/// </summary>
public class DuplicateNameRepository : DapperRepositoryBase, IDuplicateNameRepository
{
    // Whitelist of allowed table/column combinations to prevent SQL injection
    private static readonly Dictionary<string, HashSet<string>> AllowedTableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ApprovalWorkflows"] = new(StringComparer.OrdinalIgnoreCase) { "Name" },
        ["SyncProjects"] = new(StringComparer.OrdinalIgnoreCase) { "Name" },
        ["Identities"] = new(StringComparer.OrdinalIgnoreCase) { "DisplayName" },
        ["Tags"] = new(StringComparer.OrdinalIgnoreCase) { "Name" },
        ["AspNetRoles"] = new(StringComparer.OrdinalIgnoreCase) { "Name" },
        ["DirectoryConnections"] = new(StringComparer.OrdinalIgnoreCase) { "Name" },
        ["ComplianceFrameworks"] = new(StringComparer.OrdinalIgnoreCase) { "Name" },
        ["CompliancePolicies"] = new(StringComparer.OrdinalIgnoreCase) { "Name" },
        ["Groups"] = new(StringComparer.OrdinalIgnoreCase) { "Name", "DisplayName" },
    };

    public DuplicateNameRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<bool> IsWorkflowNameDuplicateAsync(string name, Guid? excludeId = null)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = excludeId.HasValue
                ? "SELECT COUNT(1) FROM ApprovalWorkflows WHERE LOWER(Name) = LOWER(@Name) AND Id != @ExcludeId"
                : "SELECT COUNT(1) FROM ApprovalWorkflows WHERE LOWER(Name) = LOWER(@Name)";

            var count = await connection.ExecuteScalarAsync<int>(sql, new { Name = name, ExcludeId = excludeId });
            return count > 0;
        });
    }

    public async Task<bool> IsSyncProjectNameDuplicateAsync(string name, Guid? excludeId = null)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = excludeId.HasValue
                ? "SELECT COUNT(1) FROM SyncProjects WHERE LOWER(Name) = LOWER(@Name) AND Id != @ExcludeId"
                : "SELECT COUNT(1) FROM SyncProjects WHERE LOWER(Name) = LOWER(@Name)";

            var count = await connection.ExecuteScalarAsync<int>(sql, new { Name = name, ExcludeId = excludeId });
            return count > 0;
        });
    }

    public async Task<bool> IsPersonNameDuplicateAsync(string displayName, Guid? excludeId = null)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = excludeId.HasValue
                ? "SELECT COUNT(1) FROM Identities WHERE LOWER(DisplayName) = LOWER(@DisplayName) AND Id != @ExcludeId"
                : "SELECT COUNT(1) FROM Identities WHERE LOWER(DisplayName) = LOWER(@DisplayName)";

            var count = await connection.ExecuteScalarAsync<int>(sql, new { DisplayName = displayName, ExcludeId = excludeId });
            return count > 0;
        });
    }

    public async Task<bool> IsTagNameDuplicateAsync(string name, Guid? excludeId = null)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = excludeId.HasValue
                ? "SELECT COUNT(1) FROM Tags WHERE LOWER(Name) = LOWER(@Name) AND Id != @ExcludeId"
                : "SELECT COUNT(1) FROM Tags WHERE LOWER(Name) = LOWER(@Name)";

            var count = await connection.ExecuteScalarAsync<int>(sql, new { Name = name, ExcludeId = excludeId });
            return count > 0;
        });
    }

    public async Task<bool> IsRoleNameDuplicateAsync(string name, string? excludeId = null)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = !string.IsNullOrEmpty(excludeId)
                ? "SELECT COUNT(1) FROM AspNetRoles WHERE LOWER(Name) = LOWER(@Name) AND Id != @ExcludeId"
                : "SELECT COUNT(1) FROM AspNetRoles WHERE LOWER(Name) = LOWER(@Name)";

            var count = await connection.ExecuteScalarAsync<int>(sql, new { Name = name, ExcludeId = excludeId });
            return count > 0;
        });
    }

    public async Task<bool> IsConnectionNameDuplicateAsync(string name, Guid? excludeId = null)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = excludeId.HasValue
                ? "SELECT COUNT(1) FROM DirectoryConnections WHERE LOWER(Name) = LOWER(@Name) AND Id != @ExcludeId"
                : "SELECT COUNT(1) FROM DirectoryConnections WHERE LOWER(Name) = LOWER(@Name)";

            var count = await connection.ExecuteScalarAsync<int>(sql, new { Name = name, ExcludeId = excludeId });
            return count > 0;
        });
    }

    public async Task<bool> IsNameDuplicateAsync(string tableName, string nameColumn, string name, Guid? excludeId = null)
    {
        // Validate table/column against whitelist to prevent SQL injection
        if (!AllowedTableColumns.TryGetValue(tableName, out var allowedColumns))
            throw new ArgumentException($"Table '{tableName}' is not allowed for duplicate name checks.", nameof(tableName));

        if (!allowedColumns.Contains(nameColumn))
            throw new ArgumentException($"Column '{nameColumn}' is not allowed for table '{tableName}'.", nameof(nameColumn));

        return await ExecuteAsync(async connection =>
        {
            var sql = excludeId.HasValue
                ? $"SELECT COUNT(1) FROM [{tableName}] WHERE LOWER([{nameColumn}]) = LOWER(@Name) AND Id != @ExcludeId"
                : $"SELECT COUNT(1) FROM [{tableName}] WHERE LOWER([{nameColumn}]) = LOWER(@Name)";

            var count = await connection.ExecuteScalarAsync<int>(sql, new { Name = name, ExcludeId = excludeId });
            return count > 0;
        });
    }
}
