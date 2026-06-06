-- V102: Conversation history persistence for chat context across sessions

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('ConversationHistory') AND type = 'U')
BEGIN
    CREATE TABLE ConversationHistory (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        UserId NVARCHAR(500) NOT NULL,
        Role NVARCHAR(20) NOT NULL,          -- 'user' or 'assistant'
        Content NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        SessionId NVARCHAR(100) NULL
    );

    CREATE INDEX IX_ConversationHistory_UserId ON ConversationHistory (UserId, CreatedAt DESC);

    -- Auto-cleanup: keep max 20 turns per user (handled by app, not DB constraint)
END
