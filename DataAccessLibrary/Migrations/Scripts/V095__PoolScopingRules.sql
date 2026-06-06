-- V095: Pool scoping rules — filter by tag, OU, department
-- Allows pools to count objects matching specific criteria beyond just ObjectClass.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'AutoCountTagId')
    ALTER TABLE LicensePools ADD AutoCountTagId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'AutoCountOUFilter')
    ALTER TABLE LicensePools ADD AutoCountOUFilter NVARCHAR(500) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'AutoCountDepartment')
    ALTER TABLE LicensePools ADD AutoCountDepartment NVARCHAR(200) NULL;
GO
