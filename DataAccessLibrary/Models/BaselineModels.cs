namespace DataAccessLibrary.Models;

/// <summary>
/// Models for Golden Image Baselines (Phase 6).
/// </summary>
public static class BaselineModels
{
    /// <summary>
    /// A "known good" snapshot of an entity's state.
    /// </summary>
    public class GoldenImageBaseline
    {
        public Guid Id { get; set; }
        public string EntityType { get; set; } = string.Empty; // Identity, Object, Group
        public Guid EntityId { get; set; }
        public string? BaselineData { get; set; }
        public string? GroupMemberships { get; set; }
        public decimal? IntegrityScoreAtBaseline { get; set; }
        public decimal? RiskScoreAtBaseline { get; set; }
        public DateTime CapturedAt { get; set; }
        public string? CapturedBy { get; set; }
        public bool IsActive { get; set; }
        public string? Notes { get; set; }

        /// <summary>
        /// Display name of the entity (populated for display queries).
        /// </summary>
        public string? DisplayName { get; set; }
    }

    /// <summary>
    /// Result of comparing current state to a golden image baseline.
    /// </summary>
    public class BaselineDeviation
    {
        public Guid EntityId { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public decimal IntegrityDelta { get; set; }
        public decimal RiskDelta { get; set; }
        public int AddedGroupCount { get; set; }
        public int RemovedGroupCount { get; set; }
        public decimal OverallDeviationScore { get; set; }
        public DateTime BaselineCapturedAt { get; set; }
    }
}
