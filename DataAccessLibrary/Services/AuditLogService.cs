using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service for logging and retrieving change audit history for directory objects.
    /// Implements detailed Who, What, When, Why tracking.
    /// </summary>
    public class AuditLogService : IAuditLogService
    {
        private readonly string _connectionString;
        private readonly ILogger<AuditLogService> _logger;

        public AuditLogService(
            IConfiguration configuration,
            ILogger<AuditLogService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task LogChangeAsync(ChangeAuditEntry entry)
        {
            try
            {
                var log = ChangeAuditLog.FromEntry(entry);

                const string sql = @"
                    INSERT INTO ChangeAuditLogs (
                        Timestamp, UserId, UserDisplayName, UserEmail, IpAddress,
                        OperationType, EntityType, EntityId, EntityDisplayName, PropertyName,
                        OldValue, NewValue, RelatedEntityId, RelatedEntityName,
                        Reason, TicketNumber, ApprovedBy, ApproverName,
                        Success, ErrorMessage, CorrelationId, Source,
                        OnBehalfOfUserId, OnBehalfOfDisplayName
                    )
                    OUTPUT INSERTED.Id
                    VALUES (
                        @Timestamp, @UserId, @UserDisplayName, @UserEmail, @IpAddress,
                        @OperationType, @EntityType, @EntityId, @EntityDisplayName, @PropertyName,
                        @OldValue, @NewValue, @RelatedEntityId, @RelatedEntityName,
                        @Reason, @TicketNumber, @ApprovedBy, @ApproverName,
                        @Success, @ErrorMessage, @CorrelationId, @Source,
                        @OnBehalfOfUserId, @OnBehalfOfDisplayName
                    )";

                await using var connection = new SqlConnection(_connectionString);
                var id = await connection.ExecuteScalarAsync<long>(sql, log).ConfigureAwait(false);
                entry.Id = id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log change audit entry for {EntityType} {EntityId}",
                    entry.EntityType, entry.EntityId);
                // Don't throw - audit logging should not break main operations
            }
        }

        /// <inheritdoc />
        public async Task LogChangeSyncAsync(ChangeAuditEntry entry)
        {
            var log = ChangeAuditLog.FromEntry(entry);

            const string sql = @"
                INSERT INTO ChangeAuditLogs (
                    Timestamp, UserId, UserDisplayName, UserEmail, IpAddress,
                    OperationType, EntityType, EntityId, EntityDisplayName, PropertyName,
                    OldValue, NewValue, RelatedEntityId, RelatedEntityName,
                    Reason, TicketNumber, ApprovedBy, ApproverName,
                    Success, ErrorMessage, CorrelationId, Source,
                    OnBehalfOfUserId, OnBehalfOfDisplayName
                )
                OUTPUT INSERTED.Id
                VALUES (
                    @Timestamp, @UserId, @UserDisplayName, @UserEmail, @IpAddress,
                    @OperationType, @EntityType, @EntityId, @EntityDisplayName, @PropertyName,
                    @OldValue, @NewValue, @RelatedEntityId, @RelatedEntityName,
                    @Reason, @TicketNumber, @ApprovedBy, @ApproverName,
                    @Success, @ErrorMessage, @CorrelationId, @Source,
                    @OnBehalfOfUserId, @OnBehalfOfDisplayName
                )";

            await using var connection = new SqlConnection(_connectionString);
            var id = await connection.ExecuteScalarAsync<long>(sql, log).ConfigureAwait(false);
            entry.Id = id;
        }

        /// <inheritdoc />
        public async Task LogChangesAsync(IEnumerable<ChangeAuditEntry> entries)
        {
            try
            {
                var entryList = entries.ToList();
                if (entryList.Count == 0)
                    return;

                var logs = entryList.Select(ChangeAuditLog.FromEntry).ToList();

                const string sql = @"
                    INSERT INTO ChangeAuditLogs (
                        Timestamp, UserId, UserDisplayName, UserEmail, IpAddress,
                        OperationType, EntityType, EntityId, EntityDisplayName, PropertyName,
                        OldValue, NewValue, RelatedEntityId, RelatedEntityName,
                        Reason, TicketNumber, ApprovedBy, ApproverName,
                        Success, ErrorMessage, CorrelationId, Source,
                        OnBehalfOfUserId, OnBehalfOfDisplayName
                    )
                    OUTPUT INSERTED.Id
                    VALUES (
                        @Timestamp, @UserId, @UserDisplayName, @UserEmail, @IpAddress,
                        @OperationType, @EntityType, @EntityId, @EntityDisplayName, @PropertyName,
                        @OldValue, @NewValue, @RelatedEntityId, @RelatedEntityName,
                        @Reason, @TicketNumber, @ApprovedBy, @ApproverName,
                        @Success, @ErrorMessage, @CorrelationId, @Source,
                        @OnBehalfOfUserId, @OnBehalfOfDisplayName
                    )";

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync().ConfigureAwait(false);
                await using var transaction = connection.BeginTransaction();

                try
                {
                    for (int i = 0; i < logs.Count; i++)
                    {
                        var id = await connection.ExecuteScalarAsync<long>(sql, logs[i], transaction).ConfigureAwait(false);
                        logs[i].Id = id;
                        entryList[i].Id = id;
                    }

                    await transaction.CommitAsync().ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync().ConfigureAwait(false);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log {Count} change audit entries", entries.Count());
                // Don't throw - audit logging should not break main operations
            }
        }

        /// <inheritdoc />
        public async Task<List<ChangeAuditEntry>> GetObjectHistoryAsync(Guid objectId, int limit = 50)
        {
            try
            {
                const string sql = @"
                    SELECT TOP (@Limit) *
                    FROM ChangeAuditLogs
                    WHERE EntityId = @EntityId
                    ORDER BY Timestamp DESC";

                await using var connection = new SqlConnection(_connectionString);
                var logs = await connection.QueryAsync<ChangeAuditLog>(sql, new { EntityId = objectId, Limit = limit }).ConfigureAwait(false);

                return logs.Select(l => l.ToEntry()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get history for object {ObjectId}", objectId);
                return new List<ChangeAuditEntry>();
            }
        }

        /// <inheritdoc />
        public async Task<List<ChangeAuditEntry>> GetPersonHistoryAsync(Guid personId, int limit = 50)
        {
            try
            {
                // Get all object IDs linked to this person (Identity)
                const string objectIdsSql = @"
                    SELECT Id FROM Objects WHERE IdentityId = @PersonId";

                await using var connection = new SqlConnection(_connectionString);
                var objectIds = (await connection.QueryAsync<Guid>(objectIdsSql, new { PersonId = personId }).ConfigureAwait(false)).ToList();

                // Add the person ID itself
                var allEntityIds = new List<Guid> { personId };
                allEntityIds.AddRange(objectIds);

                // Get history for person and all linked objects
                const string sql = @"
                    SELECT TOP (@Limit) *
                    FROM ChangeAuditLogs
                    WHERE EntityId IN @EntityIds
                    ORDER BY Timestamp DESC";

                var logs = await connection.QueryAsync<ChangeAuditLog>(sql, new { EntityIds = allEntityIds, Limit = limit }).ConfigureAwait(false);

                return logs.Select(l => l.ToEntry()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get history for person {PersonId}", personId);
                return new List<ChangeAuditEntry>();
            }
        }

        /// <inheritdoc />
        public async Task<List<ChangeAuditEntry>> GetUserActivityAsync(string userId, int limit = 50)
        {
            try
            {
                const string sql = @"
                    SELECT TOP (@Limit) *
                    FROM ChangeAuditLogs
                    WHERE UserId = @UserId
                    ORDER BY Timestamp DESC";

                await using var connection = new SqlConnection(_connectionString);
                var logs = await connection.QueryAsync<ChangeAuditLog>(sql, new { UserId = userId, Limit = limit }).ConfigureAwait(false);

                return logs.Select(l => l.ToEntry()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get activity for user {UserId}", userId);
                return new List<ChangeAuditEntry>();
            }
        }

        /// <inheritdoc />
        public async Task<List<ChangeAuditEntry>> GetRecentChangesAsync(int limit = 100)
        {
            try
            {
                const string sql = @"
                    SELECT TOP (@Limit) *
                    FROM ChangeAuditLogs
                    ORDER BY Timestamp DESC";

                await using var connection = new SqlConnection(_connectionString);
                var logs = await connection.QueryAsync<ChangeAuditLog>(sql, new { Limit = limit }).ConfigureAwait(false);

                return logs.Select(l => l.ToEntry()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get recent changes");
                return new List<ChangeAuditEntry>();
            }
        }

        /// <inheritdoc />
        public async Task<List<ChangeAuditEntry>> SearchHistoryAsync(ChangeAuditSearchCriteria criteria)
        {
            try
            {
                var sql = new StringBuilder(@"
                    SELECT *
                    FROM ChangeAuditLogs
                    WHERE 1=1");

                var parameters = new DynamicParameters();

                if (criteria.EntityId.HasValue)
                {
                    sql.Append(" AND EntityId = @EntityId");
                    parameters.Add("EntityId", criteria.EntityId.Value);
                }

                if (!string.IsNullOrEmpty(criteria.EntityType))
                {
                    sql.Append(" AND EntityType = @EntityType");
                    parameters.Add("EntityType", criteria.EntityType);
                }

                if (!string.IsNullOrEmpty(criteria.UserId))
                {
                    sql.Append(" AND UserId = @UserId");
                    parameters.Add("UserId", criteria.UserId);
                }

                if (criteria.OperationType.HasValue)
                {
                    sql.Append(" AND OperationType = @OperationType");
                    parameters.Add("OperationType", (int)criteria.OperationType.Value);
                }

                if (criteria.FromDate.HasValue)
                {
                    sql.Append(" AND Timestamp >= @FromDate");
                    parameters.Add("FromDate", criteria.FromDate.Value);
                }

                if (criteria.ToDate.HasValue)
                {
                    sql.Append(" AND Timestamp <= @ToDate");
                    parameters.Add("ToDate", criteria.ToDate.Value);
                }

                if (!string.IsNullOrEmpty(criteria.PropertyName))
                {
                    sql.Append(" AND PropertyName = @PropertyName");
                    parameters.Add("PropertyName", criteria.PropertyName);
                }

                if (criteria.SuccessOnly.HasValue)
                {
                    sql.Append(" AND Success = @Success");
                    parameters.Add("Success", criteria.SuccessOnly.Value);
                }

                if (!string.IsNullOrEmpty(criteria.Source))
                {
                    sql.Append(" AND Source = @Source");
                    parameters.Add("Source", criteria.Source);
                }

                sql.Append(" ORDER BY Timestamp DESC");
                sql.Append(" OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY");
                parameters.Add("Offset", criteria.Offset);
                parameters.Add("Limit", criteria.Limit);

                await using var connection = new SqlConnection(_connectionString);
                var logs = await connection.QueryAsync<ChangeAuditLog>(sql.ToString(), parameters).ConfigureAwait(false);

                return logs.Select(l => l.ToEntry()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search history with criteria");
                return new List<ChangeAuditEntry>();
            }
        }
    }
}
