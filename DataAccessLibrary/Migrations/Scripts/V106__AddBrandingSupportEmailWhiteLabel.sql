-- V106: Add SupportEmail + WhiteLabelMode to BrandingSettings
--
-- NOTE: As of V105 there is no `BrandingSettings` table — branding is stored as
-- a single JSON blob in [Settings] under (Category='Branding', Key='BrandingJson')
-- via DataAccessLibrary.Services.BrandingService. The new fields are added to
-- the BrandingSettings model and will be persisted automatically through the
-- existing JSON serialization path; no schema change is required for them to
-- function.
--
-- This migration is kept as a defensive no-op so:
--   1. The migration sequence stays gap-free at V106.
--   2. If a future migration ever materializes the JSON into columns, this
--      script will pick up the missing columns without breaking replay.

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BrandingSettings')
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'BrandingSettings') AND name = N'SupportEmail'
    )
    BEGIN
        ALTER TABLE [BrandingSettings] ADD [SupportEmail] NVARCHAR(200) NULL;
        PRINT 'V106: Added BrandingSettings.SupportEmail column';
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'BrandingSettings') AND name = N'WhiteLabelMode'
    )
    BEGIN
        ALTER TABLE [BrandingSettings] ADD [WhiteLabelMode] BIT NOT NULL CONSTRAINT [DF_BrandingSettings_WhiteLabelMode] DEFAULT 0;
        PRINT 'V106: Added BrandingSettings.WhiteLabelMode column';
    END
END
ELSE
BEGIN
    PRINT 'V106: BrandingSettings table not present — branding is JSON in [Settings]; no schema change needed.';
END
GO
