-- V008: Create BulkIssueSnapshots, BulkOperationSessions, and BulkOperationChanges tables
-- These were accidentally omitted from V004 (only the OwnerIdentityId index/FK was included).

-- 1. BulkIssueSnapshots - trend tracking for bulk issue counts
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BulkIssueSnapshots')
BEGIN
    CREATE TABLE BulkIssueSnapshots (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        IssueId NVARCHAR(100) NOT NULL,
        IssueTitle NVARCHAR(200) NULL,
        Category NVARCHAR(50) NULL,
        AffectedCount INT NOT NULL DEFAULT 0,
        FixableCount INT NOT NULL DEFAULT 0,
        ChangeFromPrevious INT NOT NULL DEFAULT 0,
        ChangePercentage FLOAT NOT NULL DEFAULT 0,
        SnapshotDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        SnapshotType NVARCHAR(20) NOT NULL DEFAULT 'Daily',
        NotificationSent BIT NOT NULL DEFAULT 0,
        Metadata NVARCHAR(MAX) NULL
    );

    CREATE NONCLUSTERED INDEX IX_BulkIssueSnapshots_IssueId_SnapshotDate
        ON BulkIssueSnapshots (IssueId, SnapshotDate DESC);

    CREATE NONCLUSTERED INDEX IX_BulkIssueSnapshots_SnapshotDate
        ON BulkIssueSnapshots (SnapshotDate DESC);

    PRINT 'Created BulkIssueSnapshots table with indexes';
END
ELSE
BEGIN
    PRINT 'BulkIssueSnapshots already exists - skipping';
END;
GO

-- 2. BulkOperationSessions - tracks bulk fix operations for rollback support
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BulkOperationSessions')
BEGIN
    CREATE TABLE BulkOperationSessions (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        IssueId NVARCHAR(100) NOT NULL,
        IssueTitle NVARCHAR(200) NOT NULL,
        UserId NVARCHAR(256) NOT NULL,
        UserDisplayName NVARCHAR(256) NULL,
        ExecutedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ItemCount INT NOT NULL DEFAULT 0,
        SuccessCount INT NOT NULL DEFAULT 0,
        FailedCount INT NOT NULL DEFAULT 0,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Completed',
        DepartmentFilter NVARCHAR(100) NULL,
        OuFilter NVARCHAR(500) NULL,
        LastModifiedAt DATETIME2 NULL,
        RolledBackBy NVARCHAR(256) NULL,
        RolledBackAt DATETIME2 NULL,
        Notes NVARCHAR(MAX) NULL
    );

    CREATE NONCLUSTERED INDEX IX_BulkOperationSessions_ExecutedAt
        ON BulkOperationSessions (ExecutedAt DESC);

    CREATE NONCLUSTERED INDEX IX_BulkOperationSessions_IssueId
        ON BulkOperationSessions (IssueId);

    CREATE NONCLUSTERED INDEX IX_BulkOperationSessions_Status
        ON BulkOperationSessions (Status);

    PRINT 'Created BulkOperationSessions table with indexes';
END
ELSE
BEGIN
    PRINT 'BulkOperationSessions already exists - skipping';
END;
GO

-- 3. BulkOperationChanges - individual changes for rollback capability
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BulkOperationChanges')
BEGIN
    CREATE TABLE BulkOperationChanges (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        SessionId UNIQUEIDENTIFIER NOT NULL,
        EntityId UNIQUEIDENTIFIER NOT NULL,
        EntityType NVARCHAR(50) NOT NULL,
        EntityName NVARCHAR(256) NULL,
        PropertyName NVARCHAR(100) NOT NULL,
        OldValue NVARCHAR(MAX) NULL,
        NewValue NVARCHAR(MAX) NULL,
        IsRolledBack BIT NOT NULL DEFAULT 0,
        RolledBackAt DATETIME2 NULL,
        RollbackError NVARCHAR(MAX) NULL,
        Metadata NVARCHAR(MAX) NULL,
        CONSTRAINT FK_BulkOperationChanges_Sessions
            FOREIGN KEY (SessionId) REFERENCES BulkOperationSessions(Id) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX IX_BulkOperationChanges_SessionId
        ON BulkOperationChanges (SessionId);

    CREATE NONCLUSTERED INDEX IX_BulkOperationChanges_EntityId
        ON BulkOperationChanges (EntityId);

    PRINT 'Created BulkOperationChanges table with indexes';
END
ELSE
BEGIN
    PRINT 'BulkOperationChanges already exists - skipping';
END;
GO

PRINT 'V008 complete: BulkIssueSnapshots and BulkOperationSessions/Changes tables created';
GO
