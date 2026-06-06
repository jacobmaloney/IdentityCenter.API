-- V094: Add notification recipient fields to LicensePools
-- Allows pools to notify any Object (user, group, distribution list) on breach.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'BreachNotifyObjectId')
    ALTER TABLE LicensePools ADD BreachNotifyObjectId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'BreachNotifyObjectName')
    ALTER TABLE LicensePools ADD BreachNotifyObjectName NVARCHAR(256) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'BreachNotifyObjectClass')
    ALTER TABLE LicensePools ADD BreachNotifyObjectClass NVARCHAR(50) NULL;
GO
