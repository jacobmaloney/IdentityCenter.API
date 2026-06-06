-- Create SMTP Configuration Table
-- Stores email server settings with encrypted credentials

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SMTPConfiguration]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SMTPConfiguration] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [DisplayName] NVARCHAR(200) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [IsDefault] BIT NOT NULL DEFAULT 1,
        [IsActive] BIT NOT NULL DEFAULT 1,

        -- Server Settings (Encrypted)
        [Server] NVARCHAR(MAX) NOT NULL,          -- Encrypted: smtp.gmail.com
        [Port] INT NOT NULL DEFAULT 587,
        [EnableSsl] BIT NOT NULL DEFAULT 1,

        -- Authentication (Encrypted)
        [Username] NVARCHAR(MAX) NOT NULL,        -- Encrypted: user@domain.com
        [Password] NVARCHAR(MAX) NOT NULL,        -- Encrypted: password

        -- Email Settings
        [FromAddress] NVARCHAR(255) NOT NULL,     -- noreply@identitycenter.local
        [FromDisplayName] NVARCHAR(200) NULL,     -- Identity Center
        [ReplyToAddress] NVARCHAR(255) NULL,
        [ReplyToDisplayName] NVARCHAR(200) NULL,

        -- Audit Fields
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] NVARCHAR(255) NULL,
        [ModifiedAt] DATETIME2 NULL,
        [ModifiedBy] NVARCHAR(255) NULL,

        -- Test Information
        [LastTestDate] DATETIME2 NULL,
        [LastTestResult] NVARCHAR(MAX) NULL,
        [LastTestSuccess] BIT NULL
    );

    -- Index for default configuration lookup
    CREATE INDEX IX_SMTPConfiguration_IsDefault ON [dbo].[SMTPConfiguration] ([IsDefault]) WHERE [IsDefault] = 1;

    -- Index for active configuration lookup
    CREATE INDEX IX_SMTPConfiguration_IsActive ON [dbo].[SMTPConfiguration] ([IsActive]) WHERE [IsActive] = 1;

    PRINT 'SMTPConfiguration table created successfully';
END
ELSE
BEGIN
    PRINT 'SMTPConfiguration table already exists';
END
GO
