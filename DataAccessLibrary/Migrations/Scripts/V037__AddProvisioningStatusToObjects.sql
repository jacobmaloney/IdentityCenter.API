-- V037: Add ProvisioningStatus column to Objects table
-- Tracks the provisioning lifecycle for accounts created via Process Center.
-- Values: NULL (not applicable), 'Pending', 'Provisioned', 'Failed', 'Linked'

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Objects') AND name = 'ProvisioningStatus'
)
BEGIN
    ALTER TABLE Objects ADD ProvisioningStatus NVARCHAR(50) NULL;
END
GO
