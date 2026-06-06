using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service for matching ApplicationUser accounts to Identity records during authentication.
    /// Implements UC-AUTH-04: Login Person Matching
    /// Pure Dapper implementation - no EF Core.
    /// </summary>
    public class PersonMatchingAuthenticationService
    {
        private readonly string _connectionString;
        private readonly PersonMatchingService _personMatchingService;
        private readonly ILogger<PersonMatchingAuthenticationService> _logger;

        public PersonMatchingAuthenticationService(
            IConfiguration configuration,
            PersonMatchingService personMatchingService,
            ILogger<PersonMatchingAuthenticationService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _personMatchingService = personMatchingService;
            _logger = logger;
        }

        /// <summary>
        /// Matches or creates a Identity record for an ApplicationUser during authentication.
        /// Called after successful login or registration.
        /// </summary>
        /// <param name="user">The ApplicationUser who just authenticated</param>
        /// <returns>The matched or newly created Identity record</returns>
        public async Task<Identity> MatchOrCreatePersonForUserAsync(ApplicationUser user)
        {
            try
            {
                _logger.LogInformation("Starting Person matching for ApplicationUser {UserId} ({Email})",
                    user.Id, user.Email);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // If user already has a PersonId, verify it exists and return it
                if (user.PersonId.HasValue)
                {
                    var existingPerson = await connection.QueryFirstOrDefaultAsync<Identity>(
                        "SELECT * FROM Identities WHERE Id = @PersonId",
                        new { PersonId = user.PersonId.Value });

                    if (existingPerson != null)
                    {
                        _logger.LogInformation("ApplicationUser {UserId} already linked to Person {PersonId}",
                            user.Id, existingPerson.Id);
                        return existingPerson;
                    }
                    else
                    {
                        _logger.LogWarning("ApplicationUser {UserId} has PersonId {PersonId} but Person not found. Will re-match.",
                            user.Id, user.PersonId.Value);
                        user.PersonId = null; // Clear invalid reference
                    }
                }

                // Convert ApplicationUser to Identity for matching
                var identityForMatching = ConvertUserToIdentity(user);

                // Use PersonMatchingService to find or create Person
                var matchResult = await _personMatchingService.MatchOrCreatePersonAsync(identityForMatching);

                if (matchResult.Person == null)
                {
                    _logger.LogError("PersonMatchingService returned null Person for user {UserId}", user.Id);
                    throw new InvalidOperationException($"Failed to match or create Person for user {user.Id}");
                }

                // Link the ApplicationUser to the Person
                await connection.ExecuteAsync(
                    "UPDATE AspNetUsers SET PersonId = @PersonId WHERE Id = @UserId",
                    new { PersonId = matchResult.Person.Id, UserId = user.Id });

                _logger.LogInformation("Successfully linked ApplicationUser {UserId} to Person {PersonId} (Confidence: {Confidence}, Method: {Method})",
                    user.Id, matchResult.Person.Id, matchResult.Confidence, matchResult.MatchMethod);

                return matchResult.Person;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error matching Person for ApplicationUser {UserId}", user.Id);
                throw;
            }
        }

        /// <summary>
        /// Converts an ApplicationUser to an IdentityObject for Identity matching.
        /// The IdentityObject is temporary and used only for matching purposes.
        /// </summary>
        private IdentityObject ConvertUserToIdentity(ApplicationUser user)
        {
            return new IdentityObject
            {
                Id = Guid.NewGuid(), // Temporary ID for matching
                SourceType = "ApplicationUser",
                SourceUniqueId = user.Id,
                Email = user.Email ?? string.Empty,
                Username = user.UserName ?? string.Empty,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                Department = user.Department,
                JobTitle = user.Title,
                IsActive = user.IsActive,
                IsAuthoritative = false // ApplicationUser is not authoritative (AD/Azure AD are)
            };
        }

        /// <summary>
        /// Gets the Identity record linked to an ApplicationUser, if any.
        /// </summary>
        public async Task<Identity?> GetPersonForUserAsync(string userId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var user = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM AspNetUsers WHERE Id = @UserId",
                new { UserId = userId });

            if (user?.PersonId == null)
                return null;

            // Get identity with objects
            var identity = await connection.QueryFirstOrDefaultAsync<Identity>(
                "SELECT * FROM Identities WHERE Id = @PersonId",
                new { PersonId = user.PersonId.Value });

            if (identity != null)
            {
                // Load related objects
                identity.Objects = (await connection.QueryAsync<IdentityObject>(
                    "SELECT * FROM Objects WHERE IdentityId = @IdentityId",
                    new { IdentityId = identity.Id })).ToList();
            }

            return identity;
        }

        /// <summary>
        /// Unlinks a Person from an ApplicationUser (for administrative purposes).
        /// </summary>
        public async Task UnlinkPersonFromUserAsync(string userId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var user = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM AspNetUsers WHERE Id = @UserId",
                new { UserId = userId });

            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            _logger.LogInformation("Unlinking Person {PersonId} from ApplicationUser {UserId}",
                user.PersonId, userId);

            await connection.ExecuteAsync(
                "UPDATE AspNetUsers SET PersonId = NULL WHERE Id = @UserId",
                new { UserId = userId });
        }

        /// <summary>
        /// Manually links an ApplicationUser to a specific Person (for administrative purposes).
        /// </summary>
        public async Task LinkUserToPersonAsync(string userId, Guid personId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var user = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM AspNetUsers WHERE Id = @UserId",
                new { UserId = userId });

            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            var person = await connection.QueryFirstOrDefaultAsync<Identity>(
                "SELECT * FROM Identities WHERE Id = @PersonId",
                new { PersonId = personId });

            if (person == null)
                throw new InvalidOperationException($"Identity {personId} not found");

            _logger.LogInformation("Manually linking ApplicationUser {UserId} to Person {PersonId}",
                userId, personId);

            await connection.ExecuteAsync(
                "UPDATE AspNetUsers SET PersonId = @PersonId WHERE Id = @UserId",
                new { PersonId = personId, UserId = userId });
        }
    }
}
