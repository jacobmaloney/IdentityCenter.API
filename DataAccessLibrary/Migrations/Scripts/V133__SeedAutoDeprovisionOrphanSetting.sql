-- V133: Seed the orphan-net on/off switch for the Identity lifecycle auto-evaluation.
--
-- BACKGROUND: V132 added the deferred-deletion lifecycle columns to Identities.
-- The new IdentityLifecycleEvaluationJob (daily 3:30am, ahead of the 3:40am
-- purge) auto-deprovisions HR leavers (authoritative) and, as a SAFETY NET,
-- previously-linked identities that have lost all active linked Objects.
--
-- The HR-leaver signal is ALWAYS evaluated. Only the softer orphan net is
-- gateable, via Settings(Category='Lifecycle', Key='AutoDeprovisionOrphans').
-- The job already DEFAULTS the net to ON when the key is absent, so this seed is
-- purely so the switch is present and operator-discoverable in the Lifecycle
-- settings the Config Center surfaces. Inserted ON ('true') to match Option C
-- (the orphan net is part of the chosen design).
--
-- IDEMPOTENT: inserted only if the key is absent so an operator edit is never
-- clobbered on re-run. Single GO batch, no wrapping transaction, matching the
-- V130/V132 Settings-seed pattern and the GO-splitting migration runner.
--
-- DUAL-RUN SAFE: touches only the IdentityCenter Settings table. Conduit never
-- runs IC migrations.

SET NOCOUNT ON;
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Settings') AND name = N'Category')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Settings') AND name = N'Key')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'AutoDeprovisionOrphans')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'AutoDeprovisionOrphans', N'true', N'bool', 0, GETUTCDATE(), N'System');
    PRINT 'V133: Seeded Settings Lifecycle/AutoDeprovisionOrphans = true.';
END
ELSE
BEGIN
    PRINT 'V133: Lifecycle/AutoDeprovisionOrphans already present or Settings table missing -- skipped.';
END;
GO
