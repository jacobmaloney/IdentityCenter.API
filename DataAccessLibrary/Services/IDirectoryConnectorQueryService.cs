using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Abstraction for directory connector query operations.
/// Each connector type (AD, Entra ID, etc.) implements this to provide
/// directory query functionality through a unified interface.
/// </summary>
public interface IDirectoryConnectorQueryService
{
    /// <summary>
    /// The connection type this service handles (e.g., "ActiveDirectory", "EntraID").
    /// </summary>
    string ConnectionType { get; }

    /// <summary>
    /// Queries the directory for objects matching the sync step configuration.
    /// Returns a list of dictionaries where each dictionary represents one directory object
    /// with attribute name → value pairs.
    /// </summary>
    Task<List<Dictionary<string, object>>> QueryDirectoryForStepAsync(
        SyncStep step,
        DirectoryConnection connection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Queries group membership from the directory.
    /// Returns a dictionary mapping group identifier (DN for AD, object ID for Entra)
    /// to a list of member identifiers.
    /// </summary>
    Task<Dictionary<string, List<string>>> QueryGroupMembersAsync(
        DirectoryConnection connection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tests the directory connection and returns basic server info.
    /// Each connector validates its own credentials (LDAP bind for AD, Graph API call for Entra ID).
    /// </summary>
    Task<DirectoryConnectionTestResult> TestConnectionAsync(
        DirectoryConnection connection,
        CancellationToken cancellationToken);
}
