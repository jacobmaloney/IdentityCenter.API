-- V148: M365 usage storage bytes
-- Adds nullable BIGINT storage columns to M365UsageReports so a Conduit
-- m365usage push can persist OneDrive + mailbox storage (the OneDrive-on-user
-- tab gates its storage bar on used/allocated bytes, which had no home until now).
-- Conduit already collects all four (OneDrive used/allocated, mailbox used/quota),
-- so add all four to keep mailbox storage future-ready.
-- Defensive per-column guards so the migration is safe to replay and on shared DBs.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'M365UsageReports' AND COLUMN_NAME = 'OneDriveStorageUsedBytes')
BEGIN
    ALTER TABLE [M365UsageReports] ADD [OneDriveStorageUsedBytes] BIGINT NULL;
    PRINT 'V148: added M365UsageReports.OneDriveStorageUsedBytes';
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'M365UsageReports' AND COLUMN_NAME = 'OneDriveStorageAllocatedBytes')
BEGIN
    ALTER TABLE [M365UsageReports] ADD [OneDriveStorageAllocatedBytes] BIGINT NULL;
    PRINT 'V148: added M365UsageReports.OneDriveStorageAllocatedBytes';
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'M365UsageReports' AND COLUMN_NAME = 'MailboxStorageUsedBytes')
BEGIN
    ALTER TABLE [M365UsageReports] ADD [MailboxStorageUsedBytes] BIGINT NULL;
    PRINT 'V148: added M365UsageReports.MailboxStorageUsedBytes';
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'M365UsageReports' AND COLUMN_NAME = 'MailboxQuotaBytes')
BEGIN
    ALTER TABLE [M365UsageReports] ADD [MailboxQuotaBytes] BIGINT NULL;
    PRINT 'V148: added M365UsageReports.MailboxQuotaBytes';
END;
GO

PRINT 'V148: M365 usage storage bytes migration complete';
GO
