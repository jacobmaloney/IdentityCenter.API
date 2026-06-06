-- ============================================================
-- V041: Entropy Engine and Drift Tracking
-- Organizational disorder measurement + identity change velocity
-- ============================================================

-- ============================================================
-- STEP 1: Create EntropySnapshots table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EntropySnapshots')
BEGIN
    CREATE TABLE [EntropySnapshots] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [SnapshotType] NVARCHAR(50) NOT NULL,
        [Score] DECIMAL(5,2) NOT NULL,
        [Components] NVARCHAR(MAX) NULL,
        [CalculatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_EntropySnapshots] PRIMARY KEY ([Id])
    );
    PRINT 'Created EntropySnapshots table.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('EntropySnapshots') AND name = 'IX_EntropySnapshots_Type_CalculatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_EntropySnapshots_Type_CalculatedAt]
        ON [EntropySnapshots] ([SnapshotType], [CalculatedAt] DESC);
    PRINT 'Created IX_EntropySnapshots_Type_CalculatedAt index.';
END

-- ============================================================
-- STEP 2: Create IdentityDriftRecords table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'IdentityDriftRecords')
BEGIN
    CREATE TABLE [IdentityDriftRecords] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [IdentityId] UNIQUEIDENTIFIER NOT NULL,
        [DriftType] NVARCHAR(100) NOT NULL,
        [DriftMagnitude] DECIMAL(5,2) NOT NULL DEFAULT 0,
        [PreviousValue] NVARCHAR(500) NULL,
        [CurrentValue] NVARCHAR(500) NULL,
        [DetectedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [IsAcknowledged] BIT NOT NULL DEFAULT 0,
        [AcknowledgedBy] NVARCHAR(256) NULL,
        [AcknowledgedAt] DATETIME2 NULL,
        CONSTRAINT [PK_IdentityDriftRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IdentityDriftRecords_Identities] FOREIGN KEY ([IdentityId])
            REFERENCES [Identities] ([Id]) ON DELETE CASCADE
    );
    PRINT 'Created IdentityDriftRecords table.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('IdentityDriftRecords') AND name = 'IX_IdentityDriftRecords_IdentityId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_IdentityDriftRecords_IdentityId]
        ON [IdentityDriftRecords] ([IdentityId], [DetectedAt] DESC);
    PRINT 'Created IX_IdentityDriftRecords_IdentityId index.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('IdentityDriftRecords') AND name = 'IX_IdentityDriftRecords_DetectedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_IdentityDriftRecords_DetectedAt]
        ON [IdentityDriftRecords] ([DetectedAt] DESC)
        WHERE [IsAcknowledged] = 0;
    PRINT 'Created IX_IdentityDriftRecords_DetectedAt index.';
END

-- ============================================================
-- STEP 3: Add drift tracking columns to Identities
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'DriftScore')
BEGIN
    ALTER TABLE [Identities] ADD [DriftScore] DECIMAL(5,2) NULL;
    PRINT 'Added DriftScore column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'LastDriftCalculatedAt')
BEGIN
    ALTER TABLE [Identities] ADD [LastDriftCalculatedAt] DATETIME2 NULL;
    PRINT 'Added LastDriftCalculatedAt column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'GroupCountAtLastSync')
BEGIN
    ALTER TABLE [Identities] ADD [GroupCountAtLastSync] INT NULL;
    PRINT 'Added GroupCountAtLastSync column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'RiskScoreAtLastSync')
BEGIN
    ALTER TABLE [Identities] ADD [RiskScoreAtLastSync] DECIMAL(5,2) NULL;
    PRINT 'Added RiskScoreAtLastSync column to Identities.';
END

PRINT 'V041: Entropy and Drift migration complete.';
