using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IComplianceQueryRepository
{
    Task<(List<CompliancePolicyViolation> Items, int TotalCount)> GetViolationsPagedAsync(
        string? status = null, Guid? policyId = null, string? severity = null,
        int page = 1, int pageSize = 50);

    Task<List<CompliancePolicyViolation>> GetEscalationCandidatesAsync(int olderThanDays);

    Task UpdateViolationStatusAsync(Guid id, string newStatus);

    Task<int> BulkUpdateViolationStatusAsync(List<Guid> ids, string newStatus);

    Task<Dictionary<string, int>> GetViolationCountsByStatusAsync();

    Task<List<CompliancePolicyViolation>> GetViolationsForPolicyAsync(Guid policyId, string? status = null);

    Task<List<CompliancePolicyViolation>> GetViolationsForEntityAsync(Guid entityId);

    // Additional methods for ComplianceCenter page
    Task<int> GetBusinessRoleCountAsync();
    Task<List<CompliancePolicyViolation>> GetActiveViolationsAsync();
    Task<List<CompliancePolicyViolation>> GetViolationsByStatusAsync(params string[] statuses);
    Task<List<CompliancePolicyViolation>> GetRecentViolationsAsync(int days = 7, int limit = 10);
    Task<CompliancePolicyViolation?> GetViolationAsync(Guid id);
    Task UpdateViolationAsync(CompliancePolicyViolation violation);
    Task CreateViolationAsync(CompliancePolicyViolation violation);
    Task DeleteViolationsAsync(List<Guid> ids);
    Task<int> BulkUpdateViolationFieldsAsync(List<Guid> ids, string status, string? remediatedBy = null, DateTime? remediatedAt = null);
    Task<int> GetPolicyCountAsync();
    Task<Identity?> GetIdentityAsync(Guid id);
    Task<IdentityObject?> GetIdentityObjectByIdentityIdAsync(Guid identityId);
    Task<List<IdentityObject>> GetIdentityObjectsByIdentityIdsAsync(List<Guid> identityIds);
    Task<List<Identity>> SearchIdentitiesAsync(string? searchTerm = null, int limit = 50);
    Task<Tag?> GetOrCreateTagAsync(string name, string? category = null);
    Task AddIdentityTagAsync(Guid identityId, Guid tagId);
    Task<bool> IdentityTagExistsAsync(Guid identityId, Guid tagId);
}
