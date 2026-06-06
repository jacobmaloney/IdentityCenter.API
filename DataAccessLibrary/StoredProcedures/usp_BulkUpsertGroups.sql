-- ⚡ ULTRA-FAST BULK UPSERT GROUPS: Process 1000 groups in a single call
-- This procedure accepts a JSON array of groups with their attributes
-- and processes them all in a single transaction using MERGE operations
-- Expected: 1000 groups in 1-2 seconds (vs 100+ seconds one-by-one)

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_BulkUpsertGroups]
    @GroupsJson NVARCHAR(MAX)  -- JSON array of groups with nested attributes
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;  -- Auto-rollback on any error

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();
    DECLARE @GroupsProcessed INT = 0;
    DECLARE @GroupsCreated INT = 0;
    DECLARE @GroupsUpdated INT = 0;
    DECLARE @AttributesAffected INT = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Parse the JSON into a temp table for processing
        SELECT
            JSON_VALUE(value, '$.Id') AS Id,
            JSON_VALUE(value, '$.SourceConnectionId') AS SourceConnectionId,
            JSON_VALUE(value, '$.SourceUniqueId') AS SourceUniqueId,
            JSON_VALUE(value, '$.SourceType') AS SourceType,
            JSON_VALUE(value, '$.Name') AS Name,
            JSON_VALUE(value, '$.Description') AS Description,
            JSON_VALUE(value, '$.DistinguishedName') AS DistinguishedName,
            JSON_VALUE(value, '$.GroupType') AS GroupType,
            JSON_VALUE(value, '$.Email') AS Email,
            CAST(JSON_VALUE(value, '$.IsMailEnabled') AS BIT) AS IsMailEnabled,
            JSON_VALUE(value, '$.OwnerId') AS OwnerId,
            JSON_VALUE(value, '$.ManagedBy') AS ManagedBy,  -- Store owner DN for later resolution
            CAST(JSON_VALUE(value, '$.IsActive') AS BIT) AS IsActive,
            JSON_QUERY(value, '$.Attributes') AS AttributesJson
        INTO #GroupsToProcess
        FROM OPENJSON(@GroupsJson);

        -- BULK MERGE Groups table
        MERGE INTO Groups AS target
        USING (
            SELECT
                CAST(Id AS UNIQUEIDENTIFIER) AS Id,
                CAST(SourceConnectionId AS UNIQUEIDENTIFIER) AS SourceConnectionId,
                SourceUniqueId,
                SourceType,
                Name,
                Description,
                DistinguishedName,
                GroupType,
                Email,
                IsMailEnabled,
                CAST(OwnerId AS UNIQUEIDENTIFIER) AS OwnerId,
                ManagedBy,  -- Owner DN for later resolution
                IsActive
            FROM #GroupsToProcess
        ) AS source
        ON target.SourceConnectionId = source.SourceConnectionId
           AND target.SourceUniqueId = source.SourceUniqueId
        WHEN MATCHED THEN
            UPDATE SET
                SourceType = source.SourceType,
                Name = source.Name,
                Description = source.Description,
                DistinguishedName = source.DistinguishedName,
                GroupType = source.GroupType,
                Email = source.Email,
                IsMailEnabled = source.IsMailEnabled,
                OwnerId = source.OwnerId,
                ManagedBy = source.ManagedBy,  -- Store owner DN
                IsActive = source.IsActive,
                LastSyncedAt = @Now,
                LastSeenAt = @Now
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (
                Id, SourceConnectionId, SourceUniqueId, SourceType, Name,
                Description, DistinguishedName, GroupType, Email, IsMailEnabled,
                OwnerId, ManagedBy, IsActive,
                FirstSyncedAt, LastSyncedAt, LastSeenAt
            )
            VALUES (
                source.Id, source.SourceConnectionId, source.SourceUniqueId, source.SourceType, source.Name,
                source.Description, source.DistinguishedName, source.GroupType, source.Email, source.IsMailEnabled,
                source.OwnerId, source.ManagedBy, source.IsActive,
                @Now, @Now, @Now
            );

        SET @GroupsProcessed = @@ROWCOUNT;

        -- Count creates vs updates by checking if group existed before merge
        SELECT @GroupsCreated = COUNT(*)
        FROM Groups g
        INNER JOIN #GroupsToProcess t ON g.SourceConnectionId = CAST(t.SourceConnectionId AS UNIQUEIDENTIFIER)
            AND g.SourceUniqueId = t.SourceUniqueId
        WHERE g.FirstSyncedAt = @Now;

        SET @GroupsUpdated = @GroupsProcessed - @GroupsCreated;

        -- BULK PROCESS Attributes using cursor for each group
        DECLARE @CurrentGroupId UNIQUEIDENTIFIER;
        DECLARE @CurrentSourceConnectionId UNIQUEIDENTIFIER;
        DECLARE @CurrentSourceUniqueId NVARCHAR(450);
        DECLARE @CurrentAttributesJson NVARCHAR(MAX);

        DECLARE attr_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT
                CAST(t.Id AS UNIQUEIDENTIFIER),
                CAST(t.SourceConnectionId AS UNIQUEIDENTIFIER),
                t.SourceUniqueId,
                t.AttributesJson
            FROM #GroupsToProcess t
            WHERE t.AttributesJson IS NOT NULL AND LEN(t.AttributesJson) > 2;  -- Not empty array

        OPEN attr_cursor;
        FETCH NEXT FROM attr_cursor INTO @CurrentGroupId, @CurrentSourceConnectionId, @CurrentSourceUniqueId, @CurrentAttributesJson;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- Get the actual database ID (may differ from JSON Id if it's an update)
            SELECT @CurrentGroupId = Id
            FROM Groups
            WHERE SourceConnectionId = @CurrentSourceConnectionId
              AND SourceUniqueId = @CurrentSourceUniqueId;

            -- MERGE attributes for this group
            MERGE INTO GroupAttributes AS target
            USING (
                SELECT
                    @CurrentGroupId AS GroupId,
                    JSON_VALUE(value, '$.AttributeName') AS AttributeName,
                    JSON_VALUE(value, '$.AttributeValue') AS AttributeValue,
                    JSON_VALUE(value, '$.DataType') AS DataType,
                    @Now AS LastSyncedAt
                FROM OPENJSON(@CurrentAttributesJson)
            ) AS source
            ON target.GroupId = source.GroupId
               AND target.AttributeName = source.AttributeName
            WHEN MATCHED AND (
                target.AttributeValue != source.AttributeValue
                OR target.DataType != source.DataType
            ) THEN
                UPDATE SET
                    AttributeValue = source.AttributeValue,
                    DataType = source.DataType,
                    LastSyncedAt = source.LastSyncedAt
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (Id, GroupId, AttributeName, AttributeValue, DataType, LastSyncedAt)
                VALUES (NEWID(), source.GroupId, source.AttributeName, source.AttributeValue, source.DataType, source.LastSyncedAt)
            WHEN NOT MATCHED BY SOURCE AND target.GroupId = @CurrentGroupId THEN
                DELETE;

            SET @AttributesAffected = @AttributesAffected + @@ROWCOUNT;

            FETCH NEXT FROM attr_cursor INTO @CurrentGroupId, @CurrentSourceConnectionId, @CurrentSourceUniqueId, @CurrentAttributesJson;
        END;

        CLOSE attr_cursor;
        DEALLOCATE attr_cursor;

        DROP TABLE #GroupsToProcess;

        COMMIT TRANSACTION;

        -- Return summary statistics (using Objects* names for consistency with BulkUpsertResult class)
        SELECT
            @GroupsProcessed AS ObjectsProcessed,
            @GroupsCreated AS ObjectsCreated,
            @GroupsUpdated AS ObjectsUpdated,
            @AttributesAffected AS AttributesAffected;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        -- Re-throw the error
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END
GO

PRINT '⚡ ULTRA-FAST bulk upsert GROUPS stored procedure created!';
PRINT '';
PRINT '📊 Expected performance:';
PRINT '   - 1000 groups: 1-2 seconds (vs 100+ seconds one-by-one)';
PRINT '   - 50-100x faster than sequential processing';
PRINT '   - Single transaction = atomic operation';
PRINT '';
PRINT '🚀 Ready for lightning-fast group sync!';
