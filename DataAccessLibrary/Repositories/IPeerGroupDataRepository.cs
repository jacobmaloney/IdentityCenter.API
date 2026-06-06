using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IPeerGroupDataRepository
{
    Task<PeerUserInfo?> GetUserInfoAsync(Guid userId);
    Task<List<PeerMetrics>> GetPeersByDepartmentAndTitleAsync(string department, string title);
    Task<List<PeerMetrics>> GetPeersByDepartmentAsync(string department);
    Task<PeerMetrics?> GetUserMetricsAsync(Guid userId);
    Task<List<Guid>> GetAllActiveUserIdsAsync();
}
