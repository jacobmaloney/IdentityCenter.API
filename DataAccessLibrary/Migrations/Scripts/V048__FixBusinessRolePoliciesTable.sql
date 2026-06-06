-- V048: Fix BusinessRolePolicies table creation
-- V047 may have failed to create this table due to ON DELETE CASCADE conflict
-- with CompliancePolicies. This re-creates it with ON DELETE NO ACTION.

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
