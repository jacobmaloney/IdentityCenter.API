-- V076: Seed demo license assignments with realistic LastUsedAt distribution
-- Creates assignments for existing user objects against existing license pools.
-- ~30% of assignments get LastUsedAt >90 days ago (waste candidates)
-- ~20% get LastUsedAt between 30-90 days ago (at risk)
-- ~50% get LastUsedAt within last 30 days (active)
-- This makes the Waste Report tab show realistic data for demos.

-- Only seed if we have pools and user objects but no existing assignments
IF EXISTS (SELECT 1 FROM LicenseAssignments)
BEGIN
    PRINT 'V076: License assignments already exist — skipping demo seed.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM LicensePools WHERE IsActive = 1)
BEGIN
    PRINT 'V076: No license pools exist — skipping demo seed.';
    RETURN;
END

DECLARE @now DATETIME2 = GETUTCDATE();
DECLARE @poolId UNIQUEIDENTIFIER;
DECLARE @poolCount INT = 0;

-- Get the first Subscription-type pool (M365 or similar)
SELECT TOP 1 @poolId = Id FROM LicensePools
WHERE IsActive = 1 AND LicenseType IN ('Subscription', 'UserCAL')
ORDER BY ConsumedUnits DESC;

-- If no subscription pool, try any pool
IF @poolId IS NULL
    SELECT TOP 1 @poolId = Id FROM LicensePools WHERE IsActive = 1 ORDER BY ConsumedUnits DESC;

IF @poolId IS NULL
BEGIN
    PRINT 'V076: No suitable license pool found — skipping.';
    RETURN;
END

-- Get user objects to assign licenses to
DECLARE @users TABLE (Id UNIQUEIDENTIFIER, RowNum INT IDENTITY(1,1));
INSERT INTO @users (Id)
SELECT TOP 500 Id FROM Objects
WHERE ObjectClass = 'user' AND DeletedAt IS NULL AND IsActive = 1
ORDER BY NEWID();

DECLARE @totalUsers INT = (SELECT COUNT(*) FROM @users);
IF @totalUsers = 0
BEGIN
    PRINT 'V076: No user objects found — skipping.';
    RETURN;
END

-- Insert assignments with varied LastUsedAt
INSERT INTO LicenseAssignments (Id, LicensePoolId, ObjectId, AssignedAt, AssignmentSource, LastUsedAt, IsActive, LastSyncedAt)
SELECT
    NEWID(),
    @poolId,
    u.Id,
    DATEADD(DAY, -1 * (30 + (u.RowNum % 365)), @now),  -- assigned 30-395 days ago
    'Direct',
    CASE
        -- 30% waste: LastUsedAt 95-400 days ago
        WHEN u.RowNum % 10 IN (0, 1, 2) THEN DATEADD(DAY, -1 * (95 + (u.RowNum % 305)), @now)
        -- 20% at risk: LastUsedAt 30-90 days ago
        WHEN u.RowNum % 10 IN (3, 4) THEN DATEADD(DAY, -1 * (30 + (u.RowNum % 60)), @now)
        -- 50% active: LastUsedAt 0-29 days ago
        ELSE DATEADD(DAY, -1 * (u.RowNum % 29), @now)
    END,
    1,
    @now
FROM @users u;

SET @poolCount = @@ROWCOUNT;

-- Update the pool's ConsumedUnits to reflect the new assignments
UPDATE LicensePools SET ConsumedUnits = @poolCount WHERE Id = @poolId;

PRINT 'V076: Seeded ' + CAST(@poolCount AS VARCHAR) + ' demo license assignments (~30% wasted, ~20% at risk, ~50% active).';
GO
