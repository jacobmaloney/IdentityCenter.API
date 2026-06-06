-- V117: License pools — support multiple tags per pool with OR semantics.
-- Adds AutoCountTagIds (CSV of GUIDs) alongside the legacy AutoCountTagId column.
-- Backfill: copy any existing AutoCountTagId into the new CSV column.
-- Legacy column is left in place for back-compat readers; writers should populate both.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'AutoCountTagIds' AND Object_ID = Object_ID(N'LicensePools'))
BEGIN
    ALTER TABLE LicensePools ADD AutoCountTagIds NVARCHAR(MAX) NULL;
END
GO

-- Backfill rows that have AutoCountTagId set but AutoCountTagIds empty.
UPDATE LicensePools
SET    AutoCountTagIds = CONVERT(NVARCHAR(36), AutoCountTagId)
WHERE  AutoCountTagId IS NOT NULL
  AND  (AutoCountTagIds IS NULL OR LTRIM(RTRIM(AutoCountTagIds)) = '');
GO
