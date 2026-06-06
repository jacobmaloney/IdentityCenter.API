namespace DataAccessLibrary.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Table-mapped entities (schema: V056__LicenseMonitoring.sql)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Organization-level license pool sourced from an Entra ID tenant (or other
/// vendor connection). One row per SKU per DirectoryConnection.
/// </summary>
public class LicensePool
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceConnectionId { get; set; }

    /// <summary>Microsoft SKU GUID, e.g. "c7df2760-2c81-4ef7-b578-5b5392b571df".</summary>
    public string SkuId { get; set; } = string.Empty;

    /// <summary>Human-readable product name, e.g. "Microsoft 365 E5".</summary>
    public string SkuName { get; set; } = string.Empty;

    /// <summary>Microsoft part number, e.g. "SPE_E5". Null for non-Microsoft sources.</summary>
    public string? SkuPartNumber { get; set; }

    public int TotalUnits { get; set; }
    public int ConsumedUnits { get; set; }
    public int WarningUnits { get; set; }
    public int SuspendedUnits { get; set; }

    /// <summary>
    /// Computed column in SQL: TotalUnits - ConsumedUnits - WarningUnits - SuspendedUnits.
    /// Read-only from the database; do not set directly.
    /// </summary>
    public int AvailableUnits { get; set; }

    /// <summary>Monthly cost per license unit. Null if pricing is not configured.</summary>
    public decimal? CostPerUnitMonthly { get; set; }

    public string? Currency { get; set; } = "USD";

    /// <summary>Minimum buffer percentage (available/total). Below this triggers a warning.</summary>
    public int? MinBufferPercent { get; set; }

    /// <summary>Maximum utilization percentage. Above this triggers an alert.</summary>
    public int? MaxUtilizationPercent { get; set; }

    /// <summary>Manual override: Healthy, Warning, Critical, or null for auto-calculated.</summary>
    public string? AlertThreshold { get; set; }

    /// <summary>Human-readable product name (e.g. "Microsoft 365 E5" instead of "ENTERPRISEPREMIUM").</summary>
    public string? FriendlyName { get; set; }

    /// <summary>Admin notes/annotations about this license pool.</summary>
    public string? Notes { get; set; }

    /// <summary>Billing period: Monthly, Annual, or OneTime. Affects cost calculations.</summary>
    public string? BillingPeriod { get; set; } = "Monthly";

    /// <summary>License/CAL type: UserCAL, DeviceCAL, ServerCAL, Subscription, or null for auto-detect.</summary>
    public string? LicenseType { get; set; }

    /// <summary>Effective monthly cost (auto-calculated: Annual/12, Monthly as-is).</summary>
    public decimal EffectiveMonthlyRate => CostPerUnitMonthly.HasValue
        ? BillingPeriod == "Annual" ? CostPerUnitMonthly.Value / 12m : CostPerUnitMonthly.Value
        : 0m;

    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // ── V073: Category + lifecycle metadata ──

    /// <summary>FK to LicenseCategories. Groups pools for filtering and reporting.</summary>
    public Guid? LicenseCategoryId { get; set; }

    /// <summary>Cost center for financial chargeback and reporting.</summary>
    public string? CostCenter { get; set; }

    /// <summary>Pool type: Synced (Entra Graph API), Manual (SQL entitlements), AutoCount (computed from Objects table).</summary>
    public string PoolType { get; set; } = "Synced";

    /// <summary>For AutoCount pools: which ObjectClass to count (e.g., "user", "computer").</summary>
    public string? AutoCountObjectClass { get; set; }

    /// <summary>For AutoCount pools: which connection's objects to count.</summary>
    public Guid? AutoCountConnectionId { get; set; }

    /// <summary>For AutoCount pools: optional extra WHERE clause (e.g., "operatingSystem LIKE '%Server%'").</summary>
    public string? AutoCountFilter { get; set; }

    /// <summary>When the auto-count was last refreshed.</summary>
    public DateTime? LastAutoCountAt { get; set; }

    /// <summary>True when this pool was auto-generated from discovery (e.g., SQL SPNs).</summary>
    public bool AutoCreatedFromSync { get; set; }

    /// <summary>How often this pool should be reviewed (days). Null = no auto-review.</summary>
    public int? ReviewFrequencyDays { get; set; }

    /// <summary>Timestamp of the last access review completed for this pool.</summary>
    public DateTime? LastReviewedAt { get; set; }

    // ── Breach action settings (pool acts as its own policy) ──

    /// <summary>Auto-create access review campaign when threshold is breached.</summary>
    public bool OnBreachCreateReview { get; set; }

    /// <summary>Send email notification when threshold is breached.</summary>
    public bool OnBreachSendEmail { get; set; }

    /// <summary>Queue Teams message when threshold is breached.</summary>
    public bool OnBreachNotifyTeams { get; set; }

    /// <summary>Specific reviewer for breach-triggered reviews. Null = use fallback rules.</summary>
    public Guid? BreachReviewerId { get; set; }

    /// <summary>Display name of the breach reviewer.</summary>
    public string? BreachReviewerName { get; set; }

    /// <summary>Email template to use for breach notifications.</summary>
    public Guid? BreachEmailTemplateId { get; set; }

    /// <summary>
    /// Per-pool opt-in: when an auto-triggered access review campaign ends with
    /// pending assignments, deny by default instead of escalating. Off by default.
    /// </summary>
    public bool AutoDenyOnIncomplete { get; set; }

    // ── Pool scoping rules (filter which objects count toward this pool) ──

    /// <summary>Legacy single-tag filter. Kept for back-compat; mirrors the first id in AutoCountTagIds.</summary>
    public Guid? AutoCountTagId { get; set; }

    /// <summary>CSV of tag GUIDs. An object only needs to have ANY of these tags to count (OR semantics). Null/empty = no tag filter.</summary>
    public string? AutoCountTagIds { get; set; }

    /// <summary>Parsed view of <see cref="AutoCountTagIds"/>. Setter writes both AutoCountTagIds (CSV) and AutoCountTagId (first id, for back-compat).</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<Guid> AutoCountTagIdList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AutoCountTagIds))
                return AutoCountTagId.HasValue ? new List<Guid> { AutoCountTagId.Value } : new List<Guid>();
            return AutoCountTagIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();
        }
        set
        {
            var clean = (value ?? new List<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
            AutoCountTagIds = clean.Count == 0 ? null : string.Join(",", clean);
            AutoCountTagId = clean.Count == 0 ? null : clean[0];
        }
    }

    /// <summary>Only count objects whose DN contains this OU path. Null = no OU filter.</summary>
    public string? AutoCountOUFilter { get; set; }

    /// <summary>Only count objects in this department. Null = no department filter.</summary>
    public string? AutoCountDepartment { get; set; }

    /// <summary>Object to notify on breach — can be a user, group, or distribution list from any connection.</summary>
    public Guid? BreachNotifyObjectId { get; set; }

    /// <summary>Display name of the notification recipient object.</summary>
    public string? BreachNotifyObjectName { get; set; }

    /// <summary>Object class of the recipient (user, group, contact, etc.).</summary>
    public string? BreachNotifyObjectClass { get; set; }

    // ── Populated by service layer, not Dapper auto-map ──
    public List<LicenseServicePlan> ServicePlans { get; set; } = new();
    public LicenseCategory? Category { get; set; }
}

/// <summary>
/// Per-user license assignment, linked to the Objects table.
/// </summary>
public class LicenseAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicensePoolId { get; set; }

    /// <summary>FK to Objects.Id — the identity that holds this license.</summary>
    public Guid ObjectId { get; set; }

    /// <summary>When the license was originally assigned. Null if unknown.</summary>
    public DateTime? AssignedAt { get; set; }

    /// <summary>How the license is assigned: Direct, Group, or Policy.</summary>
    public string AssignmentSource { get; set; } = "Direct";

    /// <summary>ObjectId of the group driving a group-based assignment. Null for direct.</summary>
    public Guid? SourceGroupId { get; set; }

    /// <summary>Last time the user actively used this license (from Graph activity reports).</summary>
    public DateTime? LastUsedAt { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    // ── Populated by service layer ──
    /// <summary>Display name of the user (joined from Objects).</summary>
    public string? UserDisplayName { get; set; }
    /// <summary>Username / sAMAccountName (joined from Objects).</summary>
    public string? Username { get; set; }
    /// <summary>UPN (joined from Objects).</summary>
    public string? UserPrincipalName { get; set; }
    /// <summary>SKU name (joined from LicensePools).</summary>
    public string? SkuName { get; set; }
}

/// <summary>
/// Feature-level service plan detail within a license SKU.
/// E.g. "Exchange Online (Plan 2)", "Microsoft Teams", "SharePoint Online".
/// </summary>
public class LicenseServicePlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicensePoolId { get; set; }

    /// <summary>Microsoft service plan GUID.</summary>
    public string ServicePlanId { get; set; } = string.Empty;

    public string ServicePlanName { get; set; } = string.Empty;

    /// <summary>Success, Disabled, PendingInput, or null.</summary>
    public string? ProvisioningStatus { get; set; }

    /// <summary>User or Company.</summary>
    public string? AppliesTo { get; set; }
}

/// <summary>
/// Daily snapshot of a license pool's utilization, used for trend charts and
/// waste reporting. One row per pool per day (enforced by unique index).
/// </summary>
public class LicenseUsageSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicensePoolId { get; set; }

    public DateTime SnapshotDate { get; set; }

    public int TotalUnits { get; set; }
    public int ConsumedUnits { get; set; }

    /// <summary>Licenses consumed by users inactive for more than 90 days.</summary>
    public int WastedUnits { get; set; }

    /// <summary>WastedUnits * CostPerUnitMonthly at snapshot time. Null if pricing unknown.</summary>
    public decimal? EstimatedWasteMonthly { get; set; }

    // ── Populated by service layer ──
    public string? SkuName { get; set; }
}

/// <summary>
/// AI-generated or rule-generated optimization recommendation for a user's license.
/// </summary>
public class LicenseOptimizationRecommendation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObjectId { get; set; }

    /// <summary>Pool this recommendation relates to. Null for cross-pool recommendations.</summary>
    public Guid? LicensePoolId { get; set; }

    /// <summary>Remove, Downgrade, or Reassign.</summary>
    public string RecommendationType { get; set; } = string.Empty;

    public string? CurrentSkuName { get; set; }
    public string? RecommendedSkuName { get; set; }

    /// <summary>Human-readable reason, e.g. "No sign-in activity in 127 days".</summary>
    public string Reason { get; set; } = string.Empty;

    public decimal? EstimatedMonthlySavings { get; set; }

    /// <summary>Pending, Approved, Applied, or Dismissed.</summary>
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Identity (username) of the admin who reviewed this recommendation.</summary>
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? AppliedAt { get; set; }

    // ── Populated by service layer ──
    public string? UserDisplayName { get; set; }
    public string? Username { get; set; }
    public string? UserPrincipalName { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs / view models (no table backing)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregated summary for the License Monitoring dashboard header.
/// </summary>
public class LicenseDashboardSummary
{
    /// <summary>Total license units purchased across all active pools.</summary>
    public int TotalLicenses { get; set; }

    /// <summary>Total license units currently consumed.</summary>
    public int ConsumedLicenses { get; set; }

    /// <summary>Licenses held by users inactive for more than InactiveDaysThreshold days.</summary>
    public int WastedLicenses { get; set; }

    /// <summary>Threshold (days) used to determine waste. Default: 90.</summary>
    public int InactiveDaysThreshold { get; set; } = 90;

    /// <summary>Total monthly spend across all pools with pricing configured.</summary>
    public decimal TotalMonthlySpend { get; set; }

    /// <summary>Estimated monthly spend on wasted licenses.</summary>
    public decimal EstimatedMonthlyWaste { get; set; }

    /// <summary>Number of distinct license pools (SKUs) being monitored.</summary>
    public int PoolCount { get; set; }

    /// <summary>Number of pending optimization recommendations.</summary>
    public int PendingRecommendations { get; set; }

    /// <summary>Potential annual savings if all pending recommendations are applied.</summary>
    public decimal PotentialAnnualSavings => EstimatedMonthlyWaste * 12;

    /// <summary>Waste percentage: WastedLicenses / ConsumedLicenses. 0 if no consumed licenses.</summary>
    public decimal WastePercent => ConsumedLicenses > 0
        ? Math.Round((decimal)WastedLicenses / ConsumedLicenses * 100, 1)
        : 0m;
}

/// <summary>
/// Per-user waste detail row for the waste report table.
/// </summary>
public class LicenseWasteReport
{
    public Guid ObjectId { get; set; }
    public Guid LicensePoolId { get; set; }
    public Guid SourceConnectionId { get; set; }
    public string? UserDisplayName { get; set; }
    public string? Username { get; set; }
    public string? UserPrincipalName { get; set; }

    /// <summary>Name of the wasted SKU.</summary>
    public string SkuName { get; set; } = string.Empty;

    /// <summary>How the license is assigned: Direct, Group, or Policy.</summary>
    public string AssignmentSource { get; set; } = string.Empty;

    /// <summary>Last time the user was observed using this license. Null = never.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Days since the license was last used, or since assignment if never used.</summary>
    public int DaysInactive { get; set; }

    /// <summary>Monthly cost of this wasted license. Null if pricing not configured.</summary>
    public decimal? EstimatedMonthlyCost { get; set; }

    /// <summary>Recommended action generated for this user+pool pair, if any.</summary>
    public string? RecommendationType { get; set; }
    public Guid? RecommendationId { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// V071: License Categories
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// User-defined grouping for license pools. Enables filtering, reporting, and
/// cost attribution across pool types (M365, CALs, SQL, Azure, Dev/Test).
/// </summary>
public class LicenseCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Color { get; set; } = "#6366f1";
    public string? Icon { get; set; } = "fa-layer-group";
    public int SortOrder { get; set; } = 100;
    public bool IsBuiltIn { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    // Service-populated counts
    public int PoolCount { get; set; }
    public decimal TotalMonthlySpend { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// V072: License Assignment Lifecycle Events
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Audit record of a state transition for a license assignment.
/// Event types: Assigned, FirstUsed, Dormant, Reactivated, Revoked, Removed.
/// </summary>
public class LicenseAssignmentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssignmentId { get; set; }
    public Guid LicensePoolId { get; set; }
    public Guid ObjectId { get; set; }

    /// <summary>State transition type.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Who triggered the event — user ID, "System", "Sync", "Policy:name".</summary>
    public string? Actor { get; set; }

    /// <summary>Human-readable reason for the transition.</summary>
    public string? Reason { get; set; }

    /// <summary>Optional JSON payload with context.</summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class LicenseAssignmentEventTypes
{
    public const string Assigned = "Assigned";
    public const string FirstUsed = "FirstUsed";
    public const string Dormant = "Dormant";
    public const string Reactivated = "Reactivated";
    public const string Revoked = "Revoked";
    public const string Removed = "Removed";
}

// ─────────────────────────────────────────────────────────────────────────────
// V072: License Threshold Breaches
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Record of a pool breaching its configured capacity thresholds.
/// Used to trigger notifications, campaigns, and historical alerting.
/// </summary>
public class LicenseThresholdBreach
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicensePoolId { get; set; }

    /// <summary>Type of threshold: MinBufferPercent, MaxUtilizationPercent, DaysUntilExhaustion.</summary>
    public string ThresholdType { get; set; } = string.Empty;

    /// <summary>The configured threshold value that was breached.</summary>
    public decimal ThresholdValue { get; set; }

    /// <summary>The actual measured value that breached the threshold.</summary>
    public decimal ActualValue { get; set; }

    /// <summary>Warning or Critical.</summary>
    public string Severity { get; set; } = "Warning";

    public DateTime BreachedAt { get; set; } = DateTime.UtcNow;

    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedReason { get; set; }

    public bool NotificationSent { get; set; }

    /// <summary>FK to Campaigns if an access review was auto-created.</summary>
    public Guid? CampaignId { get; set; }

    /// <summary>FK to CompliancePolicyViolations if this breach was policy-driven.</summary>
    public Guid? ViolationId { get; set; }

    // Service-populated
    public string? PoolName { get; set; }
    public string? PoolSkuName { get; set; }
}

public static class LicenseThresholdTypes
{
    public const string MinBufferPercent = "MinBufferPercent";
    public const string MaxUtilizationPercent = "MaxUtilizationPercent";
    public const string DaysUntilExhaustion = "DaysUntilExhaustion";
}

// ─────────────────────────────────────────────────────────────────────────────
// CAL Auto-Attribution Candidates (User CAL + Device CAL)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Activity-based suggestion that an object is likely consuming a CAL pool
/// without a formal LicenseAssignment row. Surfaced on the object's Licenses
/// tab so admins can promote the candidate to an assignment or dismiss it.
/// Computed on-demand — no backing table.
/// </summary>
public class LicenseAttributionCandidate
{
    public Guid PoolId { get; set; }
    public string PoolName { get; set; } = "";

    /// <summary>"UserCAL" or "DeviceCAL".</summary>
    public string LicenseType { get; set; } = "";

    /// <summary>"Synced", "Manual", or "AutoCount" — drives the Assign UX branch.</summary>
    public string PoolType { get; set; } = "";

    /// <summary>Short headline like "47 sign-ins last 30 days".</summary>
    public string SignalText { get; set; } = "";

    /// <summary>"High", "Medium", or "Low".</summary>
    public string Confidence { get; set; } = "";

    /// <summary>Longer explanation for tooltip / why-this-was-suggested copy.</summary>
    public string ReasonDetail { get; set; } = "";

    /// <summary>Available seat count on the pool — drives "no seats" UI hints.</summary>
    public int AvailableUnits { get; set; }
}
