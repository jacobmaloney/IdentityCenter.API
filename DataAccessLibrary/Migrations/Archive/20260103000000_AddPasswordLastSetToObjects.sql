-- Migration: Add PasswordLastSet column to Objects table
-- This enables the PasswordAge policy rule type to evaluate password expiration

-- Add the PasswordLastSet column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'PasswordLastSet')
BEGIN
    ALTER TABLE Objects ADD PasswordLastSet DATETIME NULL;
    PRINT 'Added PasswordLastSet column to Objects table';
END
ELSE
BEGIN
    PRINT 'PasswordLastSet column already exists';
END
GO

-- Create index for performance (filtered to user objects only)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_PasswordLastSet' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_PasswordLastSet
    ON Objects(PasswordLastSet)
    WHERE ObjectClass = 'user' AND PasswordLastSet IS NOT NULL;
    PRINT 'Created IX_Objects_PasswordLastSet index';
END
GO

-- Record migration
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20260103000000_AddPasswordLastSetToObjects')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260103000000_AddPasswordLastSetToObjects', '8.0.0');
    PRINT 'Migration recorded in history';
END
GO
