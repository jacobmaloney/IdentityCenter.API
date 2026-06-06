-- ============================================================
-- V042: ML Model Metadata
-- Tracks trained ML.NET model versions and performance metrics
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MLModelMetadata')
BEGIN
    CREATE TABLE [MLModelMetadata] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [ModelName] NVARCHAR(200) NOT NULL,
        [ModelVersion] INT NOT NULL DEFAULT 1,
        [TrainedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [TrainingSampleCount] INT NOT NULL DEFAULT 0,
        [Accuracy] FLOAT NULL,
        [RSquared] FLOAT NULL,
        [RMSE] FLOAT NULL,
        [ModelFilePath] NVARCHAR(1000) NULL,
        [IsActive] BIT NOT NULL DEFAULT 0,
        [TrainingParameters] NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_MLModelMetadata] PRIMARY KEY ([Id])
    );
    PRINT 'Created MLModelMetadata table.';
END

-- Only one active model per model name
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('MLModelMetadata') AND name = 'IX_MLModelMetadata_ModelName_Active')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_MLModelMetadata_ModelName_Active]
        ON [MLModelMetadata] ([ModelName])
        WHERE [IsActive] = 1;
    PRINT 'Created IX_MLModelMetadata_ModelName_Active unique filtered index.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('MLModelMetadata') AND name = 'IX_MLModelMetadata_ModelName_TrainedAt')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MLModelMetadata_ModelName_TrainedAt]
        ON [MLModelMetadata] ([ModelName], [TrainedAt] DESC);
    PRINT 'Created IX_MLModelMetadata_ModelName_TrainedAt index.';
END

PRINT 'V042: ML Model Metadata migration complete.';
