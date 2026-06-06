-- V077: Agent Events — change events pushed by remote agents

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AgentEvents')
BEGIN
    CREATE TABLE AgentEvents (
        Id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        AgentId         NVARCHAR(450) NULL,
        EventType       NVARCHAR(100) NOT NULL,
        Severity        NVARCHAR(20) NOT NULL DEFAULT 'Info',
        SourceHost      NVARCHAR(255) NULL,
        Description     NVARCHAR(1000) NULL,
        DataJson        NVARCHAR(MAX) NULL,
        OccurredAt      DATETIME2 NOT NULL,
        ReceivedAt      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        IsAcknowledged  BIT NOT NULL DEFAULT 0,
        AcknowledgedAt  DATETIME2 NULL,
        AcknowledgedBy  NVARCHAR(256) NULL
    );

    CREATE NONCLUSTERED INDEX IX_AgentEvents_Type
        ON AgentEvents(EventType, ReceivedAt DESC);

    CREATE NONCLUSTERED INDEX IX_AgentEvents_Unacked
        ON AgentEvents(IsAcknowledged, Severity)
        WHERE IsAcknowledged = 0;

    PRINT 'V077: Created AgentEvents table';
END
GO
