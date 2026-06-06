namespace DataAccessLibrary.Models;

/// <summary>
/// Configuration for Microsoft Teams bot integration
/// Stored in database for persistence
/// </summary>
public class TeamsBotConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Azure AD Tenant ID for single-tenant bot authentication
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Microsoft App ID from Azure Bot Service registration
    /// </summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// Microsoft App Password (encrypted in database)
    /// </summary>
    public string AppPassword { get; set; } = string.Empty;

    /// <summary>
    /// Bot display name in Teams
    /// </summary>
    public string BotName { get; set; } = "Certification Center Bot";

    /// <summary>
    /// Short description for Teams app store
    /// </summary>
    public string ShortDescription { get; set; } = "Manage users, groups, and access reviews";

    /// <summary>
    /// Full description for Teams app store
    /// </summary>
    public string FullDescription { get; set; } = "Certification Center ChatHub brings powerful identity and access management capabilities directly into Microsoft Teams.";

    /// <summary>
    /// Organization/developer name
    /// </summary>
    public string DeveloperName { get; set; } = "Your Organization";

    /// <summary>
    /// Organization website URL
    /// </summary>
    public string WebsiteUrl { get; set; } = "https://localhost";

    /// <summary>
    /// Privacy policy URL (required by Teams)
    /// </summary>
    public string PrivacyUrl { get; set; } = "https://localhost/privacy";

    /// <summary>
    /// Terms of use URL (required by Teams)
    /// </summary>
    public string TermsOfUseUrl { get; set; } = "https://localhost/terms";

    /// <summary>
    /// Messaging endpoint URL (Azure Bot Service webhook)
    /// Example: https://yourbot.azurewebsites.net/api/messages
    /// </summary>
    public string MessagingEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Accent color for Teams app (hex code)
    /// </summary>
    public string AccentColor { get; set; } = "#667eea";

    /// <summary>
    /// Whether the bot is currently active
    /// </summary>
    public bool IsActive { get; set; } = false;

    /// <summary>
    /// When the configuration was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the configuration was last modified
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// User who created this configuration
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// User who last modified this configuration
    /// </summary>
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Last successful connection test timestamp
    /// </summary>
    public DateTime? LastTestedAt { get; set; }

    /// <summary>
    /// Result of last connection test
    /// </summary>
    public string? LastTestResult { get; set; }

    /// <summary>
    /// Whether the last test was successful
    /// </summary>
    public bool LastTestSuccess { get; set; } = false;
}

/// <summary>
/// Result of packaging a Teams app
/// </summary>
public class TeamsPackageResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public byte[]? PackageData { get; set; }
    public string? FileName { get; set; }
}

/// <summary>
/// Result of testing Teams bot connection
/// </summary>
public class TeamsBotTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Details { get; set; } = new();
}
