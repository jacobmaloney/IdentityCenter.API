-- V126: Add Objects.OriginalSource for inbound rows that were projected through
-- Conduit. When IC is the sink of a Conduit sync run, every row lands with
-- SourceType='Conduit' (the connector that called /api/objects/bulk). That
-- erases the upstream origin — was it ActiveDirectory? EntraID? Okta? Reports
-- and policy evaluation need to know. OriginalSource carries the upstream
-- system-type string ("ActiveDirectory", "EntraID", ...) end-to-end.
--
-- Pre-Conduit rows where SourceType is already the upstream system get
-- backfilled so reports don't show NULL for the historical set. Rows where
-- SourceType='Conduit' (synthesized post-Phase 2 item 8) are left NULL for the
-- bulk writer to populate on next sync pass.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'OriginalSource')
BEGIN
    ALTER TABLE [Objects] ADD [OriginalSource] NVARCHAR(100) NULL;
END;
GO

UPDATE [Objects]
SET [OriginalSource] = [SourceType]
WHERE [OriginalSource] IS NULL
  AND [SourceType] IS NOT NULL
  AND [SourceType] <> 'Conduit';
GO

PRINT 'V126: Objects.OriginalSource added + backfilled from SourceType';
