using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Pure helpers for the approval-time SoD conflict banner in MyApprovals.razor.
///
/// Background: V115 reverted the parallel SoD system (V113/V114, ISoDRepository).
/// SoD rules now live as <see cref="CompliancePolicyRule"/> rows under the
/// seeded SoD policy (22222222-...214) with <c>RuleType=GroupMembership</c> and
/// <c>Operator=IsMemberOfAll</c>. The same FieldName JSON contract used by
/// <c>PolicyEvaluationEngine.EvaluateGroupMembershipAsync</c> is reused here:
/// <c>FieldName</c> deserializes to <c>List&lt;Guid&gt;</c> of group IDs that
/// must all be present to constitute a conflict.
///
/// These helpers are pulled out of the Razor page so they can be unit-tested
/// in isolation — the page wires them up to <c>IPolicyRepository</c> and
/// <c>ObjectGroupMemberships</c> reads + a direct
/// <c>CompliancePolicyViolations</c> insert for the override path.
/// </summary>
public static class SoDConflictDetector
{
    /// <summary>
    /// Returns true when granting <paramref name="prospectiveGroupId"/> to a
    /// requester who currently holds <paramref name="memberGroupIds"/> would
    /// trigger the rule defined by <paramref name="ruleTargetGroupIds"/>.
    ///
    /// A rule fires when:
    ///   1. The prospective group is itself one of the rule's target groups
    ///      (otherwise the rule has nothing to say about this grant), AND
    ///   2. Every other target group in the rule is already in the
    ///      requester's membership set (so the grant would be the last one
    ///      needed to complete the toxic combination).
    ///
    /// Rules with no target groups never fire (defensive — a malformed rule
    /// is a no-op, never a block).
    /// </summary>
    public static bool IsConflict(
        IReadOnlyCollection<Guid> ruleTargetGroupIds,
        IReadOnlyCollection<Guid> memberGroupIds,
        Guid prospectiveGroupId)
    {
        if (ruleTargetGroupIds == null || ruleTargetGroupIds.Count == 0) return false;
        if (!ruleTargetGroupIds.Contains(prospectiveGroupId)) return false;

        var combinedMembership = new HashSet<Guid>(memberGroupIds ?? Array.Empty<Guid>());
        combinedMembership.Add(prospectiveGroupId);

        return ruleTargetGroupIds.All(combinedMembership.Contains);
    }

    /// <summary>
    /// Parses a <see cref="CompliancePolicyRule.FieldName"/> JSON payload into
    /// a list of group GUIDs. Returns an empty list (NOT throws) on malformed
    /// input — caller treats that as "this rule is a no-op", same as the
    /// PolicyEvaluationEngine does.
    /// </summary>
    public static List<Guid> ParseTargetGroupIds(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return new List<Guid>();
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(fieldName) ?? new List<Guid>();
        }
        catch (JsonException)
        {
            return new List<Guid>();
        }
    }

    /// <summary>
    /// Filter the rule list for the SoD policy down to just the active
    /// GroupMembership / IsMemberOfAll rules — the only shape this banner
    /// understands. Other rule types under the same policy are ignored
    /// (they're evaluated by the PolicyEvaluationEngine on its own schedule).
    /// </summary>
    public static List<CompliancePolicyRule> FilterSoDRules(IEnumerable<CompliancePolicyRule>? rules)
    {
        if (rules == null) return new List<CompliancePolicyRule>();
        return rules
            .Where(r => r.IsActive
                     && string.Equals(r.RuleType, "GroupMembership", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(r.Operator, "IsMemberOfAll", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
