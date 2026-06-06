using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services.PersonMatching;

/// <summary>
/// Result of a person matching attempt with confidence scoring.
/// Enterprise-grade matching includes audit trail and reasoning.
/// </summary>
public class PersonMatchResult
{
    /// <summary>
    /// The matched identity (null if no match found).
    /// </summary>
    public Identity? MatchedIdentity { get; set; }

    /// <summary>
    /// Confidence score 0-100. Higher = more confident in match.
    /// 90+ = High confidence (auto-link)
    /// 70-89 = Medium confidence (auto-link with audit)
    /// 50-69 = Low confidence (review queue)
    /// Below 50 = No match
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>
    /// The strategy that produced this match.
    /// </summary>
    public string MatchStrategy { get; set; } = string.Empty;

    /// <summary>
    /// The specific attribute(s) that matched.
    /// </summary>
    public string MatchedOn { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable explanation of the match decision.
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a new identity that should be created.
    /// </summary>
    public bool ShouldCreateNew { get; set; }

    /// <summary>
    /// Whether this match requires manual review.
    /// </summary>
    public bool RequiresReview { get; set; }

    /// <summary>
    /// Alternative matches found (for review queue).
    /// </summary>
    public List<AlternativeMatch> Alternatives { get; set; } = new();

    /// <summary>
    /// Timestamp of match attempt.
    /// </summary>
    public DateTime MatchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Create a successful high-confidence match result.
    /// </summary>
    public static PersonMatchResult Matched(Identity identity, int confidence, string strategy, string matchedOn, string reasoning)
    {
        return new PersonMatchResult
        {
            MatchedIdentity = identity,
            Confidence = confidence,
            MatchStrategy = strategy,
            MatchedOn = matchedOn,
            Reasoning = reasoning,
            ShouldCreateNew = false,
            RequiresReview = confidence < 70
        };
    }

    /// <summary>
    /// Create a "no match found, create new" result.
    /// </summary>
    public static PersonMatchResult CreateNew(string reasoning)
    {
        return new PersonMatchResult
        {
            MatchedIdentity = null,
            Confidence = 0,
            MatchStrategy = "CreateNew",
            MatchedOn = "N/A",
            Reasoning = reasoning,
            ShouldCreateNew = true,
            RequiresReview = false
        };
    }

    /// <summary>
    /// Create a "needs review" result with multiple potential matches.
    /// </summary>
    public static PersonMatchResult NeedsReview(List<AlternativeMatch> alternatives, string reasoning)
    {
        return new PersonMatchResult
        {
            MatchedIdentity = null,
            Confidence = 0,
            MatchStrategy = "ManualReview",
            MatchedOn = "Multiple",
            Reasoning = reasoning,
            ShouldCreateNew = false,
            RequiresReview = true,
            Alternatives = alternatives
        };
    }
}

/// <summary>
/// An alternative match candidate for review queue.
/// </summary>
public class AlternativeMatch
{
    public Identity Identity { get; set; } = null!;
    public int Confidence { get; set; }
    public string MatchedOn { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

/// <summary>
/// Strategy interface for person matching algorithms.
/// Implement this to create custom matching logic.
/// </summary>
public interface IPersonMatchingStrategy
{
    /// <summary>
    /// Unique identifier for this strategy.
    /// </summary>
    string StrategyId { get; }

    /// <summary>
    /// Display name for UI.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of how this strategy works.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Priority order (lower = tried first).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this strategy can match the given object.
    /// </summary>
    bool CanMatch(IdentityObject obj);

    /// <summary>
    /// Attempt to match the object to an existing identity.
    /// </summary>
    Task<PersonMatchResult> MatchAsync(
        IdentityObject obj,
        PersonMatchingContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context provided to matching strategies.
/// </summary>
public class PersonMatchingContext
{
    /// <summary>
    /// Repository for database lookups.
    /// </summary>
    public required ISyncRepository Repository { get; init; }

    /// <summary>
    /// The sync step configuration. Nullable for PersonMatch/PersonCreate project types
    /// which don't have workflow steps.
    /// </summary>
    public SyncStep? Step { get; init; }

    /// <summary>
    /// Source connection ID.
    /// </summary>
    public Guid SourceConnectionId { get; init; }

    /// <summary>
    /// Minimum confidence threshold for auto-matching.
    /// </summary>
    public int MinConfidenceThreshold { get; init; } = 70;

    /// <summary>
    /// Whether to create new identities for unmatched objects.
    /// </summary>
    public bool CreateNewIdentities { get; init; } = true;

    /// <summary>
    /// Whether to queue low-confidence matches for review.
    /// </summary>
    public bool EnableReviewQueue { get; init; } = true;

    /// <summary>
    /// Cache of already-matched identities (email -> identity).
    /// </summary>
    public Dictionary<string, Identity> EmailCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cache of already-matched identities (employeeId -> identity).
    /// </summary>
    public Dictionary<string, Identity> EmployeeIdCache { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Configuration for person matching behavior.
/// </summary>
public class PersonMatchingConfig
{
    /// <summary>
    /// Matching mode: Service (fast), Script (customizable), or Hybrid.
    /// </summary>
    public MatchingMode Mode { get; set; } = MatchingMode.Service;

    /// <summary>
    /// Selected strategy ID (for Service mode).
    /// </summary>
    public string StrategyId { get; set; } = "composite";

    /// <summary>
    /// Custom script ID (for Script mode).
    /// </summary>
    public Guid? CustomScriptId { get; set; }

    /// <summary>
    /// Minimum confidence for auto-match (0-100).
    /// </summary>
    public int MinConfidence { get; set; } = 70;

    /// <summary>
    /// Create new identities for unmatched objects.
    /// </summary>
    public bool CreateNewIdentities { get; set; } = true;

    /// <summary>
    /// Send low-confidence matches to review queue.
    /// </summary>
    public bool EnableReviewQueue { get; set; } = false;

    /// <summary>
    /// Attribute weights for composite matching (attribute -> weight).
    /// </summary>
    public Dictionary<string, int> AttributeWeights { get; set; } = new()
    {
        { "Email", 100 },
        { "EmployeeId", 95 },
        { "UserPrincipalName", 85 },
        { "Username", 75 },
        { "DisplayName", 50 },
        { "Name", 40 }
    };
}

public enum MatchingMode
{
    /// <summary>
    /// Use compiled C# service (fastest, production-ready).
    /// </summary>
    Service,

    /// <summary>
    /// Use Roslyn script (customizable, editable).
    /// </summary>
    Script,

    /// <summary>
    /// Try service first, fall back to script for edge cases.
    /// </summary>
    Hybrid
}
