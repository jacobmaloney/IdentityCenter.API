-- V075: Fix license policy evaluation frequency
-- V074 set EvaluationFrequencyHours=1 which fires too aggressively on startup.
-- The LicenseThresholdMonitorJob already runs hourly — policies don't need to
-- also evaluate hourly via the policy engine. Set to 24h instead.
-- Also set IsActive=0 to stop them from auto-evaluating via the identity-based
-- policy engine (which queries identities, not license pools).

UPDATE CompliancePolicies
SET EvaluationFrequencyHours = 24, IsActive = 0
WHERE Id IN (
    'C0740000-0000-0000-0000-000000000001',
    'C0740000-0000-0000-0000-000000000002',
    'C0740000-0000-0000-0000-000000000003'
);

PRINT 'V075: License policies set to 24h frequency and deactivated from identity policy engine (threshold monitor handles evaluation).';
GO
