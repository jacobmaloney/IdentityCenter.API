-- V167: Foundation for AD account creation via the Conduit agent (design §4.1, Phase 1).
--
-- Additive + nullable only; existing behaviour is unchanged when these are NULL. No values are
-- populated or backfilled here — routing-key population is a later phase.
--   - DirectoryConnections.AgentId: the routing key — which agent can reach this directory. Soft
--     reference, no FK, mirroring the V143 Objects.SourceJobServerId convention (provenance, not a
--     live integrity constraint). Filtered index over the non-null rows.
--   - ProcessInstances.PendingAgentCommandId: external correlation — lets a create's completion
--     callback find the single waiting instance. UNIQUE filtered index (one waiting instance per
--     command) so the callback is an indexed seek, never a scan.
--   - AgentCommands.ResultJson: structured agent result (objectGUID / DN / verbatim ldapError).
--     ResultMessage is NVARCHAR(2000) prose and cannot carry it.
--
-- Also extends the V036 IX_ProcessInstances_Status filtered index — whose predicate ENUMERATES
-- statuses — to admit the new 'WaitingForAgent' status, so a waiting create is covered by the same
-- active-instance index the sweeper and Process Center rely on.
--
-- IDEMPOTENT: COL_LENGTH / sys.indexes guards; each column and index independently guarded
-- (shared-DB rule). The index rebuild only fires when the predicate does not already list
-- 'WaitingForAgent', so a re-run is a clean no-op.

SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.DirectoryConnections', 'AgentId') IS NULL
BEGIN
    ALTER TABLE [dbo].[DirectoryConnections] ADD [AgentId] UNIQUEIDENTIFIER NULL;
    PRINT 'V167: Added DirectoryConnections.AgentId.';
END
ELSE
    PRINT 'V167: DirectoryConnections.AgentId already present -- nothing to do.';
GO

IF COL_LENGTH('dbo.DirectoryConnections', 'AgentId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = 'IX_DirectoryConnections_AgentId'
                     AND object_id = OBJECT_ID('dbo.DirectoryConnections'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DirectoryConnections_AgentId]
        ON [dbo].[DirectoryConnections] ([AgentId]) WHERE [AgentId] IS NOT NULL;
    PRINT 'V167: Created IX_DirectoryConnections_AgentId.';
END
ELSE
    PRINT 'V167: IX_DirectoryConnections_AgentId already present (or column missing) -- nothing to do.';
GO

IF COL_LENGTH('dbo.ProcessInstances', 'PendingAgentCommandId') IS NULL
BEGIN
    ALTER TABLE [dbo].[ProcessInstances] ADD [PendingAgentCommandId] UNIQUEIDENTIFIER NULL;
    PRINT 'V167: Added ProcessInstances.PendingAgentCommandId.';
END
ELSE
    PRINT 'V167: ProcessInstances.PendingAgentCommandId already present -- nothing to do.';
GO

IF COL_LENGTH('dbo.ProcessInstances', 'PendingAgentCommandId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = 'UX_ProcessInstances_PendingAgentCommandId'
                     AND object_id = OBJECT_ID('dbo.ProcessInstances'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UX_ProcessInstances_PendingAgentCommandId]
        ON [dbo].[ProcessInstances] ([PendingAgentCommandId]) WHERE [PendingAgentCommandId] IS NOT NULL;
    PRINT 'V167: Created UX_ProcessInstances_PendingAgentCommandId.';
END
ELSE
    PRINT 'V167: UX_ProcessInstances_PendingAgentCommandId already present (or column missing) -- nothing to do.';
GO

IF COL_LENGTH('dbo.AgentCommands', 'ResultJson') IS NULL
BEGIN
    ALTER TABLE [dbo].[AgentCommands] ADD [ResultJson] NVARCHAR(MAX) NULL;
    PRINT 'V167: Added AgentCommands.ResultJson.';
END
ELSE
    PRINT 'V167: AgentCommands.ResultJson already present -- nothing to do.';
GO

-- Extend the V036 status-filtered index to cover WaitingForAgent. The predicate enumerates
-- statuses, so a filtered index must be dropped and recreated; guarded on the filter text so it
-- only rebuilds when 'WaitingForAgent' is not already listed.
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'IX_ProcessInstances_Status'
             AND object_id = OBJECT_ID('dbo.ProcessInstances'))
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = 'IX_ProcessInstances_Status'
                     AND object_id = OBJECT_ID('dbo.ProcessInstances')
                     AND filter_definition LIKE '%WaitingForAgent%')
BEGIN
    DROP INDEX [IX_ProcessInstances_Status] ON [dbo].[ProcessInstances];
    CREATE NONCLUSTERED INDEX [IX_ProcessInstances_Status]
        ON [dbo].[ProcessInstances] ([Status])
        WHERE [Status] IN ('Running', 'WaitingForApproval', 'WaitingForDuration', 'WaitingForCondition', 'WaitingForAgent');
    PRINT 'V167: Extended IX_ProcessInstances_Status filter to include WaitingForAgent.';
END
ELSE
    PRINT 'V167: IX_ProcessInstances_Status already covers WaitingForAgent (or is absent) -- nothing to do.';
GO

PRINT 'Schema version 167 applied - AD create via agent foundation (routing key, correlation, result json)';
