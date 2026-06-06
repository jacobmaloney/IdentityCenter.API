using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Dapper;

namespace DataAccessLibrary.Services;

/// <summary>
/// One-shot BackgroundService that runs on startup to register or rediscover
/// this execution server in the RemoteAgents table.
///
/// Waits for the database schema to be ready (RemoteAgents table must exist),
/// then calls IExecutionServerContext.InitializeAsync() to establish the
/// server's identity for the lifetime of the process.
///
/// After InitializeAsync completes the service exits — all subsequent work
/// is handled by HeartbeatBackgroundService.
/// </summary>
public class ExecutionServerStartupService : BackgroundService
{
    private readonly IExecutionServerContext _executionServerContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExecutionServerStartupService> _logger;

    private const int MaxWaitAttempts = 60;        // Wait up to 5 minutes (60 × 5 s)
    private const int WaitIntervalSeconds = 5;

    public ExecutionServerStartupService(
        IExecutionServerContext executionServerContext,
        IConfiguration configuration,
        ILogger<ExecutionServerStartupService> logger)
    {
        _executionServerContext = executionServerContext ?? throw new ArgumentNullException(nameof(executionServerContext));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("ExecutionServerStartupService: starting");

        // ----------------------------------------------------------------
        // Step 1 — wait for the RemoteAgents table to exist.
        // This guards against the first-run scenario where migrations have
        // not been applied yet.
        // ----------------------------------------------------------------
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString) ||
            connectionString.Contains("(localdb)") ||
            connectionString.Contains("**") ||
            connectionString.Contains("YOUR_SERVER") ||
            connectionString.Contains("YOUR_DATABASE"))
        {
            _logger.LogWarning("ExecutionServerStartupService: no valid connection string — skipping registration (setup wizard not yet run)");
            return;
        }

        bool schemaReady = false;

        for (int attempt = 1; attempt <= MaxWaitAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);

                // Verify RemoteAgents table exists (V052 migration must have run)
                await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT TOP 1 1 FROM RemoteAgents",
                        cancellationToken: stoppingToken));

                schemaReady = true;
                _logger.LogDebug("ExecutionServerStartupService: RemoteAgents table found — schema is ready");
                break;
            }
            catch (SqlException sqlEx) when (sqlEx.Number == 208) // Invalid object name
            {
                _logger.LogDebug(
                    "ExecutionServerStartupService: RemoteAgents table not yet created (attempt {Attempt}/{Max}) — waiting {Interval}s",
                    attempt, MaxWaitAttempts, WaitIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(WaitIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ExecutionServerStartupService: cancellation requested while waiting for schema — exiting");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "ExecutionServerStartupService: schema check failed (attempt {Attempt}/{Max}) — waiting {Interval}s",
                    attempt, MaxWaitAttempts, WaitIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(WaitIntervalSeconds), stoppingToken);
            }
        }

        if (!schemaReady)
        {
            _logger.LogWarning("ExecutionServerStartupService: RemoteAgents table not available after waiting — skipping registration");
            return;
        }

        // ----------------------------------------------------------------
        // Step 2 — initialise the execution server context.
        // This upserts the RemoteAgents row and sets ServerId / ServerName /
        // IsPrimary on the singleton IExecutionServerContext.
        // ----------------------------------------------------------------
        try
        {
            await _executionServerContext.InitializeAsync(stoppingToken);

            _logger.LogInformation(
                "Execution server registered: {ServerName} (ServerId: {ServerId}, Primary: {IsPrimary})",
                _executionServerContext.ServerName,
                _executionServerContext.ServerId,
                _executionServerContext.IsPrimary);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ExecutionServerStartupService: cancellation requested during InitializeAsync — exiting");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecutionServerStartupService: InitializeAsync failed — execution server will not participate in distributed job routing");
        }

        // One-shot — return immediately after registration is complete.
    }
}
