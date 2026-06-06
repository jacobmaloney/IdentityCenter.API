-- ============================================================================
-- SAFE Manual Migration: Person/Identity Refactoring
-- Date: 2025-10-21
-- Purpose: Rename tables and columns to match new naming convention
--          Person → Identity (people)
--          Identity → IdentityObject (accounts)
-- ============================================================================

-- CRITICAL: This migration uses sp_rename to preserve all data
-- BACKUP YOUR DATABASE BEFORE RUNNING THIS MIGRATION!

BEGIN TRANSACTION;

PRINT '========================================';
PRINT 'Starting Person/Identity Refactoring Migration';
PRINT 'Date: ' + CONVERT(VARCHAR, GETDATE(), 120);
PRINT '========================================';

-- ============================================================================
-- STEP 1: Rename main entity tables
-- ============================================================================

PRINT '';
PRINT 'STEP 1: Renaming main entity tables...';

-- Rename: Persons → Identities (people)
IF OBJECT_ID('Persons', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: Persons → Identities';
    EXEC sp_rename 'Persons', 'Identities';
END
ELSE
    PRINT '  - Table Persons not found or already renamed';

-- Rename: Identities → Objects (accounts)
IF OBJECT_ID('Identities', 'U') IS NOT NULL AND OBJECT_ID('Objects', 'U') IS NULL
BEGIN
    PRINT '  - Renaming table: Identities → Objects';
    -- First, need to check if we just renamed Persons to Identities
    -- This creates a conflict, so we need to do this carefully
    -- For now, skip this step - will need to address the naming conflict
    PRINT '  - SKIPPED: Cannot rename Identities → Objects because Persons was just renamed to Identities';
    PRINT '  - Manual intervention required to resolve naming conflict';
END

-- ============================================================================
-- STEP 2: Rename attribute and membership tables
-- ============================================================================

PRINT '';
PRINT 'STEP 2: Renaming attribute and membership tables...';

-- Rename: IdentityAttributes → ObjectAttributes
IF OBJECT_ID('IdentityAttributes', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: IdentityAttributes → ObjectAttributes';
    EXEC sp_rename 'IdentityAttributes', 'ObjectAttributes';
END
ELSE
    PRINT '  - Table IdentityAttributes not found or already renamed';

-- Rename: PersonGroupMemberships → IdentityGroupMemberships
IF OBJECT_ID('PersonGroupMemberships', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: PersonGroupMemberships → IdentityGroupMemberships';
    EXEC sp_rename 'PersonGroupMemberships', 'IdentityGroupMemberships';
END
ELSE
    PRINT '  - Table PersonGroupMemberships not found or already renamed';

-- Rename: IdentityGroupMemberships → ObjectGroupMemberships
-- (Same naming conflict issue as above)
PRINT '  - NOTE: IdentityGroupMemberships → ObjectGroupMemberships rename requires manual intervention';

-- Rename: PersonMatchLogs → IdentityMatchLogs
IF OBJECT_ID('PersonMatchLogs', 'U') IS NOT NULL
BEGIN
    PRINT '  - Renaming table: PersonMatchLogs → IdentityMatchLogs';
    EXEC sp_rename 'PersonMatchLogs', 'IdentityMatchLogs';
END
ELSE
    PRINT '  - Table PersonMatchLogs not found or already renamed';

-- ============================================================================
-- CRITICAL ISSUE DETECTED
-- ============================================================================

PRINT '';
PRINT '========================================';
PRINT 'CRITICAL: NAMING CONFLICT DETECTED!';
PRINT '========================================';
PRINT '';
PRINT 'The refactoring has a naming conflict:';
PRINT '  - OLD: Persons table (people)';
PRINT '  - OLD: Identities table (accounts)';
PRINT '  - NEW: Identities table (people) <- CONFLICT with old Identities!';
PRINT '  - NEW: Objects table (accounts)';
PRINT '';
PRINT 'We cannot simply rename:';
PRINT '  1. Persons → Identities (would work)';
PRINT '  2. Identities → Objects (conflicts with step 1!)';
PRINT '';
PRINT 'SOLUTION: Use a 3-step rename process:';
PRINT '  1. Identities → Objects (rename accounts table first)';
PRINT '  2. Persons → Identities (then rename people table)';
PRINT '  3. Update all FK columns and indexes';
PRINT '';
PRINT '========================================';
PRINT 'ROLLING BACK - Manual intervention required';
PRINT '========================================';

ROLLBACK TRANSACTION;

PRINT '';
PRINT 'Migration rolled back. No changes were made.';
PRINT 'Please review the naming conflict and create a proper migration script.';
