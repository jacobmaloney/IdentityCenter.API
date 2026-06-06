-- V001: Initial Schema Version Setup
-- This migration sets up the baseline for the schema versioning system.
-- All future database changes should be added as numbered migrations (V002, V003, etc.)

-- Note: The __SchemaVersion table is created automatically by DatabaseMigrationService
-- This script serves as the initial version marker

-- Example pattern for future migrations:

-- Add column if not exists
-- IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
--                WHERE TABLE_NAME = 'YourTable' AND COLUMN_NAME = 'NewColumn')
-- BEGIN
--     ALTER TABLE YourTable ADD NewColumn NVARCHAR(256) NULL;
-- END

-- Create table if not exists
-- IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'NewTable')
-- BEGIN
--     CREATE TABLE NewTable (
--         Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
--         Name NVARCHAR(256) NOT NULL,
--         CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
--     );
-- END

-- Create index if not exists
-- IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_YourTable_Column' AND object_id = OBJECT_ID('YourTable'))
-- BEGIN
--     CREATE INDEX IX_YourTable_Column ON YourTable(Column);
-- END

PRINT 'Schema version 1 applied - baseline established';
