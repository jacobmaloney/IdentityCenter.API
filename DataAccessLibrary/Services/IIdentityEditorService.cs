using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Consolidated service for Identity CRUD, audit logging, and AD cascade operations.
/// Fixes data-loss bug where Dapper INSERT/UPDATE only covered ~15 of ~53 columns.
/// Used by both People.razor detail panel and IdentityDetails.razor full page.
/// </summary>
public interface IIdentityEditorService
{
    /// <summary>
    /// Create a new identity with ALL columns persisted (53 columns).
    /// Replaces the old SyncObjectRepository.CreateIdentityAsync which only saved 15 columns.
    /// </summary>
    Task<Guid> CreateIdentityAsync(Identity identity);

    /// <summary>
    /// Update an existing identity with ALL columns persisted (53 columns).
    /// Replaces the old AdminRepository.UpdateIdentityAsync which only saved 11 columns.
    /// </summary>
    Task UpdateIdentityAsync(Identity identity);

    /// <summary>
    /// Full save pipeline: persist to DB, generate audit entries, cascade changes to linked AD objects.
    /// Consolidates the duplicate save logic from People.razor and IdentityDetails.razor.
    /// </summary>
    /// <param name="identity">The identity to save</param>
    /// <param name="originalSnapshot">The original state (for audit diff). Null for create operations.</param>
    /// <param name="source">Source identifier for audit logs (e.g., "People.razor", "IdentityDetails.razor")</param>
    /// <returns>Result containing success status, message, and AD cascade stats</returns>
    Task<IdentitySaveResult> SaveWithCascadeAsync(Identity identity, Identity? originalSnapshot, string source);
}

/// <summary>
/// Result of an identity save operation including AD cascade statistics.
/// </summary>
/// <param name="Success">Whether the save operation succeeded</param>
/// <param name="Message">User-friendly message describing the result</param>
/// <param name="AdAccountsUpdated">Number of linked AD accounts successfully updated</param>
/// <param name="AdAccountsFailed">Number of linked AD accounts that failed to update</param>
public record IdentitySaveResult(bool Success, string Message, int AdAccountsUpdated, int AdAccountsFailed);
