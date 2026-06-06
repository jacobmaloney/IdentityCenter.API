using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

public interface IAdLicenseComplianceEngine
{
    Task EvaluateAllPoolsAsync(CancellationToken ct = default);
    Task<List<LicenseComplianceViolation>> GetPendingViolationsAsync();
}
