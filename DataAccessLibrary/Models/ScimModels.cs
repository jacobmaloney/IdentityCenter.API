namespace DataAccessLibrary.Models;

/// <summary>
/// Configuration for a SCIM 2.0 directory connection.
/// Stored as JSON in DirectoryConnection.Configuration.
/// </summary>
public class ScimConnectionConfig
{
    public int PageSize { get; set; } = 100;
    public string UserEndpoint { get; set; } = "/Users";
    public string GroupEndpoint { get; set; } = "/Groups";
    public string ServiceProviderConfigEndpoint { get; set; } = "/ServiceProviderConfig";
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Encrypted credentials for SCIM 2.0 authentication.
/// Stored encrypted as JSON in DirectoryConnection.Credentials.
/// Base URL is stored in DirectoryConnection.ConnectionString (encrypted), same pattern as other connectors.
/// </summary>
public class ScimCredentials
{
    public string? BearerToken { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
