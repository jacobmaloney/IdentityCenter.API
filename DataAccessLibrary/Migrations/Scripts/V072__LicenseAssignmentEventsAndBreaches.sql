-- V072: License lifecycle events + threshold breach history
-- LicenseAssignmentEvents: audit trail of state transitions per assignment
-- LicenseThresholdBreaches: history of when pools breached capacity thresholds

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseAssignmentEvents')
BEGIN
    CREATE TABLE [LicenseAssignmentEvents] (
        [Id]             UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [AssignmentId]   UNIQUEIDENTIFIER NOT NULL,
        [LicensePoolId]  UNIQUEIDENTIFIER NOT NULL,
        [ObjectId]       UNIQUEIDENTIFIER NOT NULL,
        [EventType]      NVARCHAR(50)     NOT NULL, -- Assigned, FirstUsed, Dormant, Reactivated, Revoked, Removed
        [Actor]          NVARCHAR(256)    NULL,     -- who triggered (user, "System", "Sync", "Policy:<name>")
        [Reason]         NVARCHAR(1000)   NULL,
        [Metadata]       NVARCHAR(MAX)    NULL,     -- JSON payload
        [CreatedAt]      DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_LicenseAssignmentEvents] PRIMARY KEY ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_LicenseAssignmentEvents_Assignment]
        ON [LicenseAssignmentEvents] ([AssignmentId], [CreatedAt] DESC);

    CREATE NONCLUSTERED INDEX [IX_LicenseAssignmentEvents_Pool]
        ON [LicenseAssignmentEvents] ([LicensePoolId], [EventType], [CreatedAt] DESC);

    CREATE NONCLUSTERED INDEX [IX_LicenseAssignmentEvents_Object]
        ON [LicenseAssignmentEvents] ([ObjectId], [CreatedAt] DESC);

    PRINT 'V072: Created LicenseAssignmentEvents table';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseThresholdBreaches')
BEGIN
    CREATE TABLE [LicenseThresholdBreaches] (
        [Id]                 UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [LicensePoolId]      UNIQUEIDENTIFIER NOT NULL,
        [ThresholdType]      NVARCHAR(50)     NOT NULL, -- MinBufferPercent, MaxUtilizationPercent, DaysUntilExhaustion
        [ThresholdValue]     DECIMAL(10,2)    NOT NULL, -- the configured threshold
        [ActualValue]        DECIMAL(10,2)    NOT NULL, -- the value that breached it
        [Severity]           NVARCHAR(20)     NOT NULL DEFAULT 'Warning', -- Warning, Critical
        [BreachedAt]         DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [Resolved]           BIT              NOT NULL DEFAULT 0,
        [ResolvedAt]         DATETIME2        NULL,
        [ResolvedReason]     NVARCHAR(500)    NULL,     -- "Capacity restored", "Reviews completed", "Threshold adjusted"
        [NotificationSent]   BIT              NOT NULL DEFAULT 0,
        [CampaignId]         UNIQUEIDENTIFIER NULL,     -- FK to auto-created access review campaign
        [ViolationId]        UNIQUEIDENTIFIER NULL,     -- FK to CompliancePolicyViolation if triggered
        CONSTRAINT [PK_LicenseThresholdBreaches] PRIMARY KEY ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_LicenseThresholdBreaches_Pool]
        ON [LicenseThresholdBreaches] ([LicensePoolId], [Resolved], [BreachedAt] DESC);

    CREATE NONCLUSTERED INDEX [IX_LicenseThresholdBreaches_Active]
        ON [LicenseThresholdBreaches] ([Resolved], [Severity], [BreachedAt] DESC)
        WHERE [Resolved] = 0;

    PRINT 'V072: Created LicenseThresholdBreaches table';
END
GO
