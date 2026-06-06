-- Migration: Add Person Matching Configuration to SyncStep
-- Date: 2025-12-26
-- Purpose: Add EnablePersonMatching and CreatePersonIfNotFound columns to SyncSteps table

-- Add EnablePersonMatching column (default TRUE for existing steps)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SyncSteps') AND name = 'EnablePersonMatching')
BEGIN
    ALTER TABLE SyncSteps ADD EnablePersonMatching BIT NOT NULL DEFAULT 1;
    PRINT 'Added EnablePersonMatching column to SyncSteps';
END
ELSE
BEGIN
    PRINT 'EnablePersonMatching column already exists';
END
GO

-- Add CreatePersonIfNotFound column (default TRUE for existing steps)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SyncSteps') AND name = 'CreatePersonIfNotFound')
BEGIN
    ALTER TABLE SyncSteps ADD CreatePersonIfNotFound BIT NOT NULL DEFAULT 1;
    PRINT 'Added CreatePersonIfNotFound column to SyncSteps';
END
ELSE
BEGIN
    PRINT 'CreatePersonIfNotFound column already exists';
END
GO

-- Set non-user object classes to have person matching disabled by default
UPDATE SyncSteps
SET EnablePersonMatching = 0
WHERE ObjectClass NOT IN ('user', 'User', 'contact', 'Contact')
  AND EnablePersonMatching = 1;

PRINT 'Disabled person matching for non-user object classes';
GO
