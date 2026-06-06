-- V082: Add CostCenter to Objects table + Seed Duplicate EmployeeID & UPN detection policies
-- CostCenter enables financial tracking per account/object
-- Duplicate detection policies catch identity governance issues

-- 1. Add CostCenter column to Objects
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'CostCenter')
    ALTER TABLE Objects ADD CostCenter NVARCHAR(100) NULL;

-- Skip if already seeded
IF EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = 'C0820000-0000-0000-0000-000000000001')
BEGIN
    PRINT 'V082: Duplicate detection policies already seeded — skipping.';
    RETURN;
END

DECLARE @now DATETIME2 = GETUTCDATE();

-- =============================================
-- Policy 1: Duplicate EmployeeID Detection
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
    'C0820000-0000-0000-0000-000000000001',
    N'Duplicate EmployeeID Detection', N'Duplicate EmployeeID Detection',
    N'Detects multiple active accounts sharing the same EmployeeID. Duplicate EmployeeIDs indicate potential identity governance issues such as orphaned accounts, data import errors, or unauthorized account creation.',
    N'DataQuality', 2, 1,
    1, 1, 24,
    0, 0, 0, 0, 0,
    N'Detection', 0, 1, 7,
    1, N'None',
    N'Detection', N'Object', 1,
    4, 24,
    @now, N'System (V082 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES (
    NEWID(), 'C0820000-0000-0000-0000-000000000001',
    N'EmployeeID has duplicates',
    N'Flags objects where the EmployeeId field value is shared by more than one active object',
    N'DuplicateField', N'EmployeeId', N'HasDuplicates',
    1.0, 1, 1, N'AND', N'All', @now
);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES (
    NEWID(), 'C0820000-0000-0000-0000-000000000001',
    N'Log Violation', N'LogViolation', N'Immediate', 0, 1, 1, @now
);

GO

-- =============================================
-- Policy 2: Duplicate UPN Detection
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
    'C0820000-0000-0000-0000-000000000002',
    N'Duplicate UPN Detection', N'Duplicate UPN Detection',
    N'Detects multiple active accounts sharing the same UserPrincipalName. Duplicate UPNs can cause authentication failures, SSO issues, and security vulnerabilities.',
    N'DataQuality', 1, 1,
    1, 1, 24,
    0, 0, 0, 0, 0,
    N'Detection', 0, 1, 7,
    1, N'None',
    N'Detection', N'Object', 1,
    4, 24,
    @now2, N'System (V082 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES (
    NEWID(), 'C0820000-0000-0000-0000-000000000002',
    N'UPN has duplicates',
    N'Flags objects where the UserPrincipalName is shared by more than one active object',
    N'DuplicateField', N'UserPrincipalName', N'HasDuplicates',
    1.0, 1, 1, N'AND', N'All', @now2
);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES (
    NEWID(), 'C0820000-0000-0000-0000-000000000002',
    N'Log Violation', N'LogViolation', N'Immediate', 0, 1, 1, @now2
);

GO

-- =============================================
-- Policy 3: Missing Cost Center (inactive by default)
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
    'C0820000-0000-0000-0000-000000000003',
    N'Missing Cost Center', N'Missing Cost Center',
    N'Detects active user accounts without a Cost Center assignment. Cost Center is required for license chargeback, access review scoping, and financial reporting.',
    N'DataQuality', 3, 2,
    0, 1, 168,
    0, 0, 0, 0, 0,
    N'Detection', 0, 1, 7,
    1, N'None',
    N'Detection', N'Object', 1,
    72, 168,
    @now3, N'System (V082 Migration)'
);

INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
VALUES (
    NEWID(), 'C0820000-0000-0000-0000-000000000003',
    N'CostCenter is empty',
    N'Flags active user objects where CostCenter is null or empty',
    N'DataQuality', N'CostCenter', N'IsEmpty',
    1.0, 1, 1, N'AND', N'All', @now3
);

INSERT INTO CompliancePolicyAction (Id, CompliancePolicyId, Name, ActionType, ExecutionTiming, RequiresApproval, Priority, IsActive, CreatedAt)
VALUES (
    NEWID(), 'C0820000-0000-0000-0000-000000000003',
    N'Log Violation', N'LogViolation', N'Immediate', 0, 1, 1, @now3
);
