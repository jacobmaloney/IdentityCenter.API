-- V058: M365 Usage Reports
-- Stores per-user M365 activity data from Graph API reports/getOffice365ActiveUserDetail.
-- Shows which M365 apps each user actually uses — critical for license optimization.
-- All tables guarded with IF NOT EXISTS so the migration is safe to replay.

-- ─────────────────────────────────────────────────────────────────────────────
-- M365UsageReports: Per-user activity across M365 apps
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'M365UsageReports')
BEGIN
    CREATE TABLE [M365UsageReports] (
        [Id]                        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [ObjectId]                  UNIQUEIDENTIFIER NOT NULL,
        [SourceConnectionId]        UNIQUEIDENTIFIER NOT NULL,
        [ReportRefreshDate]         DATE             NOT NULL,
        [UserPrincipalName]         NVARCHAR(500)    NULL,
        [DisplayName]               NVARCHAR(500)    NULL,
        [HasExchangeLicense]        BIT              NOT NULL DEFAULT 0,
        [HasOneDriveLicense]        BIT              NOT NULL DEFAULT 0,
        [HasSharePointLicense]      BIT              NOT NULL DEFAULT 0,
        [HasTeamsLicense]           BIT              NOT NULL DEFAULT 0,
        [HasYammerLicense]          BIT              NOT NULL DEFAULT 0,
        [ExchangeLastActivityDate]  DATE             NULL,
        [OneDriveLastActivityDate]  DATE             NULL,
        [SharePointLastActivityDate] DATE            NULL,
        [TeamsLastActivityDate]     DATE             NULL,
        [YammerLastActivityDate]    DATE             NULL,
        [ExchangeMailSent]          INT              NULL,
        [ExchangeMailReceived]      INT              NULL,
        [OneDriveFilesViewed]       INT              NULL,
        [OneDriveFilesSynced]       INT              NULL,
        [SharePointFilesViewed]     INT              NULL,
        [SharePointFilesShared]     INT              NULL,
        [TeamsChatMessages]         INT              NULL,
        [TeamsCallCount]            INT              NULL,
        [TeamsMeetingCount]         INT              NULL,
        [AssignedProducts]          NVARCHAR(MAX)    NULL,
        [LastSyncedAt]              DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_M365UsageReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_M365Usage_Object] FOREIGN KEY ([ObjectId])
            REFERENCES [Objects] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_M365Usage_Connection] FOREIGN KEY ([SourceConnectionId])
            REFERENCES [DirectoryConnections] ([Id]) ON DELETE NO ACTION
    );

    -- Upsert support: one row per user per report date
    CREATE UNIQUE NONCLUSTERED INDEX [UX_M365Usage_ObjectDate]
        ON [M365UsageReports] ([ObjectId], [ReportRefreshDate]);

    -- Connection-scoped report listing
    CREATE NONCLUSTERED INDEX [IX_M365Usage_Connection]
        ON [M365UsageReports] ([SourceConnectionId], [ReportRefreshDate] DESC)
        INCLUDE ([ObjectId]);

    -- Teams activity analysis (license waste detection)
    CREATE NONCLUSTERED INDEX [IX_M365Usage_TeamsActivity]
        ON [M365UsageReports] ([TeamsLastActivityDate])
        INCLUDE ([ObjectId], [TeamsChatMessages], [TeamsCallCount], [TeamsMeetingCount]);

    PRINT 'Created M365UsageReports table';
END;
GO

PRINT 'V058: M365 Usage Reports migration complete';
GO
