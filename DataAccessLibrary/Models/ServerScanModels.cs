namespace DataAccessLibrary.Models;

/// <summary>Local user or group membership discovered via WinRM scan.</summary>
public class ServerLocalUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SqlServerInventoryId { get; set; }
    public string AccountName { get; set; } = "";
    public string AccountType { get; set; } = "LocalUser"; // LocalUser, DomainUser, DomainGroup
    public string? GroupName { get; set; }
    public bool IsLocalAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public string? SID { get; set; }
    public Guid? ObjectId { get; set; }
    public string? MatchMethod { get; set; } // SID, SAMAccountName, UPN, Manual
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation (join result)
    public string? ObjectDisplayName { get; set; }
}

/// <summary>Installed Microsoft product discovered via WinRM registry scan.</summary>
public class ServerInstalledProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SqlServerInventoryId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductVersion { get; set; }
    public string? ProductEdition { get; set; }
    public string ProductCategory { get; set; } = "Other"; // WindowsServer, SQLServer, Office, Other
    public string? LicenseKey { get; set; }
    public DateTime? InstallDate { get; set; }
    public string? InstallPath { get; set; }
    public string? Publisher { get; set; }
    public bool IsLicensable { get; set; } = true;
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

/// <summary>Result of a WinRM server scan.</summary>
public class ServerScanResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string ServerName { get; set; } = "";
    public Guid? ServerId { get; set; }
    public int LocalUsersCollected { get; set; }
    public int LocalAdminsFound { get; set; }
    public int ProductsCollected { get; set; }
    public int AdMatchedUsers { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
