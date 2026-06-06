-- V003: Add CreatedAt and ModifiedAt columns to Objects table
-- Required by InternalSyncService for object-to-identity matching and manager resolution
-- Note: On a brand new DB, Objects may not exist yet (created by V004).
-- The INFORMATION_SCHEMA checks will safely return no rows if the table doesn't exist.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'Objects' AND COLUMN_NAME = 'CreatedAt')
    AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Objects')
BEGIN
    ALTER TABLE Objects ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE();
    PRINT 'Added CreatedAt column to Objects table';
END

GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'Objects' AND COLUMN_NAME = 'ModifiedAt')
    AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Objects')
BEGIN
    ALTER TABLE Objects ADD ModifiedAt DATETIME2 NULL;
    PRINT 'Added ModifiedAt column to Objects table';
END

GO

-- Backfill CreatedAt from FirstSyncedAt for existing rows (only if table exists)
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Objects')
    AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Objects' AND COLUMN_NAME = 'FirstSyncedAt')
BEGIN
    UPDATE Objects SET CreatedAt = FirstSyncedAt WHERE CreatedAt = '0001-01-01';
END

PRINT 'Schema version 3 applied - Objects CreatedAt/ModifiedAt columns added';
