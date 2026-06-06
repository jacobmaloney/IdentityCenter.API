-- V091: Extend LicenseComplianceViolations for multi-source compliance (AD, Entra, SQL)
-- Adds SourceType and LicensePoolId so violations can originate from any license source.

-- 1. Add SourceType column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicenseComplianceViolations') AND name = 'SourceType')
    ALTER TABLE LicenseComplianceViolations ADD SourceType NVARCHAR(50) NULL;
GO

-- 2. Add LicensePoolId column (FK to LicensePools for AD/Entra pool-level violations)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicenseComplianceViolations') AND name = 'LicensePoolId')
    ALTER TABLE LicenseComplianceViolations ADD LicensePoolId UNIQUEIDENTIFIER NULL;
GO

-- 3. Backfill existing rows as SQL source
UPDATE LicenseComplianceViolations SET SourceType = 'SQL' WHERE SourceType IS NULL AND SqlServerInventoryId IS NOT NULL;
GO

-- 4. Index for source-filtered violation queries
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LicenseComplianceViolations_SourceType')
    CREATE NONCLUSTERED INDEX IX_LicenseComplianceViolations_SourceType
    ON LicenseComplianceViolations (SourceType, IsResolved)
    INCLUDE (ViolationType, Severity, DetectedAt);
GO

-- 5. Index for pool-based violation lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LicenseComplianceViolations_LicensePoolId')
    CREATE NONCLUSTERED INDEX IX_LicenseComplianceViolations_LicensePoolId
    ON LicenseComplianceViolations (LicensePoolId)
    WHERE LicensePoolId IS NOT NULL AND IsResolved = 0;
GO
