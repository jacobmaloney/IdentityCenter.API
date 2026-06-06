-- V002: Add indexes to ChangeAuditLogs table for improved query performance
-- These indexes optimize common audit log queries and prevent timeouts
-- Note: On a brand new DB, ChangeAuditLogs may not exist yet (created by V004).
-- This script is skipped gracefully via the table existence check below.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChangeAuditLogs')
BEGIN
    PRINT 'ChangeAuditLogs table does not exist yet - skipping V002 (will be created with indexes by V004)';
    RETURN;
END

-- Index for querying by EntityId (filtered - only non-null values)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_EntityId' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX IX_ChangeAuditLogs_EntityId ON ChangeAuditLogs(EntityId) WHERE EntityId IS NOT NULL;
    PRINT 'Created index IX_ChangeAuditLogs_EntityId';
END

-- Index for querying by EntityType and Timestamp (common filter + sort)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_EntityType_Timestamp' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX IX_ChangeAuditLogs_EntityType_Timestamp ON ChangeAuditLogs(EntityType, Timestamp DESC);
    PRINT 'Created index IX_ChangeAuditLogs_EntityType_Timestamp';
END

-- Index for querying by Timestamp only (for recent activity queries)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_Timestamp' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX IX_ChangeAuditLogs_Timestamp ON ChangeAuditLogs(Timestamp DESC);
    PRINT 'Created index IX_ChangeAuditLogs_Timestamp';
END

-- Index for querying by UserId (filtered - only non-null values)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_UserId' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX IX_ChangeAuditLogs_UserId ON ChangeAuditLogs(UserId) WHERE UserId IS NOT NULL;
    PRINT 'Created index IX_ChangeAuditLogs_UserId';
END

-- Index for querying by CorrelationId (for tracking related changes)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_CorrelationId' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX IX_ChangeAuditLogs_CorrelationId ON ChangeAuditLogs(CorrelationId) WHERE CorrelationId IS NOT NULL;
    PRINT 'Created index IX_ChangeAuditLogs_CorrelationId';
END

-- Index for querying by Source (for filtering by origin like API, ChatUI, SyncEngine)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_Source' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX IX_ChangeAuditLogs_Source ON ChangeAuditLogs(Source) WHERE Source IS NOT NULL;
    PRINT 'Created index IX_ChangeAuditLogs_Source';
END

PRINT 'Schema version 2 applied - ChangeAuditLogs indexes added';
