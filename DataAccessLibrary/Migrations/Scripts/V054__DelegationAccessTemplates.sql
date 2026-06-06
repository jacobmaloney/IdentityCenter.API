-- ============================================================================
-- V054: Delegation & Access Templates
--
-- Implements the role-based delegation model with Access Templates,
-- Managed Scopes, and Delegation Assignments. Inspired by One Identity
-- Active Roles access template system.
--
-- Core concept: Delegation = Template (WHAT) + Principal (WHO) + Scope (WHERE)
--
-- Changes:
--   1. CREATE AccessTemplates table
--   2. CREATE TemplatePermissions table
--   3. CREATE ManagedScopes table
--   4. CREATE DelegationAssignments table
--   5. CREATE DelegationScopeComposites table
--   6. ALTER AspNetRoles: add AccessLevel, IsCustom, DefaultPages
--   7. Indexes for delegation resolution queries
--   8. Seed system access templates (Full Control, Read-Only, Helpdesk, Auditor)
--   9. Update existing roles with AccessLevel values
-- ============================================================================

-- ============================================================================
-- 1. ACCESS TEMPLATES
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AccessTemplates')
BEGIN
    CREATE TABLE [AccessTemplates] (
        [Id]            UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [Name]          NVARCHAR(200) NOT NULL,
        [Description]   NVARCHAR(1000) NULL,
        [IsSystem]      BIT NOT NULL DEFAULT 0,
        [IsActive]      BIT NOT NULL DEFAULT 1,
        [CreatedBy]     NVARCHAR(256) NULL,
        [CreatedAt]     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [ModifiedBy]    NVARCHAR(256) NULL,
        [ModifiedAt]    DATETIME2 NULL,
        CONSTRAINT [PK_AccessTemplates] PRIMARY KEY ([Id])
    );

    CREATE UNIQUE NONCLUSTERED INDEX [IX_AccessTemplates_Name]
        ON [AccessTemplates] ([Name]) WHERE [IsActive] = 1;

    PRINT 'Created AccessTemplates table';
END;
GO

-- ============================================================================
-- 2. TEMPLATE PERMISSIONS
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TemplatePermissions')
BEGIN
    CREATE TABLE [TemplatePermissions] (
        [Id]                UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [AccessTemplateId]  UNIQUEIDENTIFIER NOT NULL,
        [PermissionType]    NVARCHAR(50) NOT NULL,
        [ObjectClass]       NVARCHAR(100) NULL,
        [Target]            NVARCHAR(200) NOT NULL,
        [AccessLevel]       NVARCHAR(20) NOT NULL DEFAULT 'Read',
        [CreatedAt]         DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_TemplatePermissions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TemplatePermissions_Template] FOREIGN KEY ([AccessTemplateId])
            REFERENCES [AccessTemplates] ([Id]) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX [IX_TemplatePermissions_TemplateId]
        ON [TemplatePermissions] ([AccessTemplateId])
        INCLUDE ([PermissionType], [ObjectClass], [Target], [AccessLevel]);

    PRINT 'Created TemplatePermissions table';
END;
GO

-- ============================================================================
-- 3. MANAGED SCOPES
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ManagedScopes')
BEGIN
    CREATE TABLE [ManagedScopes] (
        [Id]              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [Name]            NVARCHAR(200) NOT NULL,
        [Description]     NVARCHAR(1000) NULL,
        [ScopeType]       NVARCHAR(50) NOT NULL,
        [ScopeDefinition] NVARCHAR(MAX) NOT NULL,
        [IsActive]        BIT NOT NULL DEFAULT 1,
        [CreatedBy]       NVARCHAR(256) NULL,
        [CreatedAt]       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [ModifiedBy]      NVARCHAR(256) NULL,
        [ModifiedAt]      DATETIME2 NULL,
        CONSTRAINT [PK_ManagedScopes] PRIMARY KEY ([Id])
    );

    CREATE UNIQUE NONCLUSTERED INDEX [IX_ManagedScopes_Name]
        ON [ManagedScopes] ([Name]) WHERE [IsActive] = 1;

    PRINT 'Created ManagedScopes table';
END;
GO

-- ============================================================================
-- 4. DELEGATION ASSIGNMENTS
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DelegationAssignments')
BEGIN
    CREATE TABLE [DelegationAssignments] (
        [Id]                UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [AccessTemplateId]  UNIQUEIDENTIFIER NOT NULL,
        [PrincipalType]     NVARCHAR(50) NOT NULL,
        [PrincipalId]       NVARCHAR(450) NOT NULL,
        [PrincipalName]     NVARCHAR(256) NULL,
        [ManagedScopeId]    UNIQUEIDENTIFIER NULL,
        [IsActive]          BIT NOT NULL DEFAULT 1,
        [ExpiresAt]         DATETIME2 NULL,
        [CreatedBy]         NVARCHAR(256) NULL,
        [CreatedAt]         DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [ModifiedBy]        NVARCHAR(256) NULL,
        [ModifiedAt]        DATETIME2 NULL,
        CONSTRAINT [PK_DelegationAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DelegationAssignments_Template] FOREIGN KEY ([AccessTemplateId])
            REFERENCES [AccessTemplates] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DelegationAssignments_Scope] FOREIGN KEY ([ManagedScopeId])
            REFERENCES [ManagedScopes] ([Id]) ON DELETE SET NULL
    );

    -- Hot query: resolve all active delegations for a principal
    CREATE NONCLUSTERED INDEX [IX_DelegationAssignments_Principal]
        ON [DelegationAssignments] ([PrincipalType], [PrincipalId])
        INCLUDE ([AccessTemplateId], [ManagedScopeId], [ExpiresAt])
        WHERE [IsActive] = 1;

    -- Find all assignments using a specific template
    CREATE NONCLUSTERED INDEX [IX_DelegationAssignments_Template]
        ON [DelegationAssignments] ([AccessTemplateId])
        WHERE [IsActive] = 1;

    PRINT 'Created DelegationAssignments table';
END;
GO

-- ============================================================================
-- 5. DELEGATION SCOPE COMPOSITES (AND logic across multiple scopes)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DelegationScopeComposites')
BEGIN
    CREATE TABLE [DelegationScopeComposites] (
        [Id]                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [DelegationAssignmentId]  UNIQUEIDENTIFIER NOT NULL,
        [ManagedScopeId]          UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT [PK_DelegationScopeComposites] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DSC_Assignment] FOREIGN KEY ([DelegationAssignmentId])
            REFERENCES [DelegationAssignments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DSC_Scope] FOREIGN KEY ([ManagedScopeId])
            REFERENCES [ManagedScopes] ([Id]) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX [IX_DSC_Assignment]
        ON [DelegationScopeComposites] ([DelegationAssignmentId])
        INCLUDE ([ManagedScopeId]);

    PRINT 'Created DelegationScopeComposites table';
END;
GO

-- ============================================================================
-- 6. EXTEND AspNetRoles WITH ACCESS LEVEL
-- ============================================================================
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'AccessLevel'
)
BEGIN
    ALTER TABLE [AspNetRoles] ADD [AccessLevel] INT NOT NULL DEFAULT 1;
    PRINT 'Added AspNetRoles.AccessLevel';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'IsCustom'
)
BEGIN
    ALTER TABLE [AspNetRoles] ADD [IsCustom] BIT NOT NULL DEFAULT 0;
    PRINT 'Added AspNetRoles.IsCustom';
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'AspNetRoles' AND COLUMN_NAME = 'DefaultPages'
)
BEGIN
    ALTER TABLE [AspNetRoles] ADD [DefaultPages] NVARCHAR(MAX) NULL;
    PRINT 'Added AspNetRoles.DefaultPages';
END;
GO

-- ============================================================================
-- 7. UPDATE EXISTING ROLES WITH ACCESS LEVELS
-- ============================================================================
-- Level 4: Admin
UPDATE [AspNetRoles] SET [AccessLevel] = 4 WHERE [NormalizedName] = 'ADMIN';
-- Level 3: Manager
UPDATE [AspNetRoles] SET [AccessLevel] = 3 WHERE [NormalizedName] IN ('MANAGER', 'USERMANAGER');
-- Level 2: Auditor, Compliance, Security, Reviewer
UPDATE [AspNetRoles] SET [AccessLevel] = 2 WHERE [NormalizedName] IN (
    'AUDITOR', 'COMPLIANCEOFFICER', 'SECURITYOFFICER', 'REVIEWER',
    'FALLBACKREVIEWER', 'AUDITVIEWER'
);
-- Level 1: User, HelpDesk (default deny, must be delegated)
UPDATE [AspNetRoles] SET [AccessLevel] = 1 WHERE [NormalizedName] IN ('USER', 'HELPDESK');
GO

PRINT 'Updated existing roles with access levels';
GO

-- ============================================================================
-- 8. SEED SYSTEM ACCESS TEMPLATES
-- ============================================================================

-- Template: Full Control
IF NOT EXISTS (SELECT 1 FROM [AccessTemplates] WHERE [Name] = 'Full Control' AND [IsSystem] = 1)
BEGIN
    DECLARE @FullControlId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO [AccessTemplates] ([Id], [Name], [Description], [IsSystem], [IsActive], [CreatedBy])
    VALUES (@FullControlId, 'Full Control',
        'Full read/write access to all object types, all attributes, all actions. Use with caution.',
        1, 1, 'System');

    -- Object types: all
    INSERT INTO [TemplatePermissions] ([AccessTemplateId], [PermissionType], [ObjectClass], [Target], [AccessLevel])
    VALUES
        (@FullControlId, 'ObjectType', NULL, '*', 'Read'),
        (@FullControlId, 'Attribute', NULL, '*', 'Write'),
        (@FullControlId, 'Action', NULL, 'Create', 'Execute'),
        (@FullControlId, 'Action', NULL, 'Delete', 'Execute'),
        (@FullControlId, 'Action', NULL, 'Enable', 'Execute'),
        (@FullControlId, 'Action', NULL, 'Disable', 'Execute'),
        (@FullControlId, 'Action', NULL, 'ResetPassword', 'Execute'),
        (@FullControlId, 'Action', NULL, 'ManageGroupMembership', 'Execute'),
        (@FullControlId, 'Action', NULL, 'EditAttributes', 'Execute'),
        (@FullControlId, 'Action', NULL, 'MoveObject', 'Execute'),
        (@FullControlId, 'Page', NULL, '*', 'Read'),
        (@FullControlId, 'CatalogResource', NULL, '*', 'Read');

    PRINT 'Seeded Full Control access template';
END;
GO

-- Template: Read-Only
IF NOT EXISTS (SELECT 1 FROM [AccessTemplates] WHERE [Name] = 'Read-Only' AND [IsSystem] = 1)
BEGIN
    DECLARE @ReadOnlyId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO [AccessTemplates] ([Id], [Name], [Description], [IsSystem], [IsActive], [CreatedBy])
    VALUES (@ReadOnlyId, 'Read-Only',
        'Read-only access to all object types. No write, create, delete, or action permissions.',
        1, 1, 'System');

    INSERT INTO [TemplatePermissions] ([AccessTemplateId], [PermissionType], [ObjectClass], [Target], [AccessLevel])
    VALUES
        (@ReadOnlyId, 'ObjectType', NULL, '*', 'Read'),
        (@ReadOnlyId, 'Attribute', NULL, '*', 'Read'),
        (@ReadOnlyId, 'Page', NULL, '/admin/objects', 'Read'),
        (@ReadOnlyId, 'Page', NULL, '/admin/people', 'Read'),
        (@ReadOnlyId, 'Page', NULL, '/catalog', 'Read'),
        (@ReadOnlyId, 'CatalogResource', NULL, '*', 'Read');

    PRINT 'Seeded Read-Only access template';
END;
GO

-- Template: Helpdesk
IF NOT EXISTS (SELECT 1 FROM [AccessTemplates] WHERE [Name] = 'Helpdesk' AND [IsSystem] = 1)
BEGIN
    DECLARE @HelpdeskId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO [AccessTemplates] ([Id], [Name], [Description], [IsSystem], [IsActive], [CreatedBy])
    VALUES (@HelpdeskId, 'Helpdesk',
        'Help desk operations: view users, reset passwords, enable/disable accounts, edit basic contact info.',
        1, 1, 'System');

    INSERT INTO [TemplatePermissions] ([AccessTemplateId], [PermissionType], [ObjectClass], [Target], [AccessLevel])
    VALUES
        -- Can see users and computers
        (@HelpdeskId, 'ObjectType', NULL, 'user', 'Read'),
        (@HelpdeskId, 'ObjectType', NULL, 'computer', 'Read'),
        -- Can read all attributes
        (@HelpdeskId, 'Attribute', 'user', '*', 'Read'),
        (@HelpdeskId, 'Attribute', 'computer', '*', 'Read'),
        -- Can write basic contact attributes on users
        (@HelpdeskId, 'Attribute', 'user', 'Phone', 'Write'),
        (@HelpdeskId, 'Attribute', 'user', 'MobilePhone', 'Write'),
        (@HelpdeskId, 'Attribute', 'user', 'Department', 'Write'),
        (@HelpdeskId, 'Attribute', 'user', 'JobTitle', 'Write'),
        (@HelpdeskId, 'Attribute', 'user', 'Office', 'Write'),
        (@HelpdeskId, 'Attribute', 'user', 'Description', 'Write'),
        -- Can reset password, enable/disable
        (@HelpdeskId, 'Action', 'user', 'ResetPassword', 'Execute'),
        (@HelpdeskId, 'Action', 'user', 'Enable', 'Execute'),
        (@HelpdeskId, 'Action', 'user', 'Disable', 'Execute'),
        (@HelpdeskId, 'Action', 'computer', 'Enable', 'Execute'),
        (@HelpdeskId, 'Action', 'computer', 'Disable', 'Execute'),
        -- Can see Objects and People pages
        (@HelpdeskId, 'Page', NULL, '/admin/objects', 'Read'),
        (@HelpdeskId, 'Page', NULL, '/admin/people', 'Read');

    PRINT 'Seeded Helpdesk access template';
END;
GO

-- Template: Auditor
IF NOT EXISTS (SELECT 1 FROM [AccessTemplates] WHERE [Name] = 'Auditor' AND [IsSystem] = 1)
BEGIN
    DECLARE @AuditorId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO [AccessTemplates] ([Id], [Name], [Description], [IsSystem], [IsActive], [CreatedBy])
    VALUES (@AuditorId, 'Auditor',
        'Compliance auditor: read-only access to all objects, compliance pages, audit trails, and reports.',
        1, 1, 'System');

    INSERT INTO [TemplatePermissions] ([AccessTemplateId], [PermissionType], [ObjectClass], [Target], [AccessLevel])
    VALUES
        (@AuditorId, 'ObjectType', NULL, '*', 'Read'),
        (@AuditorId, 'Attribute', NULL, '*', 'Read'),
        (@AuditorId, 'Page', NULL, '/admin/objects', 'Read'),
        (@AuditorId, 'Page', NULL, '/admin/people', 'Read'),
        (@AuditorId, 'Page', NULL, '/catalog', 'Read'),
        (@AuditorId, 'Page', NULL, '/compliance', 'Read'),
        (@AuditorId, 'Page', NULL, '/intelligence', 'Read'),
        (@AuditorId, 'Page', NULL, '/admin/audit', 'Read'),
        (@AuditorId, 'Page', NULL, '/admin/violations', 'Read'),
        (@AuditorId, 'Page', NULL, '/admin/reports', 'Read'),
        (@AuditorId, 'CatalogResource', NULL, '*', 'Read');

    PRINT 'Seeded Auditor access template';
END;
GO

-- Template: Group Manager
IF NOT EXISTS (SELECT 1 FROM [AccessTemplates] WHERE [Name] = 'Group Manager' AND [IsSystem] = 1)
BEGIN
    DECLARE @GroupMgrId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO [AccessTemplates] ([Id], [Name], [Description], [IsSystem], [IsActive], [CreatedBy])
    VALUES (@GroupMgrId, 'Group Manager',
        'Manage group membership, description, and properties. Cannot create or delete groups.',
        1, 1, 'System');

    INSERT INTO [TemplatePermissions] ([AccessTemplateId], [PermissionType], [ObjectClass], [Target], [AccessLevel])
    VALUES
        (@GroupMgrId, 'ObjectType', NULL, 'group', 'Read'),
        (@GroupMgrId, 'ObjectType', NULL, 'user', 'Read'),
        (@GroupMgrId, 'Attribute', 'group', '*', 'Read'),
        (@GroupMgrId, 'Attribute', 'group', 'Description', 'Write'),
        (@GroupMgrId, 'Attribute', 'group', 'DisplayName', 'Write'),
        (@GroupMgrId, 'Action', 'group', 'ManageGroupMembership', 'Execute'),
        (@GroupMgrId, 'Page', NULL, '/admin/objects', 'Read'),
        (@GroupMgrId, 'Page', NULL, '/catalog', 'Read'),
        (@GroupMgrId, 'CatalogResource', NULL, 'group', 'Read');

    PRINT 'Seeded Group Manager access template';
END;
GO

-- Template: User Account Manager
IF NOT EXISTS (SELECT 1 FROM [AccessTemplates] WHERE [Name] = 'User Account Manager' AND [IsSystem] = 1)
BEGIN
    DECLARE @UserMgrId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO [AccessTemplates] ([Id], [Name], [Description], [IsSystem], [IsActive], [CreatedBy])
    VALUES (@UserMgrId, 'User Account Manager',
        'Full user lifecycle management: create, edit, enable/disable, reset passwords, manage group membership.',
        1, 1, 'System');

    INSERT INTO [TemplatePermissions] ([AccessTemplateId], [PermissionType], [ObjectClass], [Target], [AccessLevel])
    VALUES
        (@UserMgrId, 'ObjectType', NULL, 'user', 'Read'),
        (@UserMgrId, 'ObjectType', NULL, 'group', 'Read'),
        (@UserMgrId, 'Attribute', 'user', '*', 'Write'),
        (@UserMgrId, 'Attribute', 'group', '*', 'Read'),
        (@UserMgrId, 'Action', 'user', 'Create', 'Execute'),
        (@UserMgrId, 'Action', 'user', 'Delete', 'Execute'),
        (@UserMgrId, 'Action', 'user', 'Enable', 'Execute'),
        (@UserMgrId, 'Action', 'user', 'Disable', 'Execute'),
        (@UserMgrId, 'Action', 'user', 'ResetPassword', 'Execute'),
        (@UserMgrId, 'Action', 'user', 'EditAttributes', 'Execute'),
        (@UserMgrId, 'Action', 'group', 'ManageGroupMembership', 'Execute'),
        (@UserMgrId, 'Page', NULL, '/admin/objects', 'Read'),
        (@UserMgrId, 'Page', NULL, '/admin/people', 'Read'),
        (@UserMgrId, 'Page', NULL, '/catalog', 'Read'),
        (@UserMgrId, 'CatalogResource', NULL, '*', 'Read');

    PRINT 'Seeded User Account Manager access template';
END;
GO

PRINT 'V054__DelegationAccessTemplates migration complete';
GO
