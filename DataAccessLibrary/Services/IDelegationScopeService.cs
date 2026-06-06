using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Scoped service (one per Blazor circuit) that resolves and caches the current
/// user's delegation context and provides fast access-control helpers.
/// </summary>
public interface IDelegationScopeService
{
    /// <summary>
    /// Returns the fully resolved <see cref="UserDelegationContext"/> for the
    /// current user. The result is cached for 5 minutes per circuit; call
    /// <see cref="RefreshAsync"/> to force a reload.
    /// </summary>
    Task<UserDelegationContext> GetContextAsync();

    /// <summary>
    /// Builds a SQL WHERE fragment that limits object queries to rows the current
    /// user is permitted to see based on their delegation scopes.
    /// Returns an empty string and empty parameters when the user is an admin or
    /// the delegation system is inactive (pass-through).
    /// </summary>
    /// <param name="tableAlias">
    /// Optional table alias prefix for column references (e.g. "o" yields "o.DN LIKE ...").
    /// Pass null or empty to use bare column names.
    /// </param>
    /// <returns>
    /// A tuple of (whereClause, parameters). The whereClause begins with " AND "
    /// when non-empty so it can be appended directly to an existing WHERE clause.
    /// </returns>
    Task<(string WhereClause, DynamicParameters Parameters)> BuildObjectScopeFilterAsync(string? tableAlias = null);

    /// <summary>
    /// Returns true if the current user may perform <paramref name="action"/> on
    /// objects of class <paramref name="objectClass"/>.
    /// Denied actions always win over allowed actions.
    /// Admins and pass-through contexts return true.
    /// </summary>
    Task<bool> CanPerformActionAsync(string action, string objectClass);

    /// <summary>
    /// Returns true if the current user may navigate to the given
    /// <paramref name="pagePath"/> (e.g. "/admin/directory/objects").
    /// Admins return true unconditionally.
    /// When the delegation system is inactive, returns true (pass-through).
    /// </summary>
    Task<bool> CanAccessPageAsync(string pagePath);

    /// <summary>
    /// Returns the set of attributes the current user may write on objects of
    /// class <paramref name="objectClass"/>, or null if all attributes are writable
    /// (admin / pass-through).
    /// </summary>
    Task<HashSet<string>?> GetWritableAttributesAsync(string objectClass);

    /// <summary>
    /// Forces the cached context to be discarded so the next call to
    /// <see cref="GetContextAsync"/> re-resolves from the database.
    /// </summary>
    Task RefreshAsync();

    /// <summary>
    /// Resolves a <see cref="UserDelegationContext"/> as if the current user held only
    /// the single delegation assignment identified by <paramref name="assignmentId"/>.
    /// Used by the admin "Preview" tool to show what a delegated principal would see.
    /// Returns null if the assignment does not exist.
    /// </summary>
    Task<UserDelegationContext?> PreviewDelegationAsync(Guid assignmentId, CancellationToken ct = default);
}
