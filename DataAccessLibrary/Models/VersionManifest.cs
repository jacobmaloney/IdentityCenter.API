using System.Text.Json.Serialization;

namespace DataAccessLibrary.Models;

/// <summary>
/// Represents the version manifest retrieved from the update server.
/// Contains information about the latest available version and download details.
/// </summary>
public class VersionManifest
{
    /// <summary>
    /// The current/latest version available (e.g., "1.1.22.24")
    /// </summary>
    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; set; } = "";

    /// <summary>
    /// When this version was released
    /// </summary>
    [JsonPropertyName("releaseDate")]
    public DateTime ReleaseDate { get; set; }

    /// <summary>
    /// Release notes describing what changed in this version
    /// </summary>
    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = "";

    /// <summary>
    /// Direct download URL for the update package (ZIP file)
    /// </summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    /// <summary>
    /// Database schema version number for this release.
    /// Used to determine which migrations need to be applied.
    /// </summary>
    [JsonPropertyName("databaseVersion")]
    public int DatabaseVersion { get; set; }

    /// <summary>
    /// SHA256 hash of the download file for integrity verification
    /// </summary>
    [JsonPropertyName("sha256Hash")]
    public string Sha256Hash { get; set; } = "";

    /// <summary>
    /// Size of the download file in bytes
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>
    /// Minimum version required to upgrade directly to this version.
    /// If current version is lower, may need intermediate upgrades.
    /// Optional - if not specified, any version can upgrade directly.
    /// </summary>
    [JsonPropertyName("minimumUpgradeVersion")]
    public string? MinimumUpgradeVersion { get; set; }

    /// <summary>
    /// Whether this is a critical security update
    /// </summary>
    [JsonPropertyName("isCritical")]
    public bool IsCritical { get; set; }
}

/// <summary>
/// Represents the current update status cached by the UpdateCheckerService
/// </summary>
public class UpdateStatus
{
    /// <summary>
    /// Whether an update is available
    /// </summary>
    public bool IsUpdateAvailable { get; set; }

    /// <summary>
    /// The current application version
    /// </summary>
    public string CurrentVersion { get; set; } = "";

    /// <summary>
    /// The latest available version (if update is available)
    /// </summary>
    public VersionManifest? AvailableUpdate { get; set; }

    /// <summary>
    /// When the last update check occurred
    /// </summary>
    public DateTime? LastCheckTime { get; set; }

    /// <summary>
    /// Error message if the last check failed
    /// </summary>
    public string? LastCheckError { get; set; }

    /// <summary>
    /// Whether an update check is currently in progress
    /// </summary>
    public bool IsChecking { get; set; }
}

/// <summary>
/// Progress information for update downloads
/// </summary>
public class DownloadProgress
{
    /// <summary>
    /// Bytes downloaded so far
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// Total bytes to download
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Download progress percentage (0-100)
    /// </summary>
    public int PercentComplete => TotalBytes > 0 ? (int)(BytesDownloaded * 100 / TotalBytes) : 0;

    /// <summary>
    /// Whether the download is complete
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Whether the download failed
    /// </summary>
    public bool HasError { get; set; }

    /// <summary>
    /// Error message if download failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Path to the downloaded file (when complete)
    /// </summary>
    public string? DownloadedFilePath { get; set; }
}
