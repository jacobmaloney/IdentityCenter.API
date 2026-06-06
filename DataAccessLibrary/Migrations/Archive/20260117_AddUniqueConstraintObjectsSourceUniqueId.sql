-- Migration: Add unique constraint on Objects table to prevent duplicate SourceUniqueId per connection
-- This ensures the MERGE operation cannot create duplicates even with concurrent syncs

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- First, identify and log any existing duplicates
PRINT 'Checking for existing duplicates...'

;WITH DuplicateObjects AS (
    SELECT
        SourceConnectionId,
        SourceUniqueId,
        COUNT(*) AS DupeCount,
        MIN(FirstSyncedAt) AS FirstCreated,
        MAX(FirstSyncedAt) AS LastCreated
    FROM Objects
    WHERE SourceUniqueId IS NOT NULL
    GROUP BY SourceConnectionId, SourceUniqueId
    HAVING COUNT(*) > 1
)
SELECT
    d.*,
    o.Id,
    o.DisplayName,
    o.Email,
    o.Username,
    o.FirstSyncedAt
FROM DuplicateObjects d
INNER JOIN Objects o ON o.SourceConnectionId = d.SourceConnectionId
    AND o.SourceUniqueId = d.SourceUniqueId
ORDER BY d.SourceConnectionId, d.SourceUniqueId, o.FirstSyncedAt;

-- Delete duplicates, keeping the first created (oldest)
PRINT 'Removing duplicates (keeping oldest record)...'

;WITH RankedDuplicates AS (
    SELECT
        Id,
        SourceConnectionId,
        SourceUniqueId,
        ROW_NUMBER() OVER (
            PARTITION BY SourceConnectionId, SourceUniqueId
            ORDER BY FirstSyncedAt ASC
        ) AS RowNum
    FROM Objects
    WHERE SourceUniqueId IS NOT NULL
)
DELETE FROM RankedDuplicates WHERE RowNum > 1;

PRINT CONCAT('Deleted ', @@ROWCOUNT, ' duplicate records');

-- Now add the unique constraint
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_Objects_SourceConnectionId_SourceUniqueId'
    AND object_id = OBJECT_ID('Objects')
)
BEGIN
    PRINT 'Creating unique index on Objects(SourceConnectionId, SourceUniqueId)...'

    CREATE UNIQUE NONCLUSTERED INDEX UX_Objects_SourceConnectionId_SourceUniqueId
    ON Objects (SourceConnectionId, SourceUniqueId)
    WHERE SourceUniqueId IS NOT NULL;

    PRINT 'Unique index created successfully'
END
ELSE
BEGIN
    PRINT 'Unique index already exists'
END

GO
