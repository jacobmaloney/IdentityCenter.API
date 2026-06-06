-- Seed BusinessRoles with default organizational roles for workflow routing
-- These roles can be linked to AD/Entra ID groups for automatic role assignment

IF NOT EXISTS (SELECT 1 FROM BusinessRoles WHERE Name = 'CEO')
BEGIN
    INSERT INTO BusinessRoles (Id, Name, DisplayName, Description, Category, Icon, Color, SortOrder, IsSystem, IsActive, CanApprove, CanEscalate, CreatedAt, CreatedBy)
    VALUES
    -- Executive Roles
    (NEWID(), 'CEO', 'Chief Executive Officer', 'Organization leader with final approval authority', 'Executive', 'bi-award-fill', '#dc2626', 1, 1, 1, 1, 1, GETUTCDATE(), 'System'),
    (NEWID(), 'CTO', 'Chief Technology Officer', 'Technology strategy and architecture decisions', 'Executive', 'bi-cpu-fill', '#7c3aed', 2, 1, 1, 1, 1, GETUTCDATE(), 'System'),
    (NEWID(), 'CIO', 'Chief Information Officer', 'Information systems and IT operations oversight', 'Executive', 'bi-diagram-3-fill', '#2563eb', 3, 1, 1, 1, 1, GETUTCDATE(), 'System'),
    (NEWID(), 'CFO', 'Chief Financial Officer', 'Financial decisions and budget approvals', 'Executive', 'bi-currency-dollar', '#059669', 4, 1, 1, 1, 1, GETUTCDATE(), 'System'),

    -- Security Roles
    (NEWID(), 'CISO', 'Chief Information Security Officer', 'Security policy enforcement and high-risk access approvals', 'Security', 'bi-shield-lock-fill', '#dc2626', 10, 1, 1, 1, 1, GETUTCDATE(), 'System'),
    (NEWID(), 'Security Analyst', 'Security Analyst', 'Security monitoring and incident response', 'Security', 'bi-shield-check', '#ea580c', 11, 1, 1, 1, 0, GETUTCDATE(), 'System'),
    (NEWID(), 'Security Admin', 'Security Administrator', 'Security infrastructure and access control management', 'Security', 'bi-shield-fill-exclamation', '#b91c1c', 12, 1, 1, 1, 1, GETUTCDATE(), 'System'),

    -- IT Roles
    (NEWID(), 'IT Administrator', 'IT Administrator', 'System administration and infrastructure management', 'IT', 'bi-gear-fill', '#0284c7', 20, 1, 1, 1, 1, GETUTCDATE(), 'System'),
    (NEWID(), 'Helpdesk', 'Helpdesk Support', 'First-line user support and basic access requests', 'IT', 'bi-headset', '#0891b2', 21, 1, 1, 1, 0, GETUTCDATE(), 'System'),
    (NEWID(), 'Network Admin', 'Network Administrator', 'Network infrastructure and connectivity management', 'IT', 'bi-router-fill', '#4f46e5', 22, 1, 1, 1, 1, GETUTCDATE(), 'System'),
    (NEWID(), 'DBA', 'Database Administrator', 'Database systems management and data access', 'IT', 'bi-database-fill', '#7c3aed', 23, 1, 1, 1, 1, GETUTCDATE(), 'System'),

    -- Compliance Roles
    (NEWID(), 'Compliance Officer', 'Compliance Officer', 'Regulatory compliance and audit coordination', 'Compliance', 'bi-clipboard-check-fill', '#059669', 30, 1, 1, 1, 1, GETUTCDATE(), 'System'),
    (NEWID(), 'Auditor', 'Internal Auditor', 'Internal audit and control assessment', 'Compliance', 'bi-search', '#ca8a04', 31, 1, 1, 0, 0, GETUTCDATE(), 'System'),
    (NEWID(), 'Risk Manager', 'Risk Manager', 'Risk assessment and mitigation oversight', 'Compliance', 'bi-exclamation-triangle-fill', '#dc2626', 32, 1, 1, 1, 1, GETUTCDATE(), 'System'),

    -- Operations Roles
    (NEWID(), 'HR Manager', 'HR Manager', 'Human resources management and employee lifecycle', 'Operations', 'bi-people-fill', '#ec4899', 40, 1, 1, 1, 1, GETUTCDATE(), 'System'),
    (NEWID(), 'Facilities Manager', 'Facilities Manager', 'Physical access and building management', 'Operations', 'bi-building', '#64748b', 41, 1, 1, 1, 0, GETUTCDATE(), 'System')
    ;
    PRINT 'Inserted 16 default business roles'
END
ELSE
    PRINT 'Business roles already seeded'

SELECT COUNT(*) AS TotalRoles FROM BusinessRoles;
