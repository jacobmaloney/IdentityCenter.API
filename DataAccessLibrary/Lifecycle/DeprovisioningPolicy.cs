using Dapper;
using Microsoft.Data.SqlClient;

namespace DataAccessLibrary.Lifecycle;

/// <summary>
/// THE DEPROVISIONING POLICY -- IdentityCenter-only governance.
///
/// This is the SINGLE GATE to the Deprovisioned state (LifecycleState = 2 in the
/// ARS 3-state model: 0=Active, 1=Disabled, 2=Deprovisioned). Nothing in IC may
/// promote a record to Deprovisioned without first asking this policy.
///
/// WHY A POLICY: in Active Roles, "disabled" (the AD account-disable bit) and
/// "deprovisioned" (edsvaDeprovisionStatus + edsvaDeprovisionDate, which ARMS the
/// retention/delete clock) are SEPARATE states, and whether a leaver is merely
/// DISABLED or fully DEPROVISIONED is a POLICY decision. IC mirrors that: by
/// DEFAULT a leaver / gone-from-source record is DISABLED(1) and retained
/// indefinitely (no clock, never purged). ONLY when this policy is ENABLED and the
/// record matches its scope + criteria is the record promoted to DEPROVISIONED(2),
/// stamping DeletedAt (= ARS edsvaDeprovisionDate) and arming the purge clock.
///
/// SAFE DEFAULT: the policy ships DISABLED (enabled=false). With it off, NOTHING in
/// IC ever reaches state 2, so the purge job -- which only ever targets state 2 --
/// is permanently inert until an admin deliberately turns the policy on. That is
/// the guardrail: no automatic destruction out of the box.
///
/// STORAGE: lives in the generic Settings table under Category='Lifecycle' (the
/// same place the retention window and orphan-net switch already live), read via
/// Dapper so it works identically from the API (ObjectsController tombstone path)
/// and from the Schedule worker (IdentityLifecycleEvaluationJob). This is the
/// "SIMPLE" integration Jacob asked for -- the heavyweight CompliancePolicy
/// detection/violation framework is deliberately NOT used here; this is a gate, not
/// a violation detector.
///
/// IC-ONLY: the Settings keys are read from the IdentityCenter database. Conduit has
/// no policies and never reads these keys -- it is the dumb sync pump; governance
/// (this gate) is the paid IC layer.
///
/// STORED-NOT-IN-FLIGHT: this gate is consulted by the nightly Identity evaluation
/// job (operating on the stored Identities table at rest) and by the tombstone
/// endpoint (operating on the stored Objects table). It is NOT part of the Conduit
/// sync stream -- the bulk-upsert sync path only ever moves rows between Active(0)
/// and Disabled(1) and never reaches this gate.
/// </summary>
public static class DeprovisioningPolicy
{
    public const string SettingsCategory = "Lifecycle";

    /// <summary>Master enable. Default OFF. When false, NOTHING is ever deprovisioned;
    /// leavers / gone-from-source records are Disabled(1) and retained.</summary>
    public const string KeyEnabled = "DeprovisioningPolicyEnabled";

    /// <summary>Which stored tables the policy may promote to Deprovisioned:
    /// "Objects", "Identities", or "Both" (default). A scope the policy does not
    /// cover stays Disabled(1) even when the policy is enabled.</summary>
    public const string KeyScope = "DeprovisioningPolicyScope";

    /// <summary>Criterion: an HR-terminated leaver (Identities) qualifies for
    /// Deprovisioned. Default ON. When off, terminated leavers are Disabled, not
    /// deprovisioned.</summary>
    public const string KeyCriteriaHrLeaver = "DeprovisioningPolicyHrLeaver";

    /// <summary>Criterion: a record gone-from-source / Conduit-tombstoned (Objects)
    /// OR an orphan-of-active-Objects (Identities) qualifies for Deprovisioned.
    /// Default ON. When off, gone-from-source records are Disabled, not
    /// deprovisioned.</summary>
    public const string KeyCriteriaGoneFromSource = "DeprovisioningPolicyGoneFromSource";

    /// <summary>Criterion: a record that has been Disabled/inactive for at least
    /// GraceDays qualifies for Deprovisioned. Default OFF (the strongest signals --
    /// leaver / gone -- are the default qualifiers; the "disabled for N days"
    /// sweep is opt-in so merely-suspended accounts are not aged into deletion
    /// unless an admin asks).</summary>
    public const string KeyCriteriaDisabledGrace = "DeprovisioningPolicyDisabledGrace";

    /// <summary>Grace window (days) for the DisabledGrace criterion. Default 90.</summary>
    public const string KeyDisabledGraceDays = "DeprovisioningPolicyDisabledGraceDays";

    public const int DefaultDisabledGraceDays = 90;

    public enum PolicyScope { Objects, Identities, Both }

    /// <summary>Immutable snapshot of the policy, read once per job run.</summary>
    public sealed record Snapshot(
        bool Enabled,
        PolicyScope Scope,
        bool HrLeaverQualifies,
        bool GoneFromSourceQualifies,
        bool DisabledGraceQualifies,
        int DisabledGraceDays)
    {
        public bool CoversObjects => Enabled && (Scope == PolicyScope.Objects || Scope == PolicyScope.Both);
        public bool CoversIdentities => Enabled && (Scope == PolicyScope.Identities || Scope == PolicyScope.Both);

        /// <summary>The all-off, all-safe default snapshot used whenever the policy
        /// is disabled, unreadable, or its keys are absent.</summary>
        public static Snapshot Disabled => new(
            Enabled: false,
            Scope: PolicyScope.Both,
            HrLeaverQualifies: true,
            GoneFromSourceQualifies: true,
            DisabledGraceQualifies: false,
            DisabledGraceDays: DefaultDisabledGraceDays);
    }

    /// <summary>
    /// Load the policy snapshot from the Settings table on an OPEN connection.
    /// FAILS SAFE: any error, a missing master-enable key, or an unparseable value
    /// yields the Disabled snapshot (enabled=false) -- i.e. nothing gets
    /// deprovisioned. We never fail OPEN into destruction.
    /// </summary>
    public static async Task<Snapshot> LoadAsync(SqlConnection openConnection)
    {
        try
        {
            var rows = (await openConnection.QueryAsync<(string Key, string? Value)>(
                @"SELECT [Key], [Value] FROM Settings WHERE Category = @cat",
                new { cat = SettingsCategory })).ToList();

            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows) map[r.Key] = r.Value;

            var enabled = ReadBool(map, KeyEnabled, defaultValue: false);
            if (!enabled) return Snapshot.Disabled; // short-circuit: policy off = safe default.

            var scope = ReadScope(map, KeyScope, PolicyScope.Both);
            var hrLeaver = ReadBool(map, KeyCriteriaHrLeaver, defaultValue: true);
            var gone = ReadBool(map, KeyCriteriaGoneFromSource, defaultValue: true);
            var disabledGrace = ReadBool(map, KeyCriteriaDisabledGrace, defaultValue: false);
            var graceDays = ReadInt(map, KeyDisabledGraceDays, DefaultDisabledGraceDays);
            if (graceDays < 1) graceDays = 1;

            return new Snapshot(
                Enabled: true,
                Scope: scope,
                HrLeaverQualifies: hrLeaver,
                GoneFromSourceQualifies: gone,
                DisabledGraceQualifies: disabledGrace,
                DisabledGraceDays: graceDays);
        }
        catch
        {
            // Fail safe: unreadable policy => disabled => nothing is deprovisioned.
            return Snapshot.Disabled;
        }
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string?> map, string key, bool defaultValue)
    {
        if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return defaultValue;
        raw = raw.Trim();
        if (bool.TryParse(raw, out var b)) return b;
        if (raw == "1") return true;
        if (raw == "0") return false;
        return defaultValue;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string?> map, string key, int defaultValue)
        => map.TryGetValue(key, out var raw) && int.TryParse(raw, out var n) ? n : defaultValue;

    private static PolicyScope ReadScope(IReadOnlyDictionary<string, string?> map, string key, PolicyScope defaultValue)
    {
        if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return defaultValue;
        return Enum.TryParse<PolicyScope>(raw.Trim(), ignoreCase: true, out var s) ? s : defaultValue;
    }
}
