namespace DataAccessLibrary.Configuration;

/// <summary>
/// Configuration options for the application update system.
/// Allows customers to check for and download updates from a configured URL.
/// </summary>
public class UpdateOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "UpdateOptions";

    /// <summary>
    /// Whether automatic update checking is enabled.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// URL to the version manifest JSON file.
    /// This can be a GitHub releases URL or a custom update server.
    /// Default: https://certification-center.com/updates/version-manifest.json
    /// </summary>
    public string ManifestUrl { get; set; } = "https://certification-center.com/updates/version-manifest.json";

    /// <summary>
    /// How often to check for updates, in hours.
    /// Default: 24 hours
    /// </summary>
    public int CheckIntervalHours { get; set; } = 24;

    /// <summary>
    /// Local directory path where updates are downloaded.
    /// Default: C:\Updates
    /// </summary>
    public string DownloadPath { get; set; } = @"C:\Updates";

    /// <summary>
    /// Optional proxy URL for update requests.
    /// Leave empty to use system proxy settings.
    /// </summary>
    public string? ProxyUrl { get; set; }

    /// <summary>
    /// Timeout in seconds for HTTP requests to the update server.
    /// Default: 30 seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}
