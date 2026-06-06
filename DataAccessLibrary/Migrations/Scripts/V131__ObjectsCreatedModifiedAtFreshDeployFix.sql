-- V131: Fresh-deploy fix -- ensure Objects has CreatedAt and ModifiedAt.
--
-- OWED GAP (per Jacob): the ObjectsController and the bulk-upsert path read and
-- write Objects.CreatedAt / Objects.ModifiedAt (e.g. the SELECT projection, the
-- INSERT column list, and the tombstone/revive UPDATEs all reference them). But
-- the V004 CREATE TABLE [Objects] never defined those columns -- it shipped
-- FirstSyncedAt / LastSyncedAt / LastSeenAt / DeletedAt instead. On the existing
-- lab boxes the columns were added out-of-band (the .56 box was data-patched by
-- hand), which is why this was never caught at runtime there. A genuinely FRESH
-- deploy, however, would have Objects WITHOUT CreatedAt/ModifiedAt and the
-- controller's queries against those columns would fail.
--
-- Root reason it slipped through: V003 (which historically touched Objects
-- columns) self-skips on a database where Objects does not yet exist, and V004
-- then created Objects without these two columns. So no migration ever owned
-- adding them. This migration owns it -- in CODE -- so every future fresh deploy
-- has the columns the running code already assumes.
--
-- WHAT THIS DOES:
--   1. Add CreatedAt  (datetime2 NULL) if missing, DEFAULT GETUTCDATE() for new rows.
--   2. Add ModifiedAt (datetime2 NULL) if missing. Nullable to match how the rest
--      of the schema declares ModifiedAt and how the controller treats it
--      (set on update, read as nullable).
--   3. Backfill CreatedAt  from FirstSyncedAt (the original create timestamp).
--   4. Backfill ModifiedAt from LastSyncedAt  (the last-touch timestamp).
--
-- IDEMPOTENT: every ADD is guarded by sys.columns absence + the named default
-- constraint by sys.default_constraints; backfills only fill rows still NULL, so
-- a re-run is a no-op. DUAL-RUN SAFE: IC-only table; inert for Conduit.

SET NOCOUNT ON;
GO

-- ---------------------------------------------------------------------
-- 1. CreatedAt (NULL, defaulted GETUTCDATE() so future raw INSERTs that omit
--    it still land a value -- same safety-net pattern as V128).
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Objects')
   AND NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'CreatedAt')
BEGIN
    ALTER TABLE [Objects] ADD [CreatedAt] datetime2 NULL
        CONSTRAINT [DF_Objects_CreatedAt] DEFAULT (GETUTCDATE());
    PRINT 'V131: Added Objects.CreatedAt (datetime2 NULL DEFAULT GETUTCDATE()).';
END
ELSE
BEGIN
    PRINT 'V131: Objects.CreatedAt already present or Objects table missing -- skipped.';
END;
GO

-- ---------------------------------------------------------------------
-- 2. ModifiedAt (NULL).
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Objects')
   AND NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'ModifiedAt')
BEGIN
    ALTER TABLE [Objects] ADD [ModifiedAt] datetime2 NULL;
    PRINT 'V131: Added Objects.ModifiedAt (datetime2 NULL).';
END
ELSE
BEGIN
    PRINT 'V131: Objects.ModifiedAt already present or Objects table missing -- skipped.';
END;
GO

-- ---------------------------------------------------------------------
-- 3. Backfill CreatedAt from FirstSyncedAt where still NULL.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'CreatedAt')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'FirstSyncedAt')
BEGIN
    UPDATE [Objects]
       SET [CreatedAt] = [FirstSyncedAt]
     WHERE [CreatedAt] IS NULL;
    PRINT 'V131: Backfilled Objects.CreatedAt from FirstSyncedAt (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' row(s)).';
END;
GO

-- ---------------------------------------------------------------------
-- 4. Backfill ModifiedAt from LastSyncedAt where still NULL.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'ModifiedAt')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'LastSyncedAt')
BEGIN
    UPDATE [Objects]
       SET [ModifiedAt] = [LastSyncedAt]
     WHERE [ModifiedAt] IS NULL;
    PRINT 'V131: Backfilled Objects.ModifiedAt from LastSyncedAt (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' row(s)).';
END;
GO
