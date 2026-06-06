-- V135: Seed the DEPROVISIONING POLICY -- the single gate to Deprovisioned(2).
--
-- BACKGROUND: V130/V132 added the deferred-deletion lifecycle; V134 reframed it to
-- the ARS 3-state model (0=Active, 1=Disabled, 2=Deprovisioned). Until now, two
-- paths promoted a record to Deprovisioned(2) automatically: the Objects tombstone
-- endpoint (gone-from-source) and the IdentityLifecycleEvaluationJob (HR leaver /
-- orphan). Jacob (Active Roles expert) wants that promotion to be POLICY-DRIVEN,
-- exactly as ARS separates "disabled" (account-disable bit) from "deprovisioned"
-- (edsvaDeprovisionStatus + edsvaDeprovisionDate, which arms the delete clock):
-- whether a leaver is merely DISABLED or fully DEPROVISIONED is a policy choice.
--
-- THE POLICY (IC-only governance; Conduit has no policies) lives in the generic
-- Settings table under Category='Lifecycle', read by DeprovisioningPolicy.LoadAsync
-- at both gate sites. Keys seeded here:
--   DeprovisioningPolicyEnabled        bool  DEFAULT 'false'  (master switch, OFF)
--   DeprovisioningPolicyScope          string DEFAULT 'Both'  (Objects|Identities|Both)
--   DeprovisioningPolicyHrLeaver       bool  DEFAULT 'true'   (terminated leaver qualifies)
--   DeprovisioningPolicyGoneFromSource bool  DEFAULT 'true'   (tombstone/orphan qualifies)
--   DeprovisioningPolicyDisabledGrace  bool  DEFAULT 'false'  (disabled-for-N-days qualifies)
--   DeprovisioningPolicyDisabledGraceDays int DEFAULT '90'
--
-- SAFE DEFAULT (the guardrail Jacob asked for): the MASTER SWITCH ships 'false'. With
-- it off, DeprovisioningPolicy.LoadAsync short-circuits to the all-safe snapshot, so
-- NOTHING is ever promoted to Deprovisioned(2). Because the purge job only ever
-- targets state 2, the purge is PERMANENTLY INERT until an admin enables the policy.
-- The criteria keys are seeded with sensible values so that the moment an admin flips
-- the master switch on, the policy is immediately meaningful (leaver + gone-from-source
-- qualify) rather than a dead toggle. Their defaults are NON-destructive while the
-- master switch is off.
--
-- IDEMPOTENT: each key is inserted only if absent, so an operator edit is never
-- clobbered on re-run. Single GO batch per key, no wrapping transaction, matching the
-- V133 Settings-seed pattern and the GO-splitting migration runner.
--
-- DUAL-RUN SAFE: touches only the IdentityCenter Settings table. Conduit never runs
-- IC migrations and never reads these keys.

SET NOCOUNT ON;
GO

-- Master enable -- DEFAULT OFF.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Settings') AND name = N'Category')
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Settings') AND name = N'Key')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'DeprovisioningPolicyEnabled')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'DeprovisioningPolicyEnabled', N'false', N'bool', 0, GETUTCDATE(), N'System');
    PRINT 'V135: Seeded Lifecycle/DeprovisioningPolicyEnabled = false (OFF by default).';
END
ELSE PRINT 'V135: DeprovisioningPolicyEnabled already present or Settings missing -- skipped.';
GO

-- Scope -- DEFAULT Both.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'DeprovisioningPolicyScope')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'DeprovisioningPolicyScope', N'Both', N'string', 0, GETUTCDATE(), N'System');
    PRINT 'V135: Seeded Lifecycle/DeprovisioningPolicyScope = Both.';
END
ELSE PRINT 'V135: DeprovisioningPolicyScope already present or Settings missing -- skipped.';
GO

-- Criterion: HR-terminated leaver qualifies -- DEFAULT true.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'DeprovisioningPolicyHrLeaver')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'DeprovisioningPolicyHrLeaver', N'true', N'bool', 0, GETUTCDATE(), N'System');
    PRINT 'V135: Seeded Lifecycle/DeprovisioningPolicyHrLeaver = true.';
END
ELSE PRINT 'V135: DeprovisioningPolicyHrLeaver already present or Settings missing -- skipped.';
GO

-- Criterion: gone-from-source (tombstone) / orphan qualifies -- DEFAULT true.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'DeprovisioningPolicyGoneFromSource')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'DeprovisioningPolicyGoneFromSource', N'true', N'bool', 0, GETUTCDATE(), N'System');
    PRINT 'V135: Seeded Lifecycle/DeprovisioningPolicyGoneFromSource = true.';
END
ELSE PRINT 'V135: DeprovisioningPolicyGoneFromSource already present or Settings missing -- skipped.';
GO

-- Criterion: disabled/inactive for >= grace days qualifies -- DEFAULT false (opt-in).
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'DeprovisioningPolicyDisabledGrace')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'DeprovisioningPolicyDisabledGrace', N'false', N'bool', 0, GETUTCDATE(), N'System');
    PRINT 'V135: Seeded Lifecycle/DeprovisioningPolicyDisabledGrace = false (opt-in).';
END
ELSE PRINT 'V135: DeprovisioningPolicyDisabledGrace already present or Settings missing -- skipped.';
GO

-- Grace window for the DisabledGrace criterion -- DEFAULT 90 days.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Category] = N'Lifecycle' AND [Key] = N'DeprovisioningPolicyDisabledGraceDays')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'Lifecycle', N'DeprovisioningPolicyDisabledGraceDays', N'90', N'int', 0, GETUTCDATE(), N'System');
    PRINT 'V135: Seeded Lifecycle/DeprovisioningPolicyDisabledGraceDays = 90.';
END
ELSE PRINT 'V135: DeprovisioningPolicyDisabledGraceDays already present or Settings missing -- skipped.';
GO
