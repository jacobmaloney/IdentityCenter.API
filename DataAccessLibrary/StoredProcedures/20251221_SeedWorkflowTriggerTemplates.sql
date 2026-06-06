-- ============================================
-- Seed Workflow Trigger Templates
-- Run this script on IdentityCenter13 database
-- ============================================

USE IdentityCenter13;
GO

-- Check if templates already exist
IF (SELECT COUNT(*) FROM WorkflowTriggerTemplates) > 0
BEGIN
    PRINT 'Workflow trigger templates already exist. Skipping seed.';
    RETURN;
END

PRINT 'Seeding workflow trigger templates...';

-- ========== COMPLIANCE TEMPLATES ==========

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Quarterly Access Review',
    'Automatically create access review campaigns every quarter to maintain SOX compliance',
    'Compliance',
    'bi-calendar-check',
    'text-info',
    1,
    1,
    '{"TriggerType":"Scheduled","CronExpression":"0 0 9 1 1,4,7,10 ?","Priority":10,"Actions":[{"ActionType":"CreateAccessReview","ActionConfig":{"CampaignName":"Quarterly Review - {{Date}}","ReviewType":"GroupMembership","DurationDays":14}},{"ActionType":"SendEmail","ActionConfig":{"To":"compliance@company.com","Subject":"Quarterly Access Review Started","Body":"A new quarterly access review campaign has been automatically created."}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Monthly Privileged Access Review',
    'Monthly review of users with elevated/admin privileges for security compliance',
    'Compliance',
    'bi-shield-check',
    'text-warning',
    1,
    2,
    '{"TriggerType":"Scheduled","CronExpression":"0 0 9 1 * ?","Priority":5,"Actions":[{"ActionType":"CreateAccessReview","ActionConfig":{"CampaignName":"Privileged Access Review - {{Date}}","ReviewType":"PrivilegedAccess","DurationDays":7}},{"ActionType":"CreateAuditLog","ActionConfig":{"Message":"Monthly privileged access review initiated"}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Annual Certification Campaign',
    'Yearly comprehensive access certification for regulatory compliance',
    'Compliance',
    'bi-award',
    'text-primary',
    1,
    3,
    '{"TriggerType":"Scheduled","CronExpression":"0 0 9 1 1 ?","Priority":1,"Actions":[{"ActionType":"CreateAccessReview","ActionConfig":{"CampaignName":"Annual Access Certification {{Date}}","ReviewType":"AllAccess","DurationDays":30}},{"ActionType":"SendEmail","ActionConfig":{"To":"executives@company.com","Subject":"Annual Access Certification Started","Body":"The annual access certification campaign has begun. Please complete your reviews within 30 days."}}]}',
    GETUTCDATE(),
    'System'
);

-- ========== LIFECYCLE TEMPLATES ==========

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'New Employee Onboarding',
    'Trigger workflow when a new user account is created in the directory',
    'Lifecycle',
    'bi-person-plus',
    'text-success',
    1,
    10,
    '{"TriggerType":"ObjectLifecycle","EventTypes":["ObjectCreated"],"Conditions":[{"ConditionType":"ObjectClass","Operator":"Equals","Value":"user"}],"Priority":20,"Actions":[{"ActionType":"StartWorkflow","ActionConfig":{"WorkflowName":"New Employee Provisioning"}},{"ActionType":"SendEmail","ActionConfig":{"To":"hr@company.com","Subject":"New Employee Account Created","Body":"A new user account has been created and is ready for provisioning."}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Employee Termination',
    'Trigger immediate access revocation when an employee is terminated',
    'Lifecycle',
    'bi-person-x',
    'text-danger',
    1,
    11,
    '{"TriggerType":"ObjectLifecycle","EventTypes":["ObjectDisabled","ObjectDeleted"],"Conditions":[{"ConditionType":"ObjectClass","Operator":"Equals","Value":"user"}],"Priority":1,"Actions":[{"ActionType":"StartWorkflow","ActionConfig":{"WorkflowName":"Emergency Access Revocation"}},{"ActionType":"SendEmail","ActionConfig":{"To":"security@company.com","Subject":"URGENT: User Account Disabled","Body":"A user account has been disabled. Please verify all access has been revoked."}},{"ActionType":"CreateAuditLog","ActionConfig":{"Message":"User termination trigger fired - access revocation initiated"}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Manager Change',
    'Review and update access when an employee''s manager changes',
    'Lifecycle',
    'bi-diagram-3',
    'text-info',
    1,
    12,
    '{"TriggerType":"ObjectLifecycle","EventTypes":["ObjectModified"],"Conditions":[{"ConditionType":"ObjectClass","Operator":"Equals","Value":"user"},{"ConditionType":"ObjectAttribute","FieldName":"manager","Operator":"Changed","Value":""}],"Priority":30,"Actions":[{"ActionType":"StartWorkflow","ActionConfig":{"WorkflowName":"Manager Change Review"}}]}',
    GETUTCDATE(),
    'System'
);

-- ========== SECURITY TEMPLATES ==========

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Sensitive Group Membership Alert',
    'Alert when users are added to security-sensitive groups (Domain Admins, etc.)',
    'Security',
    'bi-exclamation-triangle',
    'text-danger',
    1,
    20,
    '{"TriggerType":"ObjectLifecycle","EventTypes":["GroupMemberAdded"],"Conditions":[{"ConditionType":"GroupAttribute","FieldName":"name","Operator":"In","Value":"Domain Admins,Enterprise Admins,Schema Admins,Administrators"}],"Priority":1,"Actions":[{"ActionType":"SendEmail","ActionConfig":{"To":"security@company.com","Subject":"ALERT: Sensitive Group Membership Change","Body":"A user has been added to a highly privileged group. Please review immediately."}},{"ActionType":"CreateAuditLog","ActionConfig":{"Message":"Sensitive group membership change detected"}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Password Expiration Warning',
    'Send reminder emails when passwords are about to expire',
    'Security',
    'bi-key',
    'text-warning',
    1,
    21,
    '{"TriggerType":"Scheduled","CronExpression":"0 0 8 * * ?","Priority":50,"Actions":[{"ActionType":"SendEmail","ActionConfig":{"To":"{{user.email}}","Subject":"Password Expiration Reminder","Body":"Your password will expire soon. Please change it to maintain access."}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Inactive Account Review',
    'Weekly review of accounts that haven''t logged in for 90+ days',
    'Security',
    'bi-clock-history',
    'text-secondary',
    1,
    22,
    '{"TriggerType":"Scheduled","CronExpression":"0 0 9 ? * MON","Priority":40,"Actions":[{"ActionType":"CreateAccessReview","ActionConfig":{"CampaignName":"Inactive Account Review - {{Date}}","ReviewType":"InactiveAccounts","DurationDays":7}},{"ActionType":"SendEmail","ActionConfig":{"To":"it-operations@company.com","Subject":"Weekly Inactive Account Review","Body":"A new inactive account review has been created. Please review and disable dormant accounts."}}]}',
    GETUTCDATE(),
    'System'
);

-- ========== NOTIFICATION TEMPLATES ==========

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Daily Sync Status Report',
    'Send daily summary of directory synchronization results',
    'Notification',
    'bi-envelope',
    'text-primary',
    1,
    30,
    '{"TriggerType":"Scheduled","CronExpression":"0 0 7 * * ?","Priority":60,"Actions":[{"ActionType":"SendEmail","ActionConfig":{"To":"it-admins@company.com","Subject":"Daily Sync Status Report","Body":"Daily synchronization summary attached."}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Weekly Compliance Summary',
    'Weekly email summary of compliance status and policy violations',
    'Notification',
    'bi-clipboard-data',
    'text-info',
    1,
    31,
    '{"TriggerType":"Scheduled","CronExpression":"0 0 8 ? * FRI","Priority":50,"Actions":[{"ActionType":"SendEmail","ActionConfig":{"To":"compliance@company.com","Subject":"Weekly Compliance Summary","Body":"Weekly compliance status report attached."}},{"ActionType":"CreateAuditLog","ActionConfig":{"Message":"Weekly compliance summary generated and sent"}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Access Review Reminder',
    'Daily reminder for pending access review items',
    'Notification',
    'bi-bell',
    'text-warning',
    1,
    32,
    '{"TriggerType":"Scheduled","CronExpression":"0 0 9 * * MON-FRI","Priority":30,"Actions":[{"ActionType":"SendEmail","ActionConfig":{"To":"{{reviewer.email}}","Subject":"Pending Access Reviews Reminder","Body":"You have pending access review items that require your attention."}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Sync Completion Webhook',
    'Call external webhook when directory sync completes',
    'Notification',
    'bi-link-45deg',
    'text-secondary',
    1,
    40,
    '{"TriggerType":"SyncCompletion","EventTypes":["SyncProjectCompleted"],"Priority":50,"Actions":[{"ActionType":"CallWebhook","ActionConfig":{"Url":"https://api.example.com/webhook/sync-complete","Method":"POST"}}]}',
    GETUTCDATE(),
    'System'
);

INSERT INTO WorkflowTriggerTemplates (Id, Name, Description, Category, Icon, Color, IsSystem, SortOrder, TemplateJson, CreatedAt, CreatedBy)
VALUES (
    NEWID(),
    'Group Owner Notification',
    'Notify group owners when members are added or removed',
    'Notification',
    'bi-people',
    'text-success',
    1,
    41,
    '{"TriggerType":"ObjectLifecycle","EventTypes":["GroupMemberAdded","GroupMemberRemoved"],"Priority":40,"Actions":[{"ActionType":"SendEmail","ActionConfig":{"To":"{{group.owner.email}}","Subject":"Group Membership Changed: {{group.name}}","Body":"A membership change has occurred in a group you own."}}]}',
    GETUTCDATE(),
    'System'
);

-- Verify results
SELECT COUNT(*) AS TemplateCount FROM WorkflowTriggerTemplates;
SELECT Name, Category, Icon FROM WorkflowTriggerTemplates ORDER BY SortOrder;

PRINT 'Successfully seeded 14 workflow trigger templates!';
GO
