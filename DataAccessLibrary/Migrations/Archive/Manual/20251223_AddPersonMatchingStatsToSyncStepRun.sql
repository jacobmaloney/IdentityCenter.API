-- Migration: Add Person Matching Statistics to SyncStepRuns
-- Date: 2025-12-23
-- Description: Adds columns to track person matching results per sync step

-- Check if columns already exist before adding
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SyncStepRuns') AND name = 'PersonsMatched')
BEGIN
    ALTER TABLE SyncStepRuns ADD PersonsMatched INT NOT NULL DEFAULT 0;
    PRINT 'Added PersonsMatched column';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SyncStepRuns') AND name = 'PersonsCreated')
BEGIN
    ALTER TABLE SyncStepRuns ADD PersonsCreated INT NOT NULL DEFAULT 0;
    PRINT 'Added PersonsCreated column';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SyncStepRuns') AND name = 'PersonMatchingSkipped')
BEGIN
    ALTER TABLE SyncStepRuns ADD PersonMatchingSkipped INT NOT NULL DEFAULT 0;
    PRINT 'Added PersonMatchingSkipped column';
END

PRINT 'Migration complete: AddPersonMatchingStatsToSyncStepRun';
