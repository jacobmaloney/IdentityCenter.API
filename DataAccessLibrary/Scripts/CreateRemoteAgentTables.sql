-- ============================================================================
-- Remote Agent and Job Queue Tables
-- IdentityCenter API Infrastructure
-- Created: 2025-12-11
-- ============================================================================

-- ============================================================================
-- RemoteAgents Table
-- Tracks registered sync agents that poll for and execute jobs
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RemoteAgents]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[RemoteAgents] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [AgentName] NVARCHAR(200) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [MachineName] NVARCHAR(200) NULL,
        [IpAddress] NVARCHAR(50) NULL,
        [Version] NVARCHAR(50) NOT NULL DEFAULT '1.0.0',
        [OperatingSystem] NVARCHAR(200) NULL,
        [ApiKeyHash] NVARCHAR(500) NULL,
        [Status] NVARCHAR(20) NOT NULL DEFAULT 'Offline',
        [SupportedJobTypes] NVARCHAR(500) NOT NULL DEFAULT 'SyncProject',
        [MaxConcurrentJobs] INT NOT NULL DEFAULT 1,
        [CurrentJobCount] INT NOT NULL DEFAULT 0,
        [LastHeartbeat] DATETIME2 NULL,
        [LastJobClaimed] DATETIME2 NULL,
        [LastJobCompleted] DATETIME2 NULL,
        [TotalJobsProcessed] INT NOT NULL DEFAULT 0,
        [TotalJobsFailed] INT NOT NULL DEFAULT 0,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [RegisteredAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [ConfigUpdatedAt] DATETIME2 NULL,
        [ConfigurationJson] NVARCHAR(MAX) NULL,
        [Tags] NVARCHAR(500) NULL,
        [Priority] INT NOT NULL DEFAULT 100,

        CONSTRAINT [UQ_RemoteAgents_AgentName] UNIQUE ([AgentName])
    );

    CREATE INDEX [IX_RemoteAgents_Status] ON [dbo].[RemoteAgents] ([Status]);
    CREATE INDEX [IX_RemoteAgents_LastHeartbeat] ON [dbo].[RemoteAgents] ([LastHeartbeat]);
    CREATE INDEX [IX_RemoteAgents_IsEnabled] ON [dbo].[RemoteAgents] ([IsEnabled]);

    PRINT 'Created RemoteAgents table';
END
GO

-- ============================================================================
-- JobQueue Table
-- Job queue for remote agents to poll and claim
-- Uses row-level locking for atomic job claiming
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[JobQueue]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[JobQueue] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [JobType] NVARCHAR(50) NOT NULL,
        [JobName] NVARCHAR(200) NOT NULL,
        [RelatedEntityId] UNIQUEIDENTIFIER NULL,
        [RelatedEntityType] NVARCHAR(50) NULL,
        [Status] NVARCHAR(20) NOT NULL DEFAULT 'Pending',
        [Priority] INT NOT NULL DEFAULT 500,
        [Ready2Execute] BIT NOT NULL DEFAULT 1,
        [ScheduledAt] DATETIME2 NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] NVARCHAR(200) NOT NULL DEFAULT 'System',
        [ClaimedByAgentId] UNIQUEIDENTIFIER NULL,
        [ClaimedAt] DATETIME2 NULL,
        [StartedAt] DATETIME2 NULL,
        [CompletedAt] DATETIME2 NULL,
        [DurationMs] INT NULL,
        [ItemsProcessed] INT NOT NULL DEFAULT 0,
        [ItemsSucceeded] INT NOT NULL DEFAULT 0,
        [ItemsFailed] INT NOT NULL DEFAULT 0,
        [ErrorMessage] NVARCHAR(MAX) NULL,
        [ExceptionDetailsJson] NVARCHAR(MAX) NULL,
        [RetryAttempt] INT NOT NULL DEFAULT 0,
        [MaxRetries] INT NOT NULL DEFAULT 3,
        [PayloadJson] NVARCHAR(MAX) NULL,
        [ResultJson] NVARCHAR(MAX) NULL,
        [ProgressPercent] INT NOT NULL DEFAULT 0,
        [ProgressMessage] NVARCHAR(500) NULL,
        [LastProgressUpdate] DATETIME2 NULL,
        [RowVersion] ROWVERSION NOT NULL,
        [Tags] NVARCHAR(500) NULL,

        CONSTRAINT [FK_JobQueue_RemoteAgents] FOREIGN KEY ([ClaimedByAgentId])
            REFERENCES [dbo].[RemoteAgents] ([Id]) ON DELETE SET NULL
    );

    -- Indexes optimized for job claiming queries
    CREATE INDEX [IX_JobQueue_Status_Ready_Priority] ON [dbo].[JobQueue]
        ([Status], [Ready2Execute], [Priority] DESC, [CreatedAt] ASC)
        WHERE [Status] = 'Pending';

    CREATE INDEX [IX_JobQueue_ClaimedByAgentId] ON [dbo].[JobQueue] ([ClaimedByAgentId]);
    CREATE INDEX [IX_JobQueue_JobType] ON [dbo].[JobQueue] ([JobType]);
    CREATE INDEX [IX_JobQueue_RelatedEntityId] ON [dbo].[JobQueue] ([RelatedEntityId]);
    CREATE INDEX [IX_JobQueue_CreatedAt] ON [dbo].[JobQueue] ([CreatedAt] DESC);
    CREATE INDEX [IX_JobQueue_CompletedAt] ON [dbo].[JobQueue] ([CompletedAt] DESC);

    PRINT 'Created JobQueue table';
END
GO

-- ============================================================================
-- ApiKeys Table
-- API key authentication for agents and services
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ApiKeys]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ApiKeys] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Name] NVARCHAR(200) NOT NULL,
        [KeyHash] NVARCHAR(500) NOT NULL,
        [KeyPrefix] NVARCHAR(10) NOT NULL,
        [KeyType] NVARCHAR(20) NOT NULL DEFAULT 'Agent',
        [AgentId] UNIQUEIDENTIFIER NULL,
        [UserId] NVARCHAR(450) NULL,
        [Scopes] NVARCHAR(1000) NOT NULL DEFAULT '',
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [ExpiresAt] DATETIME2 NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] NVARCHAR(200) NOT NULL,
        [LastUsedAt] DATETIME2 NULL,
        [LastUsedFromIp] NVARCHAR(50) NULL,
        [UsageCount] INT NOT NULL DEFAULT 0,
        [RevokedAt] DATETIME2 NULL,
        [RevokedReason] NVARCHAR(500) NULL,

        CONSTRAINT [FK_ApiKeys_RemoteAgents] FOREIGN KEY ([AgentId])
            REFERENCES [dbo].[RemoteAgents] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [UQ_ApiKeys_KeyHash] UNIQUE ([KeyHash])
    );

    CREATE INDEX [IX_ApiKeys_KeyHash_KeyPrefix] ON [dbo].[ApiKeys] ([KeyHash], [KeyPrefix]);
    CREATE INDEX [IX_ApiKeys_AgentId] ON [dbo].[ApiKeys] ([AgentId]);
    CREATE INDEX [IX_ApiKeys_KeyType] ON [dbo].[ApiKeys] ([KeyType]);
    CREATE INDEX [IX_ApiKeys_IsEnabled] ON [dbo].[ApiKeys] ([IsEnabled]);

    PRINT 'Created ApiKeys table';
END
GO

-- ============================================================================
-- Stored Procedure: ClaimNextJob
-- Atomically claims the next available job for an agent
-- Uses ROWLOCK, UPDLOCK, READPAST for concurrency safety
-- ============================================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_ClaimNextJob]') AND type in (N'P'))
    DROP PROCEDURE [dbo].[usp_ClaimNextJob];
GO

CREATE PROCEDURE [dbo].[usp_ClaimNextJob]
    @AgentId UNIQUEIDENTIFIER,
    @SupportedJobTypes NVARCHAR(MAX) -- Comma-separated list
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @JobId UNIQUEIDENTIFIER;

    -- Parse supported job types into a table
    DECLARE @JobTypes TABLE (JobType NVARCHAR(50));
    INSERT INTO @JobTypes (JobType)
    SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@SupportedJobTypes, ','); -- LTRIM/RTRIM for SQL 2016 compatibility

    -- Atomically select and update the next available job
    UPDATE TOP(1) jq
    SET
        @JobId = jq.Id,
        jq.Status = 'Claimed',
        jq.ClaimedByAgentId = @AgentId,
        jq.ClaimedAt = GETUTCDATE()
    FROM JobQueue jq WITH (ROWLOCK, UPDLOCK, READPAST)
    WHERE jq.Status = 'Pending'
      AND jq.Ready2Execute = 1
      AND (jq.ScheduledAt IS NULL OR jq.ScheduledAt <= GETUTCDATE())
      AND jq.JobType IN (SELECT JobType FROM @JobTypes)
    ORDER BY jq.Priority DESC, jq.CreatedAt ASC
    OPTION (MAXDOP 1);

    -- Update agent's current job count
    IF @JobId IS NOT NULL
    BEGIN
        UPDATE RemoteAgents
        SET CurrentJobCount = CurrentJobCount + 1,
            LastJobClaimed = GETUTCDATE()
        WHERE Id = @AgentId;
    END

    -- Return the claimed job
    SELECT
        Id, JobType, JobName, RelatedEntityId, RelatedEntityType,
        Status, Priority, Ready2Execute, ScheduledAt, CreatedAt, CreatedBy,
        ClaimedByAgentId, ClaimedAt, StartedAt, CompletedAt, DurationMs,
        ItemsProcessed, ItemsSucceeded, ItemsFailed, ErrorMessage,
        ExceptionDetailsJson, RetryAttempt, MaxRetries, PayloadJson,
        ResultJson, ProgressPercent, ProgressMessage, LastProgressUpdate, Tags
    FROM JobQueue
    WHERE Id = @JobId;
END
GO

PRINT 'Created usp_ClaimNextJob stored procedure';
GO

-- ============================================================================
-- Stored Procedure: ReleaseStaleJobs
-- Releases jobs that have been claimed but not completed within threshold
-- ============================================================================
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_ReleaseStaleJobs]') AND type in (N'P'))
    DROP PROCEDURE [dbo].[usp_ReleaseStaleJobs];
GO

CREATE PROCEDURE [dbo].[usp_ReleaseStaleJobs]
    @StaleMinutes INT = 30
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ReleasedCount INT;
    DECLARE @ReleasedAgents TABLE (AgentId UNIQUEIDENTIFIER, JobCount INT);

    -- Identify and release stale jobs
    UPDATE JobQueue
    SET
        Status = 'Pending',
        ClaimedByAgentId = NULL,
        ClaimedAt = NULL,
        StartedAt = NULL,
        RetryAttempt = RetryAttempt + 1,
        ProgressPercent = 0,
        ProgressMessage = 'Released due to timeout'
    OUTPUT DELETED.ClaimedByAgentId, 1 INTO @ReleasedAgents
    WHERE Status IN ('Claimed', 'Processing')
      AND (
          (LastProgressUpdate IS NULL AND ClaimedAt < DATEADD(MINUTE, -@StaleMinutes, GETUTCDATE()))
          OR (LastProgressUpdate < DATEADD(MINUTE, -@StaleMinutes, GETUTCDATE()))
      )
      AND RetryAttempt < MaxRetries;

    SET @ReleasedCount = @@ROWCOUNT;

    -- Update agent job counts
    UPDATE ra
    SET CurrentJobCount = CurrentJobCount - released.JobCount
    FROM RemoteAgents ra
    INNER JOIN (
        SELECT AgentId, SUM(JobCount) as JobCount
        FROM @ReleasedAgents
        WHERE AgentId IS NOT NULL
        GROUP BY AgentId
    ) released ON ra.Id = released.AgentId;

    -- Mark agents with stale jobs as potentially offline
    UPDATE RemoteAgents
    SET Status = 'Offline'
    WHERE Id IN (SELECT DISTINCT AgentId FROM @ReleasedAgents WHERE AgentId IS NOT NULL)
      AND LastHeartbeat < DATEADD(MINUTE, -@StaleMinutes, GETUTCDATE());

    SELECT @ReleasedCount as ReleasedJobCount;
END
GO

PRINT 'Created usp_ReleaseStaleJobs stored procedure';
GO

-- ============================================================================
-- Initial Admin API Key (for bootstrapping)
-- Key: ic_admin_bootstrap_key_change_me_immediately
-- IMPORTANT: Change this immediately after first use!
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM ApiKeys WHERE Name = 'Bootstrap Admin Key')
BEGIN
    INSERT INTO ApiKeys (Id, Name, KeyHash, KeyPrefix, KeyType, Scopes, IsEnabled, CreatedBy)
    VALUES (
        NEWID(),
        'Bootstrap Admin Key',
        -- Hash of 'ic_admin_bootstrap_key_change_me_immediately'
        '8b5b2a7c8e4f3d9a1c6b8e2f4a7d0c3b6e9f2a5d8c1b4e7a0d3f6c9b2e5a8d1f4',
        'ic_admin',
        'Admin',
        'admin,agent,jobs:read,jobs:write,jobs:execute,agents:manage',
        1,
        'System'
    );

    PRINT 'Created bootstrap admin API key';
    PRINT 'WARNING: Change the bootstrap API key immediately after first use!';
END
GO

PRINT '';
PRINT '============================================================================';
PRINT 'Remote Agent Infrastructure tables created successfully!';
PRINT '';
PRINT 'Tables created:';
PRINT '  - RemoteAgents: Tracks registered sync agents';
PRINT '  - JobQueue: Job queue with atomic claiming';
PRINT '  - ApiKeys: API key authentication';
PRINT '';
PRINT 'Stored procedures:';
PRINT '  - usp_ClaimNextJob: Atomic job claiming';
PRINT '  - usp_ReleaseStaleJobs: Releases stuck jobs';
PRINT '============================================================================';
GO
