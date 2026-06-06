-- V062: Add BillingPeriod and LicenseType columns to LicensePools
-- BillingPeriod: Monthly, Annual, OneTime — affects cost calculations
-- LicenseType: UserCAL, DeviceCAL, ServerCAL, Subscription — controls auto-consumed counting

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'BillingPeriod')
    ALTER TABLE LicensePools ADD BillingPeriod NVARCHAR(20) NULL DEFAULT 'Monthly';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'LicenseType')
    ALTER TABLE LicensePools ADD LicenseType NVARCHAR(50) NULL;
GO

-- Default all existing Microsoft-synced pools to Monthly / Subscription
UPDATE LicensePools SET BillingPeriod = 'Monthly' WHERE BillingPeriod IS NULL AND SkuId NOT LIKE 'MANUAL-%';
UPDATE LicensePools SET LicenseType = 'Subscription' WHERE LicenseType IS NULL AND SkuId NOT LIKE 'MANUAL-%';
UPDATE LicensePools SET LicenseType = 'UserCAL' WHERE LicenseType IS NULL AND SkuId LIKE 'MANUAL-%';
GO

PRINT 'V062: LicensePool BillingPeriod and LicenseType columns added';
GO
