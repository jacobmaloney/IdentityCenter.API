-- V130: ARS-style deferred-deletion lifecycle for Objects.
--
-- BACKGROUND: Phase 2.2 (commit f1650dd6) gave Objects a reversible soft-delete:
-- the tombstone endpoint stamps DeletedAt when Conduit reports an object absent
-- from a COMPLETE source read, and the bulk-upsert revive path clears DeletedAt
-- when the object reappears. This migration adds the EXPLICIT lifecycle-state
-- field that turns that soft-delete into a true deprovision -> retention-window
-- -> hard-purge lifecycle, modeled on One Identity Active Roles deferred deletion.
--
--   LifecycleState 0 = Active        (DeletedAt IS NULL)
--   LifecycleState 1 = Deprovisioned (DeletedAt = the moment it disappeared;
--                                      DeletedAt is the retention clock)
--
-- An object revived within the retention window goes back to 0 (handled in the
-- bulk-upsert revive path). An object still at state 1 past the window is
-- HARD-PURGED by the daily ObjectDeprovisionPurgeJob (V131 ships the job; this
-- migration ships only the column + the global retention setting).
--
-- IDEMPOTENT: column add is guarded by sys.columns; the default constraint is
-- guarded by sys.default_constraints; the backfill is naturally idempotent
-- (re-running sets the same value). Each statement is its own GO batch with no
-- wrapping transaction, matching the V127/V128 pattern and the migration runner
-- (DatabaseMigrationService splits on GO and runs each batch independently).
--
-- DUAL-RUN SAFE: touches only the IdentityCenter Objects table. Conduit has no
-- Objects table and never runs IC migrations, so this is inert there.

SET NOCOUNT ON;
GO

-- ---------------------------------------------------------------------
-- 1. Add Objects.LifecycleState (INT NOT NULL DEFAULT 0 = Active).
--    Guard the ADD on column absence and on table presence so a database
--    where Objects does not yet exist is skipped gracefully (it never is
--    past V004, but the guard keeps the script defensive + re-runnable).
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Objects')
   AND NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'LifecycleState')
BEGIN
    ALTER TABLE [Objects] ADD [LifecycleState] INT NOT NULL
        CONSTRAINT [DF_Objects_LifecycleState] DEFAULT (0);
    PRINT 'V130: Added Objects.LifecycleState (INT NOT NULL DEFAULT 0 = Active).';
END
ELSE
BEGIN
    PRINT 'V130: Objects.LifecycleState already present or Objects table missing -- skipped.';
END;
GO

-- ---------------------------------------------------------------------
-- 2. Backfill: any row already soft-deleted (DeletedAt IS NOT NULL) is, by
--    definition, deprovisioned -> LifecycleState = 1. Rows with DeletedAt
--    NULL keep the column default of 0 (Active). Idempotent: after the first
--    run no Active row has a non-null DeletedAt mismatch to correct.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'LifecycleState')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'DeletedAt')
BEGIN
    UPDATE [Objects]
       SET [LifecycleState] = 1
     WHERE [DeletedAt] IS NOT NULL
       AND [LifecycleState] <> 1;
    PRINT 'V130: Backfilled LifecycleState=1 for already soft-deleted rows (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' row(s)).';
END;
GO

-- ---------------------------------------------------------------------
-- 3. Filtered index to make the daily purge sweep cheap: it scans only
--    deprovisioned rows. Guarded on index absence + column presence.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'LifecycleState')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_Objects_Lifecycle_Purge' AND object_id = OBJECT_ID(N'dbo.Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Objects_Lifecycle_Purge]
        ON [Objects] ([LifecycleState], [DeletedAt])
        WHERE [LifecycleState] = 1;
    PRINT 'V130: Created filtered index IX_Objects_Lifecycle_Purge.';
END;
GO

-- ---------------------------------------------------------------------
-- 4. Global retention setting: ObjectDeprovisionRetentionDays (default 30),
--    in the Settings key-value table under category 'Lifecycle'. The purge
--    job reads this at runtime. Insert only if absent so an operator-edited
--    value is never clobbered on re-run.
--
--    Settings shape (verified against V004): Id INT IDENTITY (do NOT supply),
--    Category NOT NULL, [Key] NOT NULL, Value NVARCHAR(MAX) NOT NULL,
--    IsEncrypted BIT NOT NULL, DataType NVARCHAR(50) NULL, ModifiedAt
--    DATETIME2 NOT NULL, ModifiedBy NVARCHAR(256) NULL. There is NO CreatedAt.
--    Columns + values here mirror exactly what UpsertSettingAsync writes.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Settings') AND name = N'Category')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Settings') AND name = N'Key')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'ObjectDeprovisionRetentionDays')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'ObjectDeprovisionRetentionDays', N'30', N'int', 0, GETUTCDATE(), N'System');
    PRINT 'V130: Seeded Settings ObjectDeprovisionRetentionDays = 30 (Lifecycle).';
END
ELSE
BEGIN
    PRINT 'V130: ObjectDeprovisionRetentionDays already present or Settings table missing -- skipped.';
END;
GO
