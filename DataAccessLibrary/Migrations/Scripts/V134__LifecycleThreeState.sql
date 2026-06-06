-- V134: Reframe the deferred-deletion lifecycle to the ARS THREE-state model.
--
-- BACKGROUND: V130 (Objects) and V132 (Identities) shipped a TWO-state
-- LifecycleState: 0=Active, 1=Deprovisioned. Jacob (Active Roles expert) wants
-- IC's LifecycleState to match ARS edsva-deprovisionStatus, which is THREE states:
--
--   0 = Active        -- enabled / normal.
--   1 = Disabled      -- present but switched off; RETAINED INDEFINITELY, NEVER on
--                        the purge clock. DeletedAt IS NULL (no retention clock).
--                        Objects: the AD account-disable bit (UAC ACCOUNTDISABLE
--                        0x2) / IsActive=0 but still present in source.
--                        Identities: HR-inactive/suspended but NOT a terminated leaver.
--   2 = Deprovisioned -- marked for deferred deletion; DeletedAt = retention clock;
--                        revivable within the window; hard-purged after.
--                        Objects: the Conduit tombstone (gone from source).
--                        Identities: HR leaver / orphan-of-active-Objects.
--
-- THIS MIGRATION does four things, for BOTH Objects and Identities:
--
--   PART A (REMAP committed data): every row that currently means "Deprovisioned"
--     under the OLD two-state scheme is LifecycleState=1 AND has DeletedAt stamped
--     (the retention clock). Under the NEW scheme that is value 2. So:
--         UPDATE ... SET LifecycleState = 2 WHERE LifecycleState = 1 AND DeletedAt IS NOT NULL.
--     This is the data-correctness remap. It is naturally idempotent and CANNOT
--     double-apply: after it runs, no row matches (LifecycleState=1 AND DeletedAt
--     NOT NULL) any more -- the very rows it would touch have become 2. A second run
--     finds 0 rows. A Disabled(1) row (DeletedAt NULL, see PART C) is NOT matched by
--     this predicate, so the remap never turns a Disabled row into Deprovisioned.
--
--   PART B (rebuild the filtered purge indexes): V130/V132 created
--     IX_Objects_Lifecycle_Purge / IX_Identities_Lifecycle_Purge filtered on
--     LifecycleState = 1 (the OLD Deprovisioned value). The purge clock now lives on
--     value 2, so the indexes must be refiltered to WHERE LifecycleState = 2. Drop +
--     recreate (a filtered-index predicate cannot be altered in place).
--
--   PART C (backfill Disabled = 1 where UNAMBIGUOUS): seed the new middle state from
--     current disabled signals that are columnar and certain. See the FLAG comments
--     on each table for exactly what is backfilled vs. deferred to the running
--     sync/evaluation jobs (so the transition is audited rather than silently stamped).
--
-- IDEMPOTENT: the remap is self-extinguishing (PART A note). The index work is
-- guarded on sys.indexes + column presence. The Disabled backfills only touch rows
-- still at state 0 with the disabled signal, so a re-run is a no-op once they are 1.
-- Each statement is its own GO batch with no wrapping transaction, matching the
-- V127/V128/V130/V132 pattern and the GO-splitting migration runner.
--
-- DUAL-RUN SAFE: touches only IdentityCenter tables (Objects, Identities). Conduit
-- has neither table and never runs IC migrations -- inert there.

SET NOCOUNT ON;
GO

-- =====================================================================
-- PART A: REMAP old Deprovisioned (1 + DeletedAt) -> new Deprovisioned (2).
-- =====================================================================

-- A1. Objects.
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'LifecycleState')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'DeletedAt')
BEGIN
    UPDATE [Objects]
       SET [LifecycleState] = 2
     WHERE [LifecycleState] = 1
       AND [DeletedAt] IS NOT NULL;
    PRINT 'V134: Remapped Objects old-Deprovisioned (1+DeletedAt) -> 2 (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' row(s)).';
END
ELSE
BEGIN
    PRINT 'V134: Objects.LifecycleState/DeletedAt not present -- remap skipped.';
END;
GO

-- A2. Identities.
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'LifecycleState')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'DeletedAt')
BEGIN
    UPDATE [Identities]
       SET [LifecycleState] = 2
     WHERE [LifecycleState] = 1
       AND [DeletedAt] IS NOT NULL;
    PRINT 'V134: Remapped Identities old-Deprovisioned (1+DeletedAt) -> 2 (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' row(s)).';
END
ELSE
BEGIN
    PRINT 'V134: Identities.LifecycleState/DeletedAt not present -- remap skipped.';
END;
GO

-- =====================================================================
-- PART B: Rebuild the filtered purge indexes to target LifecycleState = 2.
-- =====================================================================

-- B1. Objects: drop the old (WHERE LifecycleState=1) index, recreate on =2.
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = N'IX_Objects_Lifecycle_Purge' AND object_id = OBJECT_ID(N'dbo.Objects'))
BEGIN
    DROP INDEX [IX_Objects_Lifecycle_Purge] ON [Objects];
    PRINT 'V134: Dropped old IX_Objects_Lifecycle_Purge (filtered on state 1).';
END;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'LifecycleState')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_Objects_Lifecycle_Purge' AND object_id = OBJECT_ID(N'dbo.Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Objects_Lifecycle_Purge]
        ON [Objects] ([LifecycleState], [DeletedAt])
        WHERE [LifecycleState] = 2;
    PRINT 'V134: Recreated IX_Objects_Lifecycle_Purge (filtered on state 2 = Deprovisioned).';
END;
GO

-- B2. Identities: drop the old (WHERE LifecycleState=1) index, recreate on =2.
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = N'IX_Identities_Lifecycle_Purge' AND object_id = OBJECT_ID(N'dbo.Identities'))
BEGIN
    DROP INDEX [IX_Identities_Lifecycle_Purge] ON [Identities];
    PRINT 'V134: Dropped old IX_Identities_Lifecycle_Purge (filtered on state 1).';
END;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'LifecycleState')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_Identities_Lifecycle_Purge' AND object_id = OBJECT_ID(N'dbo.Identities'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Identities_Lifecycle_Purge]
        ON [Identities] ([LifecycleState], [DeletedAt])
        WHERE [LifecycleState] = 2;
    PRINT 'V134: Recreated IX_Identities_Lifecycle_Purge (filtered on state 2 = Deprovisioned).';
END;
GO

-- =====================================================================
-- PART C: Backfill Disabled = 1 from UNAMBIGUOUS current disabled signals.
-- =====================================================================
--
-- FLAG (Objects -- BACKFILLED): an Objects row that is present-but-disabled is
--   IsActive = 0 AND DeletedAt IS NULL (not tombstoned). After PART A those rows are
--   still at LifecycleState = 0 (the remap only touched DeletedAt-stamped rows). They
--   are, by the 3-state definition, Disabled. This signal is columnar and certain, so
--   we backfill them to 1 here. Disabled rows get NO DeletedAt and are NEVER on the
--   purge clock. We deliberately EXCLUDE any row with DeletedAt set (those are
--   Deprovisioned=2 from PART A and must keep their retention clock).
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'LifecycleState')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'IsActive')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Objects') AND name = N'DeletedAt')
BEGIN
    UPDATE [Objects]
       SET [LifecycleState] = 1
     WHERE [LifecycleState] = 0
       AND [IsActive] = 0
       AND [DeletedAt] IS NULL;
    PRINT 'V134: Backfilled Objects Disabled=1 from present-but-disabled accounts (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' row(s)).';
END;
GO

-- FLAG (Identities -- PARTIALLY BACKFILLED, rest DEFERRED):
--   BACKFILL (certain): Status = 'Inactive' AND NOT a terminated leaver
--     (no past TerminationDate AND no past LastWorkDay) AND DeletedAt IS NULL AND
--     still state 0 -> these are SUSPENDED people, i.e. Disabled = 1. Columnar,
--     unambiguous. Backfill them.
--   DEFER (intentional): Status='Inactive' WITH a past TerminationDate/LastWorkDay
--     (a true leaver) that was never deprovisioned (DeletedAt NULL, state 0) is NOT
--     stamped to 2 here. Reason: moving a person to Deprovisioned starts a
--     destructive retention clock and MUST be AUDITED. The nightly
--     IdentityLifecycleEvaluationJob owns that transition and writes it through the
--     audited AdminRepository.DeprovisionIdentityAsync path. Leaving such a row at 0
--     for one night is safe (purge only ever targets state 2); the evaluation job
--     promotes it to 2 with a ChangeAuditLogs entry on its next run. We do NOT do
--     silent destructive state changes in a migration.
--   The TerminationDate/LastWorkDay column guards let a database whose Identities
--   table predates those columns fall back to the Status-only test.
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'LifecycleState')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'Status')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'DeletedAt')
BEGIN
    DECLARE @nowUtc DATETIME2 = SYSUTCDATETIME();
    DECLARE @hasTerm BIT = CASE WHEN COL_LENGTH('dbo.Identities','TerminationDate') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasLWD  BIT = CASE WHEN COL_LENGTH('dbo.Identities','LastWorkDay')     IS NOT NULL THEN 1 ELSE 0 END;

    IF @hasTerm = 1 AND @hasLWD = 1
    BEGIN
        UPDATE [Identities]
           SET [LifecycleState] = 1
         WHERE [LifecycleState] = 0
           AND [DeletedAt] IS NULL
           AND [Status] = 'Inactive'
           AND ([TerminationDate] IS NULL OR [TerminationDate] >= @nowUtc)
           AND ([LastWorkDay]     IS NULL OR [LastWorkDay]     >= @nowUtc);
    END
    ELSE IF @hasTerm = 1
    BEGIN
        UPDATE [Identities]
           SET [LifecycleState] = 1
         WHERE [LifecycleState] = 0
           AND [DeletedAt] IS NULL
           AND [Status] = 'Inactive'
           AND ([TerminationDate] IS NULL OR [TerminationDate] >= @nowUtc);
    END
    ELSE
    BEGIN
        UPDATE [Identities]
           SET [LifecycleState] = 1
         WHERE [LifecycleState] = 0
           AND [DeletedAt] IS NULL
           AND [Status] = 'Inactive';
    END
    PRINT 'V134: Backfilled Identities Disabled=1 from suspended (Status=Inactive, not terminated) (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' row(s)).';
END;
GO
