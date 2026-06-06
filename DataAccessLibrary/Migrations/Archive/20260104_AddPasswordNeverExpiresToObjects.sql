-- Migration: Add PasswordNeverExpires column to Objects table
-- Date: 2026-01-04
-- Description: Adds PasswordNeverExpires flag synced from AD userAccountControl attribute

-- Add the column if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'PasswordNeverExpires')
BEGIN
    ALTER TABLE Objects ADD PasswordNeverExpires BIT NOT NULL DEFAULT 0;
    PRINT 'Added PasswordNeverExpires column to Objects table';
END
ELSE
BEGIN
    PRINT 'PasswordNeverExpires column already exists';
END

-- Create index for querying accounts with password never expires
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('Objects') AND name = 'IX_Objects_PasswordNeverExpires')
BEGIN
    CREATE INDEX IX_Objects_PasswordNeverExpires ON Objects(PasswordNeverExpires) WHERE PasswordNeverExpires = 1;
    PRINT 'Created filtered index on PasswordNeverExpires';
END

-- Update the report query to use the new column
UPDATE Reports
SET QueryDefinition = 'SELECT Id, DisplayName, Username, Email, DN, IsActive, PasswordLastSet, FirstSyncedAt
    FROM Objects
    WHERE ObjectClass = ''user'' AND PasswordNeverExpires = 1
    ORDER BY DisplayName'
WHERE Name = 'accounts_password_never_expires';

PRINT 'Migration complete. Run a sync to populate the PasswordNeverExpires values from AD userAccountControl.';
