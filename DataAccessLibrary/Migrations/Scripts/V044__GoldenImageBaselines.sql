-- ============================================================
-- V044: Golden Image Baselines
-- "Known good" snapshots and deviation detection
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'GoldenImageBaselines')
BEGIN
    CREATE TABLE [GoldenImageBaselines] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [EntityType] NVARCHAR(50) NOT NULL,
        [EntityId] UNIQUEIDENTIFIER NOT NULL,
        [BaselineData] NVARCHAR(MAX) NULL,
        [GroupMemberships] NVARCHAR(MAX) NULL,
        [IntegrityScoreAtBaseline] DECIMAL(5,2) NULL,
        [RiskScoreAtBaseline] DECIMAL(5,2) NULL,
        [CapturedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [CapturedBy] NVARCHAR(256) NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [Notes] NVARCHAR(1000) NULL,
        CONSTRAINT [PK_GoldenImageBaselines] PRIMARY KEY ([Id])
    );
    PRINT 'Created GoldenImageBaselines table.';
END

-- Only one active baseline per entity
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('GoldenImageBaselines') AND name = 'IX_GoldenImageBaselines_Entity_Active')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_GoldenImageBaselines_Entity_Active]
        ON [GoldenImageBaselines] ([EntityType], [EntityId])
        WHERE [IsActive] = 1;
    PRINT 'Created IX_GoldenImageBaselines_Entity_Active unique filtered index.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('GoldenImageBaselines') AND name = 'IX_GoldenImageBaselines_EntityId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_GoldenImageBaselines_EntityId]
        ON [GoldenImageBaselines] ([EntityId]);
    PRINT 'Created IX_GoldenImageBaselines_EntityId index.';
END

PRINT 'V044: Golden Image Baselines migration complete.';
