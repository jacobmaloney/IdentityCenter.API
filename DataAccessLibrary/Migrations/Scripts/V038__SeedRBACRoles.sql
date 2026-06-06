-- V038: Seed RBAC Roles
-- Seeds User, Manager, and Auditor roles for role-based authorization
-- Assigns User role to the seeded admin user

-- =============================================
-- 1. Seed User role (base self-service access)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'USER')
BEGIN
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [Description], [Permissions], [AdGroupMappings], [EntraIdGroupMappings], [IsSystem], [CreatedAt], [ConcurrencyStamp])
    VALUES (NEWID(), N'User', N'USER', N'Base role - self-service access to catalog and requests', N'', N'', N'', 1, GETUTCDATE(), NEWID());
END

GO

-- =============================================
-- 2. Seed Manager role (approve requests, browse directory, view compliance)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'MANAGER')
BEGIN
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [Description], [Permissions], [AdGroupMappings], [EntraIdGroupMappings], [IsSystem], [CreatedAt], [ConcurrencyStamp])
    VALUES (NEWID(), N'Manager', N'MANAGER', N'Approve requests, browse directory, view compliance', N'', N'', N'', 1, GETUTCDATE(), NEWID());
END

GO

-- =============================================
-- 3. Seed Auditor role (read-only reports, audit trails, analytics, compliance)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'AUDITOR')
BEGIN
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [Description], [Permissions], [AdGroupMappings], [EntraIdGroupMappings], [IsSystem], [CreatedAt], [ConcurrencyStamp])
    VALUES (NEWID(), N'Auditor', N'AUDITOR', N'Read-only reports, audit trails, analytics, compliance', N'', N'', N'', 1, GETUTCDATE(), NEWID());
END

GO

-- =============================================
-- 4. Assign User role to seeded admin user
-- =============================================
DECLARE @UserRoleId NVARCHAR(450);
SELECT @UserRoleId = [Id] FROM [AspNetRoles] WHERE [NormalizedName] = N'USER';

IF @UserRoleId IS NOT NULL
   AND EXISTS (SELECT 1 FROM [AspNetUsers] WHERE [Id] = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890')
   AND NOT EXISTS (
        SELECT 1 FROM [AspNetUserRoles]
        WHERE [UserId] = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890' AND [RoleId] = @UserRoleId
   )
BEGIN
    INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
    VALUES ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', @UserRoleId);
END

GO
