-- V074: Seed built-in License Management policies
-- These policies target LicensePool entities (not identities) and use the
-- LicenseCapacity rule type. When a pool breaches its thresholds, the
-- LicenseThresholdMonitorService creates CompliancePolicyViolation records
-- linked to these policies. Standard policy workflow (CreateAccessReview,
-- SendNotification) applies from there.

IF EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = 'C0740000-0000-0000-0000-000000000001')
BEGIN
    PRINT 'V074: License management policies already seeded — skipping.';
    RETURN;
END

DECLARE @now DATETIME2 = GETUTCDATE();

-- =============================================================================
-- Policy 1: License Capacity — Buffer Alert (global, applies to all pools)
-- =============================================================================
INSERT INTO CompliancePolicies (
    Id, Name, DisplayName, Description, Category, Severity, Priority,
    IsActive, IsBuiltIn, EvaluationFrequencyHours,
    CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions,
    EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays,
    EnableReminderSchedule, ScopeInheritance,
    PolicyType, TargetEntityType, RemoveOutOfScopeViolations,
    SlaCriticalHours, SlaHighHours, CreatedAt, CreatedBy
)
VALUES (
    'C0740000-0000-0000-0000-000000000001',
    N'License Capacity Alert', N'License Capacity — Global Buffer Alert',
    N'Fires when any license pool reaches its MinBufferPercent threshold. Triggers notifications and creates violations for downstream access review workflow.',
    N'LicenseManagement', 2, 2,
    1, 1, 1,  -- ACTIVE, evaluated hourly by LicenseThresholdMonitorJob
    0, 0, 0, 0, 0,
    N'Monitor', 0, 1, 7,
    1, N'None',
    N'Detection', N'LicensePool', 1,
    4, 24, @now, N'System (V074 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES
    (NEWID(), 'C0740000-0000-0000-0000-000000000001', N'Any pool breaches buffer threshold', N'Matches any license pool whose MinBufferPercent threshold is breached', N'LicenseCapacity', N'*', N'Breaches', N'MinBufferPercent', 1.0, 1, 1, N'AND', N'AND', @now);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES
    (NEWID(), 'C0740000-0000-0000-0000-000000000001', N'Log Violation', N'LogViolation', N'Immediate', 0, 1, 1, @now),
    (NEWID(), 'C0740000-0000-0000-0000-000000000001', N'Notify Admins', N'SendNotification', N'Immediate', 0, 2, 1, @now);

GO

-- =============================================================================
-- Policy 2: License Over-Utilization — triggers Access Review
-- =============================================================================
DECLARE @now2 DATETIME2 = GETUTCDATE();

INSERT INTO CompliancePolicies (
    Id, Name, DisplayName, Description, Category, Severity, Priority,
    IsActive, IsBuiltIn, EvaluationFrequencyHours,
    CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions,
    EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays,
    EnableReminderSchedule, ScopeInheritance,
    PolicyType, TargetEntityType, RemoveOutOfScopeViolations,
    SlaCriticalHours, SlaHighHours, CreatedAt, CreatedBy
)
VALUES (
    'C0740000-0000-0000-0000-000000000002',
    N'License Over-Utilization', N'License Over-Utilization — Auto Access Review',
    N'Fires when a license pool exceeds its MaxUtilizationPercent threshold. Auto-creates an access review campaign to reclaim unused licenses from dormant users.',
    N'LicenseManagement', 1, 1,
    1, 1, 1,
    0, 0, 0, 0, 0,
    N'Monitor', 0, 1, 3,
    1, N'None',
    N'Detection', N'LicensePool', 1,
    4, 12, @now2, N'System (V074 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES
    (NEWID(), 'C0740000-0000-0000-0000-000000000002', N'Any pool exceeds utilization', N'Matches any license pool exceeding its MaxUtilizationPercent threshold', N'LicenseCapacity', N'*', N'Breaches', N'MaxUtilizationPercent', 1.0, 1, 1, N'AND', N'AND', @now2);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES
    (NEWID(), 'C0740000-0000-0000-0000-000000000002', N'Log Violation', N'LogViolation', N'Immediate', 0, 1, 1, @now2),
    (NEWID(), 'C0740000-0000-0000-0000-000000000002', N'Notify Admins', N'SendNotification', N'Immediate', 0, 2, 1, @now2),
    (NEWID(), 'C0740000-0000-0000-0000-000000000002', N'Create License Reclamation Review', N'CreateAccessReview', N'Immediate', 1, 3, 1, @now2);

GO

-- =============================================================================
-- Policy 3: Quarterly License Audit (scheduled review of all pools)
-- =============================================================================
DECLARE @now3 DATETIME2 = GETUTCDATE();

INSERT INTO CompliancePolicies (
    Id, Name, DisplayName, Description, Category, Severity, Priority,
    IsActive, IsBuiltIn, EvaluationFrequencyHours,
    CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions,
    EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays,
    EnableReminderSchedule, ScopeInheritance,
    PolicyType, TargetEntityType, RemoveOutOfScopeViolations,
    SlaCriticalHours, SlaHighHours, CreatedAt, CreatedBy
)
VALUES (
    'C0740000-0000-0000-0000-000000000003',
    N'Quarterly License Audit', N'Quarterly License Audit',
    N'Quarterly review of all active license pools. Creates an access review campaign every 90 days to verify license assignments remain valid.',
    N'LicenseManagement', 3, 5,
    0, 1, 2160,  -- INACTIVE by default (user enables when ready), quarterly (90 days * 24)
    0, 0, 0, 0, 0,
    N'Monitor', 0, 7, 30,
    1, N'None',
    N'Detection', N'LicensePool', 0,
    168, 336, @now3, N'System (V074 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES
    (NEWID(), 'C0740000-0000-0000-0000-000000000003', N'All active pools', N'Matches all active license pools for scheduled audit', N'LicenseCapacity', N'*', N'Always', N'', 1.0, 1, 1, N'AND', N'AND', @now3);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES
    (NEWID(), 'C0740000-0000-0000-0000-000000000003', N'Create Quarterly Audit Review', N'CreateAccessReview', N'Immediate', 1, 1, 1, @now3);

GO

PRINT 'V074: Seeded 3 license management policies (Capacity Alert: active, Over-Utilization: active, Quarterly Audit: inactive).';
