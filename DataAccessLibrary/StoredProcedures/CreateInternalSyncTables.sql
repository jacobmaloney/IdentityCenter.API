-- Create InternalSyncSteps table and related tables
-- Run this if the migration was recorded but tables weren't created

-- First, remove the migration record so tables can be created fresh
DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260101011357_AddInternalSyncSteps';
GO

-- Create InternalSyncRuns table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[InternalSyncRuns]') AND type in (N'U'))
BEGIN
    CREATE TABLE [InternalSyncRuns] (
        [Id] uniqueidentifier NOT NULL,
        [OperationType] nvarchar(50) NOT NULL,
        [MatchStrategy] nvarchar(50) NULL,
        [StartedAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        [Status] nvarchar(20) NOT NULL,
        [TotalProcessed] int NOT NULL,
        [Matched] int NOT NULL,
        [Created] int NOT NULL,
        [Skipped] int NOT NULL,
        [Errors] int NOT NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [SyncProjectId] uniqueidentifier NULL,
        CONSTRAINT [PK_InternalSyncRuns] PRIMARY KEY ([Id])
    );
    PRINT 'Created InternalSyncRuns table';
END
ELSE
    PRINT 'InternalSyncRuns table already exists';
GO

-- Create InternalSyncSteps table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[InternalSyncSteps]') AND type in (N'U'))
BEGIN
    CREATE TABLE [InternalSyncSteps] (
        [Id] uniqueidentifier NOT NULL,
        [SyncProjectId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(max) NULL,
        [ExecutionOrder] int NOT NULL,
        [Direction] nvarchar(30) NOT NULL,
        [StepType] nvarchar(50) NOT NULL,
        [ObjectClassFilter] nvarchar(100) NULL,
        [IsEnabled] bit NOT NULL,
        [ContinueOnError] bit NOT NULL,
        [Configuration] nvarchar(max) NULL,
        [SourceConnectionId] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ModifiedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_InternalSyncSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InternalSyncSteps_SyncProjects_SyncProjectId] FOREIGN KEY ([SyncProjectId]) REFERENCES [SyncProjects] ([Id]) ON DELETE CASCADE
    );
    PRINT 'Created InternalSyncSteps table';
END
ELSE
    PRINT 'InternalSyncSteps table already exists';
GO

-- Create InternalSyncStepMappings table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[InternalSyncStepMappings]') AND type in (N'U'))
BEGIN
    CREATE TABLE [InternalSyncStepMappings] (
        [Id] uniqueidentifier NOT NULL,
        [InternalSyncStepId] uniqueidentifier NOT NULL,
        [SourceField] nvarchar(200) NOT NULL,
        [TargetField] nvarchar(200) NOT NULL,
        [OverwriteExisting] bit NOT NULL,
        [IsRequired] bit NOT NULL,
        [DefaultValue] nvarchar(500) NULL,
        [Transformation] nvarchar(max) NULL,
        [MappingOrder] int NOT NULL,
        [IsEnabled] bit NOT NULL,
        CONSTRAINT [PK_InternalSyncStepMappings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InternalSyncStepMappings_InternalSyncSteps_InternalSyncStepId] FOREIGN KEY ([InternalSyncStepId]) REFERENCES [InternalSyncSteps] ([Id]) ON DELETE CASCADE
    );
    PRINT 'Created InternalSyncStepMappings table';
END
ELSE
    PRINT 'InternalSyncStepMappings table already exists';
GO

-- Create InternalSyncStepRuns table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[InternalSyncStepRuns]') AND type in (N'U'))
BEGIN
    CREATE TABLE [InternalSyncStepRuns] (
        [Id] uniqueidentifier NOT NULL,
        [InternalSyncRunId] uniqueidentifier NOT NULL,
        [InternalSyncStepId] uniqueidentifier NOT NULL,
        [StepName] nvarchar(200) NOT NULL,
        [StepType] nvarchar(50) NOT NULL,
        [ExecutionOrder] int NOT NULL,
        [StartedAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        [Status] nvarchar(20) NOT NULL,
        [Processed] int NOT NULL,
        [Matched] int NOT NULL,
        [Created] int NOT NULL,
        [Updated] int NOT NULL,
        [Skipped] int NOT NULL,
        [Errors] int NOT NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [DurationSeconds] float NULL,
        CONSTRAINT [PK_InternalSyncStepRuns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InternalSyncStepRuns_InternalSyncRuns_InternalSyncRunId] FOREIGN KEY ([InternalSyncRunId]) REFERENCES [InternalSyncRuns] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InternalSyncStepRuns_InternalSyncSteps_InternalSyncStepId] FOREIGN KEY ([InternalSyncStepId]) REFERENCES [InternalSyncSteps] ([Id])
    );
    PRINT 'Created InternalSyncStepRuns table';
END
ELSE
    PRINT 'InternalSyncStepRuns table already exists';
GO

-- Create indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_InternalSyncStepMappings_Step')
BEGIN
    CREATE INDEX [IX_InternalSyncStepMappings_Step] ON [InternalSyncStepMappings] ([InternalSyncStepId]);
    PRINT 'Created IX_InternalSyncStepMappings_Step index';
END;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_InternalSyncStepRuns_InternalSyncStepId')
BEGIN
    CREATE INDEX [IX_InternalSyncStepRuns_InternalSyncStepId] ON [InternalSyncStepRuns] ([InternalSyncStepId]);
    PRINT 'Created IX_InternalSyncStepRuns_InternalSyncStepId index';
END;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_InternalSyncStepRuns_Run')
BEGIN
    CREATE INDEX [IX_InternalSyncStepRuns_Run] ON [InternalSyncStepRuns] ([InternalSyncRunId]);
    PRINT 'Created IX_InternalSyncStepRuns_Run index';
END;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_InternalSyncSteps_Project_Order')
BEGIN
    CREATE INDEX [IX_InternalSyncSteps_Project_Order] ON [InternalSyncSteps] ([SyncProjectId], [ExecutionOrder]);
    PRINT 'Created IX_InternalSyncSteps_Project_Order index';
END;
GO

-- Re-add the migration record
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260101011357_AddInternalSyncSteps', N'8.0.0');
PRINT 'Added migration history record';
GO

PRINT 'Internal Sync tables setup complete!';
GO
