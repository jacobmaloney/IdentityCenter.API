using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services.PersonMatching.Strategies;

/// <summary>
/// Email-based matching strategy - highest confidence for email matches.
/// </summary>
public class EmailMatchingStrategy : IPersonMatchingStrategy
{
    public string StrategyId => "email";
    public string Name => "Email Match";
    public string Description => "Matches on email address. High confidence when email is available.";
    public int Priority => 1;

    public bool CanMatch(IdentityObject obj)
    {
        return !string.IsNullOrWhiteSpace(obj.Email);
    }

    public async Task<PersonMatchResult> MatchAsync(
        IdentityObject obj,
        PersonMatchingContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(obj.Email))
        {
            return PersonMatchResult.CreateNew("No email available for matching");
        }

        var email = obj.Email.Trim().ToLowerInvariant();

        // Check cache first
        if (context.EmailCache.TryGetValue(email, out var cachedIdentity))
        {
            return PersonMatchResult.Matched(
                cachedIdentity,
                confidence: 98,
                strategy: StrategyId,
                matchedOn: $"Email: {email}",
                reasoning: $"Exact email match (cached): {email}");
        }

        // Database lookup
        var identity = await context.Repository.FindIdentityByEmailAsync(email, cancellationToken);

        if (identity != null)
        {
            // Cache the result
            context.EmailCache[email] = identity;

            return PersonMatchResult.Matched(
                identity,
                confidence: 98,
                strategy: StrategyId,
                matchedOn: $"Email: {email}",
                reasoning: $"Exact email match: {email}");
        }

        // No match
        return PersonMatchResult.CreateNew($"No identity found with email: {email}");
    }
}

/// <summary>
/// Username (sAMAccountName) matching strategy - uses the Username property.
/// </summary>
public class UsernameMatchingStrategy : IPersonMatchingStrategy
{
    public string StrategyId => "username";
    public string Name => "Username Match";
    public string Description => "Matches on sAMAccountName/Username. Good for AD environments.";
    public int Priority => 3;

    public bool CanMatch(IdentityObject obj)
    {
        return !string.IsNullOrWhiteSpace(obj.Username);
    }

    public async Task<PersonMatchResult> MatchAsync(
        IdentityObject obj,
        PersonMatchingContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(obj.Username))
        {
            return PersonMatchResult.CreateNew("No username available for matching");
        }

        var username = obj.Username.Trim();

        // Database lookup
        var identity = await context.Repository.FindIdentityByUsernameAsync(username, cancellationToken);

        if (identity != null)
        {
            return PersonMatchResult.Matched(
                identity,
                confidence: 80,
                strategy: StrategyId,
                matchedOn: $"Username: {username}",
                reasoning: $"Exact username match: {username}");
        }

        // No match
        return PersonMatchResult.CreateNew($"No identity found with username: {username}");
    }
}

/// <summary>
/// UPN matching strategy - derives UPN from email or constructs from username.
/// </summary>
public class UPNMatchingStrategy : IPersonMatchingStrategy
{
    public string StrategyId => "upn";
    public string Name => "UPN Match";
    public string Description => "Matches on UserPrincipalName. Best for AD-integrated environments.";
    public int Priority => 2;

    public bool CanMatch(IdentityObject obj)
    {
        // Can match if we have email (often same as UPN) or username
        return !string.IsNullOrWhiteSpace(obj.Email) || !string.IsNullOrWhiteSpace(obj.Username);
    }

    public async Task<PersonMatchResult> MatchAsync(
        IdentityObject obj,
        PersonMatchingContext context,
        CancellationToken cancellationToken = default)
    {
        // Use email as UPN candidate (common pattern in AD)
        var upn = obj.Email?.Trim();

        if (string.IsNullOrWhiteSpace(upn))
        {
            return PersonMatchResult.CreateNew("No UPN/email available for matching");
        }

        // Database lookup
        var identity = await context.Repository.FindIdentityByUPNAsync(upn, cancellationToken);

        if (identity != null)
        {
            return PersonMatchResult.Matched(
                identity,
                confidence: 90,
                strategy: StrategyId,
                matchedOn: $"UPN: {upn}",
                reasoning: $"Exact UPN match: {upn}");
        }

        // No match
        return PersonMatchResult.CreateNew($"No identity found with UPN: {upn}");
    }
}

/// <summary>
/// Name-based matching strategy - lower confidence but catches fuzzy matches.
/// </summary>
public class NameMatchingStrategy : IPersonMatchingStrategy
{
    public string StrategyId => "name";
    public string Name => "Name Match";
    public string Description => "Matches on FirstName + LastName. Lower confidence, handles duplicates.";
    public int Priority => 5;

    public bool CanMatch(IdentityObject obj)
    {
        return !string.IsNullOrWhiteSpace(obj.FirstName) && !string.IsNullOrWhiteSpace(obj.LastName);
    }

    public async Task<PersonMatchResult> MatchAsync(
        IdentityObject obj,
        PersonMatchingContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(obj.FirstName) || string.IsNullOrWhiteSpace(obj.LastName))
        {
            return PersonMatchResult.CreateNew("First and/or last name not available for matching");
        }

        var firstName = obj.FirstName.Trim();
        var lastName = obj.LastName.Trim();

        // Database lookup
        var identities = await context.Repository.FindIdentitiesByNameAsync(firstName, lastName, cancellationToken);

        if (identities.Count == 0)
        {
            return PersonMatchResult.CreateNew($"No identity found with name: {firstName} {lastName}");
        }

        if (identities.Count == 1)
        {
            var identity = identities[0];

            // Additional confidence boost if department or email domain match
            var confidence = 60;
            var matchDetails = $"{firstName} {lastName}";

            if (!string.IsNullOrWhiteSpace(obj.Email) &&
                !string.IsNullOrWhiteSpace(identity.PrimaryEmail) &&
                GetEmailDomain(obj.Email) == GetEmailDomain(identity.PrimaryEmail))
            {
                confidence = 75;
                matchDetails += $" + email domain ({GetEmailDomain(obj.Email)})";
            }

            if (!string.IsNullOrWhiteSpace(obj.Department) &&
                !string.IsNullOrWhiteSpace(identity.Department) &&
                obj.Department.Equals(identity.Department, StringComparison.OrdinalIgnoreCase))
            {
                confidence += 10;
                matchDetails += $" + department ({obj.Department})";
            }

            return PersonMatchResult.Matched(
                identity,
                confidence: confidence,
                strategy: StrategyId,
                matchedOn: $"Name: {firstName} {lastName}",
                reasoning: $"Name match: {matchDetails}");
        }

        // Multiple matches - pick best candidate or flag for review
        var bestMatch = identities.First();
        var alternatives = identities.Skip(1).Select(alt => new AlternativeMatch
        {
            Identity = alt,
            Confidence = 35,
            MatchedOn = $"Name: {firstName} {lastName}",
            Reasoning = $"Alternative match: {alt.DisplayName}"
        }).ToList();

        return new PersonMatchResult
        {
            MatchedIdentity = bestMatch,
            Confidence = 40,
            MatchStrategy = StrategyId,
            MatchedOn = $"Name: {firstName} {lastName}",
            Reasoning = $"Multiple identities ({identities.Count}) found with name: {firstName} {lastName}. Best candidate selected.",
            RequiresReview = true,
            Alternatives = alternatives
        };
    }

    private static string GetEmailDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email.Substring(atIndex + 1).ToLowerInvariant() : string.Empty;
    }
}

/// <summary>
/// Employee ID matching strategy - uses extended attribute lookup.
/// Note: EmployeeId is stored in ObjectAttributes, not directly on IdentityObject.
/// </summary>
public class EmployeeIdMatchingStrategy : IPersonMatchingStrategy
{
    public string StrategyId => "employeeid";
    public string Name => "Employee ID Match";
    public string Description => "Matches on Employee ID. High confidence when HR data is available.";
    public int Priority => 2;

    public bool CanMatch(IdentityObject obj)
    {
        // This strategy requires ObjectAttributes - for now, always return true
        // and handle gracefully in MatchAsync
        return true;
    }

    public Task<PersonMatchResult> MatchAsync(
        IdentityObject obj,
        PersonMatchingContext context,
        CancellationToken cancellationToken = default)
    {
        // EmployeeId is in ObjectAttributes - this strategy requires
        // the caller to provide attributes or use the Configurable strategy
        // For base implementation, we skip this match
        return Task.FromResult(PersonMatchResult.CreateNew(
            "EmployeeId matching requires ObjectAttributes. Use Configurable strategy for attribute-based matching."));
    }
}
