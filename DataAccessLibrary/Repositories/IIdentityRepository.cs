using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories
{
    /// <summary>
    /// Repository interface for Identity (Object) operations.
    /// High-performance Dapper-based data access.
    /// </summary>
    public interface IIdentityRepository
    {
        /// <summary>
        /// Get all identities for a specific person, ordered by authoritative status and source type.
        /// </summary>
        Task<List<IdentityObject>> GetIdentitiesByIdentityIdAsync(Guid personId);

        /// <summary>
        /// Get paginated list of identities with filtering by source type and search term.
        /// </summary>
        Task<(List<IdentityObject> Identities, int TotalCount)> GetIdentitiesPagedAsync(
            int pageNumber = 1,
            int pageSize = 20,
            string? searchTerm = null,
            string? sourceType = null,
            bool? isActive = null,
            bool? isAuthoritative = null);

        /// <summary>
        /// Get identity by ID with full details.
        /// </summary>
        Task<IdentityObject?> GetIdentityByIdAsync(Guid identityId);

        /// <summary>
        /// Get identities by source connection ID.
        /// </summary>
        Task<List<IdentityObject>> GetIdentitiesBySourceConnectionAsync(Guid sourceConnectionId);

        /// <summary>
        /// Get identity statistics: total identities, by source type, active/inactive counts.
        /// </summary>
        Task<IdentityStatistics> GetIdentityStatisticsAsync();

        /// <summary>
        /// Search for identities by email address (exact or partial match).
        /// </summary>
        Task<List<IdentityObject>> SearchByEmailAsync(string email, bool exactMatch = false);

        /// <summary>
        /// Get authoritative identity for a person (the primary/source of truth identity).
        /// </summary>
        Task<IdentityObject?> GetAuthoritativeIdentityForPersonAsync(Guid personId);

        /// <summary>
        /// Get identities that haven't been seen since a specific date (potentially stale/deleted accounts).
        /// </summary>
        Task<List<IdentityObject>> GetStaleIdentitiesAsync(DateTime sinceDate, int limit = 100);

        /// <summary>
        /// Get identities with group membership counts.
        /// </summary>
        Task<List<IdentityWithGroupCount>> GetIdentitiesWithGroupCountsAsync(
            int pageNumber = 1,
            int pageSize = 20,
            string? sourceType = null);

        /// <summary>
        /// Find duplicate objects linked to the same person (same email/username).
        /// </summary>
        Task<List<DuplicateObjectGroup>> FindDuplicateObjectsAsync();

        /// <summary>
        /// Delete a duplicate object by ID (also cleans up group memberships).
        /// </summary>
        Task<bool> DeleteDuplicateObjectAsync(Guid objectId);

        /// <summary>
        /// Bulk delete duplicate objects by IDs (cleans up related data in one transaction).
        /// </summary>
        Task<int> BulkDeleteDuplicateObjectsAsync(IEnumerable<Guid> objectIds);
    }

    /// <summary>
    /// Represents a group of duplicate objects linked to the same person.
    /// </summary>
    public class DuplicateObjectGroup
    {
        public Guid PersonId { get; set; }
        public string? PersonDisplayName { get; set; }
        public List<IdentityObject> DuplicateObjects { get; set; } = new();
        public string DuplicateKey { get; set; } = string.Empty; // e.g., email or username
    }
}
