-- ============================================================
-- V043: Governance Policies and Quarantine
-- Configurable auto-governance with safety-first defaults
-- ============================================================

-- ============================================================
-- STEP 1: Create GovernancePolicies table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'GovernancePolicies')
BEGIN
    CREATE TABLE [GovernancePolicies] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [Name] NVARCHAR(200) NOT NULL,
        [Description] NVARCHAR(1000) NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 0,
        [Priority] INT NOT NULL DEFAULT 100,
        [TriggerConditions] NVARCHAR(MAX) NULL,
        [ActionType] NVARCHAR(50) NOT NULL,
        [ActionConfig] NVARCHAR(MAX) NULL,
        [RequiresApproval] BIT NOT NULL DEFAULT 1,
        [ConfidenceThreshold] DECIMAL(5,2) NOT NULL DEFAULT 80.00,
        [MaxActionsPerRun] INT NOT NULL DEFAULT 50,
        [CooldownHours] INT NOT NULL DEFAULT 24,
        [ExcludeAdminAccounts] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [ModifiedAt] DATETIME2 NULL,
        [CreatedBy] NVARCHAR(256) NULL,
        CONSTRAINT [PK_GovernancePolicies] PRIMARY KEY ([Id])
    );
    PRINT 'Created GovernancePolicies table.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('GovernancePolicies') AND name = 'IX_GovernancePolicies_IsEnabled')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_GovernancePolicies_IsEnabled]
        ON [GovernancePolicies] ([IsEnabled], [Priority]);
    PRINT 'Created IX_GovernancePolicies_IsEnabled index.';
END

-- ============================================================
-- STEP 2: Create QuarantineRecords table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'QuarantineRecords')
BEGIN
    CREATE TABLE [QuarantineRecords] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [IdentityId] UNIQUEIDENTIFIER NULL,
        [ObjectId] UNIQUEIDENTIFIER NULL,
        [GovernancePolicyId] UNIQUEIDENTIFIER NULL,
        [QuarantineType] NVARCHAR(20) NOT NULL DEFAULT 'Soft',
        [PreviousOU] NVARCHAR(2000) NULL,
        [QuarantineOU] NVARCHAR(2000) NULL,
        [PreviousEnabled] BIT NULL,
        [RemovedGroupIds] NVARCHAR(MAX) NULL,
        [Reason] NVARCHAR(1000) NULL,
        [QuarantinedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [QuarantinedBy] NVARCHAR(256) NULL,
        [ExpiresAt] DATETIME2 NULL,
        [ReleasedAt] DATETIME2 NULL,
        [ReleasedBy] NVARCHAR(256) NULL,
        [ReleaseReason] NVARCHAR(1000) NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        CONSTRAINT [PK_QuarantineRecords] PRIMARY KEY ([Id])
    );
    PRINT 'Created QuarantineRecords table.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('QuarantineRecords') AND name = 'IX_QuarantineRecords_IdentityId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_QuarantineRecords_IdentityId]
        ON [QuarantineRecords] ([IdentityId])
        WHERE [IsActive] = 1;
    PRINT 'Created IX_QuarantineRecords_IdentityId index.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('QuarantineRecords') AND name = 'IX_QuarantineRecords_ExpiresAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_QuarantineRecords_ExpiresAt]
        ON [QuarantineRecords] ([ExpiresAt])
        WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
    PRINT 'Created IX_QuarantineRecords_ExpiresAt index.';
END

PRINT 'V043: Governance Policies migration complete.';
