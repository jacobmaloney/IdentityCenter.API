namespace DataAccessLibrary.Services;

/// <summary>
/// Generates deployment commands and optionally deploys agents to remote
/// domain-joined servers via PowerShell Remoting (WinRM).
///
/// Flow: Generate API key → build install command → execute remotely (or copy-paste)
/// </summary>
public interface IAgentDeploymentService
{
    /// <summary>
    /// Generate a deployment command for a specific host.
    /// Creates an API key and returns the PowerShell command to install the agent.
    /// </summary>
    /// <param name="hostname">Target server hostname</param>
    /// <param name="apiBaseUrlOverride">Optional override for the API base URL (e.g., from NavigationManager). If null, uses config.</param>
    Task<AgentDeploymentResult> GenerateDeploymentAsync(string hostname, string? apiBaseUrlOverride = null, CancellationToken ct = default);

    /// <summary>
    /// Deploy the agent to a remote host via PowerShell Remoting (WinRM).
    /// Requires the app pool identity to have admin access on the remote host (domain-joined).
    /// </summary>
    Task<AgentDeploymentResult> DeployToHostAsync(string hostname, string? apiBaseUrlOverride = null, CancellationToken ct = default);
}

public class AgentDeploymentResult
{
    public bool Success { get; set; }
    public string? AgentId { get; set; }
    public string? ApiKey { get; set; }
    public string? InstallCommand { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Output { get; set; }
    public string Hostname { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
