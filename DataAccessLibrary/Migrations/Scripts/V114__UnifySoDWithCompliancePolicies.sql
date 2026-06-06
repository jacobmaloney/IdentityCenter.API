-- V114: Unify SoD into the CompliancePolicy framework.
--
-- V113 shipped a parallel SoD system (SoDRules + SoDViolations) before we
-- realised the existing CompliancePolicy framework was designed for exactly
-- this — there is even a pre-seeded "Separation of Duties" policy at
-- 22222222-2222-2222-2222-222222222214 with a reserved RuleType of
-- 'SeparationOfDuties' on CompliancePolicyRule.
--
-- This migration:
--   1. Adds a CompliancePolicyId FK on SoDRules so each rule lives under the
--      seeded SoD parent policy. Default routes new rows to the parent.
--   2. Adds a RuleSourceId column on CompliancePolicyViolations so SoD-origin
--      violations retain a back-pointer to the firing SoDRule (used for
--      deduplication on re-scan and for "which rule fired?" in the UI).
--   3. Drops the parallel SoDViolations table — empty in any real environment.
--      NOTE for production: any deployment that has actual SoDViolations data
--      should migrate those rows into CompliancePolicyViolations BEFORE
--      running this migration. V113 only shipped 8 days ago so this is
--      pre-revenue and there is no deployed data yet.
--
-- Idempotent: every step uses sys.* / OBJECT_ID guards.

-- ─── 1) Add CompliancePolicyId FK on SoDRules ───────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('SoDRules') AND name = 'CompliancePolicyId')
BEGIN
    ALTER TABLE SoDRules
        ADD CompliancePolicyId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_SoDRules_CompliancePolicyId
            DEFAULT '22222222-2222-2222-2222-222222222214';
END
GO

-- Backfill any pre-existing seeded rules whose default did not stamp them
-- (paranoid — the NOT NULL DEFAULT above handles new rows; this UPDATE is
-- a no-op on a fresh schema but cleans up rules created without a default
-- in any partial-state environment).
UPDATE SoDRules
SET CompliancePolicyId = '22222222-2222-2222-2222-222222222214'
WHERE CompliancePolicyId IS NULL
   OR CompliancePolicyId = '00000000-0000-0000-0000-000000000000';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_SoDRules_CompliancePolicies_CompliancePolicyId')
BEGIN
    -- Only add the FK if the parent policy actually exists. On a fresh DB,
    -- DapperPolicySeedService.cs runs after migrations and seeds the parent;
    -- on existing DBs it has run already. Guard so partial-state databases
    -- don't choke.
    IF EXISTS (
        SELECT 1 FROM CompliancePolicies
        WHERE Id = '22222222-2222-2222-2222-222222222214')
    BEGIN
        ALTER TABLE SoDRules
            ADD CONSTRAINT FK_SoDRules_CompliancePolicies_CompliancePolicyId
            FOREIGN KEY (CompliancePolicyId)
            REFERENCES CompliancePolicies (Id)
            ON DELETE NO ACTION;
    END
END
GO

-- ─── 2) Add RuleSourceId on CompliancePolicyViolations ───────────────────────
-- SoD violations need a back-pointer to the SoDRule that fired so we can:
--   * Deduplicate on re-scan (same EntityId + same RuleSourceId + Status='Open')
--   * Surface "which rule fired" in the violations dashboard
--   * Link the approval-time exception write back to the same rule
-- Nullable because non-SoD violations never use it.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('CompliancePolicyViolations')
      AND name = 'RuleSourceId')
BEGIN
    ALTER TABLE CompliancePolicyViolations
        ADD RuleSourceId UNIQUEIDENTIFIER NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_CompliancePolicyViolations_RuleSourceId'
      AND object_id = OBJECT_ID('CompliancePolicyViolations'))
BEGIN
    CREATE INDEX IX_CompliancePolicyViolations_RuleSourceId
        ON CompliancePolicyViolations (RuleSourceId)
        WHERE RuleSourceId IS NOT NULL;
END
GO

-- ─── 3) Drop the parallel SoDViolations table ────────────────────────────────
-- Empty in any real environment (V113 was 8 days ago, pre-revenue).
-- Drop indexes first, then the table — both guarded.

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_SoDViolations_Status'
      AND object_id = OBJECT_ID('SoDViolations'))
BEGIN
    DROP INDEX IX_SoDViolations_Status ON SoDViolations;
END
GO

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_SoDViolations_ObjectId'
      AND object_id = OBJECT_ID('SoDViolations'))
BEGIN
    DROP INDEX IX_SoDViolations_ObjectId ON SoDViolations;
END
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SoDViolations')
BEGIN
    DROP TABLE [SoDViolations];
END
GO
