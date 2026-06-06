-- =============================================
-- Bulk Upsert Object Group Memberships
-- COMPLETE FIX: All 14 columns with proper NULL type casting
-- =============================================

IF OBJECT_ID('dbo.usp_BulkUpsertObjectGroupMemberships', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_BulkUpsertObjectGroupMemberships;
GO

CREATE PROCEDURE dbo.usp_BulkUpsertObjectGroupMemberships
    @MembershipsJson NVARCHAR(MAX)  -- JSON array of memberships
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET QUOTED_IDENTIFIER ON; -- CRITICAL: Required for MERGE statement to work with indexes

    BEGIN TRANSACTION;
    BEGIN TRY
        -- Parse JSON into temp table, DEDUPE using ROW_NUMBER to avoid duplicate key errors
        ;WITH ParsedData AS (
            SELECT
                CAST(ObjectId AS UNIQUEIDENTIFIER) AS ObjectId,
                CAST(GroupId AS UNIQUEIDENTIFIER) AS GroupId,
                CAST(IsDirect AS BIT) AS IsDirect,
                ROW_NUMBER() OVER (PARTITION BY ObjectId, GroupId ORDER BY (SELECT NULL)) AS RowNum
            FROM OPENJSON(@MembershipsJson)
            WITH (
                ObjectId NVARCHAR(36),
                GroupId NVARCHAR(36),
                IsDirect BIT
            )
        )
        SELECT
            NEWID() AS Id,
            ObjectId,
            GroupId,
            IsDirect,
            CAST(NULL AS NVARCHAR(MAX)) AS MembershipPath,
            GETUTCDATE() AS AddedAt,
            GETUTCDATE() AS LastSyncedAt,
            CAST(NULL AS DATETIME2) AS RemovedAt,
            CAST(1 AS BIT) AS IsActive,
            CAST(NULL AS NVARCHAR(255)) AS AddedBy,
            CAST(NULL AS NVARCHAR(500)) AS Justification,
            CAST(NULL AS DATETIME2) AS ExpirationDate,
            CAST(NULL AS NVARCHAR(255)) AS RemovedBy,
            CAST(NULL AS NVARCHAR(500)) AS RemovalReason
        INTO #NewMemberships
        FROM ParsedData
        WHERE RowNum = 1;  -- Only take first occurrence of each ObjectId+GroupId pair

        -- Create index for MERGE performance (no duplicates now, so this won't fail)
        CREATE UNIQUE INDEX IX_Temp_ObjectGroup ON #NewMemberships(ObjectId, GroupId);

        -- Bulk upsert using MERGE
        MERGE INTO ObjectGroupMemberships AS target
        USING #NewMemberships AS source
        ON target.ObjectId = source.ObjectId
           AND target.GroupId = source.GroupId
        WHEN MATCHED THEN
            -- Update existing membership (reactivate if removed)
            UPDATE SET
                RemovedAt = NULL,
                LastSyncedAt = GETUTCDATE(),
                IsDirect = source.IsDirect,
                IsActive = 1
        WHEN NOT MATCHED BY TARGET THEN
            -- Insert new membership with ALL 14 columns
            INSERT (Id, ObjectId, GroupId, IsDirect, MembershipPath, AddedAt, LastSyncedAt, RemovedAt, IsActive,
                    AddedBy, Justification, ExpirationDate, RemovedBy, RemovalReason)
            VALUES (source.Id, source.ObjectId, source.GroupId, source.IsDirect,
                    source.MembershipPath, source.AddedAt, source.LastSyncedAt, source.RemovedAt, source.IsActive,
                    source.AddedBy, source.Justification, source.ExpirationDate, source.RemovedBy, source.RemovalReason);

        -- Return summary
        SELECT
            @@ROWCOUNT AS TotalAffected,
            (SELECT COUNT(*) FROM #NewMemberships) AS TotalProcessed;

        DROP TABLE #NewMemberships;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END;
GO

PRINT '✅ COMPLETE FIX: All 14 columns with proper NULL casting applied!';
PRINT '   - Fixed: NULL values now explicitly CAST to correct types';
PRINT '   - Fixed: All 14 table columns included in INSERT';
PRINT '   - Ready for membership sync!';
