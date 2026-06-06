using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for governance policies and quarantine records.
/// </summary>
public interface IGovernancePolicyRepository
{
    // === Governance Policies ===

    Task<List<GovernanceModels.GovernancePolicy>> GetEnabledPoliciesAsync(CancellationToken cancellationToken = default);
    Task<List<GovernanceModels.GovernancePolicy>> GetAllPoliciesAsync(CancellationToken cancellationToken = default);
    Task<GovernanceModels.GovernancePolicy?> GetPolicyByIdAsync(Guid policyId, CancellationToken cancellationToken = default);
    Task<Guid> InsertPolicyAsync(GovernanceModels.GovernancePolicy policy, CancellationToken cancellationToken = default);
    Task UpdatePolicyAsync(GovernanceModels.GovernancePolicy policy, CancellationToken cancellationToken = default);
    Task TogglePolicyAsync(Guid policyId, bool isEnabled, CancellationToken cancellationToken = default);

    // === Quarantine Records ===

    Task<Guid> InsertQuarantineRecordAsync(GovernanceModels.QuarantineRecord record, CancellationToken cancellationToken = default);
    Task<List<GovernanceModels.QuarantineRecord>> GetActiveQuarantinesAsync(CancellationToken cancellationToken = default);
    Task<GovernanceModels.QuarantineRecord?> GetQuarantineByIdentityAsync(Guid identityId, CancellationToken cancellationToken = default);
    Task ReleaseQuarantineAsync(Guid quarantineId, string releasedBy, string? releaseReason, CancellationToken cancellationToken = default);
    Task<List<GovernanceModels.QuarantineRecord>> GetExpiredQuarantinesAsync(CancellationToken cancellationToken = default);
    Task<int> GetActiveQuarantineCountAsync(CancellationToken cancellationToken = default);
}
