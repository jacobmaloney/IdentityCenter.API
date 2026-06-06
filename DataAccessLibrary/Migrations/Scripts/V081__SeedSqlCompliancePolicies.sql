-- V081: Seed SQL Server compliance policies for infrastructure license management.
-- All seeded as IsActive=0 so users review and activate when ready.

-- Skip if already seeded
IF EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = 'C0810000-0000-0000-0000-000000000001')
BEGIN
    PRINT 'V081: SQL compliance policies already seeded — skipping.';
    RETURN;
END

DECLARE @now DATETIME2 = GETUTCDATE();

-- =============================================
-- Policy 1: No Developer Edition in Production
-- =============================================
INSERT INTO CompliancePolicies (
    Id, Name, DisplayName, Description, Category, Severity, Priority,
    IsActive, IsBuiltIn, EvaluationFrequencyHours,
    CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions,
    EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays,
    EnableReminderSchedule, ScopeInheritance,
    PolicyType, TargetEntityType, RemoveOutOfScopeViolations,
    SlaCriticalHours, SlaHighHours,
    CreatedAt, CreatedBy
)
VALUES (
    'C0810000-0000-0000-0000-000000000001',
    N'No Developer Edition in Production', N'No Developer Edition in Production',
    N'Detects active SQL Server instances running Developer Edition, which is not licensed for production use. Developer Edition violations may trigger audit findings and license penalties.',
    N'InfrastructureLicense', 1, 1,
    0, 1, 168,
    0, 0, 0, 0, 0,
    N'Monitor', 0, 1, 7,
    1, N'None',
    N'Detection', N'Object', 1,
    4, 24,
    @now, N'System (V081 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES
    (NEWID(), 'C0810000-0000-0000-0000-000000000001', N'SQL Edition is Developer', N'Server has SQL Developer Edition installed', N'AttributeMatch', N'sqlServerEdition', N'Equals', N'Developer', 1.0, 1, 1, N'AND', N'AND', @now),
    (NEWID(), 'C0810000-0000-0000-0000-000000000001', N'Server is Active', N'Server account is currently enabled', N'AccountStatus', N'IsActive', N'Equals', N'true', 1.0, 2, 1, N'AND', N'AND', @now);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES
    (NEWID(), 'C0810000-0000-0000-0000-000000000001', N'Log Violation', N'LogViolation', N'Immediate', 0, 1, 1, @now),
    (NEWID(), 'C0810000-0000-0000-0000-000000000001', N'Notify Infrastructure Team', N'SendNotification', N'Immediate', 0, 2, 1, @now);

GO

-- =============================================
-- Policy 2: All SQL Servers Must Have Owner
-- =============================================
DECLARE @now2 DATETIME2 = GETUTCDATE();

INSERT INTO CompliancePolicies (
    Id, Name, DisplayName, Description, Category, Severity, Priority,
    IsActive, IsBuiltIn, EvaluationFrequencyHours,
    CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions,
    EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays,
    EnableReminderSchedule, ScopeInheritance,
    PolicyType, TargetEntityType, RemoveOutOfScopeViolations,
    SlaCriticalHours, SlaHighHours,
    CreatedAt, CreatedBy
)
VALUES (
    'C0810000-0000-0000-0000-000000000002',
    N'All SQL Servers Must Have Owner', N'All SQL Servers Must Have Owner',
    N'Detects SQL Server instances that have no assigned owner. Every database server should have an accountable owner for patching, licensing, and access review compliance.',
    N'InfrastructureLicense', 2, 2,
    0, 1, 168,
    0, 0, 0, 0, 0,
    N'Monitor', 0, 3, 14,
    1, N'None',
    N'Detection', N'Object', 0,
    24, 72,
    @now2, N'System (V081 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES
    (NEWID(), 'C0810000-0000-0000-0000-000000000002', N'Has SQL Server', N'Server has SQL Server installed (any edition)', N'AttributeMatch', N'sqlServerEdition', N'IsNotNull', NULL, 1.0, 1, 1, N'AND', N'AND', @now2),
    (NEWID(), 'C0810000-0000-0000-0000-000000000002', N'No Owner Assigned', N'Server has no OwnerIdentityId set', N'AccountStatus', N'OwnerIdentityId', N'IsNull', NULL, 1.0, 2, 1, N'AND', N'AND', @now2);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES
    (NEWID(), 'C0810000-0000-0000-0000-000000000002', N'Log Violation', N'LogViolation', N'Immediate', 0, 1, 1, @now2);

GO

-- =============================================
-- Policy 3: End of Life SQL Versions
-- =============================================
DECLARE @now3 DATETIME2 = GETUTCDATE();

INSERT INTO CompliancePolicies (
    Id, Name, DisplayName, Description, Category, Severity, Priority,
    IsActive, IsBuiltIn, EvaluationFrequencyHours,
    CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions,
    EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays,
    EnableReminderSchedule, ScopeInheritance,
    PolicyType, TargetEntityType, RemoveOutOfScopeViolations,
    SlaCriticalHours, SlaHighHours,
    CreatedAt, CreatedBy
)
VALUES (
    'C0810000-0000-0000-0000-000000000003',
    N'End of Life SQL Versions', N'End of Life SQL Version Detection',
    N'Detects servers running SQL Server 2012 or 2014 which are out of Microsoft extended support. No security patches are available without Extended Security Updates (ESU).',
    N'InfrastructureLicense', 2, 3,
    0, 1, 168,
    0, 0, 0, 0, 0,
    N'Monitor', 0, 3, 14,
    1, N'None',
    N'Detection', N'Object', 1,
    24, 72,
    @now3, N'System (V081 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES
    (NEWID(), 'C0810000-0000-0000-0000-000000000003', N'SQL Version is 2012 or 2014', N'Server is running an end-of-life SQL Server version', N'AttributeMatch', N'sqlServerVersion', N'Contains', N'2012|2014', 1.0, 1, 1, N'AND', N'AND', @now3);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES
    (NEWID(), 'C0810000-0000-0000-0000-000000000003', N'Log Violation', N'LogViolation', N'Immediate', 0, 1, 1, @now3),
    (NEWID(), 'C0810000-0000-0000-0000-000000000003', N'Notify Infrastructure Team', N'SendNotification', N'Immediate', 0, 2, 1, @now3);

GO

-- =============================================
-- Policy 4: SQL Enterprise License Audit
-- =============================================
DECLARE @now4 DATETIME2 = GETUTCDATE();

INSERT INTO CompliancePolicies (
    Id, Name, DisplayName, Description, Category, Severity, Priority,
    IsActive, IsBuiltIn, EvaluationFrequencyHours,
    CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions,
    EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays,
    EnableReminderSchedule, ScopeInheritance,
    PolicyType, TargetEntityType, RemoveOutOfScopeViolations,
    SlaCriticalHours, SlaHighHours,
    CreatedAt, CreatedBy
)
VALUES (
    'C0810000-0000-0000-0000-000000000004',
    N'SQL Enterprise License Audit', N'Quarterly SQL Enterprise License Review',
    N'Identifies all SQL Server Enterprise Edition instances for quarterly license compliance review. Enterprise Edition is the highest-cost SQL SKU — regular audits ensure accurate license allocation.',
    N'InfrastructureLicense', 3, 4,
    0, 1, 168,
    0, 0, 0, 0, 0,
    N'Monitor', 0, 7, 30,
    1, N'None',
    N'Detection', N'Object', 1,
    168, 336,
    @now4, N'System (V081 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES
    (NEWID(), 'C0810000-0000-0000-0000-000000000004', N'SQL Edition is Enterprise', N'Server has SQL Server Enterprise Edition', N'AttributeMatch', N'sqlServerEdition', N'Equals', N'Enterprise', 1.0, 1, 1, N'AND', N'AND', @now4);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES
    (NEWID(), 'C0810000-0000-0000-0000-000000000004', N'Create Access Review Campaign', N'CreateAccessReview', N'Immediate', 1, 1, 1, @now4),
    (NEWID(), 'C0810000-0000-0000-0000-000000000004', N'Log Violation', N'LogViolation', N'Immediate', 0, 2, 1, @now4);

GO

PRINT 'V081: Seeded 4 SQL Server compliance policies (InfrastructureLicense category, all inactive).';
