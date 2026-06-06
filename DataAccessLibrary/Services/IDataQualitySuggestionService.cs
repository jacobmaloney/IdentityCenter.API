using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Analyzes an identity/object against its peers (same ObjectClass + Department)
/// to identify missing fields that most peers have populated.
/// Returns actionable suggestions like "93% of Sales users have a phone number."
/// </summary>
public interface IDataQualitySuggestionService
{
    /// <summary>
    /// Get data quality suggestions for a specific object.
    /// Compares field completeness against peers in the same ObjectClass + Department.
    /// </summary>
    Task<List<DataQualitySuggestion>> GetSuggestionsAsync(Guid objectId, CancellationToken ct = default);
}

/// <summary>
/// A single data quality suggestion for a missing field.
/// </summary>
public class DataQualitySuggestion
{
    /// <summary>Field name (e.g., "Phone", "Email", "Manager")</summary>
    public string FieldName { get; set; } = "";

    /// <summary>Display label for the field</summary>
    public string FieldLabel { get; set; } = "";

    /// <summary>Icon class for the field</summary>
    public string Icon { get; set; } = "fa-circle-info";

    /// <summary>What percentage of peers have this field populated (0-100)</summary>
    public int PeerPercent { get; set; }

    /// <summary>Total peers in the comparison group</summary>
    public int PeerCount { get; set; }

    /// <summary>Human-readable suggestion text</summary>
    public string Message { get; set; } = "";

    /// <summary>Priority: Higher = more important to fix. Based on peer% and field importance.</summary>
    public int Priority { get; set; }

    /// <summary>Severity: "high" (>90% peers have it), "medium" (70-90%), "low" (<70%)</summary>
    public string Severity { get; set; } = "medium";
}
