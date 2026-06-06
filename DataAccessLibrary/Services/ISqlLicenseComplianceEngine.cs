using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

public interface ISqlLicenseComplianceEngine
{
    Task EvaluateAllServersAsync(CancellationToken ct = default, bool excludeDemo = false);
    Task EvaluateServerAsync(Guid serverId, CancellationToken ct = default);
    Task<List<LicenseComplianceViolation>> GetPendingViolationsAsync();
}
