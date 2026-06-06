-- V084: SQL Server Permissions table for access governance
-- Stores discovered SQL Server logins, database users, and permissions
-- Mapped to AD Objects via SID or username for access certification

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SqlServerPermissions')
BEGIN
    CREATE TABLE SqlServerPermissions (
        Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        SqlServerInventoryId UNIQUEIDENTIFIER NOT NULL,
        -- Principal info
        PrincipalName NVARCHAR(256) NOT NULL,
        PrincipalType NVARCHAR(50) NOT NULL, -- SqlLogin, WindowsLogin, WindowsGroup, DatabaseUser, ServerRole, DatabaseRole
        PrincipalSid NVARCHAR(200) NULL, -- Windows SID for AD matching
        -- Permission info
        PermissionScope NVARCHAR(20) NOT NULL, -- Server, Database
        DatabaseName NVARCHAR(256) NULL, -- NULL for server-level
        PermissionName NVARCHAR(200) NOT NULL, -- CONTROL, ALTER, db_owner, db_datareader, etc.
        PermissionClass NVARCHAR(50) NOT NULL DEFAULT 'OBJECT', -- SERVER, DATABASE, SCHEMA, OBJECT, ROLE_MEMBERSHIP
        GrantState NVARCHAR(20) NOT NULL DEFAULT 'GRANT', -- GRANT, DENY, GRANT_WITH_GRANT, REVOKE
        -- AD mapping
        ObjectId UNIQUEIDENTIFIER NULL, -- FK to Objects (resolved from SID or username)
        MatchMethod NVARCHAR(50) NULL, -- SID, Username, UPN, Manual
        -- Risk classification
        IsPrivileged BIT NOT NULL DEFAULT 0, -- sysadmin, db_owner, CONTROL SERVER, etc.
        RiskLevel NVARCHAR(20) NULL, -- Critical, High, Medium, Low
        -- Lifecycle
        DiscoveredAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastSeenAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        IsActive BIT NOT NULL DEFAULT 1,
        -- Metadata
        SourceAgentId NVARCHAR(100) NULL,
        CONSTRAINT PK_SqlServerPermissions PRIMARY KEY (Id),
        CONSTRAINT FK_SqlServerPermissions_Server FOREIGN KEY (SqlServerInventoryId) REFERENCES SqlServerInventory(Id)
    );

    CREATE INDEX IX_SqlServerPermissions_Server ON SqlServerPermissions (SqlServerInventoryId);
    CREATE INDEX IX_SqlServerPermissions_ObjectId ON SqlServerPermissions (ObjectId) WHERE ObjectId IS NOT NULL;
    CREATE INDEX IX_SqlServerPermissions_Principal ON SqlServerPermissions (PrincipalName, PermissionScope);
    CREATE INDEX IX_SqlServerPermissions_Privileged ON SqlServerPermissions (IsPrivileged) WHERE IsPrivileged = 1;

    PRINT 'V084: Created SqlServerPermissions table with indexes';
END
ELSE
BEGIN
    PRINT 'V084: SqlServerPermissions table already exists — skipping.';
END
