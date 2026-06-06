-- ============================================================
-- V039: Active Intelligence Foundation
-- Adds Identity Integrity Score, Governance State, and History
-- ============================================================

-- ============================================================
-- STEP 1: Add Integrity columns to Identities table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'IntegrityScore')
BEGIN
    ALTER TABLE [Identities] ADD [IntegrityScore] DECIMAL(5,2) NULL;
    PRINT 'Added IntegrityScore column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'IntegrityLevel')
BEGIN
    ALTER TABLE [Identities] ADD [IntegrityLevel] NVARCHAR(20) NULL;
    PRINT 'Added IntegrityLevel column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'IntegrityLastCalculatedAt')
BEGIN
    ALTER TABLE [Identities] ADD [IntegrityLastCalculatedAt] DATETIME2 NULL;
    PRINT 'Added IntegrityLastCalculatedAt column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'IntegrityFactors')
BEGIN
    ALTER TABLE [Identities] ADD [IntegrityFactors] NVARCHAR(MAX) NULL;
    PRINT 'Added IntegrityFactors column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'GovernanceState')
BEGIN
    ALTER TABLE [Identities] ADD [GovernanceState] NVARCHAR(30) NULL;
    PRINT 'Added GovernanceState column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'GovernanceStateChangedAt')
BEGIN
    ALTER TABLE [Identities] ADD [GovernanceStateChangedAt] DATETIME2 NULL;
    PRINT 'Added GovernanceStateChangedAt column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'GovernanceStateChangedBy')
BEGIN
    ALTER TABLE [Identities] ADD [GovernanceStateChangedBy] NVARCHAR(256) NULL;
    PRINT 'Added GovernanceStateChangedBy column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'GovernanceStateReason')
BEGIN
    ALTER TABLE [Identities] ADD [GovernanceStateReason] NVARCHAR(1000) NULL;
    PRINT 'Added GovernanceStateReason column to Identities.';
END

-- ============================================================
-- STEP 2: Create IdentityIntegrityHistory table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'IdentityIntegrityHistory')
BEGIN
    CREATE TABLE [IdentityIntegrityHistory] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [IdentityId] UNIQUEIDENTIFIER NOT NULL,
        [IntegrityScore] DECIMAL(5,2) NOT NULL,
        [IntegrityLevel] NVARCHAR(20) NOT NULL,
        [FactorBreakdown] NVARCHAR(MAX) NULL,
        [CalculatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_IdentityIntegrityHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IdentityIntegrityHistory_Identities] FOREIGN KEY ([IdentityId])
            REFERENCES [Identities] ([Id]) ON DELETE CASCADE
    );
    PRINT 'Created IdentityIntegrityHistory table.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('IdentityIntegrityHistory') AND name = 'IX_IdentityIntegrityHistory_IdentityId_CalculatedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_IdentityIntegrityHistory_IdentityId_CalculatedAt]
        ON [IdentityIntegrityHistory] ([IdentityId], [CalculatedAt] DESC);
    PRINT 'Created IX_IdentityIntegrityHistory_IdentityId_CalculatedAt index.';
END

-- ============================================================
-- STEP 3: Create GovernanceActions table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'GovernanceActions')
BEGIN
    CREATE TABLE [GovernanceActions] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [IdentityId] UNIQUEIDENTIFIER NULL,
        [ObjectId] UNIQUEIDENTIFIER NULL,
        [GroupId] UNIQUEIDENTIFIER NULL,
        [ActionType] NVARCHAR(100) NOT NULL,
        [TriggerSource] NVARCHAR(200) NULL,
        [PreviousState] NVARCHAR(MAX) NULL,
        [NewState] NVARCHAR(MAX) NULL,
        [Reason] NVARCHAR(1000) NULL,
        [ConfidenceScore] DECIMAL(5,2) NULL,
        [PerformedBy] NVARCHAR(256) NULL,
        [PerformedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [RevertedAt] DATETIME2 NULL,
        [RevertedBy] NVARCHAR(256) NULL,
        CONSTRAINT [PK_GovernanceActions] PRIMARY KEY ([Id])
    );
    PRINT 'Created GovernanceActions table.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('GovernanceActions') AND name = 'IX_GovernanceActions_IdentityId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_GovernanceActions_IdentityId]
        ON [GovernanceActions] ([IdentityId])
        WHERE [IdentityId] IS NOT NULL;
    PRINT 'Created IX_GovernanceActions_IdentityId index.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('GovernanceActions') AND name = 'IX_GovernanceActions_PerformedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_GovernanceActions_PerformedAt]
        ON [GovernanceActions] ([PerformedAt] DESC);
    PRINT 'Created IX_GovernanceActions_PerformedAt index.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('GovernanceActions') AND name = 'IX_GovernanceActions_ActionType')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_GovernanceActions_ActionType]
        ON [GovernanceActions] ([ActionType]);
    PRINT 'Created IX_GovernanceActions_ActionType index.';
END

PRINT 'V039: Active Intelligence Foundation migration complete.';
