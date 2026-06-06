namespace IdentityCenter.API.Models;

// ── SQL Server inventory (from agent) ────────────────────────────────────────
public class AgentSqlInventoryPayload
{
    public string AgentId { get; set; } = "";
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
    public List<AgentDatabasePayload> Databases { get; set; } = new();
}

public class AgentDatabasePayload
{
    public string Name { get; set; } = "";
    public double SizeGb { get; set; }
    public double LogSizeGb { get; set; }
    public string? RecoveryModel { get; set; }
    public int? CompatibilityLevel { get; set; }
    public bool IsSystemDb { get; set; }
    public DateTime? LastBackupAt { get; set; }
    public string? LastBackupType { get; set; }
    public string? State { get; set; }
}

// ── General computer inventory ────────────────────────────────────────────────
public class AgentComputerInventoryPayload
{
    public string AgentId { get; set; } = "";
    public List<AgentComputerRecord> Computers { get; set; } = new();
}

public class AgentComputerRecord
{
    public string Hostname { get; set; } = "";
    public string? Fqdn { get; set; }
    public string? IpAddress { get; set; }
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public int? CpuCores { get; set; }
    public double? MemoryGb { get; set; }
    public DateTime? LastBootTime { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
}

// ── Events (change detection) ────────────────────────────────────────────────
public class AgentEventPayload
{
    public string AgentId { get; set; } = "";
    public List<AgentEvent> Events { get; set; } = new();
}

public class AgentEvent
{
    public string EventType { get; set; } = "";
    public string Severity { get; set; } = "Info";
    public string SourceHost { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, string> Data { get; set; } = new();
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

// ── Object discovery ──────────────────────────────────────────────────────────
public class AgentObjectDiscoveryPayload
{
    public string AgentId { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public List<AgentDiscoveredObject> Objects { get; set; } = new();
}

public class AgentDiscoveredObject
{
    public string ObjectClass { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? DN { get; set; }
    public string? SourceUniqueId { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
}

// ── Network scan results ──────────────────────────────────────────────────────
public class AgentNetworkScanPayload
{
    public string AgentId { get; set; } = "";
    public string CidrRange { get; set; } = "";
    public List<AgentNetworkScanResult> Results { get; set; } = new();
}

public class AgentNetworkScanResult
{
    public string IpAddress { get; set; } = "";
    public string? Hostname { get; set; }
    public int Port { get; set; }
    public bool IsOpen { get; set; }
}

// ── SQL Server permissions (from agent) ─────────────────────────────────────
public class AgentSqlPermissionsPayload
{
    public string AgentId { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string? InstanceName { get; set; }
    public List<AgentSqlPermissionRecord> Permissions { get; set; } = new();
}

public class AgentSqlPermissionRecord
{
    public string PrincipalName { get; set; } = "";
    public string PrincipalType { get; set; } = ""; // SqlLogin, WindowsLogin, WindowsGroup, DatabaseUser, ServerRole, DatabaseRole
    public string? PrincipalSid { get; set; }
    public string PermissionScope { get; set; } = "Database"; // Server, Database
    public string? DatabaseName { get; set; }
    public string PermissionName { get; set; } = ""; // db_owner, CONTROL, SELECT, etc.
    public string PermissionClass { get; set; } = "ROLE_MEMBERSHIP"; // SERVER, DATABASE, ROLE_MEMBERSHIP
    public string GrantState { get; set; } = "GRANT";
}

// ── Admin API key creation ────────────────────────────────────────────────────
public class CreateApiKeyRequest
{
    public string Name { get; set; } = "";
    public string Scope { get; set; } = "agent";
    public DateTime? ExpiresAt { get; set; }
}
