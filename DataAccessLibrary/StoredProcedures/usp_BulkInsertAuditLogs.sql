-- Stored Procedure: Bulk Insert Audit Logs
-- Ultra-fast batch insert for audit logging
CREATE OR ALTER PROCEDURE [dbo].[usp_BulkInsertAuditLogs]
    @AuditLogsJson NVARCHAR(MAX) -- JSON array of audit log entries
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();

    INSERT INTO SyncAuditLogs (
        Id, SyncStepRunId, ObjectId, OperationType,
        ObjectDisplayName, SourceUniqueId, Email, Username, UserPrincipalName,
        ChangeDetails, ChangeCount, ErrorMessage, ProcessingTimeMs, Timestamp
    )
    SELECT
        NEWID(),
        SyncStepRunId,
        ObjectId,
        OperationType,
        ObjectDisplayName,
        SourceUniqueId,
        Email,
        Username,
        UserPrincipalName,
        ChangeDetails,
        ChangeCount,
        ErrorMessage,
        ProcessingTimeMs,
        @Now
    FROM OPENJSON(@AuditLogsJson)
    WITH (
        SyncStepRunId UNIQUEIDENTIFIER '$.SyncStepRunId',
        ObjectId UNIQUEIDENTIFIER '$.ObjectId',
        OperationType NVARCHAR(50) '$.OperationType',
        ObjectDisplayName NVARCHAR(200) '$.ObjectDisplayName',
        SourceUniqueId NVARCHAR(500) '$.SourceUniqueId',
        Email NVARCHAR(256) '$.Email',
        Username NVARCHAR(256) '$.Username',
        UserPrincipalName NVARCHAR(500) '$.UserPrincipalName',
        ChangeDetails NVARCHAR(MAX) '$.ChangeDetails',
        ChangeCount INT '$.ChangeCount',
        ErrorMessage NVARCHAR(MAX) '$.ErrorMessage',
        ProcessingTimeMs DECIMAL(18,2) '$.ProcessingTimeMs'
    );

    SELECT @@ROWCOUNT AS AuditLogsInserted;
END
GO
