-- V138: Agent command channel for remote scan agents (Conduit SQL Discovery).
--
-- IC queues commands here ("Request Agent Scan" on the SQL Servers page); the
-- Conduit-side poller consumes them through the API:
--   GET  /api/agent/commands/pending           -> Pending commands, oldest first
--   POST /api/agent/commands/{id}/ack          -> Pending -> Acked (claim)
--   POST /api/agent/commands/{id}/complete     -> { success, message } -> Completed/Failed
--
-- Status vocabulary: Pending / Acked / Completed / Failed.
-- Single batch, idempotent.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AgentCommands')
BEGIN
    CREATE TABLE AgentCommands (
        Id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        CommandType     NVARCHAR(50) NOT NULL,
        PayloadJson     NVARCHAR(MAX) NULL,
        Status          NVARCHAR(20) NOT NULL DEFAULT 'Pending',
        RequestedBy     NVARCHAR(256) NULL,
        RequestedAt     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        AckedAt         DATETIME2 NULL,
        CompletedAt     DATETIME2 NULL,
        Success         BIT NULL,
        ResultMessage   NVARCHAR(2000) NULL
    );
    CREATE INDEX IX_AgentCommands_Status_RequestedAt ON AgentCommands(Status, RequestedAt);
    PRINT 'V138: Created AgentCommands table.';
END
ELSE
BEGIN
    PRINT 'V138: AgentCommands table already exists -- nothing to do.';
END
