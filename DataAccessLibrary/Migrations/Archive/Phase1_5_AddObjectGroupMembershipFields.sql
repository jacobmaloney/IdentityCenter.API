-- Phase 1.5: Add Access Review and Justification Tracking fields to IdentityGroupMemberships
-- UC-GRP-01-03: Manage Group Members with Justification
-- UC-GRP-01-04: Conduct Access Review

-- Add IsActive field (soft delete)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'IdentityGroupMemberships' AND COLUMN_NAME = 'IsActive')
BEGIN
    ALTER TABLE [IdentityGroupMemberships]
    ADD [IsActive] bit NOT NULL DEFAULT 1;
    PRINT 'Added IsActive column to IdentityGroupMemberships';
END
ELSE
BEGIN
    PRINT 'IsActive column already exists in IdentityGroupMemberships';
END
GO

-- Add AddedBy field (audit trail)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'IdentityGroupMemberships' AND COLUMN_NAME = 'AddedBy')
BEGIN
    ALTER TABLE [IdentityGroupMemberships]
    ADD [AddedBy] nvarchar(256) NULL;
    PRINT 'Added AddedBy column to IdentityGroupMemberships';
END
ELSE
BEGIN
    PRINT 'AddedBy column already exists in IdentityGroupMemberships';
END
GO

-- Add Justification field (required for sensitive groups)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'IdentityGroupMemberships' AND COLUMN_NAME = 'Justification')
BEGIN
    ALTER TABLE [IdentityGroupMemberships]
    ADD [Justification] nvarchar(max) NULL;
    PRINT 'Added Justification column to IdentityGroupMemberships';
END
ELSE
BEGIN
    PRINT 'Justification column already exists in IdentityGroupMemberships';
END
GO

-- Add ExpirationDate field (temporary access)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'IdentityGroupMemberships' AND COLUMN_NAME = 'ExpirationDate')
BEGIN
    ALTER TABLE [IdentityGroupMemberships]
    ADD [ExpirationDate] datetime2 NULL;
    PRINT 'Added ExpirationDate column to IdentityGroupMemberships';
END
ELSE
BEGIN
    PRINT 'ExpirationDate column already exists in IdentityGroupMemberships';
END
GO

-- Add RemovedBy field (audit trail for removals)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'IdentityGroupMemberships' AND COLUMN_NAME = 'RemovedBy')
BEGIN
    ALTER TABLE [IdentityGroupMemberships]
    ADD [RemovedBy] nvarchar(256) NULL;
    PRINT 'Added RemovedBy column to IdentityGroupMemberships';
END
ELSE
BEGIN
    PRINT 'RemovedBy column already exists in IdentityGroupMemberships';
END
GO

-- Add RemovalReason field (compliance tracking)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'IdentityGroupMemberships' AND COLUMN_NAME = 'RemovalReason')
BEGIN
    ALTER TABLE [IdentityGroupMemberships]
    ADD [RemovalReason] nvarchar(max) NULL;
    PRINT 'Added RemovalReason column to IdentityGroupMemberships';
END
ELSE
BEGIN
    PRINT 'RemovalReason column already exists in IdentityGroupMemberships';
END
GO

-- Create indexes for performance
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityGroupMemberships_IsActive' AND object_id = OBJECT_ID('IdentityGroupMemberships'))
BEGIN
    CREATE INDEX IX_IdentityGroupMemberships_IsActive
    ON [IdentityGroupMemberships] ([IsActive]);
    PRINT 'Created IX_IdentityGroupMemberships_IsActive index';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityGroupMemberships_ExpirationDate' AND object_id = OBJECT_ID('IdentityGroupMemberships'))
BEGIN
    CREATE INDEX IX_IdentityGroupMemberships_ExpirationDate
    ON [IdentityGroupMemberships] ([ExpirationDate])
    WHERE [ExpirationDate] IS NOT NULL;
    PRINT 'Created IX_IdentityGroupMemberships_ExpirationDate filtered index';
END
GO

PRINT 'Phase 1.5 migration completed successfully';
