using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IAuditRepository
{
    Task<(List<AuditLog> Items, int TotalCount)> GetAuditLogsPagedAsync(
        string? level = null, string? category = null, string? userId = null,
        string? action = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        int page = 1, int pageSize = 50);

    Task<List<string>> GetAuditCategoriesAsync();

    Task<Dictionary<string, int>> GetAuditCountByLevelAsync();

    Task<(List<ChangeAuditLog> Items, int TotalCount)> GetChangeAuditLogsPagedAsync(
        string? entityType = null, Guid? entityId = null,
        DateTime? dateFrom = null, DateTime? dateTo = null,
        int page = 1, int pageSize = 50);

    Task<List<AuditLog>> GetAuditLogsByEntityAsync(string entityId, string? entityType = null, int? limit = null);

    Task<(int TotalLogs, int ErrorCount, int WarningCount, int TodayCount)> GetAuditStatsAsync();

    Task<(List<SyncAuditLog> Items, int TotalCount)> GetSyncAuditLogsPagedAsync(
        string? action = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        int page = 1, int pageSize = 50);

    Task<(List<ChangeAuditLog> Items, int TotalCount)> GetChangeAuditLogsFilteredPagedAsync(
        string? userId = null, string? action = null,
        DateTime? dateFrom = null, DateTime? dateTo = null,
        int page = 1, int pageSize = 50);
}
