namespace DataAccessLibrary.Models;

/// <summary>
/// Models for the Effective Access Engine (Phase 2).
/// </summary>
public static class EffectiveAccessModels
{
    /// <summary>
    /// Direct object-to-group membership from ObjectGroupMemberships.
    /// </summary>
    public class DirectMembership
    {
        public Guid ObjectId { get; set; }
        public Guid GroupId { get; set; }
        public Guid? MembershipId { get; set; }
    }

    /// <summary>
    /// Group-to-group nesting (a group is a member of another group).
    /// </summary>
    public class GroupNesting
    {
        public Guid ChildGroupId { get; set; }
        public Guid ParentGroupId { get; set; }
    }

    /// <summary>
    /// Materialized effective access entry (flattened recursive membership).
    /// </summary>
    public class EffectiveAccessEntry
    {
        public Guid Id { get; set; }
        public Guid ObjectId { get; set; }
        public Guid GroupId { get; set; }
        public string? AccessPath { get; set; }
        public int Depth { get; set; }
        public bool IsDirect { get; set; }
        public Guid? SourceMembershipId { get; set; }
        public DateTime MaterializedAt { get; set; }

        /// <summary>
        /// Group name (populated for display queries).
        /// </summary>
        public string? GroupName { get; set; }
    }

    /// <summary>
    /// Blast radius metrics for a single group.
    /// </summary>
    public class GroupBlastRadiusRecord
    {
        public Guid GroupId { get; set; }
        public int DirectMemberCount { get; set; }
        public int EffectiveMemberCount { get; set; }
        public int MaxDepth { get; set; }
        public int NestedGroupCount { get; set; }
        public decimal BlastRadiusScore { get; set; }
        public DateTime CalculatedAt { get; set; }

        /// <summary>
        /// Group name (populated for display queries).
        /// </summary>
        public string? GroupName { get; set; }
    }
}
