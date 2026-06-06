-- V090: Add auto-count pool support to LicensePools
-- Enables AD CAL tracking by auto-counting Objects matching a filter

-- PoolType: Synced (Entra Graph API), Manual (SQL entitlements), AutoCount (computed from Objects table)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'PoolType')
BEGIN
    ALTER TABLE LicensePools ADD PoolType NVARCHAR(20) NOT NULL DEFAULT 'Synced';
    PRINT 'V090: Added PoolType to LicensePools';
END
GO

-- AutoCount configuration: which ObjectClass + ConnectionId to count
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'AutoCountObjectClass')
BEGIN
    ALTER TABLE LicensePools ADD AutoCountObjectClass NVARCHAR(100) NULL;
    ALTER TABLE LicensePools ADD AutoCountConnectionId UNIQUEIDENTIFIER NULL;
    ALTER TABLE LicensePools ADD AutoCountFilter NVARCHAR(500) NULL;  -- Optional extra SQL WHERE clause
    ALTER TABLE LicensePools ADD LastAutoCountAt DATETIME2 NULL;
    PRINT 'V090: Added AutoCount columns to LicensePools';
END
GO

-- Set existing pools to their correct type
UPDATE LicensePools SET PoolType = 'Synced' WHERE PoolType = 'Synced' OR PoolType = '';
GO
