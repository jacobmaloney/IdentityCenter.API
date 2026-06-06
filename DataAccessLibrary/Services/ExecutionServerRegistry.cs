using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;

namespace DataAccessLibrary.Services;

/// <summary>
/// Dapper-based implementation of IExecutionServerRegistry.
/// Manages registration, heartbeat telemetry, orphan recovery, and job-type
/// assignments for all execution servers in the cluster.
///
/// Scoped lifetime: creates a fresh SqlConnection per public method call via
/// CreateConnection(), so it is safe to use from both background timers and
/// ASP.NET request pipelines.
/// </summary>
public class ExecutionServerRegistry : IExecutionServerRegistry
{
    private readonly string _connectionString;
    private readonly ILogger<ExecutionServerRegistry> _logger;

    public ExecutionServerRegistry(IConfiguration configuration, ILogger<ExecutionServerRegistry> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    // =========================================================================
    // SERVER REGISTRATION
    // =========================================================================

    /// <inheritdoc />
    public async Task<Guid> RegisterServerAsync(
        ExecutionServerRegistration server,
        CancellationToken cancellationToken = default)
    {
        // MERGE on AgentName so re-registrations from the same machine are idempotent.
        const string sql = @"
            MERGE [RemoteAgents] WITH (HOLDLOCK) AS target
            USING (SELECT @AgentName AS AgentName) AS source
                ON target.[AgentName] = source.[AgentName]

            WHEN MATCHED THEN
                UPDATE SET
                    [Description]     = @Description,
                    [MachineName]     = @MachineName,
                    [IpAddress]       = @IpAddress,
                    [Version]         = @Version,
                    [OperatingSystem] = @OperatingSystem,
                    [IsPrimary]       = @IsPrimary,
                    [ServerRole]      = @ServerRole,
                    [BaseUrl]         = @BaseUrl,
                    [SupportedJobTypes] = @SupportedJobTypes,
                    [MaxConcurrentJobs] = @MaxConcurrentJobs,
                    [Tags]            = @Tags,
                    [Priority]        = @Priority,
                    [EnvironmentName] = @EnvironmentName,
                    [DotNetVersion]   = @DotNetVersion,
                    [Status]          = 'Online',
                    [LastStartedAt]   = GETUTCDATE(),
                    [DrainStartedAt]  = NULL

            WHEN NOT MATCHED THEN
                INSERT (
                    [Id], [AgentName], [Description], [MachineName], [IpAddress],
                    [Version], [OperatingSystem], [IsPrimary], [ServerRole], [BaseUrl],
                    [SupportedJobTypes], [MaxConcurrentJobs], [CurrentJobCount],
                    [TotalJobsProcessed], [TotalJobsFailed],
                    [Tags], [Priority], [EnvironmentName], [DotNetVersion],
                    [Status], [IsEnabled], [RegisteredAt], [LastStartedAt],
                    [ApiKeyHash]
                )
                VALUES (
                    @Id, @AgentName, @Description, @MachineName, @IpAddress,
                    @Version, @OperatingSystem, @IsPrimary, @ServerRole, @BaseUrl,
                    @SupportedJobTypes, @MaxConcurrentJobs, 0,
                    0, 0,
                    @Tags, @Priority, @EnvironmentName, @DotNetVersion,
                    'Online', 1, GETUTCDATE(), GETUTCDATE(),
                    ''
                )

            OUTPUT INSERTED.[Id];
        ";

        var id = server.Id ?? Guid.NewGuid();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var resultId = await connection.ExecuteScalarAsync<Guid>(sql, new
        {
            Id               = id,
            server.AgentName,
            server.Description,
            server.MachineName,
            server.IpAddress,
            server.Version,
            server.OperatingSystem,
            server.IsPrimary,
            server.ServerRole,
            server.BaseUrl,
            server.SupportedJobTypes,
            server.MaxConcurrentJobs,
            server.Tags,
            server.Priority,
            server.EnvironmentName,
            server.DotNetVersion
        });

        _logger.LogInformation(
            "Registered execution server {AgentName} with ID {ServerId}",
            server.AgentName, resultId);

        return resultId;
    }

    /// <inheritdoc />
    public async Task<bool> UnregisterServerAsync(
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        // Cascade DELETE on ServerHeartbeats and ServerJobTypeAssignments is
        // defined in V052, so a single DELETE here is sufficient.
        // Active JobQueue rows that reference this server are handled by the
        // FK on ClaimedByAgentId (nullable) — they remain with NULL agent.
        const string sql = @"
            DELETE FROM [RemoteAgents]
            WHERE [Id] = @ServerId;

            SELECT @@ROWCOUNT;
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteScalarAsync<int>(sql, new { ServerId = serverId });

        if (rows > 0)
        {
            _logger.LogInformation("Unregistered execution server {ServerId}", serverId);
            return true;
        }

        _logger.LogWarning("UnregisterServerAsync: server {ServerId} not found", serverId);
        return false;
    }

    /// <inheritdoc />
    public async Task<List<ExecutionServerInfo>> GetAllServersAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                ra.[Id], ra.[AgentName], ra.[Description], ra.[MachineName],
                ra.[IpAddress], ra.[Version], ra.[OperatingSystem], ra.[Status],
                ra.[IsPrimary], ra.[ServerRole], ra.[BaseUrl], ra.[SupportedJobTypes],
                ra.[MaxConcurrentJobs], ra.[CurrentJobCount],
                ra.[LastHeartbeat], ra.[LastJobClaimed], ra.[LastJobCompleted],
                ra.[TotalJobsProcessed], ra.[TotalJobsFailed],
                ra.[IsEnabled], ra.[RegisteredAt], ra.[DrainStartedAt], ra.[LastStartedAt],
                ra.[EnvironmentName], ra.[DotNetVersion], ra.[Tags], ra.[Priority]
            FROM [RemoteAgents] ra
            ORDER BY ra.[Priority] DESC, ra.[AgentName];
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<ExecutionServerInfo>(sql);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<ExecutionServerInfo?> GetServerAsync(
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                ra.[Id], ra.[AgentName], ra.[Description], ra.[MachineName],
                ra.[IpAddress], ra.[Version], ra.[OperatingSystem], ra.[Status],
                ra.[IsPrimary], ra.[ServerRole], ra.[BaseUrl], ra.[SupportedJobTypes],
                ra.[MaxConcurrentJobs], ra.[CurrentJobCount],
                ra.[LastHeartbeat], ra.[LastJobClaimed], ra.[LastJobCompleted],
                ra.[TotalJobsProcessed], ra.[TotalJobsFailed],
                ra.[IsEnabled], ra.[RegisteredAt], ra.[DrainStartedAt], ra.[LastStartedAt],
                ra.[EnvironmentName], ra.[DotNetVersion], ra.[Tags], ra.[Priority]
            FROM [RemoteAgents] ra
            WHERE ra.[Id] = @ServerId;
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<ExecutionServerInfo>(
            sql, new { ServerId = serverId });
    }

    /// <inheritdoc />
    public async Task<List<ExecutionServerInfo>> GetOnlineServersAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                ra.[Id], ra.[AgentName], ra.[Description], ra.[MachineName],
                ra.[IpAddress], ra.[Version], ra.[OperatingSystem], ra.[Status],
                ra.[IsPrimary], ra.[ServerRole], ra.[BaseUrl], ra.[SupportedJobTypes],
                ra.[MaxConcurrentJobs], ra.[CurrentJobCount],
                ra.[LastHeartbeat], ra.[LastJobClaimed], ra.[LastJobCompleted],
                ra.[TotalJobsProcessed], ra.[TotalJobsFailed],
                ra.[IsEnabled], ra.[RegisteredAt], ra.[DrainStartedAt], ra.[LastStartedAt],
                ra.[EnvironmentName], ra.[DotNetVersion], ra.[Tags], ra.[Priority]
            FROM [RemoteAgents] ra
            WHERE ra.[Status] = 'Online'
              AND ra.[IsEnabled] = 1
              AND ra.[LastHeartbeat] > DATEADD(MINUTE, -5, GETUTCDATE())
            ORDER BY ra.[Priority] DESC, ra.[AgentName];
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<ExecutionServerInfo>(sql);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<ExecutionServerInfo?> GetPrimaryServerAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP 1
                ra.[Id], ra.[AgentName], ra.[Description], ra.[MachineName],
                ra.[IpAddress], ra.[Version], ra.[OperatingSystem], ra.[Status],
                ra.[IsPrimary], ra.[ServerRole], ra.[BaseUrl], ra.[SupportedJobTypes],
                ra.[MaxConcurrentJobs], ra.[CurrentJobCount],
                ra.[LastHeartbeat], ra.[LastJobClaimed], ra.[LastJobCompleted],
                ra.[TotalJobsProcessed], ra.[TotalJobsFailed],
                ra.[IsEnabled], ra.[RegisteredAt], ra.[DrainStartedAt], ra.[LastStartedAt],
                ra.[EnvironmentName], ra.[DotNetVersion], ra.[Tags], ra.[Priority]
            FROM [RemoteAgents] ra
            WHERE ra.[IsPrimary] = 1;
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<ExecutionServerInfo>(sql);
    }

    // =========================================================================
    // HEARTBEAT & TELEMETRY
    // =========================================================================

    /// <inheritdoc />
    public async Task RecordHeartbeatAsync(
        ServerHeartbeatData heartbeat,
        CancellationToken cancellationToken = default)
    {
        // Update the agent row and insert the time-series record in a single
        // round-trip using a transaction on the same connection.
        const string updateAgent = @"
            UPDATE [RemoteAgents]
            SET [LastHeartbeat]   = GETUTCDATE(),
                [Status]          = 'Online',
                [CurrentJobCount] = @ActiveJobCount
            WHERE [Id] = @ServerId;
        ";

        const string insertHeartbeat = @"
            INSERT INTO [ServerHeartbeats] (
                [Id], [ServerId], [Timestamp],
                [CpuPercent], [MemoryPercent], [MemoryUsedMb], [DiskFreeGb],
                [ActiveJobCount], [ThreadPoolActive], [ThreadPoolQueued],
                [GcGen0Count], [GcGen2Count], [HeapSizeMb],
                [IsHealthy], [StatusMessage]
            )
            VALUES (
                NEWID(), @ServerId, GETUTCDATE(),
                @CpuPercent, @MemoryPercent, @MemoryUsedMb, @DiskFreeGb,
                @ActiveJobCount, @ThreadPoolActive, @ThreadPoolQueued,
                @GcGen0Count, @GcGen2Count, @HeapSizeMb,
                @IsHealthy, @StatusMessage
            );
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(updateAgent, heartbeat, transaction: (IDbTransaction)tx);
            await connection.ExecuteAsync(insertHeartbeat, heartbeat, transaction: (IDbTransaction)tx);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<ServerHeartbeatData>> GetRecentHeartbeatsAsync(
        Guid serverId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                [ServerId], [Timestamp],
                [CpuPercent], [MemoryPercent], [MemoryUsedMb], [DiskFreeGb],
                [ActiveJobCount], [ThreadPoolActive], [ThreadPoolQueued],
                [GcGen0Count], [GcGen2Count], [HeapSizeMb],
                [IsHealthy], [StatusMessage]
            FROM [ServerHeartbeats]
            WHERE [ServerId] = @ServerId
              AND [Timestamp] > @Cutoff
            ORDER BY [Timestamp] DESC;
        ";

        var cutoff = DateTime.UtcNow.Subtract(duration);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<ServerHeartbeatData>(
            sql, new { ServerId = serverId, Cutoff = cutoff });

        return rows.ToList();
    }

    // =========================================================================
    // ORPHAN DETECTION & RECOVERY
    // =========================================================================

    /// <inheritdoc />
    public async Task<int> DetectAndRecoverOrphansAsync(
        int heartbeatTimeoutMinutes = 10,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var result = await connection.ExecuteScalarAsync<int>(
            "usp_ReassignOrphanedJobs",
            new { HeartbeatTimeoutMinutes = heartbeatTimeoutMinutes },
            commandType: CommandType.StoredProcedure);

        _logger.LogInformation(
            "DetectAndRecoverOrphans: {Count} jobs reassigned (timeout={Timeout}min)",
            result, heartbeatTimeoutMinutes);

        return result;
    }

    /// <inheritdoc />
    public async Task CleanupOldHeartbeatsAsync(
        int retentionDays = 7,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            "usp_CleanupOldHeartbeats",
            new { RetentionDays = retentionDays },
            commandType: CommandType.StoredProcedure);

        _logger.LogInformation(
            "CleanupOldHeartbeats: completed (retention={Days} days)", retentionDays);
    }

    // =========================================================================
    // JOB TYPE ASSIGNMENTS
    // =========================================================================

    /// <inheritdoc />
    public async Task SetJobTypeAssignmentsAsync(
        Guid serverId,
        List<JobTypeAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        const string deleteExisting = @"
            DELETE FROM [ServerJobTypeAssignments]
            WHERE [ServerId] = @ServerId;
        ";

        const string insertAssignment = @"
            INSERT INTO [ServerJobTypeAssignments] (
                [Id], [ServerId], [JobType], [IsEnabled], [Priority], [MaxConcurrent],
                [CreatedAt], [ModifiedAt]
            )
            VALUES (
                @Id, @ServerId, @JobType, @IsEnabled, @Priority, @MaxConcurrent,
                GETUTCDATE(), NULL
            );
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(
                deleteExisting,
                new { ServerId = serverId },
                transaction: (IDbTransaction)tx);

            foreach (var assignment in assignments)
            {
                await connection.ExecuteAsync(insertAssignment, new
                {
                    Id           = assignment.Id ?? Guid.NewGuid(),
                    ServerId     = serverId,
                    assignment.JobType,
                    assignment.IsEnabled,
                    assignment.Priority,
                    assignment.MaxConcurrent
                }, transaction: (IDbTransaction)tx);
            }

            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "SetJobTypeAssignments: updated {Count} assignments for server {ServerId}",
                assignments.Count, serverId);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<JobTypeAssignment>> GetJobTypeAssignmentsAsync(
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [Id], [ServerId], [JobType], [IsEnabled], [Priority], [MaxConcurrent]
            FROM [ServerJobTypeAssignments]
            WHERE [ServerId] = @ServerId
            ORDER BY [Priority] DESC, [JobType];
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<JobTypeAssignment>(
            sql, new { ServerId = serverId });

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<List<ExecutionServerInfo>> GetServersForJobTypeAsync(
        string jobType,
        CancellationToken cancellationToken = default)
    {
        // Returns servers that either have an explicit enabled assignment for the
        // requested job type OR support all job types via the '*' wildcard in
        // SupportedJobTypes. Results are ordered by assignment priority (desc)
        // so callers can pick the best server first.
        const string sql = @"
            SELECT DISTINCT
                ra.[Id], ra.[AgentName], ra.[Description], ra.[MachineName],
                ra.[IpAddress], ra.[Version], ra.[OperatingSystem], ra.[Status],
                ra.[IsPrimary], ra.[ServerRole], ra.[BaseUrl], ra.[SupportedJobTypes],
                ra.[MaxConcurrentJobs], ra.[CurrentJobCount],
                ra.[LastHeartbeat], ra.[LastJobClaimed], ra.[LastJobCompleted],
                ra.[TotalJobsProcessed], ra.[TotalJobsFailed],
                ra.[IsEnabled], ra.[RegisteredAt], ra.[DrainStartedAt], ra.[LastStartedAt],
                ra.[EnvironmentName], ra.[DotNetVersion], ra.[Tags], ra.[Priority],
                COALESCE(ja.[Priority], ra.[Priority]) AS AssignmentPriority
            FROM [RemoteAgents] ra
            LEFT JOIN [ServerJobTypeAssignments] ja
                ON ja.[ServerId] = ra.[Id]
               AND ja.[JobType]  = @JobType
               AND ja.[IsEnabled] = 1
            WHERE ra.[IsEnabled]  = 1
              AND ra.[Status]     = 'Online'
              AND ra.[LastHeartbeat] > DATEADD(MINUTE, -5, GETUTCDATE())
              AND (
                    -- Explicit assignment for this job type
                    ja.[Id] IS NOT NULL
                    OR
                    -- Server supports all job types (wildcard)
                    ra.[SupportedJobTypes] = '*'
                    OR
                    -- Job type appears in the comma-separated list
                    (',' + ra.[SupportedJobTypes] + ',' LIKE '%,' + @JobType + ',%')
                  )
            ORDER BY AssignmentPriority DESC, ra.[CurrentJobCount] ASC;
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<ExecutionServerInfo>(
            sql, new { JobType = jobType });

        return rows.ToList();
    }

    // =========================================================================
    // SERVER MANAGEMENT
    // =========================================================================

    /// <inheritdoc />
    public async Task UpdateServerConfigAsync(
        Guid serverId,
        ExecutionServerConfigUpdate update,
        CancellationToken cancellationToken = default)
    {
        // Build the SET clause dynamically based on which fields are provided.
        // All non-null properties are applied; null properties are left unchanged.
        var setClauses = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("ServerId", serverId);

        if (update.Description is not null)
        {
            setClauses.Add("[Description] = @Description");
            parameters.Add("Description", update.Description);
        }
        if (update.MaxConcurrentJobs.HasValue)
        {
            setClauses.Add("[MaxConcurrentJobs] = @MaxConcurrentJobs");
            parameters.Add("MaxConcurrentJobs", update.MaxConcurrentJobs.Value);
        }
        if (update.SupportedJobTypes is not null)
        {
            setClauses.Add("[SupportedJobTypes] = @SupportedJobTypes");
            parameters.Add("SupportedJobTypes", update.SupportedJobTypes);
        }
        if (update.Tags is not null)
        {
            setClauses.Add("[Tags] = @Tags");
            parameters.Add("Tags", update.Tags);
        }
        if (update.Priority.HasValue)
        {
            setClauses.Add("[Priority] = @Priority");
            parameters.Add("Priority", update.Priority.Value);
        }

        if (setClauses.Count == 0)
        {
            _logger.LogDebug("UpdateServerConfigAsync: no fields to update for {ServerId}", serverId);
            return;
        }

        var sql = $"""
            UPDATE [RemoteAgents]
            SET {string.Join(", ", setClauses)}
            WHERE [Id] = @ServerId;
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(sql, parameters);

        _logger.LogInformation(
            "UpdateServerConfig: updated {Fields} field(s) for server {ServerId}",
            setClauses.Count, serverId);

        if (rows == 0)
        {
            _logger.LogWarning("UpdateServerConfigAsync: server {ServerId} not found", serverId);
        }
    }

    /// <inheritdoc />
    public async Task SetServerEnabledAsync(
        Guid serverId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [RemoteAgents]
            SET [IsEnabled] = @IsEnabled
            WHERE [Id] = @ServerId;
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(sql, new { ServerId = serverId, IsEnabled = enabled });

        _logger.LogInformation(
            "SetServerEnabled: server {ServerId} enabled={Enabled}", serverId, enabled);
    }

    /// <inheritdoc />
    public async Task DrainServerAsync(
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [RemoteAgents]
            SET [DrainStartedAt] = GETUTCDATE(),
                [Status]         = 'Draining'
            WHERE [Id] = @ServerId
              AND [DrainStartedAt] IS NULL;
        ";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(sql, new { ServerId = serverId });

        if (rows > 0)
        {
            _logger.LogInformation(
                "DrainServer: initiated drain on server {ServerId}", serverId);
        }
        else
        {
            _logger.LogDebug(
                "DrainServer: server {ServerId} was already draining or not found", serverId);
        }
    }
}
