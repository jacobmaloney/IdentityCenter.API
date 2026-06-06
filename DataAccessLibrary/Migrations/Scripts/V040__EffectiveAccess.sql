-- ============================================================
-- V040: Effective Access Engine
-- Materialized recursive group memberships and blast radius
-- ============================================================

-- ============================================================
-- STEP 1: Create EffectiveAccessEntries table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EffectiveAccessEntries')
BEGIN
    CREATE TABLE [EffectiveAccessEntries] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [ObjectId] UNIQUEIDENTIFIER NOT NULL,
        [GroupId] UNIQUEIDENTIFIER NOT NULL,
        [AccessPath] NVARCHAR(MAX) NULL,
        [Depth] INT NOT NULL DEFAULT 0,
        [IsDirect] BIT NOT NULL DEFAULT 0,
        [SourceMembershipId] UNIQUEIDENTIFIER NULL,
        [MaterializedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_EffectiveAccessEntries] PRIMARY KEY ([Id])
    );
    PRINT 'Created EffectiveAccessEntries table.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('EffectiveAccessEntries') AND name = 'IX_EffectiveAccessEntries_ObjectId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_EffectiveAccessEntries_ObjectId]
        ON [EffectiveAccessEntries] ([ObjectId])
        INCLUDE ([GroupId], [Depth], [IsDirect]);
    PRINT 'Created IX_EffectiveAccessEntries_ObjectId index.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('EffectiveAccessEntries') AND name = 'IX_EffectiveAccessEntries_GroupId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_EffectiveAccessEntries_GroupId]
        ON [EffectiveAccessEntries] ([GroupId])
        INCLUDE ([ObjectId], [Depth], [IsDirect]);
    PRINT 'Created IX_EffectiveAccessEntries_GroupId index.';
END

-- ============================================================
-- STEP 2: Create GroupBlastRadius table
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'GroupBlastRadius')
BEGIN
    CREATE TABLE [GroupBlastRadius] (
        [GroupId] UNIQUEIDENTIFIER NOT NULL,
        [DirectMemberCount] INT NOT NULL DEFAULT 0,
        [EffectiveMemberCount] INT NOT NULL DEFAULT 0,
        [MaxDepth] INT NOT NULL DEFAULT 0,
        [NestedGroupCount] INT NOT NULL DEFAULT 0,
        [BlastRadiusScore] DECIMAL(5,2) NOT NULL DEFAULT 0,
        [CalculatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_GroupBlastRadius] PRIMARY KEY ([GroupId])
    );
    PRINT 'Created GroupBlastRadius table.';
END

-- ============================================================
-- STEP 3: Add effective access columns to Identities
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EffectiveGroupCount')
BEGIN
    ALTER TABLE [Identities] ADD [EffectiveGroupCount] INT NULL;
    PRINT 'Added EffectiveGroupCount column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EffectiveAdminGroupCount')
BEGIN
    ALTER TABLE [Identities] ADD [EffectiveAdminGroupCount] INT NULL;
    PRINT 'Added EffectiveAdminGroupCount column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'MaxAccessDepth')
BEGIN
    ALTER TABLE [Identities] ADD [MaxAccessDepth] INT NULL;
    PRINT 'Added MaxAccessDepth column to Identities.';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EffectiveAccessLastCalculatedAt')
BEGIN
    ALTER TABLE [Identities] ADD [EffectiveAccessLastCalculatedAt] DATETIME2 NULL;
    PRINT 'Added EffectiveAccessLastCalculatedAt column to Identities.';
END

PRINT 'V040: Effective Access migration complete.';
