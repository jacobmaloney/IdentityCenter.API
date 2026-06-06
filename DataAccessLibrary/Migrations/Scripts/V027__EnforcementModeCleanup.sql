-- V027: Clean up EnforcementMode values
-- Migrate "Soft" -> "Monitor" and backfill NULLs

-- CompliancePolicies: Soft -> Monitor
UPDATE CompliancePolicies SET EnforcementMode = 'Monitor' WHERE EnforcementMode = 'Soft';

-- CompliancePolicies: backfill NULLs
UPDATE CompliancePolicies SET EnforcementMode = 'Monitor' WHERE EnforcementMode IS NULL;
