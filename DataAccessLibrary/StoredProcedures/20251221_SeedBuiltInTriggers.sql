-- ============================================
-- Seed Built-In Workflow Triggers
-- Run this on IdentityCenter13 database
-- ============================================

USE IdentityCenter13;
GO

-- Check if triggers already exist
IF (SELECT COUNT(*) FROM WorkflowTriggers) > 0
BEGIN
    PRINT 'Workflow triggers already exist. Skipping seed.';
    RETURN;
END

PRINT 'Seeding built-in workflow triggers...';

-- Stable GUIDs for referential integrity
DECLARE @triggerInactiveUser90 UNIQUEIDENTIFIER = 'A1000001-0001-0001-0001-000000000001';
DECLARE @triggerInactiveUser180 UNIQUEIDENTIFIER = 'A1000001-0001-0001-0001-000000000002';
DECLARE @triggerNewUserProvisioning UNIQUEIDENTIFIER = 'A1000002-0001-0001-0001-000000000001';
DECLARE @triggerUserTermination UNIQUEIDENTIFIER = 'A1000002-0001-0001-0001-000000000002';
DECLARE @triggerManagerChange UNIQUEIDENTIFIER = 'A1000002-0001-0001-0001-000000000003';
DECLARE @triggerSensitiveGroupAdd UNIQUEIDENTIFIER = 'A1000003-0001-0001-0001-000000000001';
DECLARE @triggerPrivilegedGroupAdd UNIQUEIDENTIFIER = 'A1000003-0001-0001-0001-000000000002';
DECLARE @triggerPasswordExpiring UNIQUEIDENTIFIER = 'A1000004-0001-0001-0001-000000000001';
DECLARE @triggerOrphanedAccount UNIQUEIDENTIFIER = 'A1000005-0001-0001-0001-000000000001';

-- ================================================================
-- COMPLIANCE TRIGGERS
-- ================================================================

-- 1. Inactive User 90 Days - Manager Review
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerInactiveUser90,
    'Inactive User 90 Days - Manager Review',
    'When a user has been inactive for 90 days, notify their manager for review. If no action in 7 days, escalate.',
    'Compliance',
    'PolicyViolation',
    '["PolicyViolationDetected"]',
    0, 1, 10, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive, TimeoutMinutes)
VALUES (
    'B1000001-0001-0001-0001-000000000001',
    @triggerInactiveUser90,
    'SendEmail',
    'Notify Manager',
    '{"recipientType":"Manager","templateName":"InactiveUserReview","subject":"Action Required: Inactive User Review - {{TargetDisplayName}}"}',
    1, 1, 10080
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000001-0001-0001-0001-000000000002',
    @triggerInactiveUser90,
    'CreateAccessReview',
    'Create Review Campaign',
    '{"campaignName":"Inactive User Review - 90 Days","reviewerType":"Manager","dueInDays":7}',
    2, 1
);

INSERT INTO TriggerConditions (Id, TriggerId, ConditionType, FieldName, Operator, Value, LogicalGroup, SortOrder, IsActive)
VALUES (
    'C1000001-0001-0001-0001-000000000001',
    @triggerInactiveUser90,
    'PolicySeverity',
    'Severity',
    'In',
    '["Medium","High","Critical"]',
    'AND', 1, 1
);

-- 2. Inactive User 180 Days - Auto Disable
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerInactiveUser180,
    'Inactive User 180 Days - Auto Disable',
    'When a user has been inactive for 180 days, automatically disable the account and notify manager.',
    'Compliance',
    'PolicyViolation',
    '["PolicyViolationDetected"]',
    0, 1, 20, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000001-0001-0001-0001-000000000003',
    @triggerInactiveUser180,
    'DisableAccount',
    'Disable User Account',
    '{"reason":"Inactive for 180+ days","notifyManager":true}',
    1, 1
);

-- ================================================================
-- LIFECYCLE TRIGGERS
-- ================================================================

-- 3. New User Provisioning
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerNewUserProvisioning,
    'New User - Manager Notification',
    'When a new user is synced, notify their manager to confirm the hire and review initial access.',
    'Lifecycle',
    'ObjectLifecycle',
    '["ObjectCreated","IdentityCreated"]',
    0, 1, 10, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000002-0001-0001-0001-000000000001',
    @triggerNewUserProvisioning,
    'SendEmail',
    'Welcome Manager Notification',
    '{"recipientType":"Manager","templateName":"NewUserOnboarding","subject":"New Team Member: {{TargetDisplayName}}"}',
    1, 1
);

INSERT INTO TriggerConditions (Id, TriggerId, ConditionType, FieldName, Operator, Value, LogicalGroup, SortOrder, IsActive)
VALUES (
    'C1000002-0001-0001-0001-000000000001',
    @triggerNewUserProvisioning,
    'ObjectClass',
    'ObjectClass',
    'Equals',
    'user',
    'AND', 1, 1
);

-- 4. User Termination - Offboarding
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerUserTermination,
    'User Disabled - Offboarding Alert',
    'When a user account is disabled, notify IT and manager to complete offboarding tasks.',
    'Lifecycle',
    'ObjectLifecycle',
    '["ObjectDisabled","IdentityDeactivated"]',
    0, 1, 5, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000002-0001-0001-0001-000000000002',
    @triggerUserTermination,
    'SendEmail',
    'Offboarding Notification',
    '{"recipientType":"RoleHolder","roleName":"IT Administrator","templateName":"UserOffboarding","subject":"Offboarding Required: {{TargetDisplayName}}"}',
    1, 1
);

-- 5. Manager Change - Access Review
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerManagerChange,
    'Manager Change - Access Review',
    'When a user''s manager changes, create an access review for the new manager to certify existing access.',
    'Lifecycle',
    'ObjectLifecycle',
    '["ObjectModified","IdentityModified"]',
    0, 1, 50, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000002-0001-0001-0001-000000000003',
    @triggerManagerChange,
    'CreateAccessReview',
    'New Manager Access Review',
    '{"campaignName":"Manager Change Review - {{TargetDisplayName}}","reviewerType":"Manager","dueInDays":14}',
    1, 1
);

INSERT INTO TriggerConditions (Id, TriggerId, ConditionType, FieldName, Operator, Value, LogicalGroup, SortOrder, IsActive)
VALUES (
    'C1000002-0001-0001-0001-000000000002',
    @triggerManagerChange,
    'ChangedAttribute',
    'manager',
    'Equals',
    'true',
    'AND', 1, 1
);

-- ================================================================
-- SECURITY TRIGGERS
-- ================================================================

-- 6. Sensitive Group Addition
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerSensitiveGroupAdd,
    'Sensitive Group - Owner Approval Required',
    'When a user is added to a group tagged ''Sensitive'', require group owner approval.',
    'Security',
    'ObjectLifecycle',
    '["GroupMemberAdded"]',
    0, 1, 5, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000003-0001-0001-0001-000000000001',
    @triggerSensitiveGroupAdd,
    'StartWorkflow',
    'Group Owner Approval',
    '{"workflowName":"Group Membership Approval","assignReviewer":"GroupOwner","timeoutDays":3,"timeoutAction":"Deny"}',
    1, 1
);

INSERT INTO TriggerConditions (Id, TriggerId, ConditionType, FieldName, Operator, Value, LogicalGroup, SortOrder, IsActive)
VALUES (
    'C1000003-0001-0001-0001-000000000001',
    @triggerSensitiveGroupAdd,
    'GroupComplianceTags',
    'Tags',
    'Contains',
    'Sensitive',
    'AND', 1, 1
);

-- 7. Privileged Group Addition - Multi-Level Approval
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerPrivilegedGroupAdd,
    'Privileged Group - Multi-Level Approval',
    'When a user is added to a privileged admin group, require manager + CISO approval.',
    'Security',
    'ObjectLifecycle',
    '["GroupMemberAdded"]',
    0, 1, 1, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000003-0001-0001-0001-000000000002',
    @triggerPrivilegedGroupAdd,
    'StartWorkflow',
    'Multi-Level Approval',
    '{"workflowName":"High Risk Access Approval","approvalChain":["Manager","RoleHolder:CISO"],"timeoutDays":2,"timeoutAction":"Deny"}',
    1, 1
);

INSERT INTO TriggerConditions (Id, TriggerId, ConditionType, FieldName, Operator, Value, LogicalGroup, SortOrder, IsActive)
VALUES (
    'C1000003-0001-0001-0001-000000000002',
    @triggerPrivilegedGroupAdd,
    'GroupComplianceTags',
    'Tags',
    'Contains',
    'Privileged',
    'AND', 1, 1
);

-- ================================================================
-- NOTIFICATION TRIGGERS
-- ================================================================

-- 8. Password Expiring
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerPasswordExpiring,
    'Password Expiring - User Notification',
    'Notify users when their password will expire in 14 days.',
    'Notification',
    'ObjectLifecycle',
    '["ObjectPasswordExpiring"]',
    0, 1, 80, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000004-0001-0001-0001-000000000001',
    @triggerPasswordExpiring,
    'SendEmail',
    'Password Expiration Warning',
    '{"recipientType":"TargetUser","templateName":"PasswordExpiring","subject":"Your password will expire in {{DaysUntilExpiry}} days"}',
    1, 1
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000004-0001-0001-0001-000000000002',
    @triggerPasswordExpiring,
    'SendTeamsMessage',
    'Teams Reminder',
    '{"recipientType":"TargetUser","message":"Your password expires in {{DaysUntilExpiry}} days. Please change it soon."}',
    2, 1
);

-- 9. Orphaned Account Detection
INSERT INTO WorkflowTriggers (Id, Name, Description, Category, TriggerType, EventTypes, IsActive, IsSystem, Priority, CreatedAt, CreatedBy)
VALUES (
    @triggerOrphanedAccount,
    'Orphaned Account - IT Review',
    'When an account has no manager, flag for IT review.',
    'Compliance',
    'PolicyViolation',
    '["PolicyViolationDetected"]',
    0, 1, 60, GETUTCDATE(), 'System'
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000005-0001-0001-0001-000000000001',
    @triggerOrphanedAccount,
    'SetIdentityTag',
    'Tag as Orphaned',
    '{"tagName":"Orphaned","action":"Add"}',
    1, 1
);

INSERT INTO TriggerActions (Id, TriggerId, ActionType, ActionName, ActionConfig, ExecutionOrder, IsActive)
VALUES (
    'B1000005-0001-0001-0001-000000000002',
    @triggerOrphanedAccount,
    'SendEmail',
    'Notify IT Team',
    '{"recipientType":"RoleHolder","roleName":"IT Administrator","templateName":"OrphanedAccountAlert","subject":"Orphaned Account: {{TargetDisplayName}}"}',
    2, 1
);

-- Verify results
SELECT COUNT(*) AS TriggerCount FROM WorkflowTriggers;
SELECT Name, Category, TriggerType, IsActive FROM WorkflowTriggers ORDER BY Category, Priority;

PRINT 'Successfully seeded 9 built-in workflow triggers!';
GO
