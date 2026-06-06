namespace DataAccessLibrary.Models;

/// <summary>
/// Result of integrity score calculation for a single identity.
/// </summary>
public class IntegrityResult
{
    public Guid IdentityId { get; set; }
    public decimal Score { get; set; }
    public string Level { get; set; } = "Unknown";
    public List<IntegrityFactor> Factors { get; set; } = new();
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Computes Level from Score using standard thresholds.
    /// </summary>
    public static string ScoreToLevel(decimal score) => score switch
    {
        >= 90m => "Excellent",
        >= 75m => "High",
        >= 55m => "Medium",
        >= 35m => "Low",
        _ => "Critical"
    };
}

/// <summary>
/// Individual factor contributing to integrity score.
/// </summary>
public class IntegrityFactor
{
    public string Name { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal RawScore { get; set; }
    public decimal WeightedScore { get; set; }
    public string? Detail { get; set; }
}

/// <summary>
/// Organization-wide integrity summary.
/// </summary>
public class IntegritySummary
{
    public decimal AverageScore { get; set; }
    public string AverageLevel { get; set; } = "Unknown";
    public int TotalIdentities { get; set; }
    public int ExcellentCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public int CriticalCount { get; set; }
    public DateTime CalculatedAt { get; set; }
}

/// <summary>
/// Point-in-time integrity history record.
/// </summary>
public class IntegrityHistoryPoint
{
    public Guid Id { get; set; }
    public Guid IdentityId { get; set; }
    public decimal IntegrityScore { get; set; }
    public string IntegrityLevel { get; set; } = string.Empty;
    public string? FactorBreakdown { get; set; }
    public DateTime CalculatedAt { get; set; }
}

/// <summary>
/// Record of a governance action taken on an identity/object/group.
/// </summary>
public class GovernanceActionRecord
{
    public Guid Id { get; set; }
    public Guid? IdentityId { get; set; }
    public Guid? ObjectId { get; set; }
    public Guid? GroupId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? TriggerSource { get; set; }
    public string? PreviousState { get; set; }
    public string? NewState { get; set; }
    public string? Reason { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public string? PerformedBy { get; set; }
    public DateTime PerformedAt { get; set; }
    public DateTime? RevertedAt { get; set; }
    public string? RevertedBy { get; set; }

    public bool IsReverted => RevertedAt.HasValue;
}
