-- V086: Store encrypted connection string on SqlServerInventory for persistent rescans
-- One-click rescan: IC reads the encrypted connection string and reconnects automatically

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'EncryptedConnectionString')
BEGIN
    ALTER TABLE SqlServerInventory ADD EncryptedConnectionString NVARCHAR(MAX) NULL;
    PRINT 'V086: Added EncryptedConnectionString column to SqlServerInventory';
END
ELSE
BEGIN
    PRINT 'V086: EncryptedConnectionString already exists - skipping';
END
