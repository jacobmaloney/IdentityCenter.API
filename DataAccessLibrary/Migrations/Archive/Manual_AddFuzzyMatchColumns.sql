-- Manual Migration: Add Fuzzy Match Columns
-- Run this in SSMS against IdentityCenter13 database

-- Add columns to AttributeMappings
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'AttributeMappings') AND name = 'UseFuzzyMatch')
BEGIN
    ALTER TABLE AttributeMappings ADD UseFuzzyMatch BIT NOT NULL DEFAULT 0;
    PRINT 'Added UseFuzzyMatch column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'AttributeMappings') AND name = 'FuzzyMatchThreshold')
BEGIN
    ALTER TABLE AttributeMappings ADD FuzzyMatchThreshold FLOAT NOT NULL DEFAULT 0.85;
    PRINT 'Added FuzzyMatchThreshold column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'AttributeMappings') AND name = 'FuzzyMatchAlgorithm')
BEGIN
    ALTER TABLE AttributeMappings ADD FuzzyMatchAlgorithm NVARCHAR(50) NOT NULL DEFAULT 'Levenshtein';
    PRINT 'Added FuzzyMatchAlgorithm column';
END

-- Add columns to SyncStepRuns for person matching stats
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'SyncStepRuns') AND name = 'PersonsMatched')
BEGIN
    ALTER TABLE SyncStepRuns ADD PersonsMatched INT NOT NULL DEFAULT 0;
    PRINT 'Added PersonsMatched column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'SyncStepRuns') AND name = 'PersonsCreated')
BEGIN
    ALTER TABLE SyncStepRuns ADD PersonsCreated INT NOT NULL DEFAULT 0;
    PRINT 'Added PersonsCreated column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'SyncStepRuns') AND name = 'PersonMatchingSkipped')
BEGIN
    ALTER TABLE SyncStepRuns ADD PersonMatchingSkipped INT NOT NULL DEFAULT 0;
    PRINT 'Added PersonMatchingSkipped column';
END

PRINT 'Fuzzy Match columns migration complete!';
