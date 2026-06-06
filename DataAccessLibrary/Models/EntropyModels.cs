namespace DataAccessLibrary.Models;

/// <summary>
/// Models for the Entropy Engine and Drift Tracking (Phase 3).
/// </summary>
public static class EntropyModels
{
    /// <summary>
    /// A snapshot of entropy at a point in time.
    /// </summary>
    public class EntropySnapshot
    {
        public Guid Id { get; set; }
        public string SnapshotType { get; set; } = string.Empty;
        public decimal Score { get; set; }
        public string? Components { get; set; }
        public DateTime CalculatedAt { get; set; }
    }

    /// <summary>
    /// Result of full entropy calculation across all dimensions.
    /// </summary>
    public class EntropyResult
    {
        public decimal OverallScore { get; set; }
        public decimal StructuralScore { get; set; }
        public decimal TemporalScore { get; set; }
        public decimal BehavioralScore { get; set; }
        public string OverallLevel { get; set; } = "Unknown";
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

        public static string ScoreToLevel(decimal score) => score switch
        {
            >= 80m => "Critical",
            >= 60m => "High",
            >= 40m => "Moderate",
            >= 20m => "Low",
            _ => "Minimal"
        };
    }

    /// <summary>
    /// A detected drift event for an identity.
    /// </summary>
    public class DriftRecord
    {
        public Guid Id { get; set; }
        public Guid IdentityId { get; set; }
        public string DriftType { get; set; } = string.Empty;
        public decimal DriftMagnitude { get; set; }
        public string? PreviousValue { get; set; }
        public string? CurrentValue { get; set; }
        public DateTime DetectedAt { get; set; }
        public bool IsAcknowledged { get; set; }
        public string? AcknowledgedBy { get; set; }
        public DateTime? AcknowledgedAt { get; set; }

        /// <summary>
        /// Display name of the identity (populated for display queries).
        /// </summary>
        public string? DisplayName { get; set; }
    }

    /// <summary>
    /// Baseline data for drift detection: identity's state at last sync.
    /// </summary>
    public class IdentityDriftBaseline
    {
        public Guid IdentityId { get; set; }
        public int? GroupCountAtLastSync { get; set; }
        public decimal? RiskScoreAtLastSync { get; set; }
        public decimal? RiskScore { get; set; }
    }
}
