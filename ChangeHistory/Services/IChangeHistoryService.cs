using ChangeHistory.Models;

namespace ChangeHistory.Services;

/// <summary>
/// Service for recording and retrieving change audit history.
/// All write operations are fire-and-forget to never block CRUD.
/// </summary>
public interface IChangeHistoryService
{
    // Write
    Task RecordAsync(ChangeRecord record);
    Task RecordBatchAsync(IEnumerable<ChangeRecord> records);

    // Read
    Task<List<ChangeRecord>> GetEntityHistoryAsync(Guid entityId, int limit = 50);
    Task<List<ChangeRecord>> GetPersonHistoryAsync(Guid personId, int limit = 50);
    Task<List<ChangeRecord>> GetUserActivityAsync(string userId, int limit = 50);
    Task<List<ChangeRecord>> GetRecentAsync(int limit = 100);
    Task<List<ChangeRecord>> SearchAsync(ChangeSearchCriteria criteria);
    Task<List<ChangeRecord>> GetByCorrelationIdAsync(Guid correlationId, int limit = 5000);
}
