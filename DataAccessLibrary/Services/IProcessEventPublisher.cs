using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Publishes lifecycle events for trigger evaluation.
/// Called from repositories after INSERT/UPDATE/DELETE operations.
/// </summary>
public interface IProcessEventPublisher
{
    /// <summary>
    /// Publish an event to be processed by matching workflow triggers.
    /// Implementations must never throw — event publishing must not break repository operations.
    /// </summary>
    /// <param name="eventType">The type of event occurring</param>
    /// <param name="entityId">ID of the affected entity</param>
    /// <param name="entityType">Type of entity (Identity, Object, Group, etc.)</param>
    /// <param name="eventData">Optional additional event data</param>
    Task PublishAsync(
        WorkflowEventType eventType,
        Guid? entityId,
        string entityType,
        Dictionary<string, object>? eventData = null);
}
