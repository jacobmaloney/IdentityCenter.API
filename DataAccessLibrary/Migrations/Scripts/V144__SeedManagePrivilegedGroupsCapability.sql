-- V144: read-only-default write-capability flag for privileged-group membership.
--
-- Phase D increment 2 (ADUC-style AD object management) adds Add/Remove member
-- routing for AD groups. Membership writes to privileged, role-assignable groups
-- (Domain Admins, Enterprise Admins, Schema Admins, Administrators, Account
-- Operators) require this SEPARATE override in addition to CanManageMembership.
-- Seeded 'false' (fail-closed) so the override must be explicitly enabled before
-- any privileged-group membership write is permitted. The WriteCapabilityGate
-- fails closed when the flag is missing, so the seed is a convenience, not a
-- security dependency.
--
-- IDEMPOTENT: inserted only if absent so an operator-edited value is never
-- clobbered. Matches the V142 WriteCapabilities seed pattern.
--
-- DUAL-RUN SAFE: touches only the IdentityCenter Settings table; Conduit never
-- runs IC migrations.

SET NOCOUNT ON;
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
   AND NOT EXISTS (SELECT 1 FROM [Settings]
                   WHERE [Category] = N'WriteCapabilities' AND [Key] = N'CanManagePrivilegedGroups')
BEGIN
    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    VALUES (N'WriteCapabilities', N'CanManagePrivilegedGroups', N'false', N'bool', 0, GETUTCDATE(), N'System');
    PRINT 'V144: Seeded WriteCapabilities CanManagePrivilegedGroups = false.';
END
ELSE
BEGIN
    PRINT 'V144: CanManagePrivilegedGroups already present or Settings table missing -- nothing to do.';
END;
GO
