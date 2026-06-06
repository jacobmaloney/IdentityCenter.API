using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

public interface IEntraLicenseComplianceEngine
{
    Task EvaluateAllPoolsAsync(CancellationToken ct = default);
    Task<List<LicenseComplianceViolation>> GetPendingViolationsAsync();
}
