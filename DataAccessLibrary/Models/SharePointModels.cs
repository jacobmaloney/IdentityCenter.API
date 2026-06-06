namespace DataAccessLibrary.Models;

/// <summary>
/// Configuration for a SharePoint Online / M365 connection.
/// Uses the same Azure AD app registration as Entra ID but queries SharePoint/Teams/OneDrive Graph endpoints.
/// Stored as JSON in DirectoryConnection.Configuration.
/// </summary>
public class SharePointConnectionConfig
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Scopes { get; set; } = "https://graph.microsoft.com/.default";
    public string? SiteFilter { get; set; }
    public int PageSize { get; set; } = 999;
}

/// <summary>
/// Encrypted credentials for SharePoint Online authentication.
/// Stored encrypted as JSON in DirectoryConnection.Credentials.
/// </summary>
public class SharePointCredentials
{
    public string ClientSecret { get; set; } = string.Empty;
}
