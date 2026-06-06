-- V047: Business Role Entitlement Management
-- Adds entitlements, policies, membership rules, and provisioning log tables
-- Plus enforcement columns on BusinessRoles

-- =============================================
-- 1. New columns on BusinessRoles
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('BusinessRoles') AND name = 'EnforcementMode')
BEGIN
    ALTER TABLE BusinessRoles ADD EnforcementMode nvarchar(20) NOT NULL DEFAULT 'Monitor';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('BusinessRoles') AND name = 'HasMembershipRules')
BEGIN
    ALTER TABLE BusinessRoles ADD HasMembershipRules bit NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('BusinessRoles') AND name = 'LastEnforcementAt')
BEGIN
    ALTER TABLE BusinessRoles ADD LastEnforcementAt datetime2 NULL;
END
GO

-- =============================================
-- 2. BusinessRoleEntitlements
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BusinessRoleEntitlements')
BEGIN
    CREATE TABLE BusinessRoleEntitlements (
        Id uniqueidentifier NOT NULL DEFAULT NEWID(),
        BusinessRoleId uniqueidentifier NOT NULL,
        EntitlementType nvarchar(50) NOT NULL DEFAULT 'ADGroup',
        TargetObjectId uniqueidentifier NOT NULL,
        TargetDN nvarchar(1000) NULL,
        TargetDisplayName nvarchar(500) NULL,
        IsAutoProvision bit NOT NULL DEFAULT 1,
        IsAutoDeprovision bit NOT NULL DEFAULT 0,
        IsActive bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(200) NULL,
        ModifiedAt datetime2 NULL,
        ModifiedBy nvarchar(200) NULL,
        CONSTRAINT PK_BusinessRoleEntitlements PRIMARY KEY (Id),
        CONSTRAINT FK_BusinessRoleEntitlements_BusinessRoles
            FOREIGN KEY (BusinessRoleId) REFERENCES BusinessRoles(Id) ON DELETE CASCADE
    );

    -- Unique: one entitlement per type+target per role
    CREATE UNIQUE NONCLUSTERED INDEX UX_BusinessRoleEntitlements_RoleTypeTarget
        ON BusinessRoleEntitlements (BusinessRoleId, EntitlementType, TargetObjectId)
        WHERE IsActive = 1;

    CREATE NONCLUSTERED INDEX IX_BusinessRoleEntitlements_BusinessRoleId
        ON BusinessRoleEntitlements (BusinessRoleId);
END
GO

-- =============================================
-- 3. BusinessRolePolicies
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BusinessRolePolicies')
BEGIN
    CREATE TABLE BusinessRolePolicies (
        Id uniqueidentifier NOT NULL DEFAULT NEWID(),
        BusinessRoleId uniqueidentifier NOT NULL,
        CompliancePolicyId uniqueidentifier NOT NULL,
        IsActive bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(200) NULL,
        CONSTRAINT PK_BusinessRolePolicies PRIMARY KEY (Id),
        CONSTRAINT FK_BusinessRolePolicies_BusinessRoles
            FOREIGN KEY (BusinessRoleId) REFERENCES BusinessRoles(Id) ON DELETE CASCADE,
        CONSTRAINT FK_BusinessRolePolicies_CompliancePolicies
            FOREIGN KEY (CompliancePolicyId) REFERENCES CompliancePolicies(Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE NONCLUSTERED INDEX UX_BusinessRolePolicies_RolePolicy
        ON BusinessRolePolicies (BusinessRoleId, CompliancePolicyId);

    CREATE NONCLUSTERED INDEX IX_BusinessRolePolicies_BusinessRoleId
        ON BusinessRolePolicies (BusinessRoleId);
END
GO

-- =============================================
-- 4. BusinessRoleMembershipRules
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BusinessRoleMembershipRules')
BEGIN
    CREATE TABLE BusinessRoleMembershipRules (
        Id uniqueidentifier NOT NULL DEFAULT NEWID(),
        BusinessRoleId uniqueidentifier NOT NULL,
        FieldName nvarchar(100) NOT NULL,
        Operator nvarchar(50) NOT NULL DEFAULT 'Equals',
        Value nvarchar(500) NULL,
        LogicalOperator nvarchar(10) NOT NULL DEFAULT 'AND',
        RuleGroupId int NOT NULL DEFAULT 0,
        GroupOperator nvarchar(10) NOT NULL DEFAULT 'AND',
        SortOrder int NOT NULL DEFAULT 0,
        IsActive bit NOT NULL DEFAULT 1,
        CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy nvarchar(200) NULL,
        ModifiedAt datetime2 NULL,
        ModifiedBy nvarchar(200) NULL,
        CONSTRAINT PK_BusinessRoleMembershipRules PRIMARY KEY (Id),
        CONSTRAINT FK_BusinessRoleMembershipRules_BusinessRoles
            FOREIGN KEY (BusinessRoleId) REFERENCES BusinessRoles(Id) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX IX_BusinessRoleMembershipRules_BusinessRoleId
        ON BusinessRoleMembershipRules (BusinessRoleId);
END
GO

-- =============================================
-- 5. BusinessRoleProvisioningLog
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BusinessRoleProvisioningLog')
BEGIN
    CREATE TABLE BusinessRoleProvisioningLog (
        Id uniqueidentifier NOT NULL DEFAULT NEWID(),
        BusinessRoleId uniqueidentifier NOT NULL,
        EntitlementId uniqueidentifier NULL,
        IdentityId uniqueidentifier NULL,
        ObjectId uniqueidentifier NULL,
        Action nvarchar(50) NOT NULL,
        TargetDN nvarchar(1000) NULL,
        TargetDisplayName nvarchar(500) NULL,
        Success bit NOT NULL DEFAULT 1,
        ErrorMessage nvarchar(2000) NULL,
        ExecutedBy nvarchar(200) NULL,
        ExecutedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT PK_BusinessRoleProvisioningLog PRIMARY KEY (Id)
    );

    -- No FKs for write performance - this is an audit log
    CREATE NONCLUSTERED INDEX IX_BusinessRoleProvisioningLog_BusinessRoleId
        ON BusinessRoleProvisioningLog (BusinessRoleId, ExecutedAt DESC);

    CREATE NONCLUSTERED INDEX IX_BusinessRoleProvisioningLog_ExecutedAt
        ON BusinessRoleProvisioningLog (ExecutedAt DESC);
END
GO
