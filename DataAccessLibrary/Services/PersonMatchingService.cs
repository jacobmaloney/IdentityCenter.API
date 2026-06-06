using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dapper;
using DataAccessLibrary.Configuration;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// LEGACY: This service is deprecated. Use Internal Projects (Object → Person workflows) instead.
    /// The strategy-based IPersonMatchingService in DataAccessLibrary.Services.PersonMatching is the modern replacement.
    /// This class is retained only for backwards compatibility with PersonMatchingAuthenticationService.
    /// </summary>
    [Obsolete("Use Internal Projects workflow or IPersonMatchingService from DataAccessLibrary.Services.PersonMatching instead. This legacy service will be removed in a future version.")]
    public class PersonMatchingService
    {
        private readonly ILogger<PersonMatchingService> _logger;
        private readonly ISyncRepository _syncRepository;
        private readonly PersonMatchingOptions _matchingOptions;
        private readonly string _connectionString;
        private readonly FuzzyMatchingService _fuzzyMatchingService;

        public PersonMatchingService(
            ILogger<PersonMatchingService> logger,
            ISyncRepository syncRepository,
            IOptions<PersonMatchingOptions> matchingOptions,
            IConfiguration configuration,
            FuzzyMatchingService fuzzyMatchingService)
        {
            _logger = logger;
            _syncRepository = syncRepository;
            _matchingOptions = matchingOptions.Value;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _fuzzyMatchingService = fuzzyMatchingService;
        }

        /// <summary>
        /// Attempts to find or create a person for the given identity.
        /// PHASE 3 OPTIMIZATION: Accepts optional PersonLookupCache and Identity cache for O(1) in-memory lookups.
        /// THREAD-SAFETY FIX: Uses Dapper with SqlConnection for thread-safe parallel processing.
        /// Each parallel task gets its own SqlConnection instance to eliminate thread contention.
        /// </summary>
        public async Task<PersonMatchResult> MatchOrCreatePersonAsync(
            IdentityObject identityObject,
            Repositories.IdentityLookupCache? personCache = null,
            Dictionary<string, Repositories.ObjectWithAttributes>? identityCache = null)
        {
            _logger.LogInformation("Attempting to match identity {IdentityId} from {SourceType}",
                identityObject.SourceUniqueId, identityObject.SourceType);

            // Try exact email match first (highest confidence)
            if (!string.IsNullOrWhiteSpace(identityObject.Email))
            {
                var emailMatch = await TryMatchByEmailAsync(identityObject, personCache, identityCache);
                if (emailMatch != null)
                {
                    return emailMatch;
                }
            }

            // Try name + department match (medium confidence)
            if (!string.IsNullOrWhiteSpace(identityObject.FirstName) &&
                !string.IsNullOrWhiteSpace(identityObject.LastName))
            {
                var nameMatch = await TryMatchByNameAndDepartmentAsync(identityObject, personCache);
                if (nameMatch != null)
                {
                    return nameMatch;
                }
            }

            // No match found - create new person
            _logger.LogInformation("No match found for identity {IdentityId}, creating new person",
                identityObject.SourceUniqueId);

            var newPerson = await CreateNewPersonAsync(identityObject);
            return new PersonMatchResult
            {
                Person = newPerson,
                MatchMethod = "NewPerson",
                Confidence = 100,
                IsNewPerson = true
            };
        }

        /// <summary>
        /// Tries to match identity by email address.
        /// PHASE 3 OPTIMIZATION: Uses in-memory cache when available, falls back to database queries.
        /// </summary>
        private async Task<PersonMatchResult?> TryMatchByEmailAsync(
            IdentityObject identityObject,
            Repositories.IdentityLookupCache? personCache = null,
            Dictionary<string, Repositories.ObjectWithAttributes>? identityCache = null)
        {
            var email = identityObject.Email?.ToLower().Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            Identity? person = null;

            // PHASE 3 PERFORMANCE: Try cache first for O(1) lookup
            if (personCache != null && personCache.ByEmail.TryGetValue(email, out var cachedPerson))
            {
                person = cachedPerson;
                _logger.LogDebug("CACHE HIT: Found person {PersonId} by email {Email} (O(1) lookup)", person.Id, email);
            }
            else
            {
                // Fallback to database query if no cache
                person = await _syncRepository.FindIdentityByEmailAsync(email);
            }

            if (person != null)
            {
                _logger.LogInformation("Found email match for {Email}: Person {PersonId}",
                    email, person.Id);

                // NOTE: Match logging handled later after Identity is created in database
                // We cannot log the match here because the Identity record doesn't exist in the database yet

                return new PersonMatchResult
                {
                    Person = person,
                    MatchMethod = "Email",
                    Confidence = _matchingOptions.HighConfidenceThreshold,
                    IsNewPerson = false
                };
            }

            // PHASE 3 PERFORMANCE: Check existing identities via cache (reuses Phase 1 optimization!)
            Repositories.ObjectWithAttributes? existingIdentityData = null;

            if (identityCache != null)
            {
                // Search through Identity cache for matching email
                existingIdentityData = identityCache.Values.FirstOrDefault(i =>
                    i.Object.Email != null &&
                    i.Object.Email.Equals(email, StringComparison.OrdinalIgnoreCase) &&
                    i.Object.IdentityId.HasValue);

                if (existingIdentityData != null)
                {
                    _logger.LogDebug("CACHE HIT: Found identity with email {Email} via Phase 1 cache", email);
                }
            }
            else
            {
                // Fallback to database query if no cache
                var existingIdentity = await _syncRepository.FindObjectByEmailAsync(email);
                if (existingIdentity != null && existingIdentity.IdentityId.HasValue)
                {
                    existingIdentityData = new Repositories.ObjectWithAttributes
                    {
                        Object = existingIdentity,
                        Attributes = new List<ObjectAttribute>()
                    };
                }
            }

            if (existingIdentityData != null && existingIdentityData.Object.IdentityId.HasValue)
            {
                Identity? identityPerson = null;

                // Try to find person from cache first
                if (personCache != null)
                {
                    identityPerson = personCache.ByEmail.Values.FirstOrDefault(p => p.Id == existingIdentityData.Object.IdentityId.Value);
                }

                if (identityPerson == null)
                {
                    // Fallback to database
                    identityPerson = await _syncRepository.FindIdentityByIdAsync(existingIdentityData.Object.IdentityId.Value);
                }

                if (identityPerson != null)
                {
                    _logger.LogInformation("Found email match via existing identity for {Email}: Person {PersonId}",
                        email, existingIdentityData.Object.IdentityId);

                    // NOTE: Match logging handled later after Identity is created in database
                    // We cannot log the match here because the Identity record doesn't exist in the database yet

                    return new PersonMatchResult
                    {
                        Person = identityPerson,
                        MatchMethod = "Email",
                        Confidence = _matchingOptions.HighConfidenceThreshold,
                        IsNewPerson = false
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Tries to match identity by name and department.
        /// PHASE 3 OPTIMIZATION: Uses in-memory cache when available, falls back to database queries.
        /// </summary>
        private async Task<PersonMatchResult?> TryMatchByNameAndDepartmentAsync(
            IdentityObject identityObject,
            Repositories.IdentityLookupCache? personCache = null)
        {
            var firstName = identityObject.FirstName?.ToLower().Trim();
            var lastName = identityObject.LastName?.ToLower().Trim();
            var department = identityObject.Department?.ToLower().Trim();

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                return null;
            }

            List<Identity> candidates;

            // PHASE 3 PERFORMANCE: Try cache first for O(1) lookup
            if (personCache != null && personCache.ByName.TryGetValue((firstName, lastName), out var cachedCandidates))
            {
                candidates = cachedCandidates;
                _logger.LogDebug("CACHE HIT: Found {Count} person(s) by name {FirstName} {LastName} (O(1) lookup)",
                    candidates.Count, firstName, lastName);
            }
            else
            {
                // Fallback to database query if no cache
                candidates = await _syncRepository.FindIdentitiesByNameAsync(firstName, lastName);
            }

            if (!candidates.Any())
            {
                return null;
            }

            // If only one candidate, it's a good match
            if (candidates.Count == 1)
            {
                var person = candidates[0];
                var confidence = string.IsNullOrWhiteSpace(department) ?
                    _matchingOptions.MediumConfidenceThreshold :
                    (person.Department?.ToLower() == department ? _matchingOptions.HighConfidenceThreshold : _matchingOptions.MediumConfidenceThreshold);

                _logger.LogInformation("Found single name match for {FirstName} {LastName}: Person {PersonId} (confidence: {Confidence})",
                    firstName, lastName, person.Id, confidence);

                // NOTE: Match logging handled later after Identity is created in database
                // We cannot log the match here because the Identity record doesn't exist in the database yet

                return new PersonMatchResult
                {
                    Person = person,
                    MatchMethod = "NameDepartment",
                    Confidence = confidence,
                    IsNewPerson = false
                };
            }

            // Multiple candidates - use department to disambiguate
            if (!string.IsNullOrWhiteSpace(department))
            {
                var departmentMatch = candidates.FirstOrDefault(p =>
                    p.Department?.ToLower() == department);

                if (departmentMatch != null)
                {
                    _logger.LogInformation("Found name+department match for {FirstName} {LastName} in {Department}: Person {PersonId}",
                        firstName, lastName, department, departmentMatch.Id);

                    // NOTE: Match logging handled later after Identity is created in database
                    // We cannot log the match here because the Identity record doesn't exist in the database yet

                    return new PersonMatchResult
                    {
                        Person = departmentMatch,
                        MatchMethod = "NameDepartment",
                        Confidence = _matchingOptions.MediumConfidenceThreshold,
                        IsNewPerson = false
                    };
                }
            }

            // Multiple candidates with no department match - too ambiguous, don't match
            _logger.LogWarning("Multiple person matches found for {FirstName} {LastName} with no department match - creating new person",
                firstName, lastName);

            return null;
        }

        /// <summary>
        /// Creates a new person record from an identity.
        /// Thread-safe: Uses its own SqlConnection to support parallel processing.
        /// </summary>
        private async Task<Identity> CreateNewPersonAsync(IdentityObject identityObject)
        {
            var person = new Identity
            {
                Id = Guid.NewGuid(),
                DisplayName = identityObject.DisplayName ?? $"{identityObject.FirstName} {identityObject.LastName}".Trim(),
                FirstName = identityObject.FirstName,
                LastName = identityObject.LastName,
                PrimaryEmail = identityObject.Email,
                PrimaryPhone = identityObject.Phone,
                Department = identityObject.Department,
                JobTitle = identityObject.JobTitle,
                AuthoritativeSourceId = identityObject.SourceConnectionId,
                IsActive = identityObject.IsActive,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };

            // THREAD-SAFE: Create own SqlConnection to avoid threading issues with parallel processing
            const string sql = @"
                INSERT INTO Identities (Id, DisplayName, FirstName, LastName, PrimaryEmail, PrimaryPhone,
                    Department, JobTitle, AuthoritativeSourceId, IsActive, CreatedAt, LastSeenAt)
                VALUES (@Id, @DisplayName, @FirstName, @LastName, @PrimaryEmail, @PrimaryPhone,
                    @Department, @JobTitle, @AuthoritativeSourceId, @IsActive, @CreatedAt, @LastSeenAt)";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(sql, person);

            _logger.LogInformation("Created new identity {IdentityId} for object {ObjectId}",
                person.Id, identityObject.SourceUniqueId);

            // NOTE: Match logging is handled later in UpsertObjectWithDapperAsync after the IdentityObject is created
            // We cannot log the match here because the IdentityObject record doesn't exist in the database yet

            return person;
        }

        /// <summary>
        /// Updates person attributes from authoritative identity
        /// </summary>
        public async Task UpdatePersonFromAuthoritativeIdentityAsync(Identity person, IdentityObject identityObject)
        {
            if (!identityObject.IsAuthoritative)
            {
                return;
            }

            var updated = false;

            if (!string.IsNullOrWhiteSpace(identityObject.Email) && person.PrimaryEmail != identityObject.Email)
            {
                person.PrimaryEmail = identityObject.Email;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(identityObject.Phone) && person.PrimaryPhone != identityObject.Phone)
            {
                person.PrimaryPhone = identityObject.Phone;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(identityObject.Department) && person.Department != identityObject.Department)
            {
                person.Department = identityObject.Department;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(identityObject.JobTitle) && person.JobTitle != identityObject.JobTitle)
            {
                person.JobTitle = identityObject.JobTitle;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(identityObject.DisplayName) && person.DisplayName != identityObject.DisplayName)
            {
                person.DisplayName = identityObject.DisplayName;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(identityObject.FirstName) && person.FirstName != identityObject.FirstName)
            {
                person.FirstName = identityObject.FirstName;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(identityObject.LastName) && person.LastName != identityObject.LastName)
            {
                person.LastName = identityObject.LastName;
                updated = true;
            }

            if (updated)
            {
                person.ModifiedAt = DateTime.UtcNow;
                person.AuthoritativeSourceId = identityObject.SourceConnectionId;

                const string sql = @"
                    UPDATE Identities
                    SET PrimaryEmail = @PrimaryEmail, PrimaryPhone = @PrimaryPhone, Department = @Department,
                        JobTitle = @JobTitle, DisplayName = @DisplayName, FirstName = @FirstName,
                        LastName = @LastName, ModifiedAt = @ModifiedAt, AuthoritativeSourceId = @AuthoritativeSourceId
                    WHERE Id = @Id";

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync(sql, person);

                _logger.LogInformation("Updated identity {IdentityId} from authoritative object {ObjectId}",
                    person.Id, identityObject.Id);
            }
        }

        /// <summary>
        /// Logs an identity match for auditing
        /// </summary>
        private async Task LogMatchAsync(Guid identityId, Guid objectId, string matchMethod,
            int confidence, string? matchCriteria)
        {
            var matchLog = new IdentityMatchLog
            {
                Id = Guid.NewGuid(),
                IdentityId = identityId,
                ObjectId = objectId,
                MatchMethod = matchMethod,
                MatchConfidence = confidence,
                MatchCriteria = matchCriteria,
                IsManualMatch = false,
                MatchedAt = DateTime.UtcNow
            };

            const string sql = @"
                INSERT INTO IdentityMatchLogs (Id, IdentityId, ObjectId, MatchMethod, MatchConfidence,
                    MatchCriteria, IsManualMatch, MatchedAt)
                VALUES (@Id, @IdentityId, @ObjectId, @MatchMethod, @MatchConfidence,
                    @MatchCriteria, @IsManualMatch, @MatchedAt)";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(sql, matchLog);
        }

        /// <summary>
        /// Manually links an object to an identity
        /// </summary>
        public async Task<bool> ManualMatchAsync(Guid objectId, Guid identityId, string userId)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Check if object exists
                var identityObject = await connection.QueryFirstOrDefaultAsync<IdentityObject>(
                    "SELECT * FROM Objects WHERE Id = @Id",
                    new { Id = objectId },
                    transaction);

                // Check if identity exists
                var identity = await connection.QueryFirstOrDefaultAsync<Identity>(
                    "SELECT * FROM Identities WHERE Id = @Id",
                    new { Id = identityId },
                    transaction);

                if (identityObject == null || identity == null)
                {
                    await transaction.RollbackAsync();
                    return false;
                }

                // Update the object with the identity link
                await connection.ExecuteAsync(@"
                    UPDATE Objects
                    SET IdentityId = @IdentityId, MatchMethod = @MatchMethod, MatchConfidence = @MatchConfidence
                    WHERE Id = @Id",
                    new { Id = objectId, IdentityId = identityId, MatchMethod = "Manual", MatchConfidence = 100 },
                    transaction);

                // Insert match log
                var matchLog = new IdentityMatchLog
                {
                    Id = Guid.NewGuid(),
                    IdentityId = identityId,
                    ObjectId = objectId,
                    MatchMethod = "Manual",
                    MatchConfidence = 100,
                    IsManualMatch = true,
                    MatchedBy = userId,
                    MatchedAt = DateTime.UtcNow
                };

                await connection.ExecuteAsync(@"
                    INSERT INTO IdentityMatchLogs (Id, IdentityId, ObjectId, MatchMethod, MatchConfidence,
                        IsManualMatch, MatchedBy, MatchedAt)
                    VALUES (@Id, @IdentityId, @ObjectId, @MatchMethod, @MatchConfidence,
                        @IsManualMatch, @MatchedBy, @MatchedAt)",
                    matchLog,
                    transaction);

                await transaction.CommitAsync();

                _logger.LogInformation("Manual match created by {UserId}: Object {ObjectId} -> Identity {IdentityId}",
                    userId, objectId, identityId);

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Attempts to find or create a person using configurable attribute matching.
        /// Uses the configured AttributeMappings with UseForMatching=true, sorted by MatchWeight DESC.
        /// PHASE 4: Configurable per-step person matching based on attribute mappings.
        /// </summary>
        public async Task<PersonMatchResult> MatchOrCreatePersonWithConfigAsync(
            IdentityObject identityObject,
            List<AttributeMapping> matchingMappings,
            Repositories.IdentityLookupCache? personCache = null,
            Dictionary<string, Repositories.ObjectWithAttributes>? identityCache = null)
        {
            if (matchingMappings == null || !matchingMappings.Any())
            {
                // No matching mappings configured - fall back to default matching
                _logger.LogDebug("No matching mappings configured for step, using default matching");
                return await MatchOrCreatePersonAsync(identityObject, personCache, identityCache);
            }

            _logger.LogDebug("Attempting configurable match for identity {IdentityId} using {Count} configured attributes",
                identityObject.SourceUniqueId, matchingMappings.Count);

            // Sort by MatchWeight DESC - highest priority first
            var sortedMappings = matchingMappings
                .Where(m => m.UseForMatching && m.IsEnabled)
                .OrderByDescending(m => m.MatchWeight)
                .ToList();

            // Try each configured matching attribute in priority order
            foreach (var mapping in sortedMappings)
            {
                var result = await TryMatchByConfiguredAttributeAsync(
                    identityObject, mapping, personCache, identityCache);

                if (result != null)
                {
                    _logger.LogInformation("Configurable match found for {IdentityId} using {TargetAttr} (weight: {Weight}, confidence: {Confidence})",
                        identityObject.SourceUniqueId, mapping.TargetAttribute, mapping.MatchWeight, result.Confidence);
                    return result;
                }
            }

            // No match found - create new person
            _logger.LogInformation("No configurable match found for identity {IdentityId}, creating new person",
                identityObject.SourceUniqueId);

            var newPerson = await CreateNewPersonAsync(identityObject);
            return new PersonMatchResult
            {
                Person = newPerson,
                MatchMethod = "NewPerson",
                Confidence = 100,
                IsNewPerson = true
            };
        }

        /// <summary>
        /// Tries to match identity by a configured attribute mapping.
        /// PHASE 4: Supports Email, EmployeeId, Username, UPN, and Name matching.
        /// FUZZY MATCHING: When UseFuzzyMatch is enabled, uses similarity algorithms.
        /// </summary>
        private async Task<PersonMatchResult?> TryMatchByConfiguredAttributeAsync(
            IdentityObject identityObject,
            AttributeMapping mapping,
            Repositories.IdentityLookupCache? personCache = null,
            Dictionary<string, Repositories.ObjectWithAttributes>? identityCache = null)
        {
            var targetAttr = mapping.TargetAttribute?.ToLower() ?? "";
            string? sourceValue = GetSourceValueFromObject(identityObject, mapping);

            if (string.IsNullOrWhiteSpace(sourceValue))
            {
                _logger.LogDebug("Skipping match by {TargetAttr} - source value is empty", mapping.TargetAttribute);
                return null;
            }

            Identity? matchedPerson = null;
            string matchMethod = mapping.TargetAttribute ?? "Unknown";
            double matchSimilarity = 1.0;

            // FUZZY MATCHING: If enabled, use fuzzy matching for name-like attributes
            if (mapping.UseFuzzyMatch && (targetAttr == "firstname" || targetAttr == "lastname" ||
                targetAttr == "displayname" || targetAttr == "name"))
            {
                var fuzzyResult = await TryFuzzyMatchAsync(sourceValue, targetAttr, mapping, personCache);
                if (fuzzyResult.Person != null)
                {
                    matchMethod = $"Fuzzy{mapping.TargetAttribute}";
                    matchedPerson = fuzzyResult.Person;
                    matchSimilarity = fuzzyResult.Similarity;

                    _logger.LogInformation("Fuzzy match found: {SourceValue} -> {MatchedName} (similarity: {Similarity:P1}, algorithm: {Algorithm})",
                        sourceValue, matchedPerson.DisplayName, matchSimilarity, mapping.FuzzyMatchAlgorithm);
                }
            }
            else
            {
                // Exact matching
                switch (targetAttr)
                {
                    case "email":
                    case "primaryemail":
                    case "mail":
                        // Use existing email matching logic
                        var emailResult = await TryMatchByEmailAsync(identityObject, personCache, identityCache);
                        if (emailResult != null)
                        {
                            emailResult.Confidence = mapping.MatchWeight;
                            return emailResult;
                        }
                        break;

                    case "employeeid":
                    case "employeenumber":
                        matchedPerson = await _syncRepository.FindIdentityByEmployeeIdAsync(sourceValue);
                        matchMethod = "EmployeeId";
                        break;

                    case "username":
                    case "samaccountname":
                        matchedPerson = await _syncRepository.FindIdentityByUsernameAsync(sourceValue);
                        matchMethod = "Username";
                        break;

                    case "userprincipalname":
                    case "upn":
                        matchedPerson = await _syncRepository.FindIdentityByUPNAsync(sourceValue);
                        matchMethod = "UPN";
                        break;

                    case "displayname":
                        // Try to match by display name (lower confidence)
                        matchedPerson = await _syncRepository.FindIdentityByDisplayNameAsync(sourceValue);
                        matchMethod = "DisplayName";
                        break;

                    case "firstname":
                    case "lastname":
                        // Name matching requires both first and last name
                        if (!string.IsNullOrWhiteSpace(identityObject.FirstName) &&
                            !string.IsNullOrWhiteSpace(identityObject.LastName))
                        {
                            var nameResult = await TryMatchByNameAndDepartmentAsync(identityObject, personCache);
                            if (nameResult != null)
                            {
                                nameResult.Confidence = mapping.MatchWeight;
                                return nameResult;
                            }
                        }
                        break;

                    default:
                        _logger.LogDebug("Unsupported matching attribute: {TargetAttr}", mapping.TargetAttribute);
                        break;
                }
            }

            if (matchedPerson != null)
            {
                // Adjust confidence based on similarity for fuzzy matches
                int adjustedConfidence = mapping.UseFuzzyMatch
                    ? (int)(mapping.MatchWeight * matchSimilarity)
                    : mapping.MatchWeight;

                return new PersonMatchResult
                {
                    Person = matchedPerson,
                    MatchMethod = matchMethod,
                    Confidence = adjustedConfidence,
                    IsNewPerson = false
                };
            }

            return null;
        }

        /// <summary>
        /// Performs fuzzy matching against existing persons using the configured algorithm.
        /// </summary>
        private Task<(Identity? Person, double Similarity)> TryFuzzyMatchAsync(
            string sourceValue,
            string targetAttr,
            AttributeMapping mapping,
            Repositories.IdentityLookupCache? personCache)
        {
            if (personCache == null)
            {
                _logger.LogDebug("Fuzzy matching requires person cache - skipping");
                return Task.FromResult<(Identity?, double)>((null, 0));
            }

            Identity? bestMatch = null;
            double bestSimilarity = 0;

            // Get candidates based on target attribute
            IEnumerable<Identity> candidates;
            Func<Identity, string?> valueSelector;

            switch (targetAttr)
            {
                case "firstname":
                    // ByEmail has Identity values, ByName has List<Identity> values - flatten and combine
                    candidates = personCache.ByEmail.Values
                        .Concat(personCache.ByName.Values.SelectMany(list => list))
                        .DistinctBy(p => p.Id);
                    valueSelector = p => p.FirstName;
                    break;
                case "lastname":
                    candidates = personCache.ByEmail.Values
                        .Concat(personCache.ByName.Values.SelectMany(list => list))
                        .DistinctBy(p => p.Id);
                    valueSelector = p => p.LastName;
                    break;
                case "displayname":
                case "name":
                    candidates = personCache.ByEmail.Values
                        .Concat(personCache.ByName.Values.SelectMany(list => list))
                        .DistinctBy(p => p.Id);
                    valueSelector = p => p.DisplayName;
                    break;
                default:
                    return Task.FromResult<(Identity?, double)>((null, 0));
            }

            foreach (var person in candidates)
            {
                var candidateValue = valueSelector(person);
                if (string.IsNullOrWhiteSpace(candidateValue))
                    continue;

                double similarity = _fuzzyMatchingService.CalculateSimilarity(
                    sourceValue,
                    candidateValue,
                    mapping.FuzzyMatchAlgorithm);

                if (similarity >= mapping.FuzzyMatchThreshold && similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestMatch = person;
                }
            }

            return Task.FromResult((bestMatch, bestSimilarity));
        }

        /// <summary>
        /// Gets the source value from the IdentityObject based on the attribute mapping.
        /// First checks direct properties, then falls back to extended attributes collection.
        /// </summary>
        private string? GetSourceValueFromObject(IdentityObject obj, AttributeMapping mapping)
        {
            var targetAttr = mapping.TargetAttribute?.ToLower() ?? "";

            // First, check direct properties on IdentityObject
            var directValue = targetAttr switch
            {
                "email" or "primaryemail" or "mail" => obj.Email,
                "username" or "samaccountname" => obj.Username,
                "displayname" => obj.DisplayName,
                "firstname" => obj.FirstName,
                "lastname" => obj.LastName,
                "department" => obj.Department,
                "jobtitle" => obj.JobTitle,
                "phone" => obj.Phone,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(directValue))
                return directValue;

            // For attributes not directly on IdentityObject, look in extended attributes
            // Use the source attribute name from the mapping
            if (obj.Attributes != null && !string.IsNullOrWhiteSpace(mapping.SourceAttribute))
            {
                var extendedAttr = obj.Attributes.FirstOrDefault(a =>
                    a.AttributeName.Equals(mapping.SourceAttribute, StringComparison.OrdinalIgnoreCase));

                if (extendedAttr != null)
                    return extendedAttr.AttributeValue;
            }

            return null;
        }

        /// <summary>
        /// ULTRA-FAST: Batch create identities for multiple objects that have no match.
        /// Uses bulk insert stored procedure for 20-30x faster performance.
        /// Expected: 1000 persons in less than 100ms (vs 2-3 seconds with individual inserts).
        /// </summary>
        public async Task<Dictionary<Guid, Identity>> BatchCreateIdentitiesAsync(
            List<IdentityObject> unmatchedObjects,
            CancellationToken cancellationToken = default)
        {
            if (!unmatchedObjects.Any())
            {
                return new Dictionary<Guid, Identity>();
            }

            _logger.LogInformation("BATCH CREATE: Creating {Count} new identities using bulk insert", unmatchedObjects.Count);

            // Build list of Identity records to create
            var identitiesToCreate = unmatchedObjects.Select(obj => new Identity
            {
                Id = Guid.NewGuid(),
                FirstName = obj.FirstName,
                LastName = obj.LastName,
                PrimaryEmail = obj.Email,
                PrimaryPhone = obj.Phone,
                Department = obj.Department,
                JobTitle = obj.JobTitle,
                AuthoritativeSourceId = obj.SourceConnectionId,
                IsActive = obj.IsActive,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            }).ToList();

            // Bulk insert all identities in one database call
            var startTime = DateTime.UtcNow;
            await _syncRepository.BulkInsertIdentitiesAsync(identitiesToCreate, cancellationToken);
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("BATCH CREATE COMPLETE: Created {Count} identities in {Elapsed}ms ({Rate} persons/sec)",
                identitiesToCreate.Count, elapsed, (int)(identitiesToCreate.Count / (elapsed / 1000)));

            // Return dictionary mapping ObjectId -> Identity for quick lookup
            var result = new Dictionary<Guid, Identity>();
            for (int i = 0; i < unmatchedObjects.Count; i++)
            {
                result[unmatchedObjects[i].Id] = identitiesToCreate[i];
            }

            return result;
        }
    }

    /// <summary>
    /// Result of a person matching operation
    /// </summary>
    public class PersonMatchResult
    {
        public Identity Person { get; set; } = null!;
        public string MatchMethod { get; set; } = string.Empty;
        public int Confidence { get; set; }
        public bool IsNewPerson { get; set; }
    }
}
