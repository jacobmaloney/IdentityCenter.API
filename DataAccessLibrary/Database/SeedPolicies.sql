-- ================================================================
-- SEED COMPLIANCE POLICIES - IdentityCenter
-- 27 policies with rules - All policies seeded as DISABLED
-- ================================================================

SET QUOTED_IDENTIFIER ON;

-- ================================================================
-- LIFECYCLE POLICIES
-- ================================================================

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222201')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222201', 'Dormant Account Detection', 'Detects accounts with no login activity for 45+ days.', 'Lifecycle', 'Detection', 3, 1, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222215')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222215', 'Dormant Account - 90 Day Review Required', 'Accounts inactive for 90+ days require manager review.', 'Lifecycle', 'Detection', 3, 2, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222216')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222216', 'Dormant Account - 180 Day Auto-Disable', 'Accounts inactive for 180+ days are automatically disabled.', 'Lifecycle', 'Enforcement', 2, 3, 0, 1, 24, 0, 0, 0, 0, 0, 'Hard', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222217')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222217', 'Dormant Account - 365 Day Archive/Delete', 'Accounts inactive for 365+ days are archived.', 'Lifecycle', 'Enforcement', 1, 4, 0, 1, 24, 0, 0, 0, 0, 0, 'Hard', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222205')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222205', 'New Hire Access Review', 'Requires access review for new hires within 30 days.', 'Lifecycle', 'Detection', 3, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222206')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222206', 'Termination Processing', 'Ensures terminated users are promptly disabled.', 'Lifecycle', 'Enforcement', 1, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Hard', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222212')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222212', 'Contractor Access Expiration', 'Monitors contractor accounts for access expiration.', 'Lifecycle', 'Detection', 2, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

-- ================================================================
-- RISK POLICIES
-- ================================================================

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222203')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222203', 'Excessive Permissions Detection', 'Identifies users with more than 15 group memberships.', 'Risk', 'Detection', 2, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222208')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222208', 'High-Risk User Monitoring', 'Monitors users with high risk scores (>75).', 'Risk', 'Detection', 1, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222214')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222214', 'Separation of Duties', 'Detects users with conflicting roles.', 'Risk', 'Detection', 1, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222226')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222226', 'Admin Account Creep', 'Detects users who accumulated admin privileges.', 'Risk', 'Detection', 2, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222227')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222227', 'Cross-Department Access', 'Detects access spanning multiple departments.', 'Risk', 'Detection', 3, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222228')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222228', 'Privileged Access Review', 'Quarterly review of privileged access.', 'Risk', 'Detection', 2, 0, 0, 1, 168, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

-- ================================================================
-- COMPLIANCE POLICIES
-- ================================================================

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222202')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222202', 'Password Never Expires Detection', 'Identifies accounts with password never expires flag.', 'Compliance', 'Detection', 2, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222207')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222207', 'Missing Manager Assignment', 'Identifies users without a manager assigned.', 'Compliance', 'Detection', 3, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222209')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222209', 'Orphan Group Membership', 'Detects group memberships without business justification.', 'Compliance', 'Detection', 3, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222210')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222210', 'Expired Password Detection', 'Identifies accounts with expired passwords.', 'Compliance', 'Detection', 2, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222211')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222211', 'Disabled Account Group Membership', 'Detects disabled accounts with active group memberships.', 'Compliance', 'Detection', 2, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222213')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222213', 'Service Account Review', 'Quarterly review of service accounts.', 'Compliance', 'Detection', 2, 0, 0, 1, 168, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

-- ================================================================
-- DATA QUALITY POLICIES
-- ================================================================

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222218')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222218', 'Missing Email Address', 'Detects accounts without email addresses.', 'DataQuality', 'Detection', 3, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222219')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222219', 'Missing Department', 'Detects accounts without department assignment.', 'DataQuality', 'Detection', 3, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222220')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222220', 'Missing Job Title', 'Detects accounts without job title.', 'DataQuality', 'Detection', 3, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222221')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222221', 'Duplicate Display Names', 'Detects accounts with duplicate display names.', 'DataQuality', 'Detection', 3, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

-- ================================================================
-- SECURITY POLICIES
-- ================================================================

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222222')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222222', 'Weak Password Policy', 'Identifies accounts not meeting password complexity.', 'Security', 'Detection', 1, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222223')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222223', 'Kerberos Delegation Detection', 'Detects unconstrained Kerberos delegation.', 'Security', 'Detection', 1, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222224')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222224', 'Reversible Encryption Detection', 'Detects reversible encryption enabled.', 'Security', 'Detection', 1, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM CompliancePolicies WHERE Id = '22222222-2222-2222-2222-222222222225')
    INSERT INTO CompliancePolicies (Id, Name, Description, Category, PolicyType, Severity, Priority, IsActive, IsBuiltIn, EvaluationFrequencyHours, CurrentScope, LastViolationCount, LastActionCount, IsRunning, TotalExecutions, EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule, ScopeInheritance, CreatedAt, CreatedBy)
    VALUES ('22222222-2222-2222-2222-222222222225', 'DES Encryption Detection', 'Detects accounts using DES encryption.', 'Security', 'Detection', 1, 0, 0, 1, 24, 0, 0, 0, 0, 0, 'Monitor', 0, 0, 5, 1, 'Inherit', GETUTCDATE(), 'System');

-- ================================================================
-- POLICY RULES (add rules for each policy)
-- ================================================================

-- Dormant 45+ Days
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222201')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, DaysOffset, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222201', 'Dormant 45+ Days', 'LoginDormancy', 'LastSeenAt', 'GreaterThan', 45, 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Dormant 90+ Days
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222215')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, DaysOffset, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222215', 'Dormant 90+ Days', 'LoginDormancy', 'LastSeenAt', 'GreaterThan', 90, 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Dormant 180+ Days
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222216')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, DaysOffset, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222216', 'Dormant 180+ Days', 'LoginDormancy', 'LastSeenAt', 'GreaterThan', 180, 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Dormant 365+ Days
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222217')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, DaysOffset, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222217', 'Dormant 365+ Days', 'LoginDormancy', 'LastSeenAt', 'GreaterThan', 365, 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- New Hire
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222205')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, DaysOffset, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222205', 'New Hire < 30 Days', 'AccountStatus', 'CreatedAt', 'LessThan', 30, 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Termination Processing
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222206')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222206', 'Terminated Status', 'AccountStatus', 'EmploymentStatus', 'Equals', 'Terminated', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Contractor Expiration
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222212')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, DaysOffset, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222212', 'Contract Ending Soon', 'AccountStatus', 'ContractEndDate', 'LessThan', 7, 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Excessive Permissions
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222203')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222203', 'Group Count > 15', 'PermissionCount', 'GroupCount', 'GreaterThan', '15', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- High Risk User
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222208')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222208', 'Risk Score > 75', 'RiskThreshold', 'RiskScore', 'GreaterThan', '75', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Separation of Duties
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222214')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222214', 'Has Conflicting Roles', 'SeparationOfDuties', 'HasConflictingRoles', 'Equals', 'true', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Password Never Expires
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222202')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, ComparisonValue, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222202', 'Password Never Expires', 'AccountStatus', 'PasswordNeverExpires', 'Equals', 'true', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Missing Manager
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222207')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222207', 'No Manager', 'ManagerHierarchy', 'Manager', 'IsNull', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Missing Email
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222218')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222218', 'No Email', 'DataQuality', 'Email', 'IsNull', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Missing Department
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222219')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222219', 'No Department', 'DataQuality', 'Department', 'IsNull', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- Missing Title
IF NOT EXISTS (SELECT 1 FROM CompliancePolicyRule WHERE CompliancePolicyId = '22222222-2222-2222-2222-222222222220')
    INSERT INTO CompliancePolicyRule (Id, CompliancePolicyId, Name, RuleType, FieldName, Operator, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator, CreatedAt)
    VALUES (NEWID(), '22222222-2222-2222-2222-222222222220', 'No Job Title', 'DataQuality', 'Title', 'IsNull', 1.0, 1, 1, 'AND', 'AND', GETUTCDATE());

-- ================================================================
-- COMPLIANCE FRAMEWORKS
-- ================================================================

IF NOT EXISTS (SELECT 1 FROM ComplianceFrameworks WHERE Id = '33333333-3333-3333-3333-333333333301')
    INSERT INTO ComplianceFrameworks (Id, Name, Code, Description, Category, Version, IsActive, IsBuiltIn, ComplianceScore, TotalRequirements, ImplementedControls, Color, CreatedAt, CreatedBy)
    VALUES ('33333333-3333-3333-3333-333333333301', 'SOC 2', 'SOC2', 'SOC 2 Type II Trust Services Criteria', 'Security', '2017', 1, 1, 0, 0, 0, '#3B82F6', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM ComplianceFrameworks WHERE Id = '33333333-3333-3333-3333-333333333302')
    INSERT INTO ComplianceFrameworks (Id, Name, Code, Description, Category, Version, IsActive, IsBuiltIn, ComplianceScore, TotalRequirements, ImplementedControls, Color, CreatedAt, CreatedBy)
    VALUES ('33333333-3333-3333-3333-333333333302', 'NIST CSF', 'NIST', 'NIST Cybersecurity Framework', 'Security', '2.0', 1, 1, 0, 0, 0, '#10B981', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM ComplianceFrameworks WHERE Id = '33333333-3333-3333-3333-333333333303')
    INSERT INTO ComplianceFrameworks (Id, Name, Code, Description, Category, Version, IsActive, IsBuiltIn, ComplianceScore, TotalRequirements, ImplementedControls, Color, CreatedAt, CreatedBy)
    VALUES ('33333333-3333-3333-3333-333333333303', 'ISO 27001', 'ISO27001', 'ISO/IEC 27001:2022 Information Security', 'Security', '2022', 1, 1, 0, 0, 0, '#6366F1', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM ComplianceFrameworks WHERE Id = '33333333-3333-3333-3333-333333333304')
    INSERT INTO ComplianceFrameworks (Id, Name, Code, Description, Category, Version, IsActive, IsBuiltIn, ComplianceScore, TotalRequirements, ImplementedControls, Color, CreatedAt, CreatedBy)
    VALUES ('33333333-3333-3333-3333-333333333304', 'HIPAA', 'HIPAA', 'HIPAA Security Rule', 'Healthcare', '2013', 1, 1, 0, 0, 0, '#F59E0B', GETUTCDATE(), 'System');

IF NOT EXISTS (SELECT 1 FROM ComplianceFrameworks WHERE Id = '33333333-3333-3333-3333-333333333305')
    INSERT INTO ComplianceFrameworks (Id, Name, Code, Description, Category, Version, IsActive, IsBuiltIn, ComplianceScore, TotalRequirements, ImplementedControls, Color, CreatedAt, CreatedBy)
    VALUES ('33333333-3333-3333-3333-333333333305', 'PCI DSS', 'PCIDSS', 'Payment Card Industry Data Security Standard', 'Financial', '4.0', 1, 1, 0, 0, 0, '#EF4444', GETUTCDATE(), 'System');

-- ================================================================
-- FRAMEWORK MAPPINGS (using correct column names: FrameworkId, RequirementId)
-- ================================================================

INSERT INTO ComplianceFrameworkPolicyMappings (Id, FrameworkId, CompliancePolicyId, RequirementId, ComplianceStatus, CoveragePercentage, CreatedAt)
SELECT NEWID(), '33333333-3333-3333-3333-333333333301', '22222222-2222-2222-2222-222222222201', 'CC6.1', 'Unknown', 0, GETUTCDATE()
WHERE NOT EXISTS (SELECT 1 FROM ComplianceFrameworkPolicyMappings WHERE FrameworkId = '33333333-3333-3333-3333-333333333301' AND CompliancePolicyId = '22222222-2222-2222-2222-222222222201');

INSERT INTO ComplianceFrameworkPolicyMappings (Id, FrameworkId, CompliancePolicyId, RequirementId, ComplianceStatus, CoveragePercentage, CreatedAt)
SELECT NEWID(), '33333333-3333-3333-3333-333333333302', '22222222-2222-2222-2222-222222222201', 'PR.AC-1', 'Unknown', 0, GETUTCDATE()
WHERE NOT EXISTS (SELECT 1 FROM ComplianceFrameworkPolicyMappings WHERE FrameworkId = '33333333-3333-3333-3333-333333333302' AND CompliancePolicyId = '22222222-2222-2222-2222-222222222201');

INSERT INTO ComplianceFrameworkPolicyMappings (Id, FrameworkId, CompliancePolicyId, RequirementId, ComplianceStatus, CoveragePercentage, CreatedAt)
SELECT NEWID(), '33333333-3333-3333-3333-333333333305', '22222222-2222-2222-2222-222222222202', 'Req 8.3.9', 'Unknown', 0, GETUTCDATE()
WHERE NOT EXISTS (SELECT 1 FROM ComplianceFrameworkPolicyMappings WHERE FrameworkId = '33333333-3333-3333-3333-333333333305' AND CompliancePolicyId = '22222222-2222-2222-2222-222222222202');

INSERT INTO ComplianceFrameworkPolicyMappings (Id, FrameworkId, CompliancePolicyId, RequirementId, ComplianceStatus, CoveragePercentage, CreatedAt)
SELECT NEWID(), '33333333-3333-3333-3333-333333333301', '22222222-2222-2222-2222-222222222206', 'CC6.2', 'Unknown', 0, GETUTCDATE()
WHERE NOT EXISTS (SELECT 1 FROM ComplianceFrameworkPolicyMappings WHERE FrameworkId = '33333333-3333-3333-3333-333333333301' AND CompliancePolicyId = '22222222-2222-2222-2222-222222222206');

INSERT INTO ComplianceFrameworkPolicyMappings (Id, FrameworkId, CompliancePolicyId, RequirementId, ComplianceStatus, CoveragePercentage, CreatedAt)
SELECT NEWID(), '33333333-3333-3333-3333-333333333303', '22222222-2222-2222-2222-222222222206', 'A.9.2.6', 'Unknown', 0, GETUTCDATE()
WHERE NOT EXISTS (SELECT 1 FROM ComplianceFrameworkPolicyMappings WHERE FrameworkId = '33333333-3333-3333-3333-333333333303' AND CompliancePolicyId = '22222222-2222-2222-2222-222222222206');

INSERT INTO ComplianceFrameworkPolicyMappings (Id, FrameworkId, CompliancePolicyId, RequirementId, ComplianceStatus, CoveragePercentage, CreatedAt)
SELECT NEWID(), '33333333-3333-3333-3333-333333333304', '22222222-2222-2222-2222-222222222206', '164.312(a)(2)(iii)', 'Unknown', 0, GETUTCDATE()
WHERE NOT EXISTS (SELECT 1 FROM ComplianceFrameworkPolicyMappings WHERE FrameworkId = '33333333-3333-3333-3333-333333333304' AND CompliancePolicyId = '22222222-2222-2222-2222-222222222206');

PRINT 'Seed policies completed successfully. 27 policies seeded as DISABLED.';
GO
