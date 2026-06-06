-- V046: Add EnabledRecords and DisabledRecords columns to HRImportRuns
-- Tracks how many identities were enabled/disabled during each HR import run.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('HRImportRuns') AND name = 'EnabledRecords')
BEGIN
    ALTER TABLE [HRImportRuns] ADD [EnabledRecords] INT NOT NULL DEFAULT 0;
    PRINT 'Added EnabledRecords column to HRImportRuns';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('HRImportRuns') AND name = 'DisabledRecords')
BEGIN
    ALTER TABLE [HRImportRuns] ADD [DisabledRecords] INT NOT NULL DEFAULT 0;
    PRINT 'Added DisabledRecords column to HRImportRuns';
END
