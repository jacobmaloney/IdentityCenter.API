-- Create ChangeAuditLogs table
-- This table tracks all changes made to directory objects for audit purposes

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChangeAuditLogs')
BEGIN
    CREATE TABLE ChangeAuditLogs (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UserId NVARCHAR(256) NULL,
        UserDisplayName NVARCHAR(256) NULL,
        UserEmail NVARCHAR(256) NULL,
        IpAddress NVARCHAR(45) NULL,
        OperationType INT NOT NULL,
        EntityType NVARCHAR(50) NULL,
        EntityId UNIQUEIDENTIFIER NULL,
        EntityDisplayName NVARCHAR(256) NULL,
        PropertyName NVARCHAR(100) NULL,
        OldValue NVARCHAR(2000) NULL,
        NewValue NVARCHAR(2000) NULL,
        RelatedEntityId UNIQUEIDENTIFIER NULL,
        RelatedEntityName NVARCHAR(256) NULL,
        Reason NVARCHAR(500) NULL,
        TicketNumber NVARCHAR(100) NULL,
        ApprovedBy UNIQUEIDENTIFIER NULL,
        ApproverName NVARCHAR(256) NULL,
        Success BIT NOT NULL DEFAULT 1,
        ErrorMessage NVARCHAR(1000) NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        Source NVARCHAR(50) NULL
    );

    -- Create indexes for common queries
    CREATE INDEX IX_ChangeAuditLogs_EntityId ON ChangeAuditLogs(EntityId) WHERE EntityId IS NOT NULL;
    CREATE INDEX IX_ChangeAuditLogs_EntityType_Timestamp ON ChangeAuditLogs(EntityType, Timestamp DESC);
    CREATE INDEX IX_ChangeAuditLogs_Timestamp ON ChangeAuditLogs(Timestamp DESC);
    CREATE INDEX IX_ChangeAuditLogs_UserId ON ChangeAuditLogs(UserId) WHERE UserId IS NOT NULL;
    CREATE INDEX IX_ChangeAuditLogs_CorrelationId ON ChangeAuditLogs(CorrelationId) WHERE CorrelationId IS NOT NULL;

    PRINT 'ChangeAuditLogs table created successfully';
END
ELSE
BEGIN
    PRINT 'ChangeAuditLogs table already exists';
END
