-- =============================================
-- Mark Removed Object Group Memberships
-- Marks memberships as removed if they no longer exist in source
-- =============================================

IF OBJECT_ID('dbo.usp_MarkRemovedObjectGroupMemberships', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_MarkRemovedObjectGroupMemberships;
GO

CREATE PROCEDURE dbo.usp_MarkRemovedObjectGroupMemberships
    @ObjectId UNIQUEIDENTIFIER,
    @CurrentGroupIdsJson NVARCHAR(MAX)  -- JSON array of current group IDs
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Parse current group IDs
    SELECT CAST(value AS UNIQUEIDENTIFIER) AS GroupId
    INTO #CurrentGroups
    FROM OPENJSON(@CurrentGroupIdsJson);

    -- Mark memberships as removed if they're not in the current list
    UPDATE ObjectGroupMemberships
    SET RemovedAt = GETUTCDATE()
    WHERE ObjectId = @ObjectId
      AND RemovedAt IS NULL
      AND GroupId NOT IN (SELECT GroupId FROM #CurrentGroups);

    SELECT @@ROWCOUNT AS MembershipsRemoved;

    DROP TABLE #CurrentGroups;
END;
GO

PRINT '✅ Created stored procedure: usp_MarkRemovedObjectGroupMemberships';
