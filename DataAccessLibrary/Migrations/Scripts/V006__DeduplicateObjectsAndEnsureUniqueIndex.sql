-- V006: Deduplicate Objects table and ensure unique index
-- Root cause: stale in-memory cache during sync caused the same AD objects
-- to be inserted multiple times when processed by overlapping steps/workflows.

-- Step 1: Identify duplicates and pick keepers (earliest FirstSyncedAt wins)
IF OBJECT_ID('tempdb..#DuplicateKeepers') IS NOT NULL DROP TABLE #DuplicateKeepers;
IF OBJECT_ID('tempdb..#DuplicatesToDelete') IS NOT NULL DROP TABLE #DuplicatesToDelete;

SELECT
    SourceConnectionId,
    SourceUniqueId,
    MIN(FirstSyncedAt) AS KeeperFirstSyncedAt
INTO #DuplicateKeepers
FROM Objects
WHERE SourceUniqueId IS NOT NULL AND SourceConnectionId IS NOT NULL
GROUP BY SourceConnectionId, SourceUniqueId
HAVING COUNT(*) > 1;

-- If no duplicates, skip everything
IF EXISTS (SELECT 1 FROM #DuplicateKeepers)
BEGIN
    DECLARE @groupCount INT;
    SELECT @groupCount = COUNT(*) FROM #DuplicateKeepers;
    PRINT 'Found duplicate object groups: ' + CAST(@groupCount AS VARCHAR(20));

    -- For each duplicate group, find the keeper ID and all duplicate IDs
    -- Keeper = the one with earliest FirstSyncedAt (ties broken by smallest Id)
    ;WITH RankedDups AS (
        SELECT
            o.Id,
            o.SourceConnectionId,
            o.SourceUniqueId,
            ROW_NUMBER() OVER (
                PARTITION BY o.SourceConnectionId, o.SourceUniqueId
                ORDER BY o.FirstSyncedAt ASC, o.Id ASC
            ) AS rn
        FROM Objects o
        INNER JOIN #DuplicateKeepers dk
            ON o.SourceConnectionId = dk.SourceConnectionId
            AND o.SourceUniqueId = dk.SourceUniqueId
    )
    SELECT
        k.Id AS KeeperId,
        d.Id AS DuplicateId,
        d.SourceConnectionId,
        d.SourceUniqueId
    INTO #DuplicatesToDelete
    FROM RankedDups d
    INNER JOIN RankedDups k
        ON d.SourceConnectionId = k.SourceConnectionId
        AND d.SourceUniqueId = k.SourceUniqueId
        AND k.rn = 1
    WHERE d.rn > 1;

    DECLARE @dupCount INT;
    SELECT @dupCount = COUNT(*) FROM #DuplicatesToDelete;
    PRINT 'Duplicate objects to remove: ' + CAST(@dupCount AS VARCHAR(20));

    -- Step 2: Migrate child records from duplicates to keepers

    -- 2a: ObjectAttributes - move attributes that don't already exist on the keeper
    UPDATE oa
    SET oa.ObjectId = dd.KeeperId
    FROM ObjectAttributes oa
    INNER JOIN #DuplicatesToDelete dd ON oa.ObjectId = dd.DuplicateId
    WHERE NOT EXISTS (
        SELECT 1 FROM ObjectAttributes existing
        WHERE existing.ObjectId = dd.KeeperId
        AND existing.AttributeName = oa.AttributeName
    );
    PRINT 'Migrated ObjectAttributes to keepers';

    -- Delete remaining duplicate ObjectAttributes (already exist on keeper)
    DELETE oa
    FROM ObjectAttributes oa
    INNER JOIN #DuplicatesToDelete dd ON oa.ObjectId = dd.DuplicateId;
    PRINT 'Cleaned up remaining duplicate ObjectAttributes';

    -- 2b: IdentityMatchLogs - point to keeper
    UPDATE iml
    SET iml.ObjectId = dd.KeeperId
    FROM IdentityMatchLogs iml
    INNER JOIN #DuplicatesToDelete dd ON iml.ObjectId = dd.DuplicateId;
    PRINT 'Migrated IdentityMatchLogs';

    -- 2c: Objects.ManagerObjectId - point to keeper
    UPDATE o
    SET o.ManagerObjectId = dd.KeeperId
    FROM Objects o
    INNER JOIN #DuplicatesToDelete dd ON o.ManagerObjectId = dd.DuplicateId;
    PRINT 'Updated ManagerObjectId references';

    -- 2d: BusinessRoles.LinkedGroupId - point to keeper
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('BusinessRoles') AND name = 'LinkedGroupId')
    BEGIN
        UPDATE br
        SET br.LinkedGroupId = dd.KeeperId
        FROM BusinessRoles br
        INNER JOIN #DuplicatesToDelete dd ON br.LinkedGroupId = dd.DuplicateId;
        PRINT 'Updated BusinessRoles.LinkedGroupId references';
    END

    -- 2e: ObjectGroupMemberships - migrate group references
    -- Update GroupId references (where the duplicate was a group)
    UPDATE ogm
    SET ogm.GroupId = dd.KeeperId
    FROM ObjectGroupMemberships ogm
    INNER JOIN #DuplicatesToDelete dd ON ogm.GroupId = dd.DuplicateId
    WHERE NOT EXISTS (
        SELECT 1 FROM ObjectGroupMemberships existing
        WHERE existing.GroupId = dd.KeeperId AND existing.ObjectId = ogm.ObjectId
    );
    -- Delete remaining duplicate group memberships
    DELETE ogm
    FROM ObjectGroupMemberships ogm
    INNER JOIN #DuplicatesToDelete dd ON ogm.GroupId = dd.DuplicateId;

    -- ObjectId references will CASCADE on delete, but migrate first
    UPDATE ogm
    SET ogm.ObjectId = dd.KeeperId
    FROM ObjectGroupMemberships ogm
    INNER JOIN #DuplicatesToDelete dd ON ogm.ObjectId = dd.DuplicateId
    WHERE NOT EXISTS (
        SELECT 1 FROM ObjectGroupMemberships existing
        WHERE existing.GroupId = ogm.GroupId AND existing.ObjectId = dd.KeeperId
    );
    DELETE ogm
    FROM ObjectGroupMemberships ogm
    INNER JOIN #DuplicatesToDelete dd ON ogm.ObjectId = dd.DuplicateId;
    PRINT 'Migrated ObjectGroupMemberships';

    -- 2f: ObjectTags - migrate tags that don't exist on keeper
    UPDATE ot
    SET ot.ObjectId = dd.KeeperId
    FROM ObjectTags ot
    INNER JOIN #DuplicatesToDelete dd ON ot.ObjectId = dd.DuplicateId
    WHERE NOT EXISTS (
        SELECT 1 FROM ObjectTags existing
        WHERE existing.ObjectId = dd.KeeperId AND existing.TagId = ot.TagId
    );
    DELETE ot
    FROM ObjectTags ot
    INNER JOIN #DuplicatesToDelete dd ON ot.ObjectId = dd.DuplicateId;
    PRINT 'Migrated ObjectTags';

    -- 2g: SyncAuditLogs - SET NULL on delete, but let's point to keeper for history
    UPDATE sal
    SET sal.ObjectId = dd.KeeperId
    FROM SyncAuditLogs sal
    INNER JOIN #DuplicatesToDelete dd ON sal.ObjectId = dd.DuplicateId;
    PRINT 'Updated SyncAuditLogs references';

    -- Step 3: Delete the duplicate object rows
    DELETE o
    FROM Objects o
    INNER JOIN #DuplicatesToDelete dd ON o.Id = dd.DuplicateId;
    PRINT 'Deleted ' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' duplicate object rows';

    -- Step 4: Also clean up duplicate Identity links
    -- When objects were duplicated, they may have created duplicate Identity records too
    -- Find Identities that have duplicate linked objects (same SourceUniqueId)
    -- and null out the IdentityId on orphaned objects
    UPDATE o
    SET o.IdentityId = NULL
    FROM Objects o
    WHERE o.IdentityId IS NOT NULL
    AND o.Id NOT IN (
        SELECT MIN(Id)
        FROM Objects
        WHERE IdentityId = o.IdentityId AND SourceUniqueId = o.SourceUniqueId
        GROUP BY IdentityId, SourceUniqueId
    )
    AND EXISTS (
        SELECT 1 FROM Objects o2
        WHERE o2.IdentityId = o.IdentityId
        AND o2.SourceUniqueId = o.SourceUniqueId
        AND o2.Id < o.Id
    );
END
ELSE
BEGIN
    PRINT 'No duplicate objects found - skipping deduplication';
END

-- Step 5: Ensure the unique index exists (filtered to allow multiple NULL SourceUniqueIds)
-- Drop any existing non-filtered or non-unique version and recreate as filtered unique
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_SourceUnique' AND object_id = OBJECT_ID('Objects'))
BEGIN
    -- Check if it needs to be recreated (not unique, or not filtered)
    DECLARE @isUnique BIT;
    SELECT @isUnique = is_unique FROM sys.indexes WHERE name = 'IX_Objects_SourceUnique' AND object_id = OBJECT_ID('Objects');
    DECLARE @hasFilter BIT;
    SELECT @hasFilter = CASE WHEN has_filter = 1 THEN 1 ELSE 0 END FROM sys.indexes WHERE name = 'IX_Objects_SourceUnique' AND object_id = OBJECT_ID('Objects');

    IF @isUnique = 0 OR @hasFilter = 0
    BEGIN
        DROP INDEX [IX_Objects_SourceUnique] ON [Objects];
        CREATE UNIQUE INDEX [IX_Objects_SourceUnique] ON [Objects] ([SourceConnectionId], [SourceUniqueId])
            WHERE [SourceUniqueId] IS NOT NULL;
        PRINT 'Recreated IX_Objects_SourceUnique as filtered UNIQUE index';
    END
    ELSE
    BEGIN
        PRINT 'IX_Objects_SourceUnique already exists as filtered unique';
    END
END
ELSE
BEGIN
    CREATE UNIQUE INDEX [IX_Objects_SourceUnique] ON [Objects] ([SourceConnectionId], [SourceUniqueId])
        WHERE [SourceUniqueId] IS NOT NULL;
    PRINT 'Created IX_Objects_SourceUnique as filtered unique index';
END

-- Cleanup temp tables
DROP TABLE IF EXISTS #DuplicateKeepers;
DROP TABLE IF EXISTS #DuplicatesToDelete;

PRINT 'V006 complete: Objects deduplicated and unique index verified';
GO
