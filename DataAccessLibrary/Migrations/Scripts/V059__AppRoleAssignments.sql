-- V059: App Role Assignments
-- Stores enterprise app access assignments from Graph API servicePrincipals/{id}/appRoleAssignedTo.
-- Shows who has access to which enterprise apps (Salesforce, ServiceNow, etc).
-- All tables guarded with IF NOT EXISTS so the migration is safe to replay.

-- ─────────────────────────────────────────────────────────────────────────────
-- AppRoleAssignments: Per-principal role assignments to enterprise apps
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AppRoleAssignments')
BEGIN
    CREATE TABLE [AppRoleAssignments] (
        [Id]                   UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [SourceConnectionId]   UNIQUEIDENTIFIER NOT NULL,
        [AppRoleAssignmentId]  NVARCHAR(200)    NULL,
        [PrincipalId]          UNIQUEIDENTIFIER NULL,
        [PrincipalObjectId]    UNIQUEIDENTIFIER NULL,
        [PrincipalType]        NVARCHAR(50)     NOT NULL,
        [PrincipalDisplayName] NVARCHAR(500)    NULL,
        [ResourceId]           UNIQUEIDENTIFIER NULL,
        [ResourceObjectId]     UNIQUEIDENTIFIER NULL,
        [ResourceDisplayName]  NVARCHAR(500)    NOT NULL,
        [AppRoleId]            UNIQUEIDENTIFIER NULL,
        [AppRoleName]          NVARCHAR(500)    NULL,
        [CreatedDateTime]      DATETIME2        NULL,
        [IsActive]             BIT              NOT NULL DEFAULT 1,
        [LastSyncedAt]         DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_AppRoleAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AppRole_Connection] FOREIGN KEY ([SourceConnectionId])
            REFERENCES [DirectoryConnections] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AppRole_PrincipalObject] FOREIGN KEY ([PrincipalObjectId])
            REFERENCES [Objects] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_AppRole_ResourceObject] FOREIGN KEY ([ResourceObjectId])
            REFERENCES [Objects] ([Id]) ON DELETE NO ACTION
    );

    -- Who has access to what (user detail pages)
    CREATE NONCLUSTERED INDEX [IX_AppRole_Principal]
        ON [AppRoleAssignments] ([PrincipalObjectId], [IsActive])
        INCLUDE ([ResourceDisplayName], [AppRoleName], [CreatedDateTime]);

    -- Who has access to a specific app (app detail pages)
    CREATE NONCLUSTERED INDEX [IX_AppRole_Resource]
        ON [AppRoleAssignments] ([ResourceObjectId], [IsActive])
        INCLUDE ([PrincipalObjectId], [PrincipalType], [PrincipalDisplayName]);

    -- Connection-scoped listing
    CREATE NONCLUSTERED INDEX [IX_AppRole_Connection]
        ON [AppRoleAssignments] ([SourceConnectionId], [IsActive])
        INCLUDE ([PrincipalObjectId], [ResourceDisplayName]);

    -- Dedup on Graph API assignment ID per connection
    CREATE UNIQUE NONCLUSTERED INDEX [UX_AppRole_AssignmentId]
        ON [AppRoleAssignments] ([SourceConnectionId], [AppRoleAssignmentId])
        WHERE [AppRoleAssignmentId] IS NOT NULL;

    PRINT 'Created AppRoleAssignments table';
END;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- EnterpriseApps: Materialized view of service principals with role assignments
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EnterpriseApps')
BEGIN
    CREATE TABLE [EnterpriseApps] (
        [Id]                    UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [SourceConnectionId]    UNIQUEIDENTIFIER NOT NULL,
        [ServicePrincipalId]    NVARCHAR(200)    NOT NULL,
        [ObjectId]              UNIQUEIDENTIFIER NULL,
        [AppId]                 NVARCHAR(200)    NULL,
        [DisplayName]           NVARCHAR(500)    NOT NULL,
        [ServicePrincipalType]  NVARCHAR(100)    NULL,
        [SignInAudience]        NVARCHAR(100)    NULL,
        [Homepage]              NVARCHAR(1000)   NULL,
        [LogoUrl]               NVARCHAR(1000)   NULL,
        [TotalAssignments]      INT              NOT NULL DEFAULT 0,
        [UserAssignments]       INT              NOT NULL DEFAULT 0,
        [GroupAssignments]      INT              NOT NULL DEFAULT 0,
        [IsEnabled]             BIT              NOT NULL DEFAULT 1,
        [LastSyncedAt]          DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_EnterpriseApps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EntApps_Connection] FOREIGN KEY ([SourceConnectionId])
            REFERENCES [DirectoryConnections] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EntApps_Object] FOREIGN KEY ([ObjectId])
            REFERENCES [Objects] ([Id]) ON DELETE SET NULL
    );

    -- Connection-scoped app listing
    CREATE NONCLUSTERED INDEX [IX_EntApps_Connection]
        ON [EnterpriseApps] ([SourceConnectionId], [IsEnabled])
        INCLUDE ([DisplayName], [TotalAssignments]);

    -- Upsert support: one row per connection + service principal
    CREATE UNIQUE NONCLUSTERED INDEX [UX_EntApps_ConnectionSP]
        ON [EnterpriseApps] ([SourceConnectionId], [ServicePrincipalId]);

    PRINT 'Created EnterpriseApps table';
END;
GO

PRINT 'V059: App Role Assignments migration complete';
GO
