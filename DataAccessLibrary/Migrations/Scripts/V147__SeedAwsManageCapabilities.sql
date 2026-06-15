-- V147: read-only-default write-capability flags for AWS IAM management.
--
-- The AWS IAM write-back slice routes IAM writes through ObjectWriteBackService behind the
-- same WriteCapabilityGate as the AD/SQL paths. Two capabilities gate it:
--   CanAwsManageWrite      — apply AWS IAM writes (attribute edits, enable/disable,
--                            inline-policy and tag changes on IAM users/roles).
--   CanAwsManagePrivileged — additionally required for privileged operations such as
--                            managed-policy attach/detach.
-- Privileged operations additionally require step-up — enforced server-side in
-- ObjectWriteBackService, not here.
--
-- Both seeded 'false' (fail-closed) so each must be explicitly enabled before any AWS
-- management is permitted. The WriteCapabilityGate fails closed when a flag is missing,
-- so the seed is a convenience, not a security dependency.
--
-- IDEMPOTENT: inserted only if absent so an operator-edited value is never clobbered.
-- Matches the V142/V144/V145 WriteCapabilities seed pattern.
--
-- DUAL-RUN SAFE: touches only the IdentityCenter Settings table; Conduit never runs IC
-- migrations.

SET NOCOUNT ON;
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
BEGIN
    DECLARE @awsCaps TABLE ([Key] NVARCHAR(200));
    INSERT INTO @awsCaps ([Key]) VALUES
        (N'CanAwsManageWrite'),
        (N'CanAwsManagePrivileged');

    INSERT INTO [Settings] ([Category], [Key], [Value], [DataType], [IsEncrypted], [ModifiedAt], [ModifiedBy])
    SELECT N'WriteCapabilities', c.[Key], N'false', N'bool', 0, GETUTCDATE(), N'System'
      FROM @awsCaps c
     WHERE NOT EXISTS (
         SELECT 1 FROM [Settings] s
          WHERE s.[Category] = N'WriteCapabilities' AND s.[Key] = c.[Key]);

    PRINT 'V147: Seeded WriteCapabilities CanAwsManageWrite/CanAwsManagePrivileged = false for any not already present.';
END
ELSE
BEGIN
    PRINT 'V147: Settings table missing -- nothing to do.';
END;
GO
