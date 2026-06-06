-- V118: Index Objects.ObjectClass for the most common read path in the system.
-- GetObjectsAsync("user"/"group"/"computer"/...) was scanning the full Objects table
-- on every call. Composite covering index satisfies the typical projection
-- (Id/DisplayName/CN/Username/IdentityId/IsActive) without a key-lookup.

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Objects_ObjectClass' AND object_id = OBJECT_ID(N'dbo.Objects'))
BEGIN
    CREATE INDEX IX_Objects_ObjectClass ON dbo.Objects (ObjectClass)
        INCLUDE (IsActive, DisplayName, CN, Username, IdentityId);
END
GO

-- Filtered index for "live objects" (DeletedAt IS NULL) — every read query is supposed
-- to filter on this but nothing was helping. Keeps the live working set small for
-- table-wide scans (e.g. policy evaluation).

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Objects_DeletedAt_NotDeleted' AND object_id = OBJECT_ID(N'dbo.Objects'))
BEGIN
    CREATE INDEX IX_Objects_DeletedAt_NotDeleted ON dbo.Objects (DeletedAt)
        WHERE DeletedAt IS NULL;
END
GO
