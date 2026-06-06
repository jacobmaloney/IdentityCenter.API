namespace DataAccessLibrary.Models;

/// <summary>
/// Result of API key validation
/// </summary>
public class ApiKeyValidationResult
{
    public bool IsValid { get; set; }
    public Guid KeyId { get; set; }
    public string? KeyName { get; set; }
    public string? KeyType { get; set; }
    public string? Scopes { get; set; }
    public Guid? AgentId { get; set; }
    public string? UserId { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Basic user info returned from risk data queries
/// </summary>
public class RiskUserInfo
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
}

/// <summary>
/// Violation count grouped by severity
/// </summary>
public class ViolationCount
{
    public string Severity { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Risk distribution item for org summary
/// </summary>
public class RiskDistributionItem
{
    public string RiskLevel { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Risk trend data point from history table
/// </summary>
public class RiskTrendDataPoint
{
    public DateTime Date { get; set; }
    public double OverallRiskScore { get; set; }
    public int AnomalyCount { get; set; }
    public int HighRiskUserCount { get; set; }
    public int ViolationCount { get; set; }
}

/// <summary>
/// User info with title for peer group analysis
/// </summary>
public class PeerUserInfo
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Peer group metrics for statistical comparison
/// </summary>
public class PeerMetrics
{
    public Guid UserId { get; set; }
    public int GroupCount { get; set; }
    public int AdminGroupCount { get; set; }
    public double RiskScore { get; set; }
}
