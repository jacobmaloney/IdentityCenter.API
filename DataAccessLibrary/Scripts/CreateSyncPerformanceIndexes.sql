-- Performance indexes for sync operations
-- Run this script to fix slow UpdateStepRunMetricsAsync and related operations

-- SyncStepRuns indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepRuns_SyncProjectRunId' AND object_id = OBJECT_ID('SyncStepRuns'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncStepRuns_SyncProjectRunId
    ON SyncStepRuns(SyncProjectRunId)
    INCLUDE (Status, ObjectsQueried, ObjectsProcessed, ObjectsCreated, ObjectsUpdated, ObjectsSkipped, ErrorCount);
    PRINT 'Created IX_SyncStepRuns_SyncProjectRunId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepRuns_Status' AND object_id = OBJECT_ID('SyncStepRuns'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncStepRuns_Status
    ON SyncStepRuns(Status)
    WHERE Status = 'Running';
    PRINT 'Created IX_SyncStepRuns_Status';
END

-- SyncProjectRuns indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectRuns_SyncProjectId' AND object_id = OBJECT_ID('SyncProjectRuns'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncProjectRuns_SyncProjectId
    ON SyncProjectRuns(SyncProjectId, StartedAt DESC)
    INCLUDE (Status, ProgressPercentage, CompletedSteps, TotalSteps);
    PRINT 'Created IX_SyncProjectRuns_SyncProjectId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectRuns_Status' AND object_id = OBJECT_ID('SyncProjectRuns'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncProjectRuns_Status
    ON SyncProjectRuns(Status)
    WHERE Status = 'Running';
    PRINT 'Created IX_SyncProjectRuns_Status';
END

-- Objects indexes for sync lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_SourceConnectionId_SourceUniqueId' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_Objects_SourceConnectionId_SourceUniqueId
    ON Objects(SourceConnectionId, SourceUniqueId)
    WHERE SourceUniqueId IS NOT NULL;
    PRINT 'Created IX_Objects_SourceConnectionId_SourceUniqueId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_DN' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_DN
    ON Objects(SourceConnectionId, DN)
    WHERE DN IS NOT NULL;
    PRINT 'Created IX_Objects_DN';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_ManagerSourceId' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_ManagerSourceId
    ON Objects(SourceConnectionId, ManagerSourceId)
    WHERE ManagerSourceId IS NOT NULL;
    PRINT 'Created IX_Objects_ManagerSourceId';
END

-- ObjectAttributes index for bulk lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectAttributes_ObjectId' AND object_id = OBJECT_ID('ObjectAttributes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_ObjectAttributes_ObjectId
    ON ObjectAttributes(ObjectId)
    INCLUDE (AttributeName, AttributeValue);
    PRINT 'Created IX_ObjectAttributes_ObjectId';
END

-- Groups indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_SourceConnectionId_SourceUniqueId' AND object_id = OBJECT_ID('Groups'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_Groups_SourceConnectionId_SourceUniqueId
    ON Groups(SourceConnectionId, SourceUniqueId)
    WHERE SourceUniqueId IS NOT NULL;
    PRINT 'Created IX_Groups_SourceConnectionId_SourceUniqueId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_ObjectSid' AND object_id = OBJECT_ID('Groups'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Groups_ObjectSid
    ON Groups(ObjectSid)
    WHERE ObjectSid IS NOT NULL;
    PRINT 'Created IX_Groups_ObjectSid (for primary group resolution)';
END

-- ObjectGroupMemberships indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectGroupMemberships_ObjectId_IsActive' AND object_id = OBJECT_ID('ObjectGroupMemberships'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_ObjectGroupMemberships_ObjectId_IsActive
    ON ObjectGroupMemberships(ObjectId, IsActive)
    INCLUDE (GroupId, IsDirect, IsPrimary);
    PRINT 'Created IX_ObjectGroupMemberships_ObjectId_IsActive';
END

-- PostSyncTasks indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PostSyncTasks_SyncProjectRunId' AND object_id = OBJECT_ID('PostSyncTasks'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PostSyncTasks_SyncProjectRunId
    ON PostSyncTasks(SyncProjectRunId)
    INCLUDE (Status, TaskType, Priority);
    PRINT 'Created IX_PostSyncTasks_SyncProjectRunId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PostSyncTasks_Status' AND object_id = OBJECT_ID('PostSyncTasks'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PostSyncTasks_Status
    ON PostSyncTasks(Status)
    WHERE Status IN ('Pending', 'Running');
    PRINT 'Created IX_PostSyncTasks_Status';
END

-- SyncAuditLogs indexes for efficient querying
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncAuditLogs_SyncStepRunId' AND object_id = OBJECT_ID('SyncAuditLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncAuditLogs_SyncStepRunId
    ON SyncAuditLogs(SyncStepRunId)
    INCLUDE (OperationType, Timestamp);
    PRINT 'Created IX_SyncAuditLogs_SyncStepRunId';
END

-- Update statistics on sync tables
UPDATE STATISTICS SyncStepRuns;
UPDATE STATISTICS SyncProjectRuns;
UPDATE STATISTICS Objects;
UPDATE STATISTICS ObjectAttributes;
UPDATE STATISTICS Groups;
UPDATE STATISTICS ObjectGroupMemberships;
UPDATE STATISTICS PostSyncTasks;

PRINT 'Index creation complete. Statistics updated.';
