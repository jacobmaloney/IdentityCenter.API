using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service for managing groups with direct management capabilities
    /// UC-GRP-01: Group Management with Access Review
    /// PHASE 2: Direct management service layer with AD write-back
    /// </summary>
    public interface IGroupService
    {
        // ====================================================================
        // BASIC CRUD OPERATIONS
        // ====================================================================

        /// <summary>
        /// Gets a group by ID with all related data loaded
        /// </summary>
        /// <param name="id">Group ID</param>
        /// <param name="includeMembers">Whether to include member list (default: false for performance)</param>
        /// <param name="includeAttributes">Whether to include extended attributes</param>
        /// <returns>Group with requested related data, or null if not found</returns>
        Task<Group?> GetByIdAsync(Guid id, bool includeMembers = false, bool includeAttributes = false);

        /// <summary>
        /// Gets all groups with optional filtering
        /// </summary>
        /// <param name="sourceConnectionId">Filter by source connection (optional)</param>
        /// <param name="isActive">Filter by active status (optional)</param>
        /// <param name="requiresReview">Filter by review requirement (optional)</param>
        /// <param name="riskLevel">Filter by risk level (optional)</param>
        /// <param name="skip">Number of records to skip for pagination</param>
        /// <param name="take">Number of records to take for pagination</param>
        /// <returns>List of groups matching criteria</returns>
        Task<List<Group>> GetAllAsync(
            Guid? sourceConnectionId = null,
            bool? isActive = null,
            bool? requiresReview = null,
            string? riskLevel = null,
            int skip = 0,
            int take = 100);

        /// <summary>
        /// Gets count of groups matching criteria (for pagination)
        /// </summary>
        Task<int> GetCountAsync(
            Guid? sourceConnectionId = null,
            bool? isActive = null,
            bool? requiresReview = null,
            string? riskLevel = null);

        /// <summary>
        /// Searches for groups by name or email
        /// </summary>
        /// <param name="searchTerm">Search term to match against name or email</param>
        /// <param name="limit">Maximum number of results (default: 50)</param>
        /// <returns>List of matching groups</returns>
        Task<List<Group>> SearchAsync(string searchTerm, int limit = 50);

        // ====================================================================
        // UPDATE OPERATIONS WITH AD WRITE-BACK
        // UC-GRP-01-02: Edit Group Properties
        // ====================================================================

        /// <summary>
        /// Updates a group's properties with audit logging and AD write-back
        /// </summary>
        /// <param name="group">Group with updated properties</param>
        /// <param name="modifiedBy">User making the change</param>
        /// <param name="writeBackToAD">Whether to write changes back to AD (default: true)</param>
        /// <returns>Updated group</returns>
        /// <exception cref="InvalidOperationException">If AD write-back fails and rollback is needed</exception>
        Task<Group> UpdateAsync(Group group, string modifiedBy, bool writeBackToAD = true);

        /// <summary>
        /// Updates only specific properties of a group (partial update)
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <param name="propertyUpdates">Dictionary of property names and new values</param>
        /// <param name="modifiedBy">User making the change</param>
        /// <param name="writeBackToAD">Whether to write changes back to AD</param>
        /// <returns>Updated group</returns>
        Task<Group> UpdatePropertiesAsync(Guid groupId, Dictionary<string, object> propertyUpdates, string modifiedBy, bool writeBackToAD = true);

        // ====================================================================
        // MEMBER MANAGEMENT
        // UC-GRP-01-03: Manage Group Members
        // ====================================================================

        /// <summary>
        /// Gets all members of a group
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <param name="includeInactive">Whether to include removed members</param>
        /// <returns>List of memberships</returns>
        Task<List<ObjectGroupMembership>> GetMembersAsync(Guid groupId, bool includeInactive = false);

        /// <summary>
        /// Adds a member to a group with justification tracking
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <param name="objectId">Object (user/computer) ID to add</param>
        /// <param name="justification">Reason for adding (required if RequiresJustification=true)</param>
        /// <param name="addedBy">User making the change</param>
        /// <param name="expirationDate">Optional expiration date for temporary access</param>
        /// <param name="writeBackToAD">Whether to add member in AD (default: true)</param>
        /// <returns>Created membership record</returns>
        /// <exception cref="ArgumentException">If justification required but not provided</exception>
        /// <exception cref="InvalidOperationException">If AD write-back fails</exception>
        Task<ObjectGroupMembership> AddMemberAsync(
            Guid groupId,
            Guid objectId,
            string? justification,
            string addedBy,
            DateTime? expirationDate = null,
            bool writeBackToAD = true);

        /// <summary>
        /// Removes a member from a group
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <param name="objectId">Object ID to remove</param>
        /// <param name="reason">Reason for removal</param>
        /// <param name="removedBy">User making the change</param>
        /// <param name="writeBackToAD">Whether to remove member in AD (default: true)</param>
        /// <returns>True if removed, false if not found</returns>
        Task<bool> RemoveMemberAsync(
            Guid groupId,
            Guid objectId,
            string? reason,
            string removedBy,
            bool writeBackToAD = true);

        /// <summary>
        /// Bulk adds multiple members to a group
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <param name="objectIds">List of object IDs to add</param>
        /// <param name="justification">Reason for adding</param>
        /// <param name="addedBy">User making the change</param>
        /// <param name="writeBackToAD">Whether to add members in AD</param>
        /// <returns>List of created membership records</returns>
        Task<List<ObjectGroupMembership>> BulkAddMembersAsync(
            Guid groupId,
            List<Guid> objectIds,
            string? justification,
            string addedBy,
            bool writeBackToAD = true);

        // ====================================================================
        // RISK ASSESSMENT
        // UC-GRP-01-01: View Groups with Risk Assessment
        // ====================================================================

        /// <summary>
        /// Calculates and updates risk score for a group
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <returns>Updated group with new risk score</returns>
        Task<Group> UpdateRiskScoreAsync(Guid groupId);

        /// <summary>
        /// Calculates and updates risk scores for all groups
        /// Used by background job
        /// </summary>
        /// <param name="sourceConnectionId">Optional: only update groups from specific source</param>
        /// <returns>Number of groups updated</returns>
        Task<int> UpdateAllRiskScoresAsync(Guid? sourceConnectionId = null);

        /// <summary>
        /// Gets groups that are high risk (risk level High or Critical)
        /// </summary>
        /// <param name="limit">Maximum number of results</param>
        /// <returns>List of high-risk groups ordered by risk score descending</returns>
        Task<List<Group>> GetHighRiskGroupsAsync(int limit = 100);

        // ====================================================================
        // ACCESS REVIEW
        // UC-GRP-01-04: Conduct Access Review
        // ====================================================================

        /// <summary>
        /// Starts an access review for a group
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <param name="reviewerId">Person conducting the review</param>
        /// <param name="dueDate">When review should be completed</param>
        /// <param name="createdBy">User starting the review</param>
        /// <returns>Created access review</returns>
        Task<Campaign> StartAccessReviewAsync(
            Guid groupId,
            Guid? reviewerId,
            DateTime dueDate,
            string createdBy);

        /// <summary>
        /// Gets groups that are overdue for access review
        /// </summary>
        /// <param name="limit">Maximum number of results</param>
        /// <returns>List of groups overdue for review</returns>
        Task<List<Group>> GetOverdueForReviewAsync(int limit = 100);

        // ====================================================================
        // SYNC INTEGRATION
        // UC-GRP-01-05: Sync Group from AD
        // ====================================================================

        /// <summary>
        /// Syncs a specific group from AD on-demand
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <param name="requestedBy">User requesting the sync</param>
        /// <returns>Updated group after sync</returns>
        Task<Group> SyncFromADAsync(Guid groupId, string requestedBy);

        /// <summary>
        /// Calculates next review date based on review frequency
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <returns>Updated group with calculated next review date</returns>
        Task<Group> CalculateNextReviewDateAsync(Guid groupId);

        // ====================================================================
        // UI WRAPPER METHODS (for GroupsManagement.razor compatibility)
        // ====================================================================

        Task<List<Group>> GetAllGroupsAsync();
        Task<Group?> GetGroupByIdAsync(Guid groupId);
        Task<List<ObjectGroupMembership>> GetGroupMembersWithDetailsAsync(Guid groupId);
        Task<List<ObjectGroupMembership>> GetMembershipHistoryAsync(Guid groupId);
        Task<List<IdentityObject>> GetAvailableUsersForGroupAsync(Guid groupId);
        Task<List<Models.DirectoryConnection>> GetDirectoryConnectionsAsync();
        Task<bool> UpdateGroupAsync(Group group);
    }
}
