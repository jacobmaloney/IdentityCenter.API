using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for ownership suggestion analysis — queries org data for group members.
/// </summary>
public interface IOwnershipSuggestionRepository
{
    /// <summary>
    /// Gets org data (manager, department, direct report count) for all members of a group.
    /// </summary>
    Task<List<GroupMemberOrgData>> GetGroupMembersWithOrgDataAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Gets all groups with no owner, along with member counts.
    /// </summary>
    Task<List<OrphanedGroupSummary>> GetOrphanedGroupSummariesAsync(CancellationToken ct = default);
}
