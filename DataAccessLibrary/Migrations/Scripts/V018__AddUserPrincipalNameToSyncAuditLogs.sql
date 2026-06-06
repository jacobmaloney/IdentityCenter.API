-- V018: Add UserPrincipalName column to SyncAuditLogs table
-- Shows UPN below display name in audit log UI instead of Email

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SyncAuditLogs') AND name = 'UserPrincipalName')
    ALTER TABLE SyncAuditLogs ADD UserPrincipalName NVARCHAR(500) NULL;
GO
