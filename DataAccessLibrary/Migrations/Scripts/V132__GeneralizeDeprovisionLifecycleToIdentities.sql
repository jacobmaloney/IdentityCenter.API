-- V132: Generalize the ARS-style deferred-deletion lifecycle.
--
-- BACKGROUND: V130 shipped the lifecycle for the Objects table only, with a
-- table-specific retention setting (Lifecycle / ObjectDeprovisionRetentionDays).
-- This migration does two things:
--
--   PART 2 (generalize the setting): introduce ONE global retention key
--     Lifecycle / DeprovisionRetentionDays (default 30) that governs the
--     deferred-deletion window for EVERY lifecycle-managed table (Objects +
--     Identities now, extensible later). Back-compat: if the old
--     ObjectDeprovisionRetentionDays exists, its CONFIGURED VALUE is carried
--     forward into the new global key so an operator-tuned window is never
--     silently reset to the default. The old key is left in place (not dropped)
--     so any external reader keeps working; the purge job reads the new key
--     first and falls back to the old key, then the default.
--
--   PART 3 (extend lifecycle to Identities): add Identities.LifecycleState
--     (0=Active, 1=Deprovisioned) and Identities.DeletedAt (the retention clock),
--     mirroring Objects. A deprovisioned Identity that returns within the window
--     is revived (state->0, DeletedAt->NULL); one still at state 1 past the
--     window is HARD-PURGED by the (now generalized) ObjectDeprovisionPurgeJob
--     with a conservative, governance-aware FK cascade.
--
-- IDEMPOTENT: every column add is guarded by sys.columns, the default constraint
-- by sys.default_constraints, the index by sys.indexes, and the setting inserts
-- by an existence check. Backfills only touch rows that still need it. Each
-- statement is its own GO batch with no wrapping transaction, matching the
-- V127/V128/V130 pattern and the GO-splitting migration runner.
--
-- DUAL-RUN SAFE: touches only IdentityCenter tables (Identities, Settings).
-- Conduit has no Identities table and never runs IC migrations -- inert there.

SET NOCOUNT ON;
GO

-- =====================================================================
-- PART 2: Global retention setting + back-compat carry-forward.
-- =====================================================================

-- 2a. Seed the new global key. If the old Object-specific key already has a
--     value, carry THAT value forward (preserve an operator-tuned window);
--     otherwise default to 30. Insert only if the global key is absent so a
--     later operator edit is never clobbered on re-run.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Settings') AND name = N'Category')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Settings') AND name = N'Key')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'DeprovisionRetentionDays')
BEGIN
    DECLARE @carry NVARCHAR(MAX) = (
        SELECT TOP 1 [Value] FROM [Settings]
        WHERE [Category] = N'Lifecycle' AND [Key] = N'ObjectDeprovisionRetentionDays');

    -- Only carry a value that is a sane positive integer; otherwise default.
    DECLARE @value NVARCHAR(MAX) = N'30';
    IF @carry IS NOT NULL AND TRY_CONVERT(INT, @carry) IS NOT NULL AND TRY_CONVERT(INT, @carry) >= 1
        SET @value = @carry;

    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'DeprovisionRetentionDays', @value, N'int', 0, GETUTCDATE(), N'System');

    PRINT 'V132: Seeded global Settings Lifecycle/DeprovisionRetentionDays = ' + @value
        + CASE WHEN @carry IS NOT NULL THEN ' (carried from ObjectDeprovisionRetentionDays).' ELSE ' (default).' END;
END
ELSE
BEGIN
    PRINT 'V132: Lifecycle/DeprovisionRetentionDays already present or Settings table missing -- skipped.';
END;
GO

-- =====================================================================
-- PART 3: Identities deferred-deletion lifecycle columns.
-- =====================================================================

-- 3a. Identities.LifecycleState (INT NOT NULL DEFAULT 0 = Active).
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Identities')
   AND NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'LifecycleState')
BEGIN
    ALTER TABLE [Identities] ADD [LifecycleState] INT NOT NULL
        CONSTRAINT [DF_Identities_LifecycleState] DEFAULT (0);
    PRINT 'V132: Added Identities.LifecycleState (INT NOT NULL DEFAULT 0 = Active).';
END
ELSE
BEGIN
    PRINT 'V132: Identities.LifecycleState already present or Identities table missing -- skipped.';
END;
GO

-- 3b. Identities.DeletedAt (datetime2 NULL) -- the retention clock. Identities
--     never had a soft-delete timestamp (only IsActive + LastSeenAt), so this is
--     a net-new column. NULL = not deprovisioned; a stamped value = the moment
--     of deprovision, against which the retention window is measured.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Identities')
   AND NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'DeletedAt')
BEGIN
    ALTER TABLE [Identities] ADD [DeletedAt] datetime2 NULL;
    PRINT 'V132: Added Identities.DeletedAt (datetime2 NULL, retention clock).';
END
ELSE
BEGIN
    PRINT 'V132: Identities.DeletedAt already present or Identities table missing -- skipped.';
END;
GO

-- 3c. Filtered index so the daily purge sweep over Identities is cheap:
--     it scans only deprovisioned rows. Guarded on index absence + column
--     presence.
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Identities') AND name = N'LifecycleState')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_Identities_Lifecycle_Purge' AND object_id = OBJECT_ID(N'dbo.Identities'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Identities_Lifecycle_Purge]
        ON [Identities] ([LifecycleState], [DeletedAt])
        WHERE [LifecycleState] = 1;
    PRINT 'V132: Created filtered index IX_Identities_Lifecycle_Purge.';
END;
GO
