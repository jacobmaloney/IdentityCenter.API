using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class AuditRepository : DapperRepositoryBase, IAuditRepository
{
    public AuditRepository(IConfiguration configuration, IGlobalLogger logger) : base(configuration, logger) { }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<(List<AuditLog> Items, int TotalCount)> GetAuditLogsPagedAsync(
        string? level = null, string? category = null, string? userId = null,
        string? action = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        using var conn = CreateConnection();
        var where = "WHERE 1=1";
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(level)) { where += " AND Level = @Level"; p.Add("Level", level); }
        if (!string.IsNullOrEmpty(category)) { where += " AND Category = @Category"; p.Add("Category", category); }
        if (!string.IsNullOrEmpty(userId)) { where += " AND UserId = @UserId"; p.Add("UserId", userId); }
        if (!string.IsNullOrEmpty(action)) { where += " AND Action LIKE @Action"; p.Add("Action", $"%{action}%"); }
        if (dateFrom.HasValue) { where += " AND Timestamp >= @DateFrom"; p.Add("DateFrom", dateFrom.Value); }
        if (dateTo.HasValue) { where += " AND Timestamp <= @DateTo"; p.Add("DateTo", dateTo.Value); }

        var countSql = $"SELECT COUNT(*) FROM AuditLogs {where}";
        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);
        var dataSql = $"SELECT * FROM AuditLogs {where} ORDER BY Timestamp DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        var items = (await conn.QueryAsync<AuditLog>(dataSql, p).ConfigureAwait(false)).ToList();

        return (items, totalCount);
    }

    public async Task<List<string>> GetAuditCategoriesAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<string>(
            "SELECT DISTINCT Category FROM AuditLogs WHERE Category IS NOT NULL ORDER BY Category").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<Dictionary<string, int>> GetAuditCountByLevelAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<(string Level, int Count)>(
            "SELECT Level, COUNT(*) as [Count] FROM AuditLogs GROUP BY Level").ConfigureAwait(false);
        return result.ToDictionary(x => x.Level, x => x.Count);
    }

    public async Task<(List<ChangeAuditLog> Items, int TotalCount)> GetChangeAuditLogsPagedAsync(
        string? entityType = null, Guid? entityId = null,
        DateTime? dateFrom = null, DateTime? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        using var conn = CreateConnection();
        var where = "WHERE 1=1";
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(entityType)) { where += " AND EntityType = @EntityType"; p.Add("EntityType", entityType); }
        if (entityId.HasValue) { where += " AND EntityId = @EntityId"; p.Add("EntityId", entityId.Value); }
        if (dateFrom.HasValue) { where += " AND Timestamp >= @DateFrom"; p.Add("DateFrom", dateFrom.Value); }
        if (dateTo.HasValue) { where += " AND Timestamp <= @DateTo"; p.Add("DateTo", dateTo.Value); }

        var countSql = $"SELECT COUNT(*) FROM ChangeAuditLogs {where}";
        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);
        var dataSql = $"SELECT * FROM ChangeAuditLogs {where} ORDER BY Timestamp DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        var items = (await conn.QueryAsync<ChangeAuditLog>(dataSql, p).ConfigureAwait(false)).ToList();

        return (items, totalCount);
    }

    public async Task<List<AuditLog>> GetAuditLogsByEntityAsync(string entityId, string? entityType = null, int? limit = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT TOP (@Limit) * FROM AuditLogs WHERE EntityId = @EntityId";
        if (!string.IsNullOrEmpty(entityType))
            sql += " AND EntityType = @EntityType";
        sql += " ORDER BY Timestamp DESC";

        var result = await conn.QueryAsync<AuditLog>(sql, new { EntityId = entityId, EntityType = entityType, Limit = limit ?? 100 }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<(int TotalLogs, int ErrorCount, int WarningCount, int TodayCount)> GetAuditStatsAsync()
    {
        using var conn = CreateConnection();
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var today = DateTime.UtcNow.Date;

        var totalLogs = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs").ConfigureAwait(false);
        var errorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AuditLogs WHERE Level = 'Error' AND Timestamp >= @Yesterday",
            new { Yesterday = yesterday }).ConfigureAwait(false);
        var warningCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AuditLogs WHERE Level = 'Warning' AND Timestamp >= @Yesterday",
            new { Yesterday = yesterday }).ConfigureAwait(false);
        var todayCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AuditLogs WHERE Timestamp >= @Today",
            new { Today = today }).ConfigureAwait(false);

        return (totalLogs, errorCount, warningCount, todayCount);
    }

    public async Task<(List<SyncAuditLog> Items, int TotalCount)> GetSyncAuditLogsPagedAsync(
        string? action = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        using var conn = CreateConnection();
        var where = "WHERE 1=1";
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(action))
        {
            where += " AND (OperationType LIKE @Action OR ObjectDisplayName LIKE @Action)";
            p.Add("Action", $"%{action}%");
        }
        if (dateFrom.HasValue) { where += " AND Timestamp >= @DateFrom"; p.Add("DateFrom", dateFrom.Value); }
        if (dateTo.HasValue) { where += " AND Timestamp <= @DateTo"; p.Add("DateTo", dateTo.Value); }

        var countSql = $"SELECT COUNT(*) FROM SyncAuditLogs {where}";
        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);
        var dataSql = $"SELECT * FROM SyncAuditLogs {where} ORDER BY Timestamp DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        var items = (await conn.QueryAsync<SyncAuditLog>(dataSql, p).ConfigureAwait(false)).ToList();

        return (items, totalCount);
    }

    public async Task<(List<ChangeAuditLog> Items, int TotalCount)> GetChangeAuditLogsFilteredPagedAsync(
        string? userId = null, string? action = null,
        DateTime? dateFrom = null, DateTime? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        using var conn = CreateConnection();
        var where = "WHERE 1=1";
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(userId))
        {
            where += " AND (UserId LIKE @UserId OR UserDisplayName LIKE @UserId)";
            p.Add("UserId", $"%{userId}%");
        }
        if (!string.IsNullOrEmpty(action))
        {
            where += " AND (CAST(OperationType AS NVARCHAR(100)) LIKE @Action OR PropertyName LIKE @Action)";
            p.Add("Action", $"%{action}%");
        }
        if (dateFrom.HasValue) { where += " AND Timestamp >= @DateFrom"; p.Add("DateFrom", dateFrom.Value); }
        if (dateTo.HasValue) { where += " AND Timestamp <= @DateTo"; p.Add("DateTo", dateTo.Value); }

        var countSql = $"SELECT COUNT(*) FROM ChangeAuditLogs {where}";
        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);
        var dataSql = $"SELECT * FROM ChangeAuditLogs {where} ORDER BY Timestamp DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        var items = (await conn.QueryAsync<ChangeAuditLog>(dataSql, p).ConfigureAwait(false)).ToList();

        return (items, totalCount);
    }
}
