using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Manages VirtualContainers — custom OU-like groupings for flat identity sources.
/// </summary>
public interface IVirtualContainerRepository
{
    /// <summary>Returns all active containers for a connection, ordered by SortOrder then Name.</summary>
    Task<List<VirtualContainer>> GetContainersForConnectionAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>Returns a single container by primary key, or null if not found.</summary>
    Task<VirtualContainer?> GetContainerAsync(Guid id, CancellationToken ct = default);

    /// <summary>Inserts a new container and returns its generated Id.</summary>
    Task<Guid> CreateContainerAsync(VirtualContainer container, CancellationToken ct = default);

    /// <summary>Updates Name, ParentId, ContainerType, AttributeName, AttributeValue,
    /// RuleExpression, IconClass, SortOrder, and IsActive. Sets ModifiedAt to UtcNow.</summary>
    Task UpdateContainerAsync(VirtualContainer container, CancellationToken ct = default);

    /// <summary>Hard-deletes a container row by Id.</summary>
    Task DeleteContainerAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Executes usp_AutoDiscoverVirtualContainers for the given connection.
    /// Returns the number of new containers inserted.
    /// </summary>
    Task<int> AutoDiscoverContainersAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of Objects that match the container's filter rule.
    /// Supports ContainerType values: Attribute and ObjectClass.
    /// </summary>
    Task<int> GetObjectCountForContainerAsync(VirtualContainer container, CancellationToken ct = default);
}
