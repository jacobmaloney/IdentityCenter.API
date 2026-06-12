-- V140: Multi-installation agent targeting for the agent command channel.
--
-- 1. AgentCommands gains targeting columns:
--      TargetAgentId    NULL = legacy broadcast (pre-V140 behavior)
--      ClaimedByAgentId set by the atomic claim (POST /api/agent/commands/claim)
--      AttemptCount     incremented on every claim
--    plus a covering index for the claim scan.
-- 2. New Agents registry: admin pre-registers an agent (Flow A), IC mints a
--    per-agent API key (ApiKeys.AgentId -> Agents.Id), the agent heartbeats
--    into LastSeenAt/Version/Capabilities.
--
-- Idempotent; per-column guards so a partially-applied schema heals.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AgentCommands' AND COLUMN_NAME = 'TargetAgentId')
BEGIN
    ALTER TABLE AgentCommands ADD TargetAgentId UNIQUEIDENTIFIER NULL;
    PRINT 'V140: Added AgentCommands.TargetAgentId.';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AgentCommands' AND COLUMN_NAME = 'ClaimedByAgentId')
BEGIN
    ALTER TABLE AgentCommands ADD ClaimedByAgentId UNIQUEIDENTIFIER NULL;
    PRINT 'V140: Added AgentCommands.ClaimedByAgentId.';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AgentCommands' AND COLUMN_NAME = 'AttemptCount')
BEGIN
    ALTER TABLE AgentCommands ADD AttemptCount INT NOT NULL CONSTRAINT DF_AgentCommands_AttemptCount DEFAULT 0 WITH VALUES;
    PRINT 'V140: Added AgentCommands.AttemptCount.';
END

GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_AgentCommands_TargetAgentId_Status_RequestedAt'
                 AND object_id = OBJECT_ID('AgentCommands'))
BEGIN
    CREATE INDEX IX_AgentCommands_TargetAgentId_Status_RequestedAt
        ON AgentCommands(TargetAgentId, Status, RequestedAt);
    PRINT 'V140: Created IX_AgentCommands_TargetAgentId_Status_RequestedAt.';
END

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Agents')
BEGIN
    CREATE TABLE Agents (
        Id             UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        Name           NVARCHAR(256) NOT NULL,
        Location       NVARCHAR(256) NULL,
        Capabilities   NVARCHAR(1024) NULL,   -- JSON array of allow-listed capability strings
        Version        NVARCHAR(64) NULL,
        LastSeenAt     DATETIME2 NULL,
        LastSeenFromIp NVARCHAR(64) NULL,
        IsActive       BIT NOT NULL DEFAULT 1,
        CreatedAt      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    PRINT 'V140: Created Agents table.';
END
ELSE
BEGIN
    PRINT 'V140: Agents table already exists -- nothing to do.';
END
