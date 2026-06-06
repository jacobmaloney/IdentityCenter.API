-- Create BusinessRoles and BusinessRoleMembers tables for IdentityCenter13
-- Run this if EF migrations are pointing to wrong database

-- Check if tables already exist
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BusinessRoles')
BEGIN
    CREATE TABLE [dbo].[BusinessRoles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [DisplayName] nvarchar(200) NULL,
        [Description] nvarchar(1000) NULL,
        [Category] nvarchar(50) NULL,
        [ADGroupDN] nvarchar(500) NULL,
        [ADGroupObjectId] uniqueidentifier NULL,
        [LinkedGroupId] uniqueidentifier NULL,
        [Icon] nvarchar(50) NULL,
        [Color] nvarchar(20) NULL,
        [SortOrder] int NOT NULL DEFAULT 0,
        [IsSystem] bit NOT NULL DEFAULT 0,
        [IsActive] bit NOT NULL DEFAULT 1,
        [CanApprove] bit NOT NULL DEFAULT 1,
        [CanEscalate] bit NOT NULL DEFAULT 1,
        [FallbackEmail] nvarchar(200) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] nvarchar(100) NULL,
        [ModifiedAt] datetime2 NULL,
        [ModifiedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_BusinessRoles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BusinessRoles_Objects_LinkedGroupId] FOREIGN KEY ([LinkedGroupId]) REFERENCES [Objects] ([Id]) ON DELETE SET NULL
    );

    CREATE INDEX [IX_BusinessRoles_LinkedGroupId] ON [BusinessRoles] ([LinkedGroupId]);
    CREATE UNIQUE INDEX [IX_BusinessRoles_Name] ON [BusinessRoles] ([Name]);
    CREATE INDEX [IX_BusinessRoles_Category] ON [BusinessRoles] ([Category]);

    PRINT 'Created BusinessRoles table'
END
ELSE
    PRINT 'BusinessRoles table already exists'

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BusinessRoleMembers')
BEGIN
    CREATE TABLE [dbo].[BusinessRoleMembers] (
        [Id] uniqueidentifier NOT NULL,
        [BusinessRoleId] uniqueidentifier NOT NULL,
        [IdentityId] uniqueidentifier NOT NULL,
        [DisplayName] nvarchar(200) NULL,
        [Email] nvarchar(200) NULL,
        [LastVerifiedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [IsDirectAssignment] bit NOT NULL DEFAULT 0,
        CONSTRAINT [PK_BusinessRoleMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BusinessRoleMembers_BusinessRoles_BusinessRoleId] FOREIGN KEY ([BusinessRoleId]) REFERENCES [BusinessRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_BusinessRoleMembers_Identities_IdentityId] FOREIGN KEY ([IdentityId]) REFERENCES [Identities] ([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_BusinessRoleMembers_BusinessRoleId] ON [BusinessRoleMembers] ([BusinessRoleId]);
    CREATE INDEX [IX_BusinessRoleMembers_IdentityId] ON [BusinessRoleMembers] ([IdentityId]);
    CREATE UNIQUE INDEX [IX_BusinessRoleMembers_BusinessRoleId_IdentityId] ON [BusinessRoleMembers] ([BusinessRoleId], [IdentityId]);

    PRINT 'Created BusinessRoleMembers table'
END
ELSE
    PRINT 'BusinessRoleMembers table already exists'

-- Seed default Business Roles
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
