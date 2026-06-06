-- Migration: SyncStepTags many-to-many table for auto-tagging during sync
-- Replaces single TagId column with junction table for multiple tags per step

-- Step 1: Drop old single TagId if it exists
IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_SyncSteps_Tags_TagId')
BEGIN
    ALTER TABLE SyncSteps DROP CONSTRAINT FK_SyncSteps_Tags_TagId;
    PRINT 'Dropped FK_SyncSteps_Tags_TagId constraint';
END
GO

IF EXISTS (SELECT * FROM sys.columns WHERE Name = 'TagId' AND Object_ID = Object_ID('SyncSteps'))
BEGIN
    ALTER TABLE SyncSteps DROP COLUMN TagId;
    PRINT 'Dropped TagId column from SyncSteps table';
END
GO

-- Step 2: Create the SyncStepTags junction table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SyncStepTags')
BEGIN
    CREATE TABLE SyncStepTags (
        Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
        SyncStepId uniqueidentifier NOT NULL,
        TagId uniqueidentifier NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_SyncStepTags_SyncSteps FOREIGN KEY (SyncStepId) REFERENCES SyncSteps(Id) ON DELETE CASCADE,
        CONSTRAINT FK_SyncStepTags_Tags FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_SyncStepTags_StepTag UNIQUE (SyncStepId, TagId)
    );
    PRINT 'Created SyncStepTags table';
END
GO

-- Step 3: Create indexes for performance
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SyncStepTags_SyncStepId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncStepTags_SyncStepId ON SyncStepTags(SyncStepId);
    PRINT 'Created IX_SyncStepTags_SyncStepId index';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SyncStepTags_TagId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncStepTags_TagId ON SyncStepTags(TagId);
    PRINT 'Created IX_SyncStepTags_TagId index';
END
GO

PRINT 'SyncStepTags migration complete - multiple tags per step now supported';
GO
