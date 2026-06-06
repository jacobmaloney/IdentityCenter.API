using System.Security.Claims;
using DataAccessLibrary.Repositories;
using DataAccessLibrary.Services;
using IdentityCenter.API.Models;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminPolicy")]
public class AdminApiController : ControllerBase
{
    private readonly IAgentRepository _agentRepo;
    private readonly IJobQueueRepository _jobRepo;
    private readonly IApiKeyRepository _apiKeyRepo;
    private readonly IAuditLogService _auditLog;
    private readonly IGlobalLogger _logger;

    public AdminApiController(
        IAgentRepository agentRepo,
        IJobQueueRepository jobRepo,
        IApiKeyRepository apiKeyRepo,
        IAuditLogService auditLog,
        IGlobalLogger logger)
    {
        _agentRepo = agentRepo;
        _jobRepo = jobRepo;
        _apiKeyRepo = apiKeyRepo;
        _auditLog = auditLog;
        _logger = logger;
    }

    private (string? UserId, string? DisplayName, string? Email) ResolveCaller()
    {
        var u = User;
        var userId = u.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? u.Identity?.Name;
        var displayName = u.FindFirst(ClaimTypes.Name)?.Value ?? u.Identity?.Name;
        var email = u.FindFirst(ClaimTypes.Email)?.Value;
        return (userId, displayName, email);
    }

    private static string ResolveCreatedBy(string? email, string? displayName, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(email)) return email!;
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName!;
        if (!string.IsNullOrWhiteSpace(userId)) return userId!;
        return "Admin API";
    }

    /// <summary>
    /// Detailed health check for monitoring.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
            timestamp = DateTime.UtcNow,
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
        });
    }

    /// <summary>
    /// Dashboard stats: agent count, job queue depth, inventory counts.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        try
        {
            var agents = await _agentRepo.GetOnlineAgentsAsync();
            var queueSummary = await _jobRepo.GetQueueSummaryAsync();

            return Ok(new
            {
                onlineAgents = agents.Count,
                pendingJobs = queueSummary.TotalPending,
                processingJobs = queueSummary.TotalProcessing,
                completedToday = queueSummary.TotalCompleted24h,
                failedToday = queueSummary.TotalFailed24h,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get admin stats");
            return Ok(new
            {
                onlineAgents = 0,
                pendingJobs = 0,
                timestamp = DateTime.UtcNow,
                error = "Some stats unavailable"
            });
        }
    }

    /// <summary>
    /// Create a new API key (admin only). Returns the plain-text key ONCE.
    /// </summary>
    [HttpPost("api-keys")]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });

        try
        {
            var (userId, displayName, email) = ResolveCaller();
            var createdBy = ResolveCreatedBy(email, displayName, userId);

            var (keyId, apiKey) = await _apiKeyRepo.CreateApiKeyAsync(
                request.Name, request.Scope, request.Scope,
                expiresAt: request.ExpiresAt, createdBy: createdBy);

            var expiresLabel = request.ExpiresAt.HasValue ? request.ExpiresAt.Value.ToString("o") : "never";
            var newValue = string.Concat("Minted (scope: ", request.Scope, "; expires: ", expiresLabel, ")");

            await _auditLog.LogChangeAsync(new ChangeAuditEntry
            {
                OperationType = ChangeOperationType.Create,
                EntityType = "ApiKey",
                EntityId = keyId,
                EntityDisplayName = request.Name,
                PropertyName = "Account",
                NewValue = newValue,
                Source = "AdminApi-ApiKeyMint",
                UserId = userId,
                UserDisplayName = displayName,
                UserEmail = email
            });

            _logger.LogInformation("API key created: {Name} (scope: {Scope}) by {CreatedBy}",
                request.Name, request.Scope, createdBy);

            return Ok(new
            {
                id = keyId,
                key = apiKey,
                name = request.Name,
                scope = request.Scope,
                expiresAt = request.ExpiresAt,
                message = "Store this key securely — it cannot be retrieved again."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create API key");
            return StatusCode(500, new { error = "Failed to create API key" });
        }
    }

    /// <summary>
    /// Revoke an API key.
    /// </summary>
    [HttpDelete("api-keys/{id}")]
    public async Task<IActionResult> RevokeApiKey(Guid id)
    {
        var (userId, displayName, email) = ResolveCaller();
        var by = ResolveCreatedBy(email, displayName, userId);
        var reason = string.Concat("Revoked via Admin API by ", by);

        var revoked = await _apiKeyRepo.RevokeApiKeyAsync(id, reason);
        if (!revoked) return NotFound(new { error = "API key not found" });

        await _auditLog.LogChangeAsync(new ChangeAuditEntry
        {
            OperationType = ChangeOperationType.Delete,
            EntityType = "ApiKey",
            EntityId = id,
            PropertyName = "Account",
            OldValue = "Active",
            NewValue = "Revoked",
            Reason = reason,
            Source = "AdminApi-ApiKeyRevoke",
            UserId = userId,
            UserDisplayName = displayName,
            UserEmail = email
        });

        _logger.LogInformation("API key revoked: {KeyId} by {By}", id, by);
        return Ok(new { status = "revoked", id });
    }

    /// <summary>
    /// List all API keys (without the actual key values).
    /// </summary>
    [HttpGet("api-keys")]
    public async Task<IActionResult> ListApiKeys([FromQuery] string? scope = null)
    {
        var keys = await _apiKeyRepo.GetApiKeysAsync(scope);
        return Ok(keys.Select(k => new
        {
            k.Id,
            k.Name,
            scope = k.KeyType,
            k.CreatedAt,
            k.ExpiresAt,
            isActive = k.IsEnabled,
            k.LastUsedAt
        }));
    }
}
