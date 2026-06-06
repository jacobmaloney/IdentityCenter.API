-- V088: Add DiscoveryStatus workflow state to SqlServerInventory
-- Enables the "discovered → approved → managed" onboarding workflow for network-discovered SQL servers
-- Values: Discovered, Approved, Managed, Ignored, Retired

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'DiscoveryStatus')
BEGIN
    ALTER TABLE SqlServerInventory ADD DiscoveryStatus NVARCHAR(50) NOT NULL DEFAULT 'Managed';
    PRINT 'V088: Added DiscoveryStatus to SqlServerInventory';
END
GO

-- Existing rows default to 'Managed' (they were added manually, so they're already approved)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'DiscoveryStatus')
BEGIN
    UPDATE SqlServerInventory SET DiscoveryStatus = 'Managed' WHERE DiscoveryStatus IS NULL OR DiscoveryStatus = '';
    PRINT 'V088: Set existing inventory rows to Managed';
END
GO

-- Index for filtering by status (common query pattern on the SQL Servers page)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SqlServerInventory_DiscoveryStatus' AND object_id = OBJECT_ID('SqlServerInventory'))
BEGIN
    DECLARE @sql NVARCHAR(MAX) = N'CREATE INDEX IX_SqlServerInventory_DiscoveryStatus ON SqlServerInventory (DiscoveryStatus)';
    EXEC sp_executesql @sql;
    PRINT 'V088: Added index on DiscoveryStatus';
END
GO
