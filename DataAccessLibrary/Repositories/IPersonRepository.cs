using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories
{
    /// <summary>
    /// Repository interface for Person and Identity operations.
    /// High-performance Dapper-based data access.
    /// </summary>
    public interface IPersonRepository
    {
        /// <summary>
        /// Get paginated list of persons with identity and group counts.
        /// </summary>
        Task<(List<Identity> Persons, int TotalCount)> GetPersonsPagedAsync(
            int pageNumber = 1,
            int pageSize = 20,
            string? searchTerm = null,
            bool? isActive = null);

        /// <summary>
        /// Get statistics: total persons, identities, groups, average identities per person, and active/inactive counts.
        /// </summary>
        Task<(int TotalPersons, int TotalIdentities, int TotalGroups, double AvgIdentitiesPerPerson, int ActivePersons, int InactivePersons)> GetStatisticsAsync();

        /// <summary>
        /// Get identity and group counts for specific persons.
        /// </summary>
        Task<(Dictionary<Guid, int> IdentityCounts, Dictionary<Guid, int> GroupCounts, Dictionary<Guid, int> TagCounts)> GetCountsForPersonsAsync(
            IEnumerable<Guid> personIds);

        /// <summary>
        /// Get detailed information for a person including all identities and group memberships.
        /// </summary>
        Task<PersonDetails?> GetPersonDetailsAsync(Guid personId);

        /// <summary>
        /// Get first identity object for each person (for display name fallback).
        /// </summary>
        Task<Dictionary<Guid, IdentityObject>> GetFirstIdentitiesAsync(IEnumerable<Guid> personIds);
    }
}
