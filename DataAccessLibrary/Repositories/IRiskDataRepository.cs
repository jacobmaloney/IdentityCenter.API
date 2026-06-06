using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IRiskDataRepository
{
    Task<RiskUserInfo?> GetUserInfoAsync(Guid userId);
    Task<int> GetGroupCountAsync(Guid userId);
    Task<int> GetAdminGroupCountAsync(Guid userId);
    Task<DateTime?> GetLastLoginAsync(Guid userId);
    Task<List<ViolationCount>> GetOpenViolationsAsync(Guid userId);
    Task<List<Guid>> GetHighRiskCandidateIdsAsync(double threshold);
    Task<int> GetActiveUserCountAsync();
    Task<List<RiskDistributionItem>> GetRiskDistributionAsync();
    Task<double> GetAverageRiskScoreAsync();
    Task<bool> HasRiskScoreHistoryTableAsync();
    Task<List<RiskTrendDataPoint>> GetRiskTrendHistoryAsync(int days);
}
