using System.Security.Claims;
using System.Text;
using ChangeHistory.Models;
using Dapper;
using Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ChangeHistory.Services;

/// <summary>
/// Dapper-based implementation of IChangeHistoryService.
/// Writes to the existing ChangeAuditLogs table. Single writes are fire-and-forget.
/// </summary>
public class ChangeHistoryService : IChangeHistoryService
{
    private readonly string _connectionString;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IGlobalLogger _logger;

    private const string InsertSql = @"
        INSERT INTO ChangeAuditLogs
            (Timestamp, OperationType, EntityType, EntityId, EntityDisplayName,
             RelatedEntityId, RelatedEntityName, PropertyName, OldValue, NewValue,
             Reason, TicketNumber, ApprovedBy, ApproverName,
             ErrorMessage, UserId, UserDisplayName, UserEmail, IpAddress,
             Source, Success, CorrelationId,
             OnBehalfOfUserId, OnBehalfOfDisplayName)
        VALUES
            (@Timestamp, @OperationType, @EntityType, @EntityId, @EntityDisplayName,
             @RelatedEntityId, @RelatedEntityName, @PropertyName, @OldValue, @NewValue,
             @Reason, @TicketNumber, @ApprovedBy, @ApproverName,
             @ErrorMessage, @UserId, @UserDisplayName, @UserEmail, @IpAddress,
             @Source, @Success, @CorrelationId,
             @OnBehalfOfUserId, @OnBehalfOfDisplayName)";

    public ChangeHistoryService(
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    #region Write

    public Task RecordAsync(ChangeRecord record)
    {
        if (record == null) return Task.CompletedTask;

        EnrichWithUserContext(record);
        TruncateIfNeeded(record);

        // Fire-and-forget — audit never blocks CRUD
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                using var connection = CreateConnection();
                connection.Open();
                connection.Execute(InsertSql, record, commandTimeout: 30);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record change history for {EntityType} {EntityId}",
                    record.EntityType, record.EntityId);
            }
        });

        return Task.CompletedTask;
    }

    public async Task RecordBatchAsync(IEnumerable<ChangeRecord> records)
    {
        var list = records?.ToList();
        if (list == null || list.Count == 0) return;

        foreach (var record in list)
        {
            EnrichWithUserContext(record);
            TruncateIfNeeded(record);
        }

        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(InsertSql, list, transaction, commandTimeout: 30);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to batch record {Count} change history entries", list.Count);
        }
    }

    #endregion

    #region Read

    public async Task<List<ChangeRecord>> GetEntityHistoryAsync(Guid entityId, int limit = 50)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT TOP (@Limit) *
            FROM ChangeAuditLogs
            WHERE EntityId = @EntityId OR RelatedEntityId = @EntityId
            ORDER BY Timestamp DESC";

        var results = await connection.QueryAsync<ChangeRecord>(sql, new { EntityId = entityId, Limit = limit });
        return results.ToList();
    }

    public async Task<List<ChangeRecord>> GetPersonHistoryAsync(Guid personId, int limit = 50)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        // Get all object IDs linked to this person/identity
        const string linkedObjectsSql = "SELECT Id FROM Objects WHERE IdentityId = @PersonId";
        var linkedObjectIds = (await connection.QueryAsync<Guid>(linkedObjectsSql, new { PersonId = personId })).ToList();
        var allIds = linkedObjectIds.Append(personId).ToList();

        const string sql = @"
            SELECT TOP (@Limit) *
            FROM ChangeAuditLogs
            WHERE EntityId IN @AllIds OR RelatedEntityId IN @AllIds
            ORDER BY Timestamp DESC";

        var results = await connection.QueryAsync<ChangeRecord>(sql, new { AllIds = allIds, Limit = limit });
        return results.ToList();
    }

    public async Task<List<ChangeRecord>> GetUserActivityAsync(string userId, int limit = 50)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT TOP (@Limit) *
            FROM ChangeAuditLogs
            WHERE UserId = @UserId
            ORDER BY Timestamp DESC";

        var results = await connection.QueryAsync<ChangeRecord>(sql, new { UserId = userId, Limit = limit });
        return results.ToList();
    }

    public async Task<List<ChangeRecord>> GetRecentAsync(int limit = 100)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT TOP (@Limit) *
            FROM ChangeAuditLogs
            ORDER BY Timestamp DESC";

        var results = await connection.QueryAsync<ChangeRecord>(sql, new { Limit = limit });
        return results.ToList();
    }

    public async Task<List<ChangeRecord>> SearchAsync(ChangeSearchCriteria criteria)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        var sqlBuilder = new StringBuilder("SELECT * FROM ChangeAuditLogs WHERE 1=1");
        var parameters = new DynamicParameters();

        if (criteria.EntityId.HasValue)
        {
            sqlBuilder.Append(" AND (EntityId = @EntityId OR RelatedEntityId = @EntityId)");
            parameters.Add("EntityId", criteria.EntityId.Value);
        }

        if (!string.IsNullOrEmpty(criteria.EntityType))
        {
            sqlBuilder.Append(" AND EntityType = @EntityType");
            parameters.Add("EntityType", criteria.EntityType);
        }

        if (!string.IsNullOrEmpty(criteria.UserId))
        {
            sqlBuilder.Append(" AND UserId = @UserId");
            parameters.Add("UserId", criteria.UserId);
        }

        if (criteria.OperationType.HasValue)
        {
            sqlBuilder.Append(" AND OperationType = @OperationType");
            parameters.Add("OperationType", criteria.OperationType.Value);
        }

        if (criteria.FromDate.HasValue)
        {
            sqlBuilder.Append(" AND Timestamp >= @FromDate");
            parameters.Add("FromDate", criteria.FromDate.Value);
        }

        if (criteria.ToDate.HasValue)
        {
            sqlBuilder.Append(" AND Timestamp <= @ToDate");
            parameters.Add("ToDate", criteria.ToDate.Value);
        }

        if (!string.IsNullOrEmpty(criteria.PropertyName))
        {
            sqlBuilder.Append(" AND PropertyName = @PropertyName");
            parameters.Add("PropertyName", criteria.PropertyName);
        }

        if (criteria.SuccessOnly.HasValue)
        {
            sqlBuilder.Append(" AND Success = @Success");
            parameters.Add("Success", criteria.SuccessOnly.Value);
        }

        if (!string.IsNullOrEmpty(criteria.Source))
        {
            sqlBuilder.Append(" AND Source = @Source");
            parameters.Add("Source", criteria.Source);
        }

        if (criteria.CorrelationId.HasValue)
        {
            sqlBuilder.Append(" AND CorrelationId = @CorrelationId");
            parameters.Add("CorrelationId", criteria.CorrelationId.Value);
        }

        sqlBuilder.Append(" ORDER BY Timestamp DESC");
        sqlBuilder.Append(" OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY");
        parameters.Add("Offset", criteria.Offset);
        parameters.Add("Limit", criteria.Limit);

        var results = await connection.QueryAsync<ChangeRecord>(sqlBuilder.ToString(), parameters);
        return results.ToList();
    }

    public async Task<List<ChangeRecord>> GetByCorrelationIdAsync(Guid correlationId, int limit = 5000)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT TOP (@Limit) *
            FROM ChangeAuditLogs
            WHERE CorrelationId = @CorrelationId
            ORDER BY Timestamp";

        var results = await connection.QueryAsync<ChangeRecord>(sql, new { CorrelationId = correlationId, Limit = limit });
        return results.ToList();
    }

    #endregion

    #region Helpers

    private void EnrichWithUserContext(ChangeRecord record)
    {
        // Only enrich if WHO fields are not already set
        if (!string.IsNullOrEmpty(record.UserId)) return;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true) return;

        record.UserId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        record.UserDisplayName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value
            ?? httpContext.User.Identity?.Name;
        record.UserEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        record.IpAddress ??= httpContext.Connection?.RemoteIpAddress?.ToString();
    }

    private static void TruncateIfNeeded(ChangeRecord record)
    {
        record.OldValue = Truncate(record.OldValue, 2000);
        record.NewValue = Truncate(record.NewValue, 2000);
        record.ErrorMessage = Truncate(record.ErrorMessage, 1000);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length > maxLength ? value.Substring(0, maxLength - 3) + "..." : value;
    }

    #endregion
}
