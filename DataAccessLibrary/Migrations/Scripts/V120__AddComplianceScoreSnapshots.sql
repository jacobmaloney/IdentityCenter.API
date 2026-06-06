-- V120: ComplianceScoreSnapshots
-- One row per day capturing aggregate governance posture so the GovernanceHome
-- dashboard's compliance-score sparkline can reflect real history instead of the
-- proxy-from-violation-inflow approximation it currently uses. Written daily by
-- ComplianceScoreSnapshotJob (Quartz, ~02:30 UTC), 15 minutes after the license
-- snapshot job so framework scores have settled if any policy ran overnight.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ComplianceScoreSnapshots')
BEGIN
    CREATE TABLE [ComplianceScoreSnapshots] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [DF_ComplianceScoreSnapshots_Id] DEFAULT NEWID(),
        [SnapshotDate] date NOT NULL,
        [OverallScore] decimal(5,2) NOT NULL,
        [OpenViolations] int NOT NULL,
        [CriticalViolations] int NOT NULL,
        [HighViolations] int NOT NULL,
        [PendingApprovals] int NOT NULL,
        [ActivePolicies] int NOT NULL,
        [ActiveFrameworks] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ComplianceScoreSnapshots_CreatedAt] DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_ComplianceScoreSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_ComplianceScoreSnapshots_SnapshotDate] UNIQUE ([SnapshotDate])
    );

    CREATE INDEX [IX_ComplianceScoreSnapshots_SnapshotDate]
        ON [ComplianceScoreSnapshots] ([SnapshotDate] DESC);
END;
GO
