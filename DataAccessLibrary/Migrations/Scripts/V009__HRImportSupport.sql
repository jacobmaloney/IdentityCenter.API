-- V009: HR Import Support
-- Adds HRFieldMappings and HRImportRuns tables for HR data source integration.
-- Supports CSV, REST API, and SCIM 2.0 HR data imports into the Identities table.

-- =============================================
-- HRFieldMappings: Maps source fields to Identity properties
-- =============================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'HRFieldMappings')
BEGIN
    CREATE TABLE [HRFieldMappings] (
        [Id]                    UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [DirectoryConnectionId] UNIQUEIDENTIFIER NOT NULL,
        [SourceField]           NVARCHAR(200)    NOT NULL,
        [TargetField]           NVARCHAR(200)    NOT NULL,
        [IsRequired]            BIT              NOT NULL DEFAULT 0,
        [DefaultValue]          NVARCHAR(500)    NULL,
        [Transformation]        NVARCHAR(100)    NULL,
        [MappingOrder]          INT              NOT NULL DEFAULT 0,
        [IsEnabled]             BIT              NOT NULL DEFAULT 1,
        [IsKeyField]            BIT              NOT NULL DEFAULT 0,
        CONSTRAINT [PK_HRFieldMappings] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_HRFieldMappings_DirectoryConnections]
            FOREIGN KEY ([DirectoryConnectionId])
            REFERENCES [DirectoryConnections] ([Id]) ON DELETE CASCADE
    );

    PRINT 'Created HRFieldMappings table';
END
ELSE
BEGIN
    PRINT 'HRFieldMappings table already exists - skipping';
END
GO

-- =============================================
-- HRImportRuns: Execution history per import
-- =============================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'HRImportRuns')
BEGIN
    CREATE TABLE [HRImportRuns] (
        [Id]              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [SyncProjectId]   UNIQUEIDENTIFIER NOT NULL,
        [Status]          NVARCHAR(50)     NOT NULL DEFAULT 'Running',
        [SourceFileName]  NVARCHAR(500)    NULL,
        [TotalRecords]    INT              NOT NULL DEFAULT 0,
        [CreatedRecords]  INT              NOT NULL DEFAULT 0,
        [UpdatedRecords]  INT              NOT NULL DEFAULT 0,
        [SkippedRecords]  INT              NOT NULL DEFAULT 0,
        [ErrorRecords]    INT              NOT NULL DEFAULT 0,
        [ErrorDetails]    NVARCHAR(MAX)    NULL,
        [StartedAt]       DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        [CompletedAt]     DATETIME2        NULL,
        [DurationSeconds] INT              NOT NULL DEFAULT 0,
        CONSTRAINT [PK_HRImportRuns] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_HRImportRuns_SyncProjects]
            FOREIGN KEY ([SyncProjectId])
            REFERENCES [SyncProjects] ([Id]) ON DELETE CASCADE
    );

    PRINT 'Created HRImportRuns table';
END
ELSE
BEGIN
    PRINT 'HRImportRuns table already exists - skipping';
END
GO

-- =============================================
-- Indexes
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HRFieldMappings_ConnectionId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HRFieldMappings_ConnectionId]
        ON [HRFieldMappings] ([DirectoryConnectionId])
        INCLUDE ([SourceField], [TargetField], [IsEnabled], [MappingOrder]);
    PRINT 'Created IX_HRFieldMappings_ConnectionId index';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HRImportRuns_SyncProjectId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HRImportRuns_SyncProjectId]
        ON [HRImportRuns] ([SyncProjectId])
        INCLUDE ([Status], [StartedAt], [CompletedAt]);
    PRINT 'Created IX_HRImportRuns_SyncProjectId index';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HRImportRuns_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_HRImportRuns_Status]
        ON [HRImportRuns] ([Status], [StartedAt] DESC);
    PRINT 'Created IX_HRImportRuns_Status index';
END
GO

PRINT 'V009: HR Import Support migration complete';
