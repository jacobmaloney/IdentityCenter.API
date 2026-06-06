-- ⚡ ULTRA-FAST BULK UPSERT: Process 1000 objects in a single call
-- This procedure accepts a JSON array of objects with their attributes
-- and processes them all in a single transaction using MERGE operations
-- Expected: 1000 objects in 1-2 seconds (vs 100+ seconds one-by-one)

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_BulkUpsertObjects]
    @ObjectsJson NVARCHAR(MAX)  -- JSON array of objects with nested attributes
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;  -- Auto-rollback on any error

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();
    DECLARE @ObjectsProcessed INT = 0;
    DECLARE @ObjectsCreated INT = 0;
    DECLARE @ObjectsUpdated INT = 0;
    DECLARE @AttributesAffected INT = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Parse the JSON into a temp table for processing
        SELECT
            JSON_VALUE(value, '$.Id') AS Id,
            JSON_VALUE(value, '$.SourceConnectionId') AS SourceConnectionId,
            JSON_VALUE(value, '$.SourceUniqueId') AS SourceUniqueId,
            JSON_VALUE(value, '$.SourceType') AS SourceType,
            JSON_VALUE(value, '$.ObjectClass') AS ObjectClass,
            JSON_VALUE(value, '$.DisplayName') AS DisplayName,
            JSON_VALUE(value, '$.Email') AS Email,
            JSON_VALUE(value, '$.Username') AS Username,
            JSON_VALUE(value, '$.FirstName') AS FirstName,
            JSON_VALUE(value, '$.LastName') AS LastName,
            JSON_VALUE(value, '$.Department') AS Department,
            JSON_VALUE(value, '$.JobTitle') AS JobTitle,
            JSON_VALUE(value, '$.Phone') AS Phone,
            JSON_VALUE(value, '$.DN') AS DN,
            JSON_VALUE(value, '$.CN') AS CN,
            JSON_VALUE(value, '$.ManagerSourceId') AS ManagerSourceId,
            JSON_VALUE(value, '$.IdentityId') AS IdentityId,
            CAST(JSON_VALUE(value, '$.IsActive') AS BIT) AS IsActive,
            CAST(JSON_VALUE(value, '$.IsAuthoritative') AS BIT) AS IsAuthoritative,
            CAST(JSON_VALUE(value, '$.MatchConfidence') AS INT) AS MatchConfidence,
            JSON_VALUE(value, '$.MatchMethod') AS MatchMethod,
            CAST(JSON_VALUE(value, '$.IsBuiltIn') AS BIT) AS IsBuiltIn,
            CAST(JSON_VALUE(value, '$.IsAdminSDHolder') AS BIT) AS IsAdminSDHolder,
            JSON_QUERY(value, '$.Attributes') AS AttributesJson
        INTO #ObjectsToProcess
        FROM OPENJSON(@ObjectsJson);

        -- BULK MERGE Objects table
        -- CRITICAL FIX: Deduplicate source data using ROW_NUMBER() to prevent MERGE error
        -- "MERGE statement attempted to UPDATE or DELETE the same row more than once"
        MERGE INTO Objects AS target
        USING (
            SELECT
                CAST(Id AS UNIQUEIDENTIFIER) AS Id,
                CAST(SourceConnectionId AS UNIQUEIDENTIFIER) AS SourceConnectionId,
                SourceUniqueId,
                SourceType,
                ObjectClass,
                DisplayName,
                Email,
                Username,
                FirstName,
                LastName,
                Department,
                JobTitle,
                Phone,
                DN,
                CN,
                ManagerSourceId,
                CAST(IdentityId AS UNIQUEIDENTIFIER) AS IdentityId,
                IsActive,
                IsAuthoritative,
                MatchConfidence,
                MatchMethod,
                IsBuiltIn,
                IsAdminSDHolder
            FROM (
                SELECT *,
                    ROW_NUMBER() OVER (
                        PARTITION BY SourceConnectionId, SourceUniqueId
                        ORDER BY (SELECT NULL)  -- Keep arbitrary row from duplicates
                    ) AS RowNum
                FROM #ObjectsToProcess
            ) AS Deduped
            WHERE RowNum = 1  -- Keep only first occurrence of each unique (SourceConnectionId, SourceUniqueId)
        ) AS source
        ON target.SourceConnectionId = source.SourceConnectionId
           AND target.SourceUniqueId = source.SourceUniqueId
        WHEN MATCHED THEN
            UPDATE SET
                SourceType = source.SourceType,
                ObjectClass = source.ObjectClass,
                DisplayName = source.DisplayName,
                FirstName = source.FirstName,
                LastName = source.LastName,
                Email = source.Email,
                Username = source.Username,
                JobTitle = source.JobTitle,
                Department = source.Department,
                Phone = source.Phone,
                DN = source.DN,
                CN = source.CN,
                ManagerSourceId = source.ManagerSourceId,
                IsActive = source.IsActive,
                IsAuthoritative = source.IsAuthoritative,
                IsBuiltIn = source.IsBuiltIn,
                IsAdminSDHolder = source.IsAdminSDHolder,
                IdentityId = source.IdentityId,
                MatchConfidence = source.MatchConfidence,
                MatchMethod = source.MatchMethod,
                LastSyncedAt = @Now,
                LastSeenAt = @Now
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (
                Id, SourceConnectionId, SourceUniqueId, SourceType, ObjectClass, DisplayName,
                FirstName, LastName, Email, Username, JobTitle, Department, Phone, DN, CN,
                ManagerSourceId, IsActive, IsAuthoritative, IsBuiltIn, IsAdminSDHolder,
                IdentityId, MatchConfidence, MatchMethod,
                FirstSyncedAt, LastSyncedAt, LastSeenAt
            )
            VALUES (
                source.Id, source.SourceConnectionId, source.SourceUniqueId, source.SourceType, source.ObjectClass, source.DisplayName,
                source.FirstName, source.LastName, source.Email, source.Username, source.JobTitle, source.Department, source.Phone, source.DN, source.CN,
                source.ManagerSourceId, source.IsActive, source.IsAuthoritative, source.IsBuiltIn, source.IsAdminSDHolder,
                source.IdentityId, source.MatchConfidence, source.MatchMethod,
                @Now, @Now, @Now
            );

        SET @ObjectsProcessed = @@ROWCOUNT;

        -- Count creates vs updates by checking if object existed before merge
        SELECT @ObjectsCreated = COUNT(*)
        FROM Objects o
        INNER JOIN #ObjectsToProcess t ON o.SourceConnectionId = CAST(t.SourceConnectionId AS UNIQUEIDENTIFIER)
            AND o.SourceUniqueId = t.SourceUniqueId
        WHERE o.FirstSyncedAt = @Now;

        SET @ObjectsUpdated = @ObjectsProcessed - @ObjectsCreated;

        -- BULK PROCESS Attributes using cursor for each object
        -- (Can't do single MERGE because attributes are nested JSON per object)
        DECLARE @CurrentObjectId UNIQUEIDENTIFIER;
        DECLARE @CurrentSourceConnectionId UNIQUEIDENTIFIER;
        DECLARE @CurrentSourceUniqueId NVARCHAR(450);
        DECLARE @CurrentAttributesJson NVARCHAR(MAX);

        DECLARE attr_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT
                CAST(t.Id AS UNIQUEIDENTIFIER),
                CAST(t.SourceConnectionId AS UNIQUEIDENTIFIER),
                t.SourceUniqueId,
                t.AttributesJson
            FROM #ObjectsToProcess t
            WHERE t.AttributesJson IS NOT NULL AND LEN(t.AttributesJson) > 2;  -- Not empty array

        OPEN attr_cursor;
        FETCH NEXT FROM attr_cursor INTO @CurrentObjectId, @CurrentSourceConnectionId, @CurrentSourceUniqueId, @CurrentAttributesJson;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- Get the actual database ID (may differ from JSON Id if it's an update)
            SELECT @CurrentObjectId = Id
            FROM Objects
            WHERE SourceConnectionId = @CurrentSourceConnectionId
              AND SourceUniqueId = @CurrentSourceUniqueId;

            -- MERGE attributes for this object
            -- CRITICAL: Deduplicate attributes by AttributeName (case-insensitive) to prevent MERGE failures
            -- AD can return same attribute with different casing (objectClass vs objectclass)
            MERGE INTO ObjectAttributes AS target
            USING (
                SELECT ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt
                FROM (
                    SELECT
                        @CurrentObjectId AS ObjectId,
                        JSON_VALUE(value, '$.AttributeName') AS AttributeName,
                        JSON_VALUE(value, '$.AttributeValue') AS AttributeValue,
                        JSON_VALUE(value, '$.DataType') AS DataType,
                        @Now AS LastSyncedAt,
                        ROW_NUMBER() OVER (PARTITION BY LOWER(JSON_VALUE(value, '$.AttributeName')) ORDER BY (SELECT NULL)) AS rn
                    FROM OPENJSON(@CurrentAttributesJson)
                ) AS Deduped
                WHERE rn = 1  -- Keep only first occurrence of each attribute name (case-insensitive)
            ) AS source
            ON target.ObjectId = source.ObjectId
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
                INSERT (Id, ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt)
                VALUES (NEWID(), source.ObjectId, source.AttributeName, source.AttributeValue, source.DataType, source.LastSyncedAt)
            WHEN NOT MATCHED BY SOURCE AND target.ObjectId = @CurrentObjectId THEN
                DELETE;

            SET @AttributesAffected = @AttributesAffected + @@ROWCOUNT;

            FETCH NEXT FROM attr_cursor INTO @CurrentObjectId, @CurrentSourceConnectionId, @CurrentSourceUniqueId, @CurrentAttributesJson;
        END;

        CLOSE attr_cursor;
        DEALLOCATE attr_cursor;

        DROP TABLE #ObjectsToProcess;

        COMMIT TRANSACTION;

        -- Return summary statistics
        SELECT
            @ObjectsProcessed AS ObjectsProcessed,
            @ObjectsCreated AS ObjectsCreated,
            @ObjectsUpdated AS ObjectsUpdated,
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

PRINT '⚡ ULTRA-FAST bulk upsert stored procedure created!';
PRINT '';
PRINT '📊 Expected performance:';
PRINT '   - 1000 objects: 1-2 seconds (vs 100+ seconds one-by-one)';
PRINT '   - 50-100x faster than sequential processing';
PRINT '   - Single transaction = atomic operation';
PRINT '';
PRINT '🚀 Ready for lightning-fast bulk sync!';
