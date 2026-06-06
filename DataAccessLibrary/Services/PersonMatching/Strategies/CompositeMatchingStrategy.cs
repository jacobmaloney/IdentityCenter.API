using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.PersonMatching.Strategies;

/// <summary>
/// Enterprise-grade composite matching strategy.
/// Tries multiple attributes in priority order, calculates weighted confidence score.
/// This is the recommended production strategy.
/// </summary>
public class CompositeMatchingStrategy : IPersonMatchingStrategy
{
    private readonly List<IPersonMatchingStrategy> _strategies;

    public string StrategyId => "composite";
    public string Name => "Composite (Recommended)";
    public string Description => "Tries all matching attributes in priority order: Email → EmployeeId → UPN → Username → Name. Calculates weighted confidence score.";
    public int Priority => 0; // Highest - this is the default

    public CompositeMatchingStrategy()
    {
        // Initialize sub-strategies in priority order
        _strategies = new List<IPersonMatchingStrategy>
        {
            new EmailMatchingStrategy(),
            new EmployeeIdMatchingStrategy(),
            new UPNMatchingStrategy(),
            new UsernameMatchingStrategy(),
            new NameMatchingStrategy()
        };
    }

    public bool CanMatch(IdentityObject obj)
    {
        // Can always attempt - will try all strategies
        return true;
    }

    public async Task<PersonMatchResult> MatchAsync(
        IdentityObject obj,
        PersonMatchingContext context,
        CancellationToken cancellationToken = default)
    {
        var allMatches = new List<(PersonMatchResult result, IPersonMatchingStrategy strategy)>();
        PersonMatchResult? bestMatch = null;

        // Try each strategy in order
        foreach (var strategy in _strategies)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!strategy.CanMatch(obj))
                continue;

            var result = await strategy.MatchAsync(obj, context, cancellationToken);

            if (result.MatchedIdentity != null)
            {
                allMatches.Add((result, strategy));

                // If we get a high-confidence match, use it immediately
                if (result.Confidence >= 90)
                {
                    bestMatch = result;
                    break;
                }

                // Track best match so far
                if (bestMatch == null || result.Confidence > bestMatch.Confidence)
                {
                    bestMatch = result;
                }
            }
        }

        // If we found a good match, return it
        if (bestMatch?.MatchedIdentity != null && bestMatch.Confidence >= context.MinConfidenceThreshold)
        {
            // Enhance the result with composite strategy info
            return PersonMatchResult.Matched(
                bestMatch.MatchedIdentity,
                bestMatch.Confidence,
                strategy: $"Composite/{bestMatch.MatchStrategy}",
                matchedOn: bestMatch.MatchedOn,
                reasoning: $"Best match from composite strategy: {bestMatch.Reasoning}");
        }

        // Check if we have multiple potential matches (needs review)
        if (allMatches.Count > 1)
        {
            var uniqueIdentities = allMatches
                .Where(m => m.result.MatchedIdentity != null)
                .GroupBy(m => m.result.MatchedIdentity!.Id)
                .ToList();

            if (uniqueIdentities.Count > 1)
            {
                // Different strategies matched different identities - needs review
                var alternatives = uniqueIdentities.Select(g =>
                {
                    var best = g.OrderByDescending(m => m.result.Confidence).First();
                    return new AlternativeMatch
                    {
                        Identity = best.result.MatchedIdentity!,
                        Confidence = best.result.Confidence,
                        MatchedOn = best.result.MatchedOn,
                        Reasoning = $"Matched by {best.strategy.Name}: {best.result.Reasoning}"
                    };
                }).ToList();

                return PersonMatchResult.NeedsReview(
                    alternatives,
                    $"Multiple different identities matched by different strategies ({alternatives.Count} candidates)");
            }
        }

        // Low confidence match - flag for review if enabled
        if (bestMatch?.MatchedIdentity != null && context.EnableReviewQueue)
        {
            // Factory method already sets RequiresReview = true for confidence < 70
            return PersonMatchResult.Matched(
                bestMatch.MatchedIdentity,
                bestMatch.Confidence,
                strategy: $"Composite/{bestMatch.MatchStrategy}",
                matchedOn: bestMatch.MatchedOn,
                reasoning: $"Low confidence match ({bestMatch.Confidence}%): {bestMatch.Reasoning}");
        }

        // No match found - create new if enabled
        if (context.CreateNewIdentities)
        {
            var attemptedStrategies = _strategies
                .Where(s => s.CanMatch(obj))
                .Select(s => s.Name)
                .ToList();

            return PersonMatchResult.CreateNew(
                $"No match found after trying {attemptedStrategies.Count} strategies: {string.Join(", ", attemptedStrategies)}");
        }

        return PersonMatchResult.CreateNew("No match found and new identity creation disabled");
    }
}

/// <summary>
/// Configurable weighted matching strategy.
/// Uses step's AttributeMappings with UseForMatching and MatchWeight.
/// </summary>
public class ConfigurableMatchingStrategy : IPersonMatchingStrategy
{
    public string StrategyId => "configurable";
    public string Name => "Configurable (Step-Based)";
    public string Description => "Uses the step's attribute mapping configuration with UseForMatching and MatchWeight settings.";
    public int Priority => 0;

    public bool CanMatch(IdentityObject obj) => true;

    public async Task<PersonMatchResult> MatchAsync(
        IdentityObject obj,
        PersonMatchingContext context,
        CancellationToken cancellationToken = default)
    {
        // Get matching mappings from step configuration
        var matchingMappings = context.Step.AttributeMappings?
            .Where(m => m.UseForMatching && m.IsEnabled)
            .OrderByDescending(m => m.MatchWeight)
            .ToList() ?? new List<AttributeMapping>();

        if (!matchingMappings.Any())
        {
            // Fall back to composite strategy if no mappings configured
            var composite = new CompositeMatchingStrategy();
            return await composite.MatchAsync(obj, context, cancellationToken);
        }

        PersonMatchResult? bestMatch = null;

        foreach (var mapping in matchingMappings)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await TryMatchByAttributeAsync(obj, mapping, context, cancellationToken);

            if (result?.MatchedIdentity != null)
            {
                // Apply weight as confidence modifier
                var adjustedConfidence = Math.Min(100, result.Confidence * mapping.MatchWeight / 100);
                result.Confidence = adjustedConfidence;

                if (adjustedConfidence >= 90)
                {
                    return result; // High confidence, use immediately
                }

                if (bestMatch == null || result.Confidence > bestMatch.Confidence)
                {
                    bestMatch = result;
                }
            }
        }

        if (bestMatch != null && bestMatch.Confidence >= context.MinConfidenceThreshold)
        {
            return bestMatch;
        }

        return context.CreateNewIdentities
            ? PersonMatchResult.CreateNew($"No match found using {matchingMappings.Count} configured attributes")
            : PersonMatchResult.CreateNew("No match found");
    }

    private async Task<PersonMatchResult?> TryMatchByAttributeAsync(
        IdentityObject obj,
        AttributeMapping mapping,
        PersonMatchingContext context,
        CancellationToken cancellationToken)
    {
        var targetAttr = mapping.TargetAttribute?.ToLowerInvariant() ?? "";
        Identity? identity = null;
        string matchedOn = "";

        switch (targetAttr)
        {
            case "email":
            case "mail":
            case "primaryemail":
                if (!string.IsNullOrEmpty(obj.Email))
                {
                    identity = await context.Repository.FindIdentityByEmailAsync(obj.Email, cancellationToken);
                    matchedOn = $"Email: {obj.Email}";
                }
                break;

            case "employeeid":
            case "employeenumber":
                // EmployeeId is in ObjectAttributes, not IdentityObject
                // Skip for now - needs attribute lookup
                break;

            case "samaccountname":
            case "username":
                if (!string.IsNullOrEmpty(obj.Username))
                {
                    identity = await context.Repository.FindIdentityByUsernameAsync(obj.Username, cancellationToken);
                    matchedOn = $"Username: {obj.Username}";
                }
                break;

            case "userprincipalname":
            case "upn":
                // In AD, UPN is often email-like - try email if available
                var upn = obj.Email ?? obj.Username;
                if (!string.IsNullOrEmpty(upn))
                {
                    identity = await context.Repository.FindIdentityByUPNAsync(upn, cancellationToken);
                    matchedOn = $"UPN: {upn}";
                }
                break;

            case "displayname":
                if (!string.IsNullOrEmpty(obj.DisplayName))
                {
                    identity = await context.Repository.FindIdentityByDisplayNameAsync(obj.DisplayName, cancellationToken);
                    matchedOn = $"DisplayName: {obj.DisplayName}";
                }
                break;
        }

        if (identity != null)
        {
            return PersonMatchResult.Matched(
                identity,
                confidence: 85, // Base confidence, will be adjusted by weight
                strategy: "Configurable",
                matchedOn: matchedOn,
                reasoning: $"Matched by configured attribute {mapping.SourceAttribute} (weight: {mapping.MatchWeight})");
        }

        return null;
    }
}
