-- V125: Seed Settings(Category='Modules') rows for the canonical module list.
-- Upgrade-safe: existing customers with data for a module get that module
-- seeded as 'true' so feature regression doesn't silently hide their data.
-- Fresh installs get all modules 'false' (must explicitly enable).
--
-- NOTE: This script is intentionally ONE batch (no GO separators). @Now and
-- @ActorTag are declared once and referenced in every INSERT; T-SQL scopes
-- variables to a single batch, so a GO between the DECLAREs and the INSERTs
-- would raise "Must declare the scalar variable @Now". Each row is guarded by
-- its own IF NOT EXISTS so the seed is fully idempotent (safe after a partial
-- prior application).

DECLARE @ActorTag NVARCHAR(64) = N'v125-migration';
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

-- LicenseManagement: seed 'true' if LicensePools table exists and has rows
IF NOT EXISTS (SELECT 1 FROM Settings WHERE Category = 'Modules' AND [Key] = 'LicenseManagement')
BEGIN
    DECLARE @LMValue NVARCHAR(MAX) = 'false';
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicensePools')
    BEGIN
        DECLARE @LMCount INT;
        EXEC sp_executesql N'SELECT @c = COUNT(*) FROM (SELECT TOP 1 1 AS x FROM LicensePools) t',
            N'@c INT OUTPUT', @c = @LMCount OUTPUT;
        IF @LMCount > 0 SET @LMValue = 'true';
    END
    INSERT INTO Settings (Category, [Key], Value, IsEncrypted, DataType, ModifiedAt, ModifiedBy)
    VALUES ('Modules', 'LicenseManagement', @LMValue, 0, 'bool', @Now, @ActorTag);
END

-- EnterpriseApps: seed 'true' if Objects has any servicePrincipal rows
IF NOT EXISTS (SELECT 1 FROM Settings WHERE Category = 'Modules' AND [Key] = 'EnterpriseApps')
BEGIN
    DECLARE @EAValue NVARCHAR(MAX) = 'false';
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Objects')
    BEGIN
        DECLARE @EACount INT;
        EXEC sp_executesql N'SELECT @c = COUNT(*) FROM (SELECT TOP 1 1 AS x FROM Objects WHERE ObjectClass = ''serviceprincipal'' AND DeletedAt IS NULL) t',
            N'@c INT OUTPUT', @c = @EACount OUTPUT;
        IF @EACount > 0 SET @EAValue = 'true';
    END
    INSERT INTO Settings (Category, [Key], Value, IsEncrypted, DataType, ModifiedAt, ModifiedBy)
    VALUES ('Modules', 'EnterpriseApps', @EAValue, 0, 'bool', @Now, @ActorTag);
END

-- MachineLearning: seed 'true' if MLModelMetadata table has any rows
IF NOT EXISTS (SELECT 1 FROM Settings WHERE Category = 'Modules' AND [Key] = 'MachineLearning')
BEGIN
    DECLARE @MLValue NVARCHAR(MAX) = 'false';
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MLModelMetadata')
    BEGIN
        DECLARE @MLCount INT;
        EXEC sp_executesql N'SELECT @c = COUNT(*) FROM (SELECT TOP 1 1 AS x FROM MLModelMetadata) t',
            N'@c INT OUTPUT', @c = @MLCount OUTPUT;
        IF @MLCount > 0 SET @MLValue = 'true';
    END
    INSERT INTO Settings (Category, [Key], Value, IsEncrypted, DataType, ModifiedAt, ModifiedBy)
    VALUES ('Modules', 'MachineLearning', @MLValue, 0, 'bool', @Now, @ActorTag);
END

PRINT 'V125: Modules catalog seeded with upgrade-safe defaults';
