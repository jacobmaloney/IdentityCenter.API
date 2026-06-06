using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for Golden Image Baselines.
/// </summary>
public interface IBaselineRepository
{
    Task<Guid> CaptureBaselineAsync(BaselineModels.GoldenImageBaseline baseline, CancellationToken cancellationToken = default);
    Task<BaselineModels.GoldenImageBaseline?> GetActiveBaselineAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);
    Task<List<BaselineModels.GoldenImageBaseline>> GetAllActiveBaselinesAsync(string? entityType = null, CancellationToken cancellationToken = default);
    Task DeactivateBaselineAsync(Guid baselineId, CancellationToken cancellationToken = default);
    Task<int> GetBaselineCountAsync(CancellationToken cancellationToken = default);
}
