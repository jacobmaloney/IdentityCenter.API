-- V115: Undo the parallel SoD system shipped in V113/V114.
-- The existing CompliancePolicy framework already supports SoD via
-- RuleType='GroupMembership', Operator='IsMemberOfAll' rendered by PolicyModal
-- and evaluated by PolicyEvaluationEngine. V113's SoDRules + V114's
-- RuleSourceId column on CompliancePolicyViolations were redundant.
--
-- Idempotent: only drops what exists. Safe to re-run.

-- 1. Drop CompliancePolicyViolations.RuleSourceId column + its index (added in V114)
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CompliancePolicyViolations_RuleSourceId')
    DROP INDEX [IX_CompliancePolicyViolations_RuleSourceId] ON [CompliancePolicyViolations];
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyViolations') AND name = 'RuleSourceId')
    ALTER TABLE [CompliancePolicyViolations] DROP COLUMN [RuleSourceId];
GO

-- 2. Drop SoDRules table + indexes (V113)
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SoDRules')
    DROP TABLE [SoDRules];
GO

-- 3. Drop SoDViolations table + indexes (V113) -- should already be gone from V114, defensive
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SoDViolations')
    DROP TABLE [SoDViolations];
GO
