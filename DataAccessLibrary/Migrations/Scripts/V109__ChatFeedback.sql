-- V109: Chat feedback table
-- Captures thumbs-up / thumbs-down feedback from users on bot messages so we
-- can iterate on prompt + RAG quality. Idempotent — only creates the table on
-- a fresh database; existing installations keep their data.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ChatFeedback')
BEGIN
    CREATE TABLE ChatFeedback (
        Id          UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        MessageId   NVARCHAR(200)       NOT NULL,
        UserId      NVARCHAR(200)       NULL,
        Feedback    INT                 NOT NULL,  -- +1 or -1
        CreatedAt   DATETIME2           NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE INDEX IX_ChatFeedback_MessageId ON ChatFeedback (MessageId);
    CREATE INDEX IX_ChatFeedback_CreatedAt ON ChatFeedback (CreatedAt DESC);
END
