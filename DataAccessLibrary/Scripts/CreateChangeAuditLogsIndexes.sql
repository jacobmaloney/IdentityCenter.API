SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Create indexes for common queries if they don't exist
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_EntityId')
    CREATE INDEX IX_ChangeAuditLogs_EntityId ON ChangeAuditLogs(EntityId) WHERE EntityId IS NOT NULL;

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_EntityType_Timestamp')
    CREATE INDEX IX_ChangeAuditLogs_EntityType_Timestamp ON ChangeAuditLogs(EntityType, Timestamp DESC);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_Timestamp')
    CREATE INDEX IX_ChangeAuditLogs_Timestamp ON ChangeAuditLogs(Timestamp DESC);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_UserId')
    CREATE INDEX IX_ChangeAuditLogs_UserId ON ChangeAuditLogs(UserId) WHERE UserId IS NOT NULL;

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_CorrelationId')
    CREATE INDEX IX_ChangeAuditLogs_CorrelationId ON ChangeAuditLogs(CorrelationId) WHERE CorrelationId IS NOT NULL;

PRINT 'All indexes created successfully';
