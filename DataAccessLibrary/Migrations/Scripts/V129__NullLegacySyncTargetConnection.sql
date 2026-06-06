-- V129: Null out the legacy SyncProjects.TargetConnectionId where it equals
-- SourceConnectionId.
--
-- BACKGROUND: AutoSyncProjectGenerator historically set TargetConnectionId =
-- connection.Id (the same value as SourceConnectionId). That was a meaningless
-- legacy value -- a sync never wrote back to its own source connection, and the
-- runtime never read TargetConnectionId at all. With the Phase 1 sync-sink seam
-- (SyncSinkFactory), TargetConnectionId now has real meaning:
--     NULL          => write to the internal IdentityCenter identity store
--     <connectionId> => outbound write to an external directory (Conduit-only;
--                       IdentityCenter fails such a run fast at start)
--
-- Any pre-existing project where TargetConnectionId == SourceConnectionId was an
-- identity-store sync mislabeled with a bogus target. Left as-is, those projects
-- would now fail fast as if they targeted an external system. This migration
-- corrects them to NULL so they continue writing to the identity store exactly as
-- they always have.
--
-- We deliberately scope the UPDATE to TargetConnectionId = SourceConnectionId ONLY.
-- A project whose TargetConnectionId differs from its source is a genuine (future)
-- outbound target and must be left untouched.
--
-- IDEMPOTENT: guarded by table + column existence checks. The UPDATE itself is
-- naturally idempotent -- after the first run no row satisfies
-- TargetConnectionId = SourceConnectionId, so a second run affects 0 rows.

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SyncProjects')
   AND EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.SyncProjects') AND name = N'TargetConnectionId')
   AND EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.SyncProjects') AND name = N'SourceConnectionId')
BEGIN
    UPDATE [SyncProjects]
       SET [TargetConnectionId] = NULL
     WHERE [TargetConnectionId] IS NOT NULL
       AND [TargetConnectionId] = [SourceConnectionId];

    PRINT 'V129: Nulled legacy SyncProjects.TargetConnectionId where it equaled SourceConnectionId (' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' row(s)).';
END
ELSE
BEGIN
    PRINT 'V129: SyncProjects / required columns not present -- skipped (no-op).';
END;
GO
