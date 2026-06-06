-- ============================================================================
-- SAFE Manual Migration: Person/Identity Refactoring (3-STEP PROCESS)
-- Date: 2025-10-21
-- Purpose: Rename tables and columns using proper sequence to avoid conflicts
-- ============================================================================

-- CRITICAL: BACKUP YOUR DATABASE BEFORE RUNNING THIS MIGRATION!
-- This script must be run in SQL Server Management Studio or Azure Data Studio
-- DO NOT run through EF Core migrations!

-- ============================================================================
-- NAMING CONFLICT RESOLUTION
-- ============================================================================
-- Problem: Cannot rename Persons→Identities while Identities table exists
-- Solution: Rename in correct order:
--   Step 1: Identities → Objects (free up the "Identities" name)
--   Step 2: Persons → Identities (now safe to use the name)
--   Step 3: Update all foreign keys and indexes
-- ============================================================================

BEGIN TRANSACTION RefactorNaming;

SET NOCOUNT ON;

PRINT '========================================';
PRINT 'Person/Identity Refactoring Migration';
PRINT 'Started: ' + CONVERT(VARCHAR, GETDATE(), 120);
PRINT '========================================';

-- ============================================================================
-- STEP 1: Rename Identities → Objects (accounts)
-- This must happen FIRST to free up the "Identities" name
-- ============================================================================

PRINT '';
PRINT 'STEP 1: Renaming Identities → Objects (accounts)...';

-- Rename the table
IF OBJECT_ID('dbo.Identities', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: Identities → Objects';
    EXEC sp_rename 'dbo.Identities', 'Objects';

    -- Rename primary key constraint
    PRINT '  - Renaming primary key: PK_Identities → PK_Objects';
    EXEC sp_rename 'dbo.PK_Identities', 'PK_Objects', 'OBJECT';

    -- Rename indexes
    PRINT '  - Renaming index: IX_Identities_Email → IX_Objects_Email';
    IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Identities_Email')
        EXEC sp_rename 'dbo.Objects.IX_Identities_Email', 'IX_Objects_Email', 'INDEX';

    PRINT '  - Renaming index: IX_Identities_Username → IX_Objects_Username';
    IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Identities_Username')
        EXEC sp_rename 'dbo.Objects.IX_Identities_Username', 'IX_Objects_Username', 'INDEX';

    PRINT '  - Renaming index: IX_Identities_SourceUnique → IX_Objects_SourceUnique';
    IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Identities_SourceUnique')
        EXEC sp_rename 'dbo.Objects.IX_Identities_SourceUnique', 'IX_Objects_SourceUnique', 'INDEX';

    PRINT '  - Renaming index: IX_Identities_PersonId → IX_Objects_PersonId (temp name)';
    IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Identities_PersonId')
        EXEC sp_rename 'dbo.Objects.IX_Identities_PersonId', 'IX_Objects_PersonId', 'INDEX';

    PRINT '  ✓ Table Identities → Objects renamed successfully';
END
ELSE
    PRINT '  - Table dbo.Identities not found (may already be renamed)';

-- ============================================================================
-- STEP 2: Rename Persons → Identities (people)
-- Now safe because we freed up the "Identities" name
-- ============================================================================

PRINT '';
PRINT 'STEP 2: Renaming Persons → Identities (people)...';

IF OBJECT_ID('dbo.Persons', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: Persons → Identities';
    EXEC sp_rename 'dbo.Persons', 'Identities';

    -- Rename primary key constraint
    PRINT '  - Renaming primary key: PK_Persons → PK_Identities';
    EXEC sp_rename 'dbo.PK_Persons', 'PK_Identities', 'OBJECT';

    -- Rename indexes if they exist
    PRINT '  - Renaming index: IX_Persons_ManagerId → IX_Identities_ManagerId';
    IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Persons_ManagerId')
        EXEC sp_rename 'dbo.Identities.IX_Persons_ManagerId', 'IX_Identities_ManagerId', 'INDEX';

    PRINT '  ✓ Table Persons → Identities renamed successfully';
END
ELSE
    PRINT '  - Table dbo.Persons not found (may already be renamed)';

-- ============================================================================
-- STEP 3: Rename related tables
-- ============================================================================

PRINT '';
PRINT 'STEP 3: Renaming related tables...';

-- Rename: IdentityAttributes → ObjectAttributes
IF OBJECT_ID('dbo.IdentityAttributes', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: IdentityAttributes → ObjectAttributes';
    EXEC sp_rename 'dbo.IdentityAttributes', 'ObjectAttributes';

    PRINT '  - Renaming primary key: PK_IdentityAttributes → PK_ObjectAttributes';
    EXEC sp_rename 'dbo.PK_IdentityAttributes', 'PK_ObjectAttributes', 'OBJECT';

    PRINT '  - Renaming index: IX_IdentityAttributes_IdentityId → IX_ObjectAttributes_IdentityId (temp)';
    IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdentityAttributes_IdentityId')
        EXEC sp_rename 'dbo.ObjectAttributes.IX_IdentityAttributes_IdentityId', 'IX_ObjectAttributes_IdentityId', 'INDEX';

    PRINT '  ✓ Table IdentityAttributes → ObjectAttributes renamed';
END

-- Rename: PersonGroupMemberships → IdentityGroupMemberships
IF OBJECT_ID('dbo.PersonGroupMemberships', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: PersonGroupMemberships → IdentityGroupMemberships';
    EXEC sp_rename 'dbo.PersonGroupMemberships', 'IdentityGroupMemberships';

    PRINT '  - Renaming primary key: PK_PersonGroupMemberships → PK_IdentityGroupMemberships';
    EXEC sp_rename 'dbo.PK_PersonGroupMemberships', 'PK_IdentityGroupMemberships', 'OBJECT';

    PRINT '  ✓ Table PersonGroupMemberships → IdentityGroupMemberships renamed';
END

-- Rename: IdentityGroupMemberships → ObjectGroupMemberships (the OLD table)
IF OBJECT_ID('dbo.IdentityGroupMemberships', 'U') IS NOT NULL
    AND NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PersonGroupMemberships')
BEGIN
    -- Only rename if PersonGroupMemberships was already renamed
    PRINT '  - Renaming table: IdentityGroupMemberships → ObjectGroupMemberships';
    EXEC sp_rename 'dbo.IdentityGroupMemberships', 'ObjectGroupMemberships';

    PRINT '  - Renaming primary key: PK_IdentityGroupMemberships → PK_ObjectGroupMemberships';
    EXEC sp_rename 'dbo.PK_IdentityGroupMemberships', 'PK_ObjectGroupMemberships', 'OBJECT';

    PRINT '  ✓ Table IdentityGroupMemberships → ObjectGroupMemberships renamed';
END

-- Rename: PersonMatchLogs → IdentityMatchLogs
IF OBJECT_ID('dbo.PersonMatchLogs', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: PersonMatchLogs → IdentityMatchLogs';
    EXEC sp_rename 'dbo.PersonMatchLogs', 'IdentityMatchLogs';

    PRINT '  - Renaming primary key: PK_PersonMatchLogs → PK_IdentityMatchLogs';
    EXEC sp_rename 'dbo.PK_PersonMatchLogs', 'PK_IdentityMatchLogs', 'OBJECT';

    PRINT '  ✓ Table PersonMatchLogs → IdentityMatchLogs renamed';
END

-- ============================================================================
-- STEP 4: Rename foreign key columns
-- ============================================================================

PRINT '';
PRINT 'STEP 4: Renaming foreign key columns...';

-- In Objects table: PersonId → IdentityId
IF OBJECT_ID('dbo.Objects', 'U') IS NOT NULL
    AND EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Objects') AND name = 'PersonId')
BEGIN
    PRINT '  - Renaming column in Objects: PersonId → IdentityId';
    EXEC sp_rename 'dbo.Objects.PersonId', 'IdentityId', 'COLUMN';

    -- Rename the index
    PRINT '  - Renaming index: IX_Objects_PersonId → IX_Objects_IdentityId';
    IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Objects_PersonId')
        EXEC sp_rename 'dbo.Objects.IX_Objects_PersonId', 'IX_Objects_IdentityId', 'INDEX';
END

-- In ObjectAttributes table: IdentityId → ObjectId
IF OBJECT_ID('dbo.ObjectAttributes', 'U') IS NOT NULL
    AND EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ObjectAttributes') AND name = 'IdentityId')
BEGIN
    PRINT '  - Renaming column in ObjectAttributes: IdentityId → ObjectId';
    EXEC sp_rename 'dbo.ObjectAttributes.IdentityId', 'ObjectId', 'COLUMN';

    -- Rename the index
    PRINT '  - Renaming index: IX_ObjectAttributes_IdentityId → IX_ObjectAttributes_ObjectId';
    IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ObjectAttributes_IdentityId')
        EXEC sp_rename 'dbo.ObjectAttributes.IX_ObjectAttributes_IdentityId', 'IX_ObjectAttributes_ObjectId', 'INDEX';
END

-- In SyncAuditLogs: IdentityId → ObjectId, IdentityDisplayName → ObjectDisplayName
IF OBJECT_ID('dbo.SyncAuditLogs', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SyncAuditLogs') AND name = 'IdentityId')
    BEGIN
        PRINT '  - Renaming column in SyncAuditLogs: IdentityId → ObjectId';
        EXEC sp_rename 'dbo.SyncAuditLogs.IdentityId', 'ObjectId', 'COLUMN';

        -- Rename the index
        IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SyncAuditLogs_IdentityId')
        BEGIN
            PRINT '  - Renaming index: IX_SyncAuditLogs_IdentityId → IX_SyncAuditLogs_ObjectId';
            EXEC sp_rename 'dbo.SyncAuditLogs.IX_SyncAuditLogs_IdentityId', 'IX_SyncAuditLogs_ObjectId', 'INDEX';
        END
    END

    IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SyncAuditLogs') AND name = 'IdentityDisplayName')
    BEGIN
        PRINT '  - Renaming column in SyncAuditLogs: IdentityDisplayName → ObjectDisplayName';
        EXEC sp_rename 'dbo.SyncAuditLogs.IdentityDisplayName', 'ObjectDisplayName', 'COLUMN';
    END
END

-- ============================================================================
-- STEP 5: Drop and recreate foreign key constraints
-- (Required because table names changed)
-- ============================================================================

PRINT '';
PRINT 'STEP 5: Updating foreign key constraints...';

-- Drop old FKs referencing old table names
DECLARE @sql NVARCHAR(MAX);
DECLARE @constraint_name NVARCHAR(256);

-- Find and drop FK constraints that reference old table names
DECLARE fk_cursor CURSOR FOR
    SELECT name
    FROM sys.foreign_keys
    WHERE name LIKE '%_Identities_%' OR name LIKE '%_Persons_%'
        OR name LIKE '%_IdentityAttributes_%' OR name LIKE '%_PersonGroupMemberships_%';

OPEN fk_cursor;
FETCH NEXT FROM fk_cursor INTO @constraint_name;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = 'ALTER TABLE ' + OBJECT_SCHEMA_NAME(parent_object_id) + '.' + OBJECT_NAME(parent_object_id) +
               ' DROP CONSTRAINT ' + @constraint_name;
    PRINT '  - Dropping FK: ' + @constraint_name;
    EXEC sp_executesql @sql;
    FETCH NEXT FROM fk_cursor INTO @constraint_name;
END;

CLOSE fk_cursor;
DEALLOCATE fk_cursor;

PRINT '  NOTE: Foreign key constraints dropped. They will be recreated by EF Core on next migration.';

-- ============================================================================
-- COMPLETION
-- ============================================================================

PRINT '';
PRINT '========================================';
PRINT 'Migration completed successfully!';
PRINT 'Ended: ' + CONVERT(VARCHAR, GETDATE(), 120);
PRINT '========================================';
PRINT '';
PRINT 'Tables renamed:';
PRINT '  ✓ Identities → Objects (accounts)';
PRINT '  ✓ Persons → Identities (people)';
PRINT '  ✓ IdentityAttributes → ObjectAttributes';
PRINT '  ✓ PersonGroupMemberships → IdentityGroupMemberships';
PRINT '  ✓ IdentityGroupMemberships → ObjectGroupMemberships';
PRINT '  ✓ PersonMatchLogs → IdentityMatchLogs';
PRINT '';
PRINT 'Columns renamed:';
PRINT '  ✓ Objects.PersonId → IdentityId';
PRINT '  ✓ ObjectAttributes.IdentityId → ObjectId';
PRINT '  ✓ SyncAuditLogs.IdentityId → ObjectId';
PRINT '  ✓ SyncAuditLogs.IdentityDisplayName → ObjectDisplayName';
PRINT '';
PRINT 'NEXT STEPS:';
PRINT '  1. Review the changes in database';
PRINT '  2. Run application to verify functionality';
PRINT '  3. Add migration record to __EFMigrationsHistory';
PRINT '';

COMMIT TRANSACTION RefactorNaming;

PRINT 'Transaction committed. Migration complete.';
