-- V087: Credential profiles fully wired to SqlServerInventory
-- Enables reusable named credentials (like Quest ConnectionManager profiles)
-- and persistent Windows credential rescans

-- 1. Link SqlServerInventory to a credential profile
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'CredentialId')
BEGIN
    ALTER TABLE SqlServerInventory ADD CredentialId UNIQUEIDENTIFIER NULL;
    PRINT 'V087: Added CredentialId to SqlServerInventory';
END
GO

-- 2. Add LastScanStatus columns for bulk-scan progress tracking
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'LastScanStatus')
BEGIN
    ALTER TABLE SqlServerInventory ADD LastScanStatus NVARCHAR(50) NULL;
    ALTER TABLE SqlServerInventory ADD LastScanMessage NVARCHAR(1000) NULL;
    ALTER TABLE SqlServerInventory ADD LastScanDurationMs INT NULL;
    PRINT 'V087: Added scan status tracking columns';
END
GO

-- 3. Create index on CredentialId (must be after the ALTER batch completes)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SqlServerInventory_CredentialId' AND object_id = OBJECT_ID('SqlServerInventory'))
BEGIN
    DECLARE @sql NVARCHAR(MAX) = N'CREATE INDEX IX_SqlServerInventory_CredentialId ON SqlServerInventory (CredentialId) WHERE CredentialId IS NOT NULL';
    EXEC sp_executesql @sql;
    PRINT 'V087: Added index on SqlServerInventory.CredentialId';
END
GO
