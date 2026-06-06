using System.Reflection;
using System.Runtime.InteropServices;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Singleton service that represents the identity of the current execution server.
/// Populated once during application startup and immutable thereafter.
///
/// The primary server discovers or creates its record by querying for IsPrimary=1.
/// All mutable state (IsDraining, IsReady) is updated through the interface methods
/// which also persist the change to the RemoteAgents table.
/// </summary>
public class ExecutionServerContext : IExecutionServerContext
{
    private readonly string _connectionString;
    private readonly ILogger<ExecutionServerContext> _logger;

    // -------------------------------------------------------------------------
    // Immutable fields — written once in InitializeAsync, read-only thereafter
    // -------------------------------------------------------------------------
    private Guid _serverId;
    private string _serverName = string.Empty;
    private string _machineName = string.Empty;
    private bool _isPrimary;
    private string _serverRole = string.Empty;
    private string? _baseUrl;
    private IReadOnlyList<string> _supportedJobTypes = Array.Empty<string>();
    private int _maxConcurrentJobs;

    // -------------------------------------------------------------------------
    // Mutable fields — updated via drain/undrain/offline operations
    // -------------------------------------------------------------------------
    private volatile bool _isDraining;
    private volatile bool _isReady;

    // -------------------------------------------------------------------------
    // IExecutionServerContext properties
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public Guid ServerId => _serverId;

    /// <inheritdoc />
    public string ServerName => _serverName;

    /// <inheritdoc />
    public string MachineName => _machineName;

    /// <inheritdoc />
    public bool IsPrimary => _isPrimary;

    /// <inheritdoc />
    public string ServerRole => _serverRole;

    /// <inheritdoc />
    public string? BaseUrl => _baseUrl;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedJobTypes => _supportedJobTypes;

    /// <inheritdoc />
    public int MaxConcurrentJobs => _maxConcurrentJobs;

    /// <inheritdoc />
    public bool IsDraining => _isDraining;

    /// <inheritdoc />
    public bool IsReady => _isReady;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public ExecutionServerContext(
        IConfiguration configuration,
        ILogger<ExecutionServerContext> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ExecutionServerContext: Initializing primary execution server identity...");

        var machineName = Environment.MachineName;
        var assemblyVersion = GetAssemblyVersion();
        var dotNetVersion = RuntimeInformation.FrameworkDescription;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Query for the existing primary server row
            var existing = await connection.QueryFirstOrDefaultAsync<dynamic>(
                new CommandDefinition(
                    "SELECT * FROM RemoteAgents WHERE IsPrimary = 1",
                    cancellationToken: cancellationToken));

            if (existing != null)
            {
                // CASE 1: Row exists (seeded by V052 or a previous startup)
                // Update it with current machine info and mark online.
                Guid existingId = existing.Id;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        @"UPDATE RemoteAgents SET
                            MachineName     = @MachineName,
                            Version         = @Version,
                            Status          = 'Online',
                            LastHeartbeat   = GETUTCDATE(),
                            LastStartedAt   = GETUTCDATE(),
                            DotNetVersion   = @DotNetVersion
                          WHERE Id = @Id AND IsPrimary = 1",
                        new
                        {
                            Id = existingId,
                            MachineName = machineName,
                            Version = assemblyVersion,
                            DotNetVersion = dotNetVersion
                        },
                        cancellationToken: cancellationToken));

                // Re-read the updated row so we have authoritative values
                var updated = await connection.QueryFirstAsync<dynamic>(
                    new CommandDefinition(
                        "SELECT * FROM RemoteAgents WHERE Id = @Id",
                        new { Id = existingId },
                        cancellationToken: cancellationToken));

                PopulateFromRow(updated);

                _logger.LogInformation(
                    "ExecutionServerContext: Updated existing primary server record. ServerId={ServerId}, MachineName={MachineName}",
                    _serverId, _machineName);
            }
            else
            {
                // CASE 2: No primary row found (fresh DB without V052 seed, or migration hasn't run yet)
                // Insert a new primary server record.
                var newId = Guid.NewGuid();
                var agentName = string.Concat("Primary-", machineName);
                const string supportedJobTypes = "*";
                const int maxConcurrentJobs = 10;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        @"INSERT INTO RemoteAgents (
                            Id, AgentName, Description, MachineName, Version,
                            ApiKeyHash, Status, SupportedJobTypes, MaxConcurrentJobs,
                            CurrentJobCount, TotalJobsProcessed, TotalJobsFailed,
                            IsEnabled, RegisteredAt, Priority,
                            IsPrimary, ServerRole,
                            LastHeartbeat, LastStartedAt, DotNetVersion
                          ) VALUES (
                            @Id, @AgentName, @Description, @MachineName, @Version,
                            '', 'Online', @SupportedJobTypes, @MaxConcurrentJobs,
                            0, 0, 0,
                            1, GETUTCDATE(), 1000,
                            1, 'Primary',
                            GETUTCDATE(), GETUTCDATE(), @DotNetVersion
                          )",
                        new
                        {
                            Id = newId,
                            AgentName = agentName,
                            Description = "Auto-registered primary IdentityCenter instance",
                            MachineName = machineName,
                            Version = assemblyVersion,
                            SupportedJobTypes = supportedJobTypes,
                            MaxConcurrentJobs = maxConcurrentJobs,
                            DotNetVersion = dotNetVersion
                        },
                        cancellationToken: cancellationToken));

                // Read back the inserted row for consistency
                var inserted = await connection.QueryFirstAsync<dynamic>(
                    new CommandDefinition(
                        "SELECT * FROM RemoteAgents WHERE Id = @Id",
                        new { Id = newId },
                        cancellationToken: cancellationToken));

                PopulateFromRow(inserted);

                _logger.LogInformation(
                    "ExecutionServerContext: Inserted new primary server record. ServerId={ServerId}, MachineName={MachineName}",
                    _serverId, _machineName);
            }

            _isReady = true;
            _logger.LogInformation(
                "ExecutionServerContext: Ready. ServerId={ServerId}, ServerName={ServerName}, IsPrimary={IsPrimary}, Role={ServerRole}, SupportedJobTypes={SupportedJobTypes}, MaxConcurrentJobs={MaxConcurrentJobs}",
                _serverId, _serverName, _isPrimary, _serverRole,
                string.Join(",", _supportedJobTypes), _maxConcurrentJobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecutionServerContext: Failed to initialize execution server identity.");
            throw new InvalidOperationException(
                "ExecutionServerContext: Unable to register this server in the database. See inner exception for details.", ex);
        }
    }

    // -------------------------------------------------------------------------
    // Drain mode
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task EnterDrainModeAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogInformation("ExecutionServerContext: Entering drain mode for ServerId={ServerId}", _serverId);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                @"UPDATE RemoteAgents
                  SET DrainStartedAt = GETUTCDATE(),
                      Status         = 'Draining'
                  WHERE Id = @ServerId",
                new { ServerId = _serverId },
                cancellationToken: cancellationToken));

        _isDraining = true;

        _logger.LogWarning(
            "ExecutionServerContext: Server is now draining. No new jobs will be claimed. ServerId={ServerId}",
            _serverId);
    }

    /// <inheritdoc />
    public async Task ExitDrainModeAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogInformation("ExecutionServerContext: Exiting drain mode for ServerId={ServerId}", _serverId);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                @"UPDATE RemoteAgents
                  SET DrainStartedAt = NULL,
                      Status         = 'Online'
                  WHERE Id = @ServerId",
                new { ServerId = _serverId },
                cancellationToken: cancellationToken));

        _isDraining = false;

        _logger.LogInformation(
            "ExecutionServerContext: Server has exited drain mode and is Online. ServerId={ServerId}",
            _serverId);
    }

    // -------------------------------------------------------------------------
    // Graceful shutdown
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task MarkOfflineAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogInformation("ExecutionServerContext: Marking server offline. ServerId={ServerId}", _serverId);

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    @"UPDATE RemoteAgents
                      SET Status          = 'Offline',
                          CurrentJobCount = 0
                      WHERE Id = @ServerId",
                    new { ServerId = _serverId },
                    cancellationToken: cancellationToken));

            _logger.LogInformation(
                "ExecutionServerContext: Server marked Offline in database. ServerId={ServerId}",
                _serverId);
        }
        catch (Exception ex)
        {
            // Best-effort on shutdown — log and swallow so the process can exit cleanly.
            _logger.LogWarning(ex,
                "ExecutionServerContext: Failed to mark server offline during shutdown (best-effort). ServerId={ServerId}",
                _serverId);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates the private immutable fields from a dynamic Dapper row.
    /// </summary>
    private void PopulateFromRow(dynamic row)
    {
        _serverId       = (Guid)row.Id;
        _serverName     = (string)row.AgentName;
        _machineName    = (string)row.MachineName;
        _isPrimary      = (bool)row.IsPrimary;
        _serverRole     = (string)row.ServerRole;
        _baseUrl        = (string?)row.BaseUrl;
        _maxConcurrentJobs = (int)row.MaxConcurrentJobs;

        // Parse the drain state so in-memory state mirrors the DB
        _isDraining     = row.DrainStartedAt != null;

        // SupportedJobTypes is stored as a comma-separated string; split into a list.
        var rawJobTypes = (string)row.SupportedJobTypes;
        _supportedJobTypes = string.IsNullOrWhiteSpace(rawJobTypes)
            ? Array.Empty<string>()
            : rawJobTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
    }

    /// <summary>
    /// Returns the assembly version string for the executing assembly.
    /// Falls back to "1.0.0" if the version cannot be determined.
    /// </summary>
    private static string GetAssemblyVersion()
    {
        return Assembly.GetExecutingAssembly()
                   .GetName()
                   .Version
                   ?.ToString()
               ?? "1.0.0";
    }

    /// <summary>
    /// Throws if <see cref="InitializeAsync"/> has not yet been called successfully.
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_isReady)
            throw new InvalidOperationException(
                "ExecutionServerContext has not been initialized. " +
                "Call InitializeAsync() during application startup before using this service.");
    }
}
