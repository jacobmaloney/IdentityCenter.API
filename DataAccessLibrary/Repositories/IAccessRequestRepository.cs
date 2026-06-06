using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IAccessRequestRepository
{
    Task<AccessRequest> CreateAsync(AccessRequest request, CancellationToken cancellationToken = default);
    Task<AccessRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(List<AccessRequest> Items, int TotalCount)> GetByRequesterPagedAsync(string requesterId, string? statusFilter = null, int skip = 0, int take = 20, CancellationToken cancellationToken = default);
    Task<List<AccessRequest>> GetPendingAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid id, string status, string? approverId = null, string? comments = null, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid id, CancellationToken cancellationToken = default);
}
