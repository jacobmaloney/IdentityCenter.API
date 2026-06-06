-- V057: Sign-In Logs
-- Stores sign-in activity from Microsoft Graph API auditLogs/signIns endpoint.
-- Feeds DaysSinceLastLogin calculations, license waste detection, and security dashboards.
-- All tables guarded with IF NOT EXISTS so the migration is safe to replay.

-- ─────────────────────────────────────────────────────────────────────────────
-- SignInLogs: Individual sign-in events from Graph API
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SignInLogs')
BEGIN
    CREATE TABLE [SignInLogs] (
        [Id]                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [ObjectId]                UNIQUEIDENTIFIER NOT NULL,
        [SourceConnectionId]      UNIQUEIDENTIFIER NOT NULL,
        [SignInId]                NVARCHAR(200)    NULL,
        [SignInDateTime]          DATETIME2        NOT NULL,
        [AppDisplayName]          NVARCHAR(500)    NULL,
        [AppId]                   NVARCHAR(200)    NULL,
        [ClientAppUsed]           NVARCHAR(200)    NULL,
        [DeviceDetail]            NVARCHAR(500)    NULL,
        [IpAddress]               NVARCHAR(100)    NULL,
        [Location]                NVARCHAR(500)    NULL,
        [Status]                  NVARCHAR(50)     NULL,
        [ErrorCode]               INT              NULL,
        [RiskLevel]               NVARCHAR(50)     NULL,
        [RiskState]               NVARCHAR(50)     NULL,
        [ConditionalAccessStatus] NVARCHAR(50)     NULL,
        [IsInteractive]           BIT              NOT NULL DEFAULT 1,
        [ResourceDisplayName]     NVARCHAR(500)    NULL,
        [ResourceId]              NVARCHAR(200)    NULL,
        [CreatedAt]               DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_SignInLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SignInLogs_Object] FOREIGN KEY ([ObjectId])
            REFERENCES [Objects] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SignInLogs_Connection] FOREIGN KEY ([SourceConnectionId])
            REFERENCES [DirectoryConnections] ([Id]) ON DELETE NO ACTION
    );

    -- User sign-in history (detail pages, DaysSinceLastLogin)
    CREATE NONCLUSTERED INDEX [IX_SignInLogs_Object]
        ON [SignInLogs] ([ObjectId], [SignInDateTime] DESC)
        INCLUDE ([AppDisplayName], [Status], [IsInteractive]);

    -- Connection-scoped sign-in feed
    CREATE NONCLUSTERED INDEX [IX_SignInLogs_Connection]
        ON [SignInLogs] ([SourceConnectionId], [SignInDateTime] DESC)
        INCLUDE ([ObjectId], [AppDisplayName], [Status]);

    -- App-level sign-in analysis
    CREATE NONCLUSTERED INDEX [IX_SignInLogs_App]
        ON [SignInLogs] ([AppDisplayName], [SignInDateTime] DESC)
        INCLUDE ([ObjectId], [Status]);

    -- Dedup on Graph API sign-in event ID
    CREATE UNIQUE NONCLUSTERED INDEX [UX_SignInLogs_SignInId]
        ON [SignInLogs] ([SignInId])
        WHERE [SignInId] IS NOT NULL;

    -- Dashboard time range queries
    CREATE NONCLUSTERED INDEX [IX_SignInLogs_DateTime]
        ON [SignInLogs] ([SignInDateTime] DESC)
        INCLUDE ([ObjectId], [AppDisplayName], [Status]);

    PRINT 'Created SignInLogs table';
END;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- SignInSummary: Daily rollup per user per app for dashboards
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SignInSummary')
BEGIN
    CREATE TABLE [SignInSummary] (
        [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [ObjectId]            UNIQUEIDENTIFIER NOT NULL,
        [SourceConnectionId]  UNIQUEIDENTIFIER NOT NULL,
        [AppDisplayName]      NVARCHAR(500)    NOT NULL,
        [SummaryDate]         DATE             NOT NULL,
        [SuccessCount]        INT              NOT NULL DEFAULT 0,
        [FailureCount]        INT              NOT NULL DEFAULT 0,
        [InteractiveCount]    INT              NOT NULL DEFAULT 0,
        [NonInteractiveCount] INT              NOT NULL DEFAULT 0,
        [UniqueLocations]     INT              NOT NULL DEFAULT 0,
        CONSTRAINT [PK_SignInSummary] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SignInSummary_Object] FOREIGN KEY ([ObjectId])
            REFERENCES [Objects] ([Id]) ON DELETE CASCADE
    );

    -- Upsert & lookup: one row per user + app + date
    CREATE UNIQUE NONCLUSTERED INDEX [UX_SignInSummary_UserAppDate]
        ON [SignInSummary] ([ObjectId], [AppDisplayName], [SummaryDate]);

    -- Connection-scoped dashboard queries
    CREATE NONCLUSTERED INDEX [IX_SignInSummary_Connection]
        ON [SignInSummary] ([SourceConnectionId], [SummaryDate] DESC)
        INCLUDE ([ObjectId], [AppDisplayName], [SuccessCount]);

    PRINT 'Created SignInSummary table';
END;
GO

PRINT 'V057: Sign-In Logs migration complete';
GO
