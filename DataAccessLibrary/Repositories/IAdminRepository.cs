using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// High-performance Dapper-based repository for Admin page data access.
/// Replaces EF Core DbContext usage for better performance.
/// </summary>
public interface IAdminRepository
{
    // Directory Connections
    Task<List<DirectoryConnection>> GetDirectoryConnectionsAsync();
    Task<DirectoryConnection?> GetDirectoryConnectionAsync(Guid id);
    Task<Guid> CreateDirectoryConnectionAsync(DirectoryConnection connection);
    Task UpdateDirectoryConnectionAsync(DirectoryConnection connection);
    Task DeleteDirectoryConnectionAsync(Guid id);

    // Sync Projects
    Task<List<SyncProject>> GetSyncProjectsAsync();
    Task<SyncProject?> GetSyncProjectAsync(Guid id);
    Task UpdateSyncProjectScheduleAsync(Guid id, string? cronSchedule);
    Task<int> GetSyncProjectCountForConnectionAsync(Guid connectionId);

    // Objects (Users, Computers, Contacts, etc.)
    Task<List<IdentityObject>> GetObjectsAsync(string? objectClass = null, int? limit = null, int? offset = null,
        string? scopeWhereClause = null, DynamicParameters? scopeParams = null);
    Task<IdentityObject?> GetObjectAsync(Guid id);
    Task<int> GetObjectCountAsync(string? objectClass = null,
        string? scopeWhereClause = null, DynamicParameters? scopeParams = null);
    Task UpdateObjectAsync(IdentityObject obj);
    Task<List<IdentityObject>> SearchObjectsAsync(string searchQuery, int limit = 20,
        string? scopeWhereClause = null, DynamicParameters? scopeParams = null);
    Task<bool> ObjectExistsAsync(Guid id);
    Task<IdentityObject?> FindLinkedUserObjectAsync(Guid identityId);
    Task<List<IdentityObject>> GetObjectsByIdentityIdAsync(Guid identityId);

    // Object Attributes
    Task<List<ObjectAttribute>> GetObjectAttributesAsync(Guid objectId);
    Task<List<ObjectAttribute>> GetPersonAttributesAsync(Guid personId);
    Task UpsertObjectAttributeAsync(Guid objectId, string attributeName, string? attributeValue);
    Task DeleteObjectAttributeAsync(Guid objectId, string attributeName);
    Task DeleteAllObjectAttributesAsync(Guid objectId);
    Task<DateTime?> GetLatestTimestampAttributeAsync(List<Guid> objectIds, string[] attributeNames);

    // Identities
    Task<List<Identity>> GetIdentitiesAsync(string? objectClass = null, int? limit = null, int? offset = null);
    Task<Identity?> GetIdentityAsync(Guid id);
    Task<int> GetIdentityCountAsync(string? objectClass = null);
    Task UpdateIdentityAsync(Identity identity);
    Task<List<Identity>> SearchIdentitiesAsync(string searchTerm, int maxResults = 15, Guid? excludeId = null);
    Task<List<string>> GetDistinctIdentityFieldValuesAsync(string fieldName);
    Task<List<Identity>> GetDirectReportIdentitiesAsync(Guid managerIdentityId);

    // Field Lookup Values
    Task<List<FieldLookupValue>> GetFieldLookupValuesAsync(string fieldName);
    Task<List<string>> GetFieldLookupFieldNamesAsync();
    Task<FieldLookupValue> CreateFieldLookupValueAsync(string fieldName, string value, int sortOrder = 0);
    Task UpdateFieldLookupValueAsync(FieldLookupValue item);
    Task DeleteFieldLookupValueAsync(Guid id);

    Task DeletePersonWithObjectsAsync(Guid personId);
    Task DeleteObjectWithCleanupAsync(Guid objectId);

    /// <summary>
    /// Deferred-deletion lifecycle (ARS 3-state 0=Active/1=Disabled/2=Deprovisioned):
    /// mark an Identity as Deprovisioned (LifecycleState=2) and stamp DeletedAt = the
    /// retention clock, mirroring the Objects tombstone. Reversible:
    /// ReviveIdentityAsync restores it within the window; the daily purge job
    /// hard-deletes it after the window. Acts on any identity NOT already
    /// Deprovisioned (state &lt;&gt; 2) -- including a Disabled(1)/suspended person who is
    /// later terminated -- and never re-stamps DeletedAt on one already deprovisioned.
    /// Returns the number of rows transitioned (0 or 1).
    /// </summary>
    Task<int> DeprovisionIdentityAsync(Guid identityId, string? reason = null);

    /// <summary>
    /// Deferred-deletion lifecycle: revive a Deprovisioned Identity (state 2) back to
    /// Active (LifecycleState=0, DeletedAt=NULL, IsActive=1) "like nothing happened",
    /// provided it has not yet been purged. Returns the number of rows revived.
    /// </summary>
    Task<int> ReviveIdentityAsync(Guid identityId, string? reason = null);

    /// <summary>
    /// ARS 3-state lifecycle: move an Identity between Active(0) and Disabled(1) --
    /// the suspended-but-NOT-terminated state. Disabled is RETAINED INDEFINITELY and
    /// is NEVER on the purge clock: this method NEVER stamps DeletedAt and NEVER
    /// touches a Deprovisioned(2) row (that transition is owned by Deprovision/Revive).
    /// When disabling, sets LifecycleState=1 + IsActive=0 only if the row is currently
    /// Active(0); when enabling, sets LifecycleState=0 + IsActive=1 only if currently
    /// Disabled(1). Audited. Returns the number of rows transitioned (0 or 1).
    /// </summary>
    Task<int> SetIdentityDisabledAsync(Guid identityId, bool disabled, string? reason = null);

    // Group Memberships
    Task<List<ObjectGroupMembership>> GetObjectGroupMembershipsAsync(Guid? groupId = null, Guid? memberId = null);

    /// <summary>
    /// Returns the set of GroupIds that have at least one active membership row.
    /// Use to identify "empty groups" (groups not in this set) without a per-group query.
    /// One round-trip; replaces the N+1 pattern of calling GetObjectGroupMembershipsAsync per group.
    /// </summary>
    Task<HashSet<Guid>> GetGroupIdsWithActiveMembersAsync();
    Task<List<ObjectGroupMembership>> GetObjectGroupMembershipsWithGroupAsync(Guid objectId);
    Task<List<IdentityGroupMembership>> GetIdentityGroupMembershipsAsync(Guid? groupId = null, Guid? memberId = null);
    Task AddObjectGroupMembershipAsync(Guid groupId, Guid memberId);
    Task RemoveObjectGroupMembershipAsync(Guid groupId, Guid memberId);
    Task<List<IdentityObject>> GetGroupMembersAsync(Guid groupId);
    Task<List<Group>> GetMemberOfGroupsAsync(Guid objectId);
    Task<List<Group>> SearchGroupsAsync(string searchTerm, int limit = 20);

    // Tags
    Task<List<Tag>> GetTagsAsync();
    Task<List<Tag>> GetAllTagsAsync();
    Task<Tag?> GetTagAsync(Guid id);
    Task<Tag?> GetTagByNameAsync(string name, Guid? excludeId = null);
    Task<Guid> CreateTagAsync(Tag tag);
    Task UpdateTagAsync(Tag tag);
    Task DeleteTagAsync(Guid id);
    Task<(int ObjectCount, int IdentityCount)> GetTagUsageCountsAsync(Guid tagId);

    // Object Tags
    Task<List<ObjectTag>> GetObjectTagsAsync(Guid objectId);
    Task AddTagToObjectAsync(Guid objectId, Guid tagId, string? createdBy = null);
    Task RemoveTagFromObjectAsync(Guid objectTagId);
    Task RemoveTagFromObjectByIdsAsync(Guid objectId, Guid tagId);

    // Identity/Person Tags
    Task<HashSet<Guid>> GetIdentityIdsByTagAsync(Guid tagId);
    Task<List<IdentityTag>> GetIdentityTagsAsync(Guid identityId);
    Task AddTagToIdentityAsync(Guid identityId, Guid tagId, string? createdBy = null);
    Task RemoveTagFromIdentityAsync(Guid identityTagId);

    // Identity Providers
    Task<List<IdentityProvider>> GetIdentityProvidersAsync();

    /// <summary>
    /// Returns enabled identity providers ordered by IsPrimary DESC then Name ASC.
    /// Used by the login page to render external sign-in buttons.
    /// </summary>
    Task<List<IdentityProvider>> GetEnabledIdentityProvidersAsync();

    /// <summary>
    /// Looks up an identity provider by Name (used by external-login callbacks to
    /// resolve claim mappings for the responding provider).
    /// </summary>
    Task<IdentityProvider?> GetIdentityProviderByNameAsync(string name);
    Task<IdentityProvider?> GetIdentityProviderAsync(Guid id);
    Task<Guid> CreateIdentityProviderAsync(IdentityProvider provider);
    Task UpdateIdentityProviderAsync(IdentityProvider provider);
    Task DeleteIdentityProviderAsync(Guid id);

    // Schedule Templates
    Task<List<ScheduleTemplate>> GetScheduleTemplatesAsync(bool activeOnly = true);
    Task<ScheduleTemplate?> GetScheduleTemplateAsync(Guid id);
    Task<Guid> CreateScheduleTemplateAsync(ScheduleTemplate template);
    Task UpdateScheduleTemplateAsync(ScheduleTemplate template);
    Task DeleteScheduleTemplateAsync(Guid id);

    // Sync Audit Logs
    Task<List<SyncAuditLog>> GetSyncAuditLogsAsync(Guid? syncRunId = null, int? limit = null);
    Task<List<SyncAuditLog>> GetSyncAuditLogsByStepRunAsync(Guid stepRunId);

    // Sync Step Runs
    Task<SyncStepRun?> GetSyncStepRunAsync(Guid stepRunId);

    // Sync Project Runs
    Task<List<SyncProjectRun>> GetSyncProjectRunsAsync(Guid? projectId = null, int? limit = null);

    // Sync Project Management
    Task<int> ResetStuckSyncProjectAsync(Guid projectId);
    Task<int> ResetAllStuckSyncProjectsAsync();
    Task<List<PostSyncTask>> GetPostSyncTasksForRunsAsync(List<Guid> runIds);
    Task CancelSyncRunAsync(Guid runId);

    // Advanced Search
    Task<(List<IdentityObject> Items, int TotalCount)> AdvancedSearchObjectsAsync(
        string? objectClass = null, string? source = null, string? displayName = null,
        string? email = null, string? dn = null, bool? isActive = null,
        List<Guid>? tagIds = null, Guid? connectionId = null,
        int page = 1, int pageSize = 50,
        string? scopeWhereClause = null, DynamicParameters? scopeParams = null);

    // Active Directory Connections (filtered)
    Task<List<DirectoryConnection>> GetActiveDirectoryConnectionsAsync();

    // Tags with usage
    Task<Tag?> GetTagWithWorkflowCountAsync(Guid tagId);
    Task<List<Guid>> GetObjectIdsByTagIdsAsync(List<Guid> tagIds);

    // Object tag filtering for advanced search
    Task<List<Guid>> GetObjectTagIdsAsync(List<Guid> objectIds);

    // Sync Step Tags (SyncStepId -> List<Tag>)
    Task<Dictionary<Guid, List<Tag>>> GetSyncStepTagsAsync(List<Guid> stepIds);

    // Sync Project counts
    Task<int> GetSyncProjectRunCountAsync(Guid? projectId = null);

    // System Settings - Data Clearing
    Task<int> GetViolationCountAsync();
    Task<int> GetCampaignCountAsync();
    Task<int> GetAssignmentCountAsync();
    Task<(int AssignmentsDeleted, int CampaignsDeleted, int ViolationsDeleted)> ClearAllViolationsWithCampaignsAsync();
    Task ClearAllAccessReviewDataAsync();

    // Field Lookup with Usage (Organization Center integration)
    Task<List<FieldValueWithUsage>> GetFieldValuesWithUsageAsync(string fieldName);
    Task<Dictionary<string, int>> GetFieldLookupCountsAsync();
    Task<(List<Identity> Items, int TotalCount)> GetIdentitiesByFieldValueAsync(string fieldName, string value, int page = 1, int pageSize = 50);

    // Organizational Folder Policies
    Task<List<OrganizationalFolder>> GetFoldersForPolicyAsync(Guid policyId);

    // Dashboard
    Task<Dictionary<string, int>> GetIdentityTypeBreakdownAsync();
}
