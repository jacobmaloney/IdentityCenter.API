using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Policy repository interface
/// Data access for policies and policy rules
/// </summary>
public interface IPolicyRepository
{
    /// <summary>
    /// Get all policies
    /// </summary>
    Task<List<CompliancePolicy>> GetAllPoliciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active policies
    /// </summary>
    Task<List<CompliancePolicy>> GetActivePoliciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific policy by ID
    /// </summary>
    Task<CompliancePolicy?> GetPolicyByIdAsync(Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get policies by category
    /// </summary>
    Task<List<CompliancePolicy>> GetPoliciesByCategoryAsync(string category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new policy
    /// </summary>
    Task<CompliancePolicy> CreatePolicyAsync(CompliancePolicy policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing policy
    /// </summary>
    Task<CompliancePolicy> UpdatePolicyAsync(CompliancePolicy policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a policy
    /// </summary>
    Task DeletePolicyAsync(Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get policy rules for a specific policy
    /// </summary>
    Task<List<CompliancePolicyRule>> GetPolicyRulesAsync(Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new policy rule
    /// </summary>
    Task<CompliancePolicyRule> CreatePolicyRuleAsync(CompliancePolicyRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing policy rule
    /// </summary>
    Task<CompliancePolicyRule> UpdatePolicyRuleAsync(CompliancePolicyRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a policy rule
    /// </summary>
    Task DeletePolicyRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get policy actions for a specific policy
    /// </summary>
    Task<List<CompliancePolicyAction>> GetPolicyActionsAsync(Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new policy action
    /// </summary>
    Task<CompliancePolicyAction> CreatePolicyActionAsync(CompliancePolicyAction action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing policy action
    /// </summary>
    Task<CompliancePolicyAction> UpdatePolicyActionAsync(CompliancePolicyAction action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a policy action
    /// </summary>
    Task DeletePolicyActionAsync(Guid actionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update policy IsRunning flag
    /// </summary>
    Task UpdatePolicyIsRunningAsync(Guid policyId, bool isRunning, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new policy execution record
    /// </summary>
    Task<CompliancePolicyExecution> CreatePolicyExecutionAsync(CompliancePolicyExecution execution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a policy execution record
    /// </summary>
    Task UpdatePolicyExecutionAsync(CompliancePolicyExecution execution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the latest execution for a policy
    /// </summary>
    Task<CompliancePolicyExecution?> GetLatestExecutionAsync(Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get policies by a list of IDs
    /// </summary>
    Task<List<CompliancePolicy>> GetPoliciesByIdsAsync(IEnumerable<Guid> policyIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copy a policy with its rules and actions
    /// </summary>
    Task<CompliancePolicy> CopyPolicyAsync(Guid sourcePolicyId, string newName, bool enabled, string createdBy, CancellationToken cancellationToken = default);
}
