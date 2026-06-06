-- Migration: Add UserAccountControl column to Objects table
-- Date: 2026-01-04
-- Description: Stores raw userAccountControl value from AD for Account tab display

-- Add the column if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'UserAccountControl')
BEGIN
    ALTER TABLE Objects ADD UserAccountControl INT NULL;
    PRINT 'Added UserAccountControl column to Objects table';
END
ELSE
BEGIN
    PRINT 'UserAccountControl column already exists';
END

-- Create index for querying accounts by UAC flags (disabled accounts, etc.)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('Objects') AND name = 'IX_Objects_UserAccountControl')
BEGIN
    CREATE INDEX IX_Objects_UserAccountControl ON Objects(UserAccountControl) WHERE UserAccountControl IS NOT NULL;
    PRINT 'Created index on UserAccountControl';
END

PRINT 'Migration complete. Run a sync to populate the UserAccountControl values from AD.';
