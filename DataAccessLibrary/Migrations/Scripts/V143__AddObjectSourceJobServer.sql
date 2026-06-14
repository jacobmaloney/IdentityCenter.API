-- V143: Object provenance -- stamp which job server (syncing Conduit installation)
-- last wrote each Objects row.
--
-- A "job server" is a Conduit installation identified by a durable instance GUID it
-- persists locally. On ingest, IC resolves that GUID to an Agents row (auto-registering
-- one if absent, exactly like the DirectoryConnections auto-seed), then stamps
-- Objects.SourceJobServerId with that Agents.Id. This makes the syncing Conduit a
-- first-class entry in the EXISTING Agents registry -- no parallel registry.
--
-- SOFT REFERENCE (no FK) BY DESIGN: SourceJobServerId points at Agents(Id) but is left
-- UNCONSTRAINED, mirroring ApiKeys.AgentId (see V141). A job server may be decommissioned
-- (its Agents row deactivated/removed) while historical Objects rows still carry its id;
-- an FK would block that and could reject ingest mid-batch. The id is provenance, not a
-- live integrity constraint.
--
-- Backward-compatible: nullable. Pre-existing objects stay NULL until their next sync
-- re-stamps them.
--
-- Idempotent; column + index guarded on absence (CC/IC shared-DB convention).

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'Objects' AND COLUMN_NAME = 'SourceJobServerId')
BEGIN
    ALTER TABLE Objects ADD SourceJobServerId UNIQUEIDENTIFIER NULL;
    PRINT 'V143: Added Objects.SourceJobServerId.';
END
ELSE
BEGIN
    PRINT 'V143: Objects.SourceJobServerId already present -- nothing to do.';
END

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME = 'Objects' AND COLUMN_NAME = 'SourceJobServerId')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = 'IX_Objects_SourceJobServerId' AND object_id = OBJECT_ID('dbo.Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_SourceJobServerId
        ON Objects (SourceJobServerId);
    PRINT 'V143: Created index IX_Objects_SourceJobServerId.';
END
ELSE
BEGIN
    PRINT 'V143: Index IX_Objects_SourceJobServerId already present or column missing -- nothing to do.';
END
