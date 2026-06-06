namespace DataAccessLibrary.Models;

// ─── Entitlements (what we own) ─────────────────────────────────────────────

public class SqlLicenseEntitlement
{
    public Guid Id { get; set; }
    public string LicenseType { get; set; } = "";       // CoreBased | ServerCAL | Enterprise | Standard | Developer | Express
    public string Edition { get; set; } = "";
    public int Quantity { get; set; }
    public string QuantityUnit { get; set; } = "Cores"; // Cores | Seats | Servers
    public decimal? CostPerUnit { get; set; }
    public decimal TotalCost { get; set; }              // computed persisted column
    public string? VendorAgreementNumber { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool SoftwareAssurance { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Computed UI properties (not stored)
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.Today;
    public bool ExpiresWithin90Days => ExpiryDate.HasValue &&
        ExpiryDate.Value >= DateTime.Today &&
        ExpiryDate.Value <= DateTime.Today.AddDays(90);
}

public class SqlLicenseAssignment
{
    public Guid Id { get; set; }
    public Guid EntitlementId { get; set; }
    public string ObjectId { get; set; } = "";          // server object ID
    public int? AssignedCores { get; set; }
    public string? AssignedBy { get; set; }
    public DateTime AssignedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    // Navigation / join results
    public string? ServerName { get; set; }
    public string? Edition { get; set; }
    public int? Quantity { get; set; }
}

// ─── Inventory (what we've discovered) ──────────────────────────────────────

public class SqlServerInventory
{
    public Guid Id { get; set; }
    public string? ObjectId { get; set; }               // null until matched to an Object
    public string DiscoveryMethod { get; set; } = "";   // ActiveDirectory | NetworkScan | RemoteAgent
    public string ServerName { get; set; } = "";
    public string? Fqdn { get; set; }
    public string? IpAddress { get; set; }
    public int Port { get; set; } = 1433;
    public string? InstanceName { get; set; }
    public string? SqlEdition { get; set; }
    public string? SqlVersion { get; set; }
    public int? SqlVersionMajor { get; set; }
    public int? CpuCores { get; set; }
    public int? MemoryGb { get; set; }
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public bool IsOnline { get; set; } = true;
    public bool? IsProduction { get; set; }
    public string? OwnerId { get; set; }
    public DateTime? OwnerAssignedAt { get; set; }
    public string? OwnerAssignedBy { get; set; }
    public string? ComplianceStatus { get; set; }       // Licensed | Unlicensed | OverLicensed | Unknown | Violation
    public DateTime? ComplianceCheckedAt { get; set; }
    public DateTime LastDiscoveredAt { get; set; }
    public DateTime? LastAgentContactAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Encrypted SQL connection string used for persistent rescans. Null = requires manual input.</summary>
    public string? EncryptedConnectionString { get; set; }

    /// <summary>FK to SqlServerCredentials. When set, rescans use this credential profile (preferred over EncryptedConnectionString).</summary>
    public Guid? CredentialId { get; set; }

    /// <summary>Status of the most recent scan: Success, Failed, Running, NeverScanned</summary>
    public string? LastScanStatus { get; set; }
    public string? LastScanMessage { get; set; }
    public int? LastScanDurationMs { get; set; }

    /// <summary>Workflow state: Discovered, Approved, Managed, Ignored, Retired</summary>
    public string DiscoveryStatus { get; set; } = "Managed";

    // WinRM scan status (OS-level scan for local users, installed products)
    public string? LastWinRmScanStatus { get; set; }
    public string? LastWinRmScanMessage { get; set; }
    public DateTime? LastWinRmScanAt { get; set; }
    public int? LastWinRmScanDurationMs { get; set; }

    // Navigation / join results
    public string? OwnerDisplayName { get; set; }
    public List<SqlDatabaseInventory> Databases { get; set; } = new();
    public SqlLicenseAssignment? LicenseAssignment { get; set; }
    public List<LicenseComplianceViolation> ActiveViolations { get; set; } = new();
    public List<ServerLocalUser> LocalUsers { get; set; } = new();
    public List<ServerInstalledProduct> InstalledProducts { get; set; } = new();

    // Computed
    public bool IsEndOfLife => SqlVersionMajor.HasValue && SqlVersionMajor.Value <= 11; // SQL 2012 = v11
    public bool IsDeveloperEdition => !string.IsNullOrEmpty(SqlEdition) && SqlEdition.Contains("Developer", StringComparison.OrdinalIgnoreCase);
    public string InstanceDisplayName => string.IsNullOrEmpty(InstanceName)
        ? $"{ServerName} (default)"
        : $@"{ServerName}\{InstanceName}";
}

public class SqlDatabaseInventory
{
    public Guid Id { get; set; }
    public Guid SqlServerInventoryId { get; set; }
    public string DatabaseName { get; set; } = "";
    public decimal? SizeGb { get; set; }
    public decimal? LogSizeGb { get; set; }
    public string? RecoveryModel { get; set; }          // Simple | Full | BulkLogged
    public int? CompatibilityLevel { get; set; }
    public bool IsSystemDb { get; set; }
    public DateTime? LastBackupAt { get; set; }
    public string? LastBackupType { get; set; }         // Full | Differential | Log
    public string? State { get; set; }                  // Online | Offline | Suspect
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Computed
    public bool IsBackupOverdue => !IsSystemDb &&
        (LastBackupAt == null || LastBackupAt < DateTime.UtcNow.AddDays(-1));
    public bool IsOffline => string.Equals(State, "Offline", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(State, "Suspect", StringComparison.OrdinalIgnoreCase);
}

public class NetworkScanRange
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string CidrRange { get; set; } = "";
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastScannedAt { get; set; }
    public int? LastScanDurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class NetworkScanHistoryEntry
{
    public Guid Id { get; set; }
    public Guid? NetworkScanRangeId { get; set; }
    public string CidrRange { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string Status { get; set; } = "Running"; // Running, Success, Failed
    public int TotalScanned { get; set; }
    public int FoundServers { get; set; }
    public int NewServers { get; set; }
    public int ExistingServers { get; set; }
    public int NewObjectsCreated { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DiscoveredServersJson { get; set; }
    public string? TriggeredBy { get; set; }
}

// ─── Compliance ──────────────────────────────────────────────────────────────

public class LicenseComplianceViolation
{
    public Guid Id { get; set; }
    public Guid? SqlServerInventoryId { get; set; }
    public string? ObjectId { get; set; }
    public string ViolationType { get; set; } = "";     // DeveloperInProd | Unlicensed | EndOfLife | NoOwner | CoreDeficit | UnderLicensed | UntrackedPool | HighUtilization | OverAllocated | HighWaste | DisabledUserLicense | ApproachingCapacity
    public string Severity { get; set; } = "Warning";  // Info | Warning | Critical
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime DetectedAt { get; set; }
    public Guid? CertificationCampaignId { get; set; }

    /// <summary>Source of this violation: SQL, AD, Entra</summary>
    public string? SourceType { get; set; }

    /// <summary>FK to LicensePools for AD/Entra pool-level violations</summary>
    public Guid? LicensePoolId { get; set; }

    // Navigation
    public string? ServerName { get; set; }
    public string? PoolName { get; set; }
}

// ─── Compliance summary (for dashboard) ─────────────────────────────────────

public class SqlLicenseComplianceSummary
{
    public int TotalServers { get; set; }
    public int LicensedServers { get; set; }
    public int UnlicensedServers { get; set; }
    public int ViolationServers { get; set; }
    public int UnknownServers { get; set; }
    public int NoOwnerServers { get; set; }
    public int EndOfLifeServers { get; set; }
    public int DeveloperInProdServers { get; set; }
    public int TotalOwnedCores { get; set; }
    public int TotalDiscoveredCores { get; set; }
    public int CoreDeficit => Math.Max(0, TotalDiscoveredCores - TotalOwnedCores);
    public decimal TotalEntitlementCost { get; set; }
    public decimal EstimatedExposureCost { get; set; }
}

// ─── Extensibility: IInfrastructureLicenseSource ────────────────────────────
// Add new sources here as the product grows. For now only SqlServer is implemented.

public interface IInfrastructureLicenseSource
{
    string SourceType { get; }          // "SqlServer" | "Okta" | "SailPoint" | "CyberArk" | "ServiceNow"
    string DisplayName { get; }
    string Icon { get; }                // FA icon class
    string AccentColor { get; }         // hex color
    Task<List<InfrastructureLicenseDiscoveryResult>> DiscoverAsync(CancellationToken ct = default);
}

public class InfrastructureLicenseDiscoveryResult
{
    public string SourceType { get; set; } = "";
    public string EntityId { get; set; } = "";          // server hostname, Okta app ID, etc.
    public string EntityName { get; set; } = "";
    public Dictionary<string, string> Attributes { get; set; } = new();
    public int? LicenseCount { get; set; }
    public decimal? EstimatedCost { get; set; }
}

// ─── SQL Server Credentials (for direct scanning) ───────────────────────────

public class SqlServerCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string AuthType { get; set; } = "SqlAuth"; // SqlAuth | WindowsAuth
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

// ─── SQL Server Permissions (access governance) ──────────────────────────────

public class SqlServerPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SqlServerInventoryId { get; set; }

    /// <summary>Login or user name (e.g., DOMAIN\jsmith, sa, dbo)</summary>
    public string PrincipalName { get; set; } = "";

    /// <summary>SqlLogin, WindowsLogin, WindowsGroup, DatabaseUser, ServerRole, DatabaseRole</summary>
    public string PrincipalType { get; set; } = "";

    /// <summary>Windows SID for matching to AD Objects</summary>
    public string? PrincipalSid { get; set; }

    /// <summary>Server or Database</summary>
    public string PermissionScope { get; set; } = "Database";

    /// <summary>Database name (null for server-level permissions)</summary>
    public string? DatabaseName { get; set; }

    /// <summary>Permission or role name (e.g., db_owner, CONTROL, ALTER, SELECT)</summary>
    public string PermissionName { get; set; } = "";

    /// <summary>SERVER, DATABASE, SCHEMA, OBJECT, ROLE_MEMBERSHIP</summary>
    public string PermissionClass { get; set; } = "OBJECT";

    /// <summary>GRANT, DENY, GRANT_WITH_GRANT, REVOKE</summary>
    public string GrantState { get; set; } = "GRANT";

    /// <summary>Resolved FK to Objects table (matched via SID or username)</summary>
    public Guid? ObjectId { get; set; }

    /// <summary>How the Object was matched: SID, Username, UPN, Manual</summary>
    public string? MatchMethod { get; set; }

    /// <summary>True for sysadmin, db_owner, CONTROL SERVER, securityadmin, etc.</summary>
    public bool IsPrivileged { get; set; }

    /// <summary>Critical, High, Medium, Low</summary>
    public string? RiskLevel { get; set; }

    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string? SourceAgentId { get; set; }
}
