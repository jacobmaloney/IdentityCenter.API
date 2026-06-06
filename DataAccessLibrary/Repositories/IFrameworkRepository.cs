using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Framework repository interface
/// Data access for compliance frameworks and mappings
/// </summary>
public interface IFrameworkRepository
{
    /// <summary>
    /// Get all compliance frameworks
    /// </summary>
    Task<List<ComplianceFramework>> GetAllFrameworksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific framework by ID
    /// </summary>
    Task<ComplianceFramework?> GetFrameworkByIdAsync(Guid frameworkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get framework policy mappings for a specific framework
    /// </summary>
    Task<List<ComplianceFrameworkPolicyMapping>> GetFrameworkMappingsAsync(Guid frameworkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all framework policy mappings across all frameworks
    /// </summary>
    Task<List<ComplianceFrameworkPolicyMapping>> GetAllMappingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get policy counts per framework (FrameworkId -> count of linked policies)
    /// </summary>
    Task<Dictionary<Guid, int>> GetFrameworkPolicyCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reorder framework policy mappings by updating SortOrder
    /// </summary>
    Task ReorderFrameworkMappingsAsync(Guid frameworkId, List<Guid> orderedMappingIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copy a framework with all its policy mappings
    /// </summary>
    Task<ComplianceFramework> CopyFrameworkAsync(Guid sourceFrameworkId, string newName, string createdBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a framework policy mapping by ID
    /// </summary>
    Task DeleteMappingAsync(Guid mappingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create framework policy mapping
    /// </summary>
    Task<ComplianceFrameworkPolicyMapping> CreateMappingAsync(ComplianceFrameworkPolicyMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get frameworks by category
    /// </summary>
    Task<List<ComplianceFramework>> GetFrameworksByCategoryAsync(string category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new compliance framework
    /// </summary>
    Task<ComplianceFramework> CreateFrameworkAsync(ComplianceFramework framework, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing compliance framework
    /// </summary>
    Task<ComplianceFramework> UpdateFrameworkAsync(ComplianceFramework framework, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a compliance framework
    /// </summary>
    Task DeleteFrameworkAsync(Guid frameworkId, CancellationToken cancellationToken = default);

    // ============================================
    // FRAMEWORK ASSIGNMENT OPERATIONS
    // ============================================

    /// <summary>
    /// Create a new framework assignment (apply framework to scope)
    /// </summary>
    Task<FrameworkAssignment> CreateAssignmentAsync(FrameworkAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific assignment by ID
    /// </summary>
    Task<FrameworkAssignment?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all assignments for a specific framework
    /// </summary>
    Task<List<FrameworkAssignment>> GetAssignmentsForFrameworkAsync(Guid frameworkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all assignments for a specific connection
    /// </summary>
    Task<List<FrameworkAssignment>> GetAssignmentsForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active framework assignments
    /// </summary>
    Task<List<FrameworkAssignment>> GetActiveAssignmentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing framework assignment
    /// </summary>
    Task<FrameworkAssignment> UpdateAssignmentAsync(FrameworkAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivate a framework assignment (soft delete)
    /// </summary>
    Task DeactivateAssignmentAsync(Guid assignmentId, string reason, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a framework assignment permanently
    /// </summary>
    Task DeleteAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update compliance metrics for an assignment after evaluation
    /// </summary>
    Task UpdateAssignmentComplianceAsync(Guid assignmentId, decimal complianceScore, int totalPolicies,
        int passingPolicies, int failingPolicies, int totalViolations, int criticalViolations,
        CancellationToken cancellationToken = default);

    // ============================================
    // POLICY OVERRIDE OPERATIONS
    // ============================================

    /// <summary>
    /// Get all policy overrides for an assignment
    /// </summary>
    Task<List<FrameworkAssignmentPolicyOverride>> GetPolicyOverridesAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or update a policy override
    /// </summary>
    Task<FrameworkAssignmentPolicyOverride> UpsertPolicyOverrideAsync(FrameworkAssignmentPolicyOverride policyOverride, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a policy override
    /// </summary>
    Task DeletePolicyOverrideAsync(Guid overrideId, CancellationToken cancellationToken = default);

    // ============================================
    // COMPLIANCE SUMMARY QUERIES
    // ============================================

    /// <summary>
    /// Get overall compliance score across all active assignments
    /// </summary>
    Task<decimal> GetOverallComplianceScoreAsync(Guid? connectionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get compliance summary by framework for dashboard display
    /// </summary>
    Task<List<FrameworkComplianceSummary>> GetComplianceSummaryByFrameworkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a framework is already assigned to a connection
    /// </summary>
    Task<bool> IsFrameworkAssignedToConnectionAsync(Guid frameworkId, Guid connectionId, CancellationToken cancellationToken = default);
}
