-- ============================================================================
-- Performance Optimization Indexes
-- IdentityCenter Database
-- Created: 2025-12-12
-- Description: Indexes to optimize sync and query performance
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

PRINT 'Adding performance indexes...';

-- ============================================================================
-- Objects Table Indexes
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_SourceConnectionId_IsActive' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_SourceConnectionId_IsActive
    ON Objects(SourceConnectionId, IsActive)
    INCLUDE (SourceUniqueId, DisplayName, ObjectClass);
    PRINT 'Created: IX_Objects_SourceConnectionId_IsActive';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_SourceUniqueId' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_SourceUniqueId
    ON Objects(SourceUniqueId)
    INCLUDE (Id, DisplayName, SourceConnectionId);
    PRINT 'Created: IX_Objects_SourceUniqueId';
END

-- ============================================================================
-- ObjectAttributes Table Indexes
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectAttributes_ObjectId_AttributeName' AND object_id = OBJECT_ID('ObjectAttributes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_ObjectAttributes_ObjectId_AttributeName
    ON ObjectAttributes(ObjectId, AttributeName)
    INCLUDE (AttributeValue);
    PRINT 'Created: IX_ObjectAttributes_ObjectId_AttributeName';
END

-- ============================================================================
-- Groups Table Indexes
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_SourceConnectionId_IsActive' AND object_id = OBJECT_ID('Groups'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Groups_SourceConnectionId_IsActive
    ON Groups(SourceConnectionId, IsActive)
    INCLUDE (DistinguishedName, Name);
    PRINT 'Created: IX_Groups_SourceConnectionId_IsActive';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_DistinguishedName' AND object_id = OBJECT_ID('Groups'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Groups_DistinguishedName
    ON Groups(DistinguishedName)
    INCLUDE (Id, Name, SourceConnectionId);
    PRINT 'Created: IX_Groups_DistinguishedName';
END

-- ============================================================================
-- ObjectGroupMemberships Table Indexes
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectGroupMemberships_ObjectId_RemovedAt' AND object_id = OBJECT_ID('ObjectGroupMemberships'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_ObjectGroupMemberships_ObjectId_RemovedAt
    ON ObjectGroupMemberships(ObjectId, RemovedAt)
    INCLUDE (GroupId, IsDirect);
    PRINT 'Created: IX_ObjectGroupMemberships_ObjectId_RemovedAt';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectGroupMemberships_GroupId' AND object_id = OBJECT_ID('ObjectGroupMemberships'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_ObjectGroupMemberships_GroupId
    ON ObjectGroupMemberships(GroupId)
    INCLUDE (ObjectId, IsDirect, RemovedAt);
    PRINT 'Created: IX_ObjectGroupMemberships_GroupId';
END

-- ============================================================================
-- Identities Table Indexes (Identity = Person in the new schema)
-- The Identities table stores person-level records (PrimaryEmail, DisplayName)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Identities_PrimaryEmail' AND object_id = OBJECT_ID('Identities'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Identities_PrimaryEmail
    ON Identities(PrimaryEmail)
    INCLUDE (Id, DisplayName, IsActive);
    PRINT 'Created: IX_Identities_PrimaryEmail';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Identities_IsActive' AND object_id = OBJECT_ID('Identities'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Identities_IsActive
    ON Identities(IsActive)
    INCLUDE (Id, DisplayName, PrimaryEmail);
    PRINT 'Created: IX_Identities_IsActive';
END

-- ============================================================================
-- Objects Table (IdentityObject) - Additional index for IdentityId lookups
-- Used for linking objects to their parent Identity (person)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_IdentityId' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_IdentityId
    ON Objects(IdentityId)
    INCLUDE (Id, DisplayName, SourceConnectionId);
    PRINT 'Created: IX_Objects_IdentityId';
END

-- ============================================================================
-- SyncAuditLogs Table Indexes (for better query performance)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncAuditLogs_SyncStepRunId_Timestamp' AND object_id = OBJECT_ID('SyncAuditLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncAuditLogs_SyncStepRunId_Timestamp
    ON SyncAuditLogs(SyncStepRunId, Timestamp DESC)
    INCLUDE (OperationType, ObjectDisplayName);
    PRINT 'Created: IX_SyncAuditLogs_SyncStepRunId_Timestamp';
END

-- ============================================================================
-- JobQueue Table Indexes (for remote agent job claiming)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobQueue_Status_Priority_CreatedAt' AND object_id = OBJECT_ID('JobQueue'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobQueue_Status_Priority_CreatedAt
    ON JobQueue(Status, Priority DESC, CreatedAt ASC)
    WHERE Status = 'Pending';
    PRINT 'Created: IX_JobQueue_Status_Priority_CreatedAt (filtered)';
END

PRINT '';
PRINT '============================================================================';
PRINT 'Performance indexes have been added successfully!';
PRINT '============================================================================';
GO
