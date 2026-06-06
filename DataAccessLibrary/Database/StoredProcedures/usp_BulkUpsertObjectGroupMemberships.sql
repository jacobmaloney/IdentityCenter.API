-- =============================================
-- Stored Procedure: usp_BulkUpsertObjectGroupMemberships
-- Description: Bulk upsert object group memberships from JSON
-- Parameters: @MembershipsJson - JSON array of membership records
-- Returns: TotalAffected - number of rows inserted or updated
-- NOTE: This is a copy of the procedure in migration 20251206_AddIsPrimaryToGroupMemberships.sql
-- =============================================
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
