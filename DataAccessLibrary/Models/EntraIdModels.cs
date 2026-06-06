namespace DataAccessLibrary.Models;

/// <summary>
/// Configuration for an Entra ID (Azure AD) connection.
/// Stored as JSON in DirectoryConnection.Configuration.
/// </summary>
public class EntraIdConnectionConfig
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Scopes { get; set; } = "https://graph.microsoft.com/.default";
    public string? UserFilter { get; set; }
    public string? GroupFilter { get; set; }
    public int PageSize { get; set; } = 999;
}

/// <summary>
/// Encrypted credentials for Entra ID authentication.
/// Stored encrypted as JSON in DirectoryConnection.Credentials.
/// </summary>
public class EntraIdCredentials
{
    public string ClientSecret { get; set; } = string.Empty;
}
