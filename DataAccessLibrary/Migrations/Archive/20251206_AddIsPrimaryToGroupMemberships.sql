-- Migration: Add IsPrimary column to ObjectGroupMemberships and IdentityGroupMemberships
-- Date: 2025-12-06
-- Purpose: Track primary group memberships (from AD primaryGroupID attribute)
-- Primary groups (like Domain Users) are NOT stored in memberOf - they must be resolved via SID

USE IdentityCenter13;
GO

-- Add IsPrimary column to ObjectGroupMemberships
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ObjectGroupMemberships' AND COLUMN_NAME = 'IsPrimary')
BEGIN
    ALTER TABLE ObjectGroupMemberships ADD IsPrimary BIT NOT NULL DEFAULT 0;
    PRINT 'Added IsPrimary to ObjectGroupMemberships';
END
GO

-- Add IsPrimary column to IdentityGroupMemberships
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'IdentityGroupMemberships' AND COLUMN_NAME = 'IsPrimary')
BEGIN
    ALTER TABLE IdentityGroupMemberships ADD IsPrimary BIT NOT NULL DEFAULT 0;
    PRINT 'Added IsPrimary to IdentityGroupMemberships';
END
GO

-- Update stored procedure to handle IsPrimary
CREATE OR ALTER PROCEDURE dbo.usp_BulkUpsertObjectGroupMemberships
    @MembershipsJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    -- Parse JSON into temp table
    SELECT
        CAST(JSON_VALUE(j.value, '$.ObjectId') AS UNIQUEIDENTIFIER) AS ObjectId,
        CAST(JSON_VALUE(j.value, '$.GroupId') AS UNIQUEIDENTIFIER) AS GroupId,
        CAST(ISNULL(JSON_VALUE(j.value, '$.IsDirect'), 'true') AS BIT) AS IsDirect,
        CAST(ISNULL(JSON_VALUE(j.value, '$.IsPrimary'), 'false') AS BIT) AS IsPrimary
    INTO #Memberships
    FROM OPENJSON(@MembershipsJson) AS j;

    -- MERGE with target table
    MERGE ObjectGroupMemberships AS target
    USING #Memberships AS source
    ON (target.ObjectId = source.ObjectId AND target.GroupId = source.GroupId)
    WHEN MATCHED THEN
        UPDATE SET
            IsDirect = source.IsDirect,
            IsPrimary = source.IsPrimary,
            LastSyncedAt = GETUTCDATE(),
            RemovedAt = NULL,
            IsActive = 1
    WHEN NOT MATCHED THEN
        INSERT (Id, ObjectId, GroupId, IsDirect, IsPrimary, AddedAt, LastSyncedAt, IsActive)
        VALUES (NEWID(), source.ObjectId, source.GroupId, source.IsDirect, source.IsPrimary, GETUTCDATE(), GETUTCDATE(), 1);

    DECLARE @TotalAffected INT = @@ROWCOUNT;

    DROP TABLE #Memberships;

    SELECT @TotalAffected AS TotalAffected;
END;
GO

PRINT 'Migration complete: IsPrimary support added';
