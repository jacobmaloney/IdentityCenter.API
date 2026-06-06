-- ===============================================
-- Email Notification System - Database Schema
-- Creates tables for SMTP configuration, email templates, and email queue
-- All sensitive credentials are encrypted before storage
-- ===============================================

-- Create SMTP Configuration Table
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

-- Create Email Templates Table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[EmailTemplates]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[EmailTemplates] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Name] NVARCHAR(100) NOT NULL,                -- REVIEW_ASSIGNED, REVIEW_DUE, etc.
        [Subject] NVARCHAR(500) NOT NULL,             -- Subject line with {variables}
        [Body] NVARCHAR(MAX) NOT NULL,                -- HTML template with {variables}
        [Category] NVARCHAR(100) NULL,                -- AccessReview, Workflow, System
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [ModifiedAt] DATETIME2 NULL
    );

    -- Index for name lookup
    CREATE UNIQUE INDEX IX_EmailTemplates_Name ON [dbo].[EmailTemplates] ([Name]);

    -- Index for category lookup
    CREATE INDEX IX_EmailTemplates_Category ON [dbo].[EmailTemplates] ([Category]);

    -- Index for active templates
    CREATE INDEX IX_EmailTemplates_IsActive ON [dbo].[EmailTemplates] ([IsActive]) WHERE [IsActive] = 1;

    PRINT 'EmailTemplates table created successfully';
END
ELSE
BEGIN
    PRINT 'EmailTemplates table already exists';
END
GO

-- Create Email Queue Table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[EmailQueue]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[EmailQueue] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [ToAddress] NVARCHAR(255) NOT NULL,
        [ToDisplayName] NVARCHAR(200) NULL,
        [Subject] NVARCHAR(500) NOT NULL,
        [Body] NVARCHAR(MAX) NOT NULL,
        [IsHtml] BIT NOT NULL DEFAULT 1,

        -- Status tracking
        [Status] NVARCHAR(50) NOT NULL DEFAULT 'Pending',  -- Pending, Sending, Sent, Failed
        [RetryCount] INT NOT NULL DEFAULT 0,
        [MaxRetries] INT NOT NULL DEFAULT 3,
        [SentAt] DATETIME2 NULL,
        [ErrorMessage] NVARCHAR(MAX) NULL,

        -- Metadata
        [TemplateId] NVARCHAR(100) NULL,
        [RelatedEntityType] NVARCHAR(100) NULL,           -- Assignment, Campaign, etc.
        [RelatedEntityId] UNIQUEIDENTIFIER NULL,

        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [ProcessedAt] DATETIME2 NULL
    );

    -- Index for status lookup
    CREATE INDEX IX_EmailQueue_Status ON [dbo].[EmailQueue] ([Status]);

    -- Index for pending emails (most common query)
    CREATE INDEX IX_EmailQueue_Pending ON [dbo].[EmailQueue] ([Status], [RetryCount], [CreatedAt])
        WHERE [Status] = 'Pending';

    -- Index for created date (for cleanup jobs)
    CREATE INDEX IX_EmailQueue_CreatedAt ON [dbo].[EmailQueue] ([CreatedAt]);

    -- Index for related entities (for tracking)
    CREATE INDEX IX_EmailQueue_RelatedEntity ON [dbo].[EmailQueue] ([RelatedEntityType], [RelatedEntityId]);

    PRINT 'EmailQueue table created successfully';
END
ELSE
BEGIN
    PRINT 'EmailQueue table already exists';
END
GO

PRINT '';
PRINT '===========================================';
PRINT 'Email notification system tables created!';
PRINT '===========================================';
PRINT 'Tables created:';
PRINT '  - SMTPConfiguration (with encrypted credentials)';
PRINT '  - EmailTemplates (for reusable email content)';
PRINT '  - EmailQueue (for background email processing)';
PRINT '';
PRINT 'Next steps:';
PRINT '  1. Configure SMTP via Quick Setup Wizard or Admin Dashboard';
PRINT '  2. Email templates will be auto-seeded on first SMTP save';
PRINT '  3. Email notifications will be sent for access review events';
PRINT '';
GO
