-- V083: Add CostCenter to LicensePools + Seed License Administrator delegation template

-- 1. Add CostCenter to LicensePools
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'CostCenter')
    ALTER TABLE LicensePools ADD CostCenter NVARCHAR(100) NULL;

-- 2. Seed "License Administrator" delegation template
IF NOT EXISTS (SELECT 1 FROM AccessTemplates WHERE Name = N'License Administrator')
BEGIN
    DECLARE @templateId UNIQUEIDENTIFIER = NEWID();
    DECLARE @now DATETIME2 = GETUTCDATE();

    INSERT INTO AccessTemplates (Id, Name, Description, IsSystem, IsActive, CreatedAt, CreatedBy)
    VALUES (
        @templateId,
        N'License Administrator',
        N'Full access to License Center, analytics, cost tracking, and license compliance. Can view user accounts and computer objects for license assignment context. Ideal for IT asset managers and license compliance officers.',
        1, -- System template
        1, -- Active
        @now,
        N'System (V083 Migration)'
    );

    -- Page permissions (PermissionType = 'Page', Target = URL, AccessLevel = Read/Write)
    INSERT INTO TemplatePermissions (Id, AccessTemplateId, PermissionType, Target, AccessLevel, CreatedAt)
    VALUES
        (NEWID(), @templateId, N'Page', N'/admin/license-center', N'Write', @now),
        (NEWID(), @templateId, N'Page', N'/admin/access-analytics', N'Read', @now),
        (NEWID(), @templateId, N'Page', N'/admin/analytics-center', N'Read', @now),
        (NEWID(), @templateId, N'Page', N'/admin/audit', N'Read', @now),
        (NEWID(), @templateId, N'Page', N'/identity/identities', N'Read', @now),
        (NEWID(), @templateId, N'Page', N'/admin/objects', N'Read', @now),
        (NEWID(), @templateId, N'Page', N'/admin/compliance-center', N'Read', @now);

    -- Object type permissions (read-only for license-assignment context)
    INSERT INTO TemplatePermissions (Id, AccessTemplateId, PermissionType, ObjectClass, Target, AccessLevel, CreatedAt)
    VALUES
        (NEWID(), @templateId, N'ObjectType', N'user', N'*', N'Read', @now),
        (NEWID(), @templateId, N'ObjectType', N'computer', N'*', N'Read', @now),
        (NEWID(), @templateId, N'ObjectType', N'group', N'*', N'Read', @now);

    PRINT 'V083: Seeded License Administrator template';
END
ELSE
BEGIN
    PRINT 'V083: License Administrator template already exists — skipping.';
END
