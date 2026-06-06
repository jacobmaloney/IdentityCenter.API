using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly IAgentRepository _agentRepo;
    private readonly IApiKeyRepository _apiKeyRepo;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(
        IAgentRepository agentRepo,
        IApiKeyRepository apiKeyRepo,
        ILogger<AgentsController> logger)
    {
        _agentRepo = agentRepo;
        _apiKeyRepo = apiKeyRepo;
        _logger = logger;
    }

    /// <summary>
    /// Gets all agent statuses for the dashboard.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<RemoteAgentStatus>>> GetAgentStatuses()
    {
        var statuses = await _agentRepo.GetAgentStatusesAsync();
        return Ok(statuses);
    }

    /// <summary>
    /// Gets a specific agent by ID.
    /// </summary>
    [HttpGet("{agentId}")]
    public async Task<ActionResult<RemoteAgent>> GetAgent(Guid agentId)
    {
        var agent = await _agentRepo.GetAgentByIdAsync(agentId);
        if (agent == null)
        {
            return NotFound(new { error = "Agent not found" });
        }

        return Ok(agent);
    }

    /// <summary>
    /// Gets all currently online agents.
    /// </summary>
    [HttpGet("online")]
    public async Task<ActionResult<List<RemoteAgent>>> GetOnlineAgents()
    {
        var agents = await _agentRepo.GetOnlineAgentsAsync();
        return Ok(agents);
    }

    /// <summary>
    /// Registers a new agent or updates an existing registration.
    /// Returns the agent ID and a new API key for the agent.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<ActionResult> RegisterAgent([FromBody] RegisterAgentRequest request)
    {
        try
        {
            // Assign the agent Id up front so the API key can be linked to this
            // agent at mint time. RegisterAgentAsync preserves a non-empty Id.
            var preAssignedAgentId = Guid.NewGuid();
            var agent = new RemoteAgent
            {
                Id = preAssignedAgentId,
                AgentName = request.AgentName,
                Description = request.Description,
                MachineName = request.MachineName,
                IpAddress = GetClientIpAddress(),
                Version = request.Version,
                OperatingSystem = request.OperatingSystem,
                SupportedJobTypes = request.SupportedJobTypes ?? "SyncProject",
                MaxConcurrentJobs = request.MaxConcurrentJobs > 0 ? request.MaxConcurrentJobs : 1,
                IsEnabled = true,
                Tags = request.Tags,
                Priority = request.Priority > 0 ? request.Priority : 100
            };

            // Mint the agent's API key first. The plaintext is returned to the
            // caller exactly once below; only its hash is persisted. We hash the
            // same plaintext via the shared utility so RemoteAgents.ApiKeyHash and
            // the ApiKeys verify path (KeyHash) agree on the same SHA-256 value.
            var (keyId, apiKey) = await _apiKeyRepo.CreateApiKeyAsync(
                name: $"Agent: {request.AgentName}",
                keyType: "Agent",
                scopes: "agent,jobs:read,jobs:write,jobs:execute",
                agentId: preAssignedAgentId,
                createdBy: User.Identity?.Name ?? "API");

            var apiKeyHash = _apiKeyRepo.HashApiKey(apiKey);

            // Register the agent, storing the key hash in the NOT-NULL ApiKeyHash
            // column. On re-registration by AgentName the existing agent Id wins;
            // relink the freshly minted key to that Id so the agent_id auth claim
            // resolves correctly.
            var agentId = await _agentRepo.RegisterAgentAsync(agent, apiKeyHash);
            if (agentId != preAssignedAgentId)
            {
                await _apiKeyRepo.UpdateApiKeyAgentAsync(keyId, agentId);
            }

            return Ok(new RegisterAgentResponse
            {
                AgentId = agentId,
                ApiKeyId = keyId,
                ApiKey = apiKey, // Only returned once!
                Message = "Agent registered successfully. Save the API key - it will not be shown again."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering agent {AgentName}", request.AgentName);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Receives heartbeat from an agent.
    /// </summary>
    [HttpPost("heartbeat")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<ActionResult> Heartbeat([FromBody] AgentHeartbeat heartbeat)
    {
        try
        {
            // Verify the agent ID matches the authenticated agent
            var authenticatedAgentId = GetAgentIdFromClaims();
            if (authenticatedAgentId != heartbeat.AgentId)
            {
                return Unauthorized(new { error = "Agent ID mismatch" });
            }

            await _agentRepo.UpdateHeartbeatAsync(heartbeat);

            return Ok(new
            {
                message = "Heartbeat received",
                serverTime = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat for agent {AgentId}", heartbeat.AgentId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Updates an agent's status (e.g., when going offline).
    /// </summary>
    [HttpPut("{agentId}/status")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<ActionResult> UpdateStatus(Guid agentId, [FromBody] UpdateStatusRequest request)
    {
        try
        {
            var authenticatedAgentId = GetAgentIdFromClaims();
            if (authenticatedAgentId != agentId)
            {
                return Unauthorized(new { error = "Agent ID mismatch" });
            }

            await _agentRepo.UpdateAgentStatusAsync(agentId, request.Status);

            return Ok(new { message = "Status updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status for agent {AgentId}", agentId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    private Guid? GetAgentIdFromClaims()
    {
        var agentIdClaim = User.FindFirst("agent_id")?.Value;
        if (Guid.TryParse(agentIdClaim, out var agentId))
        {
            return agentId;
        }
        return null;
    }

    private string GetClientIpAddress()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Returns the PowerShell install script with the API URL pre-baked.
    /// Usage: iwr "https://ic.contoso.com/api/agents/install-script?key=xxx" | iex
    /// </summary>
    [HttpGet("install-script")]
    [AllowAnonymous]
    public IActionResult GetInstallScript([FromQuery] string? key = null)
    {
        var apiBaseUrl = string.Concat(Request.Scheme, "://", Request.Host);
        var apiKey = key ?? "SET_YOUR_API_KEY_HERE";

        // Build PowerShell script with string.Replace to avoid C# interpolation conflicts
        var script = @"
# IdentityCenter Agent Installer (auto-generated)
$ErrorActionPreference = 'Stop'
$installDir = 'C:\Program Files\IdentityCenter\Agent'
$apiUrl = '__API_URL__'
$apiKey = '__API_KEY__'

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
New-Item -ItemType Directory -Path ""$installDir\logs"" -Force | Out-Null

$config = @{
    Agent = @{
        ApiBaseUrl = $apiUrl
        ApiKey = $apiKey
        AgentId = ''
        AgentName = $env:COMPUTERNAME
        CollectSqlInventory = $true
        CollectComputerInfo = $true
        HeartbeatIntervalMinutes = 5
        SqlCollectionIntervalMinutes = 60
    }
}
$config | ConvertTo-Json -Depth 5 | Set-Content ""$installDir\agent-config.json"" -Encoding UTF8

Write-Host 'IdentityCenter Agent configured on' $env:COMPUTERNAME -ForegroundColor Green
Write-Host 'API: ' $apiUrl
Write-Host 'Config: ' ""$installDir\agent-config.json""
"
            .Replace("__API_URL__", apiBaseUrl)
            .Replace("__API_KEY__", apiKey)
            .Trim();

        return Content(script, "text/plain");
    }
}

// Request/Response DTOs
public class RegisterAgentRequest
{
    public string AgentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string? OperatingSystem { get; set; }
    public string? SupportedJobTypes { get; set; }
    public int MaxConcurrentJobs { get; set; } = 1;
    public string? Tags { get; set; }
    public int Priority { get; set; } = 100;
}

public class RegisterAgentResponse
{
    public Guid AgentId { get; set; }
    public Guid ApiKeyId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = "Online";
}
