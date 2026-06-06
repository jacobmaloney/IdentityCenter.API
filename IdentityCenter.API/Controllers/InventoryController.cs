using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using DataAccessLibrary.Services;
using IdentityCenter.API.Models;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize(Policy = "AgentPolicy")]
public class InventoryController : ControllerBase
{
    private readonly IGlobalLogger _logger;
    private readonly ISqlLicenseRepository _sqlLicenseRepo;
    private readonly ISqlLicenseComplianceEngine _complianceEngine;

    public InventoryController(
        IGlobalLogger logger,
        ISqlLicenseRepository sqlLicenseRepo,
        ISqlLicenseComplianceEngine complianceEngine)
    {
        _logger = logger;
        _sqlLicenseRepo = sqlLicenseRepo;
        _complianceEngine = complianceEngine;
    }

    /// <summary>
    /// Agent pushes SQL Server instance + database inventory.
    /// </summary>
    [HttpPost("sql-server")]
    public async Task<IActionResult> ReceiveSqlInventory([FromBody] AgentSqlInventoryPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.ServerName))
            return BadRequest(new { error = "ServerName is required" });

        var agentId = User.FindFirst("agent_id")?.Value;

        _logger.LogInformation("SQL inventory received from agent {AgentId}: {Server}, {DbCount} databases",
            agentId, payload.ServerName, payload.Databases?.Count ?? 0);

        try
        {
            // Map payload to inventory model
            var server = new SqlServerInventory
            {
                ServerName = payload.ServerName,
                Fqdn = payload.Fqdn,
                IpAddress = payload.IpAddress,
                Port = payload.Port,
                InstanceName = payload.InstanceName,
                SqlEdition = payload.SqlEdition,
                SqlVersion = payload.SqlVersion,
                SqlVersionMajor = payload.SqlVersionMajor,
                CpuCores = payload.CpuCores,
                MemoryGb = payload.MemoryGb,
                OsName = payload.OsName,
                OsVersion = payload.OsVersion,
                DiscoveryMethod = "RemoteAgent",
                IsOnline = true,
                LastDiscoveredAt = DateTime.UtcNow,
                LastAgentContactAt = DateTime.UtcNow
            };

            var serverId = await _sqlLicenseRepo.UpsertServerAsync(server);

            // Upsert databases if provided
            if (payload.Databases?.Count > 0)
            {
                var databases = payload.Databases.Select(d => new SqlDatabaseInventory
                {
                    SqlServerInventoryId = serverId,
                    DatabaseName = d.Name,
                    SizeGb = (decimal)d.SizeGb,
                    LogSizeGb = (decimal)d.LogSizeGb,
                    RecoveryModel = d.RecoveryModel,
                    CompatibilityLevel = d.CompatibilityLevel,
                    IsSystemDb = d.IsSystemDb,
                    LastBackupAt = d.LastBackupAt,
                    LastBackupType = d.LastBackupType,
                    State = d.State
                }).ToList();

                await _sqlLicenseRepo.UpsertDatabasesAsync(serverId, databases);
            }

            // Evaluate compliance for this server
            await _complianceEngine.EvaluateServerAsync(serverId);

            return Ok(new
            {
                status = "accepted",
                serverId,
                serverName = payload.ServerName,
                databasesReceived = payload.Databases?.Count ?? 0,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process SQL inventory for {Server}", payload.ServerName);
            return StatusCode(500, new { error = "Failed to process inventory", detail = ex.Message });
        }
    }

    /// <summary>
    /// Agent pushes general computer/server inventory from WMI or AD.
    /// </summary>
    [HttpPost("computers")]
    public async Task<IActionResult> ReceiveComputerInventory([FromBody] AgentComputerInventoryPayload payload)
    {
        if (payload?.Computers == null || !payload.Computers.Any())
            return BadRequest(new { error = "Computers list is required" });

        var agentId = User.FindFirst("agent_id")?.Value;

        _logger.LogInformation("Computer inventory received from agent {AgentId}: {Count} computers",
            agentId, payload.Computers.Count);

        // TODO: Match to Objects table by hostname, update ObjectAttributes
        int processed = payload.Computers.Count;

        return Ok(new { status = "accepted", processed, total = payload.Computers.Count });
    }

    /// <summary>
    /// Agent pushes change events it detected (new installs, service starts, etc.)
    /// </summary>
    [HttpPost("events")]
    public async Task<IActionResult> ReceiveEvents([FromBody] AgentEventPayload payload)
    {
        if (payload?.Events == null || !payload.Events.Any())
            return BadRequest(new { error = "Events list is required" });

        var agentId = User.FindFirst("agent_id")?.Value;

        _logger.LogInformation("Events received from agent {AgentId}: {Count} events",
            agentId, payload.Events.Count);

        // TODO: Persist to AgentEvents table (V077 migration)
        // TODO: Trigger alerts for high-severity events

        return Ok(new { status = "accepted", count = payload.Events.Count });
    }

    /// <summary>
    /// Agent checks if a server is already known (for deduplication).
    /// </summary>
    [HttpGet("sql-server/{serverName}")]
    public async Task<IActionResult> GetSqlServer(string serverName)
    {
        try
        {
            var server = await _sqlLicenseRepo.GetServerByNameAsync(serverName);
            if (server == null)
                return NotFound(new { error = $"Server '{serverName}' not found" });

            return Ok(server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to look up SQL server {ServerName}", serverName);
            return StatusCode(500, new { error = "Failed to look up server", detail = ex.Message });
        }
    }

    /// <summary>
    /// Receives SQL Server permissions data from an agent.
    /// Maps Windows logins to AD Objects by SID or username.
    /// </summary>
    [HttpPost("sql-permissions")]
    public async Task<IActionResult> ReceiveSqlPermissions([FromBody] AgentSqlPermissionsPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.ServerName))
            return BadRequest(new { error = "ServerName is required" });

        try
        {
            _logger.LogInformation("Receiving {Count} SQL permissions for {Server}",
                payload.Permissions.Count, payload.ServerName);

            // Find the server in inventory
            var server = await _sqlLicenseRepo.GetServerByNameAsync(payload.ServerName, payload.InstanceName);
            if (server == null)
            {
                _logger.LogWarning("SQL server {Server} not found in inventory — creating stub", payload.ServerName);
                server = new SqlServerInventory
                {
                    ServerName = payload.ServerName,
                    InstanceName = payload.InstanceName,
                    DiscoveryMethod = "RemoteAgent",
                    LastAgentContactAt = DateTime.UtcNow
                };
                server.Id = await _sqlLicenseRepo.UpsertServerAsync(server);
            }

            // Deactivate previous permissions for this server (mark stale)
            await _sqlLicenseRepo.DeactivateServerPermissionsAsync(server.Id);

            // Map and upsert each permission
            var privilegedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sysadmin", "serveradmin", "securityadmin", "db_owner", "db_securityadmin",
                "CONTROL SERVER", "CONTROL", "ALTER ANY LOGIN", "ALTER ANY DATABASE"
            };

            var permissions = new List<SqlServerPermission>();
            foreach (var rec in payload.Permissions)
            {
                var isPriv = privilegedRoles.Contains(rec.PermissionName);
                var riskLevel = isPriv ? (rec.PermissionScope == "Server" ? "Critical" : "High") : "Low";

                permissions.Add(new SqlServerPermission
                {
                    SqlServerInventoryId = server.Id,
                    PrincipalName = rec.PrincipalName,
                    PrincipalType = rec.PrincipalType,
                    PrincipalSid = rec.PrincipalSid,
                    PermissionScope = rec.PermissionScope,
                    DatabaseName = rec.DatabaseName,
                    PermissionName = rec.PermissionName,
                    PermissionClass = rec.PermissionClass,
                    GrantState = rec.GrantState,
                    IsPrivileged = isPriv,
                    RiskLevel = riskLevel,
                    SourceAgentId = payload.AgentId,
                    LastSeenAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            var (inserted, matched) = await _sqlLicenseRepo.UpsertPermissionsAsync(server.Id, permissions);

            _logger.LogInformation("SQL permissions for {Server}: {Inserted} new, {Matched} AD-matched, {Total} total",
                payload.ServerName, inserted, matched, permissions.Count);

            return Ok(new
            {
                status = "accepted",
                serverId = server.Id,
                serverName = payload.ServerName,
                permissionsReceived = permissions.Count,
                privilegedCount = permissions.Count(p => p.IsPrivileged),
                adMatchedCount = matched,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process SQL permissions for {Server}", payload.ServerName);
            return StatusCode(500, new { error = "Failed to process permissions", detail = ex.Message });
        }
    }
}
