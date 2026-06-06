-- ============================================================================
-- V052: Execution Server Foundation
--
-- Extends the existing RemoteAgents/JobQueue infrastructure to support
-- distributed execution servers. The primary IdentityCenter instance
-- becomes a self-registered execution server, and remote workers can
-- join the cluster to share job processing load.
--
-- Changes:
--   1. ALTER RemoteAgents: Add IsPrimary, ServerRole, BaseUrl, DrainStartedAt,
--      LastStartedAt, EnvironmentName, DotNetVersion
--   2. CREATE ServerHeartbeats: Time-series telemetry table
--   3. CREATE ServerJobTypeAssignments: Per-server job type routing
--   4. ALTER JobQueue: Add TargetServerId, CancellationRequested
--   5. ALTER JobExecutionHistory: Add ExecutionServerId
--   6. Filtered index for efficient distributed job claiming
--   7. Stored proc usp_ClaimJobsForServer: Atomic batch claim
--   8. Stored proc usp_EnqueueJobBatch: TVP for bulk inserts
--   9. Seed default primary server record
-- ============================================================================

-- ============================================================================
-- 1. EXTEND RemoteAgents TABLE
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'RemoteAgents' AND COLUMN_NAME = 'IsPrimary'
)
BEGIN
    ALTER TABLE [RemoteAgents] ADD [IsPrimary] bit NOT NULL CONSTRAINT [DF_RemoteAgents_IsPrimary] DEFAULT (0);
    PRINT 'Added RemoteAgents.IsPrimary';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'RemoteAgents' AND COLUMN_NAME = 'ServerRole'
)
BEGIN
    ALTER TABLE [RemoteAgents] ADD [ServerRole] nvarchar(20) NOT NULL CONSTRAINT [DF_RemoteAgents_ServerRole] DEFAULT ('Worker');
    PRINT 'Added RemoteAgents.ServerRole';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'RemoteAgents' AND COLUMN_NAME = 'BaseUrl'
)
BEGIN
    ALTER TABLE [RemoteAgents] ADD [BaseUrl] nvarchar(500) NULL;
    PRINT 'Added RemoteAgents.BaseUrl';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'RemoteAgents' AND COLUMN_NAME = 'DrainStartedAt'
)
BEGIN
    ALTER TABLE [RemoteAgents] ADD [DrainStartedAt] datetime2 NULL;
    PRINT 'Added RemoteAgents.DrainStartedAt';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'RemoteAgents' AND COLUMN_NAME = 'LastStartedAt'
)
BEGIN
    ALTER TABLE [RemoteAgents] ADD [LastStartedAt] datetime2 NULL;
    PRINT 'Added RemoteAgents.LastStartedAt';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'RemoteAgents' AND COLUMN_NAME = 'EnvironmentName'
)
BEGIN
    ALTER TABLE [RemoteAgents] ADD [EnvironmentName] nvarchar(50) NULL;
    PRINT 'Added RemoteAgents.EnvironmentName';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'RemoteAgents' AND COLUMN_NAME = 'DotNetVersion'
)
BEGIN
    ALTER TABLE [RemoteAgents] ADD [DotNetVersion] nvarchar(50) NULL;
    PRINT 'Added RemoteAgents.DotNetVersion';
END;
GO


-- ============================================================================
-- 2. CREATE ServerHeartbeats TABLE
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ServerHeartbeats')
BEGIN
    CREATE TABLE [ServerHeartbeats] (
        [Id]               uniqueidentifier NOT NULL,
        [ServerId]         uniqueidentifier NOT NULL,
        [Timestamp]        datetime2        NOT NULL,
        [CpuPercent]       float            NOT NULL,
        [MemoryPercent]    float            NOT NULL,
        [MemoryUsedMb]     bigint           NOT NULL,
        [DiskFreeGb]       float            NOT NULL,
        [ActiveJobCount]   int              NOT NULL,
        [ThreadPoolActive] int              NOT NULL,
        [ThreadPoolQueued] int              NOT NULL,
        [GcGen0Count]      bigint           NOT NULL,
        [GcGen2Count]      bigint           NOT NULL,
        [HeapSizeMb]       float            NOT NULL,
        [IsHealthy]        bit              NOT NULL,
        [StatusMessage]    nvarchar(500)    NULL,

        CONSTRAINT [PK_ServerHeartbeats] PRIMARY KEY NONCLUSTERED ([Id]),

        CONSTRAINT [FK_ServerHeartbeats_RemoteAgents] FOREIGN KEY ([ServerId])
            REFERENCES [RemoteAgents] ([Id]) ON DELETE CASCADE
    );

    -- Clustered index on (ServerId, Timestamp) for efficient time-range queries per server.
    -- This is the primary access pattern: "give me heartbeats for server X in the last N minutes."
    -- CASCADE delete ensures heartbeats are cleaned up when a server is removed.
    CREATE CLUSTERED INDEX [CIX_ServerHeartbeats_ServerId_Timestamp]
        ON [ServerHeartbeats] ([ServerId], [Timestamp] DESC);

    -- Index for cleanup queries: "delete heartbeats older than N days."
    CREATE NONCLUSTERED INDEX [IX_ServerHeartbeats_Timestamp]
        ON [ServerHeartbeats] ([Timestamp]);

    PRINT 'Created ServerHeartbeats table with clustered index on (ServerId, Timestamp)';
END;
GO


-- ============================================================================
-- 3. CREATE ServerJobTypeAssignments TABLE
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ServerJobTypeAssignments')
BEGIN
    CREATE TABLE [ServerJobTypeAssignments] (
        [Id]            uniqueidentifier NOT NULL,
        [ServerId]      uniqueidentifier NOT NULL,
        [JobType]       nvarchar(50)     NOT NULL,
        [IsEnabled]     bit              NOT NULL CONSTRAINT [DF_SJTA_IsEnabled] DEFAULT (1),
        [Priority]      int              NOT NULL CONSTRAINT [DF_SJTA_Priority] DEFAULT (100),
        [MaxConcurrent] int              NOT NULL CONSTRAINT [DF_SJTA_MaxConcurrent] DEFAULT (0),
        [CreatedAt]     datetime2        NOT NULL CONSTRAINT [DF_SJTA_CreatedAt] DEFAULT (GETUTCDATE()),
        [ModifiedAt]    datetime2        NULL,

        CONSTRAINT [PK_ServerJobTypeAssignments] PRIMARY KEY ([Id]),

        CONSTRAINT [FK_SJTA_RemoteAgents] FOREIGN KEY ([ServerId])
            REFERENCES [RemoteAgents] ([Id]) ON DELETE CASCADE,

        -- Each server can only have one assignment per job type
        CONSTRAINT [UQ_SJTA_ServerId_JobType] UNIQUE ([ServerId], [JobType])
    );

    CREATE NONCLUSTERED INDEX [IX_SJTA_JobType]
        ON [ServerJobTypeAssignments] ([JobType]) INCLUDE ([ServerId], [IsEnabled], [Priority]);

    PRINT 'Created ServerJobTypeAssignments table';
END;
GO


-- ============================================================================
-- 4. EXTEND JobQueue TABLE
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'JobQueue' AND COLUMN_NAME = 'TargetServerId'
)
BEGIN
    ALTER TABLE [JobQueue] ADD [TargetServerId] uniqueidentifier NULL;

    -- Nullable FK: if TargetServerId is set, the job is routed to that specific server.
    -- If NULL, any eligible server can claim it.
    ALTER TABLE [JobQueue] ADD CONSTRAINT [FK_JobQueue_TargetServer]
        FOREIGN KEY ([TargetServerId]) REFERENCES [RemoteAgents] ([Id]);

    PRINT 'Added JobQueue.TargetServerId with FK to RemoteAgents';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'JobQueue' AND COLUMN_NAME = 'CancellationRequested'
)
BEGIN
    ALTER TABLE [JobQueue] ADD [CancellationRequested] bit NOT NULL
        CONSTRAINT [DF_JobQueue_CancellationRequested] DEFAULT (0);
    PRINT 'Added JobQueue.CancellationRequested';
END;
GO


-- ============================================================================
-- 5. EXTEND JobExecutionHistory TABLE
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'JobExecutionHistory' AND COLUMN_NAME = 'ExecutionServerId'
)
BEGIN
    ALTER TABLE [JobExecutionHistory] ADD [ExecutionServerId] uniqueidentifier NULL;

    ALTER TABLE [JobExecutionHistory] ADD CONSTRAINT [FK_JEH_ExecutionServer]
        FOREIGN KEY ([ExecutionServerId]) REFERENCES [RemoteAgents] ([Id]);

    PRINT 'Added JobExecutionHistory.ExecutionServerId with FK to RemoteAgents';
END;
GO


-- ============================================================================
-- 6. FILTERED INDEX FOR DISTRIBUTED JOB CLAIMING
-- ============================================================================

-- This filtered index covers the exact WHERE clause used by usp_ClaimJobsForServer.
-- It only indexes rows that are Pending AND Ready2Execute, which is typically a small
-- fraction of the total JobQueue. This keeps the index tiny and fast even when the
-- table contains millions of historical rows.

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_JobQueue_ClaimDistributed' AND object_id = OBJECT_ID('JobQueue')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_JobQueue_ClaimDistributed]
        ON [JobQueue] ([Priority] DESC, [CreatedAt] ASC)
        INCLUDE ([JobType], [TargetServerId], [ScheduledAt])
        WHERE [Status] = 'Pending' AND [Ready2Execute] = 1;

    PRINT 'Created filtered index IX_JobQueue_ClaimDistributed';
END;
GO

-- Index for finding jobs targeted to a specific server
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_JobQueue_TargetServerId' AND object_id = OBJECT_ID('JobQueue')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_JobQueue_TargetServerId]
        ON [JobQueue] ([TargetServerId])
        INCLUDE ([Status], [JobType], [Priority])
        WHERE [TargetServerId] IS NOT NULL;

    PRINT 'Created filtered index IX_JobQueue_TargetServerId';
END;
GO

-- Index for finding cancellation-requested jobs (checked by executing servers)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_JobQueue_CancellationRequested' AND object_id = OBJECT_ID('JobQueue')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_JobQueue_CancellationRequested]
        ON [JobQueue] ([ClaimedByAgentId])
        INCLUDE ([Id], [CancellationRequested])
        WHERE [CancellationRequested] = 1 AND [Status] IN ('Claimed', 'Processing');

    PRINT 'Created filtered index IX_JobQueue_CancellationRequested';
END;
GO

-- Index for execution history by server
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_JEH_ExecutionServerId' AND object_id = OBJECT_ID('JobExecutionHistory')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_JEH_ExecutionServerId]
        ON [JobExecutionHistory] ([ExecutionServerId])
        INCLUDE ([JobType], [Status], [StartedAt], [DurationMs])
        WHERE [ExecutionServerId] IS NOT NULL;

    PRINT 'Created index IX_JEH_ExecutionServerId';
END;
GO


-- ============================================================================
-- 7. STORED PROCEDURE: usp_ClaimJobsForServer
-- ============================================================================

-- Atomic batch claim: claims up to @MaxJobs rows for a given server in a single
-- atomic operation. Uses ROWLOCK + UPDLOCK + READPAST to allow concurrent claims
-- from multiple servers without deadlocks.
--
-- The procedure handles three routing modes:
--   1. TargetServerId = @ServerId (server affinity - job explicitly assigned)
--   2. TargetServerId IS NULL AND JobType matches server capabilities (automatic)
--   3. Never claims jobs targeted to a different server
--
-- Returns the full set of claimed job rows.

IF OBJECT_ID('dbo.usp_ClaimJobsForServer', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_ClaimJobsForServer;
GO

CREATE PROCEDURE dbo.usp_ClaimJobsForServer
    @ServerId         uniqueidentifier,
    @SupportedJobTypes nvarchar(max),     -- Comma-separated list of job types
    @MaxJobs          int = 5
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Parse the comma-separated job types into a table variable
    DECLARE @JobTypes TABLE (JobType nvarchar(50));

    INSERT INTO @JobTypes (JobType)
    SELECT LTRIM(RTRIM(value))
    FROM STRING_SPLIT(@SupportedJobTypes, ',')
    WHERE LTRIM(RTRIM(value)) <> '';

    -- Check if this server supports all job types (wildcard)
    DECLARE @SupportsAll bit = 0;
    IF EXISTS (SELECT 1 FROM @JobTypes WHERE JobType = '*')
        SET @SupportsAll = 1;

    -- Create a table to hold the IDs of claimed jobs
    DECLARE @ClaimedIds TABLE (Id uniqueidentifier);

    BEGIN TRANSACTION;

    -- Claim jobs using atomic UPDATE with OUTPUT clause.
    -- The WHERE clause implements the routing logic:
    --   - Job must be Pending and Ready2Execute
    --   - Job must not be scheduled for the future
    --   - Job must either target THIS server specifically OR have no target and match supported types
    --   - Never claim jobs targeted to a DIFFERENT server
    -- Use CTE with ROW_NUMBER for ordered claiming (UPDATE TOP doesn't support ORDER BY)
    ;WITH ClaimCTE AS (
        SELECT TOP (@MaxJobs)
            jq.[Id], jq.[Status], jq.[ClaimedByAgentId], jq.[ClaimedAt]
        FROM [JobQueue] jq WITH (ROWLOCK, UPDLOCK, READPAST)
        WHERE jq.[Status] = 'Pending'
          AND jq.[Ready2Execute] = 1
          AND (jq.[ScheduledAt] IS NULL OR jq.[ScheduledAt] <= GETUTCDATE())
          AND (
                jq.[TargetServerId] = @ServerId
                OR
                (jq.[TargetServerId] IS NULL AND (@SupportsAll = 1 OR jq.[JobType] IN (SELECT JobType FROM @JobTypes)))
              )
        ORDER BY jq.[Priority] DESC, jq.[CreatedAt] ASC
    )
    UPDATE ClaimCTE
    SET
        [Status] = 'Claimed',
        [ClaimedByAgentId] = @ServerId,
        [ClaimedAt] = GETUTCDATE()
    OUTPUT INSERTED.[Id] INTO @ClaimedIds;

    -- Return the full job details for all claimed jobs
    SELECT
        jq.[Id], jq.[JobType], jq.[JobName], jq.[RelatedEntityId], jq.[RelatedEntityType],
        jq.[Status], jq.[Priority], jq.[Ready2Execute], jq.[ScheduledAt], jq.[CreatedAt],
        jq.[CreatedBy], jq.[ClaimedByAgentId], jq.[ClaimedAt], jq.[StartedAt],
        jq.[CompletedAt], jq.[DurationMs], jq.[ItemsProcessed], jq.[ItemsSucceeded],
        jq.[ItemsFailed], jq.[ErrorMessage], jq.[ExceptionDetailsJson],
        jq.[RetryAttempt], jq.[MaxRetries], jq.[PayloadJson], jq.[ResultJson],
        jq.[ProgressPercent], jq.[ProgressMessage], jq.[LastProgressUpdate], jq.[Tags],
        jq.[TargetServerId], jq.[CancellationRequested]
    FROM [JobQueue] jq
    INNER JOIN @ClaimedIds ci ON jq.[Id] = ci.[Id];

    -- Update the server's current job count and last claimed timestamp
    UPDATE [RemoteAgents]
    SET [CurrentJobCount] = [CurrentJobCount] + (SELECT COUNT(*) FROM @ClaimedIds),
        [LastJobClaimed] = GETUTCDATE()
    WHERE [Id] = @ServerId
      AND EXISTS (SELECT 1 FROM @ClaimedIds);

    COMMIT TRANSACTION;
END;
GO

PRINT 'Created stored procedure usp_ClaimJobsForServer';
GO


-- ============================================================================
-- 8. STORED PROCEDURE: usp_EnqueueJobBatch
-- ============================================================================

-- Table-valued parameter type for bulk job insertion.
-- Supports million-row inserts in a single round trip.

IF NOT EXISTS (SELECT 1 FROM sys.types WHERE name = 'JobQueueBatchType' AND is_table_type = 1)
BEGIN
    CREATE TYPE dbo.JobQueueBatchType AS TABLE (
        [Id]                uniqueidentifier NOT NULL,
        [JobType]           nvarchar(50)     NOT NULL,
        [JobName]           nvarchar(200)    NOT NULL,
        [RelatedEntityId]   uniqueidentifier NULL,
        [RelatedEntityType] nvarchar(50)     NULL,
        [Priority]          int              NOT NULL,
        [Ready2Execute]     bit              NOT NULL,
        [ScheduledAt]       datetime2        NULL,
        [CreatedBy]         nvarchar(200)    NOT NULL,
        [MaxRetries]        int              NOT NULL,
        [PayloadJson]       nvarchar(max)    NULL,
        [Tags]              nvarchar(500)    NULL,
        [TargetServerId]    uniqueidentifier NULL
    );
    PRINT 'Created table type JobQueueBatchType';
END;
GO

IF OBJECT_ID('dbo.usp_EnqueueJobBatch', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_EnqueueJobBatch;
GO

CREATE PROCEDURE dbo.usp_EnqueueJobBatch
    @Jobs dbo.JobQueueBatchType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @InsertedCount int;

    INSERT INTO [JobQueue] (
        [Id], [JobType], [JobName], [RelatedEntityId], [RelatedEntityType],
        [Status], [Priority], [Ready2Execute], [ScheduledAt], [CreatedAt], [CreatedBy],
        [RetryAttempt], [MaxRetries], [PayloadJson], [Tags], [TargetServerId],
        [CancellationRequested], [ItemsProcessed], [ItemsSucceeded], [ItemsFailed],
        [ProgressPercent]
    )
    SELECT
        [Id], [JobType], [JobName], [RelatedEntityId], [RelatedEntityType],
        'Pending', [Priority], [Ready2Execute], [ScheduledAt], GETUTCDATE(), [CreatedBy],
        0, [MaxRetries], [PayloadJson], [Tags], [TargetServerId],
        0, 0, 0, 0, 0
    FROM @Jobs;

    SET @InsertedCount = @@ROWCOUNT;

    SELECT @InsertedCount AS [InsertedCount];
END;
GO

PRINT 'Created stored procedure usp_EnqueueJobBatch';
GO


-- ============================================================================
-- 9. STORED PROCEDURE: usp_ReassignOrphanedJobs
-- ============================================================================

-- Finds jobs claimed by servers that have not sent a heartbeat within the
-- specified threshold and resets them to Pending for re-claiming.

IF OBJECT_ID('dbo.usp_ReassignOrphanedJobs', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_ReassignOrphanedJobs;
GO

CREATE PROCEDURE dbo.usp_ReassignOrphanedJobs
    @HeartbeatTimeoutMinutes int = 10
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OrphanedCount int;

    -- Find servers that are offline (no heartbeat within threshold)
    -- and have jobs in Claimed or Processing status
    UPDATE jq
    SET
        jq.[Status] = 'Pending',
        jq.[ClaimedByAgentId] = NULL,
        jq.[ClaimedAt] = NULL,
        jq.[StartedAt] = NULL,
        jq.[RetryAttempt] = jq.[RetryAttempt] + 1,
        jq.[ProgressPercent] = 0,
        jq.[ProgressMessage] = CONCAT('Reassigned: server ', ra.[AgentName], ' went offline at ', CONVERT(nvarchar(30), GETUTCDATE(), 126)),
        jq.[LastProgressUpdate] = GETUTCDATE(),
        jq.[TargetServerId] = NULL  -- Clear affinity so any server can pick it up
    FROM [JobQueue] jq
    INNER JOIN [RemoteAgents] ra ON jq.[ClaimedByAgentId] = ra.[Id]
    WHERE jq.[Status] IN ('Claimed', 'Processing')
      AND jq.[RetryAttempt] < jq.[MaxRetries]
      AND (
            ra.[LastHeartbeat] IS NULL
            OR ra.[LastHeartbeat] < DATEADD(MINUTE, -@HeartbeatTimeoutMinutes, GETUTCDATE())
          )
      AND ra.[DrainStartedAt] IS NULL;  -- Don't reassign jobs from draining servers (they're finishing up)

    SET @OrphanedCount = @@ROWCOUNT;

    -- Also fail jobs that have exceeded max retries
    UPDATE jq
    SET
        jq.[Status] = 'Failed',
        jq.[CompletedAt] = GETUTCDATE(),
        jq.[ErrorMessage] = CONCAT('Max retries exceeded after server failure. Last server: ', ra.[AgentName]),
        jq.[LastProgressUpdate] = GETUTCDATE()
    FROM [JobQueue] jq
    INNER JOIN [RemoteAgents] ra ON jq.[ClaimedByAgentId] = ra.[Id]
    WHERE jq.[Status] IN ('Claimed', 'Processing')
      AND jq.[RetryAttempt] >= jq.[MaxRetries]
      AND (
            ra.[LastHeartbeat] IS NULL
            OR ra.[LastHeartbeat] < DATEADD(MINUTE, -@HeartbeatTimeoutMinutes, GETUTCDATE())
          );

    -- Update offline server statuses
    UPDATE [RemoteAgents]
    SET [Status] = 'Offline',
        [CurrentJobCount] = 0
    WHERE [Status] NOT IN ('Offline', 'Draining')
      AND (
            [LastHeartbeat] IS NULL
            OR [LastHeartbeat] < DATEADD(MINUTE, -@HeartbeatTimeoutMinutes, GETUTCDATE())
          );

    SELECT @OrphanedCount AS [ReassignedCount];
END;
GO

PRINT 'Created stored procedure usp_ReassignOrphanedJobs';
GO


-- ============================================================================
-- 10. SEED DEFAULT PRIMARY SERVER RECORD
-- ============================================================================

-- Insert a well-known primary server record that the primary instance will
-- claim on startup via matching on IsPrimary = 1. If no primary record
-- exists, the startup code will INSERT one instead.
--
-- The NEWID() here generates a stable placeholder; the primary's startup
-- code will UPDATE this row with actual machine name, version, etc.

IF NOT EXISTS (SELECT 1 FROM [RemoteAgents] WHERE [IsPrimary] = 1)
BEGIN
    DECLARE @PrimaryServerId uniqueidentifier = NEWID();

    INSERT INTO [RemoteAgents] (
        [Id], [AgentName], [Description], [MachineName], [Version],
        [ApiKeyHash], [Status], [SupportedJobTypes], [MaxConcurrentJobs],
        [CurrentJobCount], [TotalJobsProcessed], [TotalJobsFailed],
        [IsEnabled], [RegisteredAt], [Priority],
        [IsPrimary], [ServerRole]
    )
    VALUES (
        @PrimaryServerId,
        'Primary Server',
        'Auto-registered primary IdentityCenter instance',
        '(pending)',           -- Updated on startup
        '1.0.0',               -- Updated on startup
        '',                    -- Primary uses internal auth, not API key
        'Offline',             -- Updated to Online on startup
        '*',                   -- Primary handles all job types by default
        10,                    -- Default max concurrent (matches Quartz thread pool)
        0, 0, 0,
        1,                     -- Enabled
        GETUTCDATE(),
        1000,                  -- Highest priority for job claiming
        1,                     -- IsPrimary = true
        'Primary'              -- ServerRole = Primary
    );

    PRINT 'Seeded default primary server record';
END;
GO


-- ============================================================================
-- 11. CLEANUP STORED PROCEDURE FOR HEARTBEAT MAINTENANCE
-- ============================================================================

IF OBJECT_ID('dbo.usp_CleanupOldHeartbeats', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_CleanupOldHeartbeats;
GO

CREATE PROCEDURE dbo.usp_CleanupOldHeartbeats
    @RetentionDays int = 7
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CutoffDate datetime2 = DATEADD(DAY, -@RetentionDays, GETUTCDATE());
    DECLARE @BatchSize int = 10000;
    DECLARE @DeletedCount int = 1;

    -- Delete in batches to avoid transaction log bloat
    WHILE @DeletedCount > 0
    BEGIN
        DELETE TOP (@BatchSize) FROM [ServerHeartbeats]
        WHERE [Timestamp] < @CutoffDate;

        SET @DeletedCount = @@ROWCOUNT;
    END;
END;
GO

PRINT 'Created stored procedure usp_CleanupOldHeartbeats';
GO

PRINT 'V052__ExecutionServerFoundation migration complete';
GO
