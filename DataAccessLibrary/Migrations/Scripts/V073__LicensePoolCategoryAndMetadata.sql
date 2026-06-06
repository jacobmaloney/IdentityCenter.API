-- V073: Extend LicensePools with category FK and lifecycle metadata
-- LicenseCategoryId: links pool to a user-defined category (from V071)
-- AutoCreatedFromSync: distinguishes auto-generated pools from manual entries
-- ReviewFrequencyDays: how often this pool should get an access review campaign
-- LastReviewedAt: when the last review campaign completed for this pool

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'LicenseCategoryId')
BEGIN
    ALTER TABLE LicensePools ADD LicenseCategoryId UNIQUEIDENTIFIER NULL;
    PRINT 'V073: Added LicenseCategoryId to LicensePools';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'AutoCreatedFromSync')
BEGIN
    ALTER TABLE LicensePools ADD AutoCreatedFromSync BIT NOT NULL DEFAULT 0;
    PRINT 'V073: Added AutoCreatedFromSync to LicensePools';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'ReviewFrequencyDays')
BEGIN
    ALTER TABLE LicensePools ADD ReviewFrequencyDays INT NULL;
    PRINT 'V073: Added ReviewFrequencyDays to LicensePools';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'LastReviewedAt')
BEGIN
    ALTER TABLE LicensePools ADD LastReviewedAt DATETIME2 NULL;
    PRINT 'V073: Added LastReviewedAt to LicensePools';
END
GO

-- Add FK after column creation
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseCategories')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'LicenseCategoryId')
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_LicensePools_LicenseCategory')
BEGIN
    ALTER TABLE LicensePools
        ADD CONSTRAINT FK_LicensePools_LicenseCategory
        FOREIGN KEY (LicenseCategoryId) REFERENCES LicenseCategories(Id) ON DELETE SET NULL;
    PRINT 'V073: Added FK LicensePools -> LicenseCategories';
END
GO

-- Auto-assign existing pools to categories based on SkuPartNumber / LicenseType
-- M365 pools -> M365 Subscriptions
-- CAL pools -> Windows CALs
-- Everything else -> Uncategorized
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseCategories')
BEGIN
    UPDATE LicensePools
    SET LicenseCategoryId = 'C0710000-0000-0000-0000-000000000001' -- M365 Subscriptions
    WHERE LicenseCategoryId IS NULL
      AND (SkuPartNumber LIKE '%E3%' OR SkuPartNumber LIKE '%E5%'
           OR SkuPartNumber LIKE 'O365%' OR SkuPartNumber LIKE 'SPE%'
           OR SkuPartNumber LIKE '%BUSINESS%' OR SkuPartNumber LIKE '%FLOW%');

    UPDATE LicensePools
    SET LicenseCategoryId = 'C0710000-0000-0000-0000-000000000002' -- Windows CALs
    WHERE LicenseCategoryId IS NULL
      AND LicenseType IN ('UserCAL', 'DeviceCAL');

    UPDATE LicensePools
    SET LicenseCategoryId = 'C0710000-0000-0000-0000-000000000006' -- Uncategorized
    WHERE LicenseCategoryId IS NULL;

    PRINT 'V073: Auto-assigned existing pools to categories';
END
GO

-- Index for category filtering
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LicensePools_Category' AND object_id = OBJECT_ID('LicensePools'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LicensePools_Category]
        ON [LicensePools] ([LicenseCategoryId], [IsActive]);
    PRINT 'V073: Added IX_LicensePools_Category index';
END
GO
