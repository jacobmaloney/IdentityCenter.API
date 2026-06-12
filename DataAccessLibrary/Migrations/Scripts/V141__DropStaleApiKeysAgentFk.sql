-- V141: Drop the stale FK_ApiKeys_RemoteAgents_AgentId constraint.
--
-- V004 created FK_ApiKeys_RemoteAgents_AgentId pointing ApiKeys.AgentId at
-- RemoteAgents(Id). V140 added the Agents installation registry, and the new
-- per-agent key flow stamps ApiKeys.AgentId with Agents ids, which the old
-- FK rejects (error 547) -- per-agent key minting fails.
--
-- ApiKeys.AgentId is intentionally UNCONSTRAINED from V141 on: the column has
-- two writers -- the legacy RemoteAgents execution-server surface (RemoteAgents
-- ids) and the new Agents installation registry (Agents ids, KeyType = 'Agent').
-- An FK to either table would break the other.
--
-- Idempotent; guarded so a database without the FK is a no-op.

IF EXISTS (SELECT 1 FROM sys.foreign_keys
           WHERE name = 'FK_ApiKeys_RemoteAgents_AgentId'
             AND parent_object_id = OBJECT_ID('dbo.ApiKeys'))
BEGIN
    ALTER TABLE dbo.ApiKeys DROP CONSTRAINT FK_ApiKeys_RemoteAgents_AgentId;
    PRINT 'V141: Dropped FK_ApiKeys_RemoteAgents_AgentId.';
END
ELSE
BEGIN
    PRINT 'V141: FK_ApiKeys_RemoteAgents_AgentId not present -- nothing to do.';
END
