-- V121: Remove Legacy Admin Seed
--
-- Earlier installs (V005) seeded a fixed admin user 'admin@identitycenter.local'
-- with a known-literal password hash. The first-run wizard separately creates
-- a real admin with a randomly-generated password via BootstrapPasswordGenerator,
-- so the V005 row is a live backdoor on every existing install.
--
-- This migration deletes the seeded row IFF the PasswordHash still matches the
-- exact literal that V005 shipped. Customers who rotated the password manually
-- have a different hash and are left untouched.
--
-- Child rows in the four standard Identity FK tables (Roles, Claims, Logins,
-- Tokens) are cleaned up first to satisfy ON DELETE NO ACTION constraints.
-- AccessRequests / UserAccess FKs are intentionally NOT touched: if the seeded
-- admin actually authored access requests the DELETE will fail loudly, which
-- is the correct signal that this row was in real use and warrants manual review.

DECLARE @LegacyAdminId NVARCHAR(450) = N'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
DECLARE @LegacyHash    NVARCHAR(MAX) = N'AQAAAAIAAYagAAAAEJNdfxKF9K9CchQCROk36Fua6u78Q8rrsiuEdFN7/UYZx0+u2az2XIYNNIrLHnvPTA==';

IF EXISTS (
    SELECT 1 FROM [AspNetUsers]
    WHERE [Id] = @LegacyAdminId AND [PasswordHash] = @LegacyHash
)
BEGIN
    DELETE FROM [AspNetUserRoles]  WHERE [UserId] = @LegacyAdminId;
    DELETE FROM [AspNetUserClaims] WHERE [UserId] = @LegacyAdminId;
    DELETE FROM [AspNetUserLogins] WHERE [UserId] = @LegacyAdminId;
    DELETE FROM [AspNetUserTokens] WHERE [UserId] = @LegacyAdminId;
    DELETE FROM [AspNetUsers]      WHERE [Id]     = @LegacyAdminId;

    PRINT 'V121: Legacy seeded admin removed (hash matched V005 literal).';
END
ELSE
BEGIN
    PRINT 'V121: Legacy seeded admin not present or password rotated - no action.';
END

GO

PRINT 'Schema version 121 applied - legacy admin seed removed';
