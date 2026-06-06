-- Migration: AddEnforcementModeAndTeamsTemplates
-- Date: 2025-12-28
-- Description: Add enforcement mode fields to CompliancePolicies and create Teams messaging tables
--
-- This migration adds:
-- 1. Enforcement mode fields to CompliancePolicies table
-- 2. TeamsMessageTemplates table
-- 3. TeamsMessageQueue table

-- =============================================
-- Step 1: Add enforcement mode columns to CompliancePolicies
-- =============================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'EnforcementMode')
BEGIN
    ALTER TABLE CompliancePolicies ADD EnforcementMode NVARCHAR(20) NOT NULL DEFAULT 'Soft';
    PRINT 'Added EnforcementMode column to CompliancePolicies';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'DailyProcessingLimit')
BEGIN
    ALTER TABLE CompliancePolicies ADD DailyProcessingLimit INT NULL DEFAULT 10;
    PRINT 'Added DailyProcessingLimit column to CompliancePolicies';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'DailyProcessedCount')
BEGIN
    ALTER TABLE CompliancePolicies ADD DailyProcessedCount INT NOT NULL DEFAULT 0;
    PRINT 'Added DailyProcessedCount column to CompliancePolicies';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'LastProcessingResetDate')
BEGIN
    ALTER TABLE CompliancePolicies ADD LastProcessingResetDate DATETIME2 NULL;
    PRINT 'Added LastProcessingResetDate column to CompliancePolicies';
END
GO

-- Update default for IsActive on new policies (existing policies keep their current value)
-- Note: This changes the default constraint, not existing data
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name LIKE '%DF%CompliancePolicies%IsActive%')
BEGIN
    PRINT 'IsActive default constraint exists - consider updating if you want new policies to default to false';
END
GO

-- =============================================
-- Step 2: Create TeamsMessageTemplates table
-- =============================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeamsMessageTemplates')
BEGIN
    CREATE TABLE TeamsMessageTemplates (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Name NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500) NULL,
        MessageTemplate NVARCHAR(MAX) NOT NULL,
        UseAdaptiveCard BIT NOT NULL DEFAULT 0,
        AdaptiveCardJson NVARCHAR(MAX) NULL,
        Category NVARCHAR(100) NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        IsBuiltIn BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ModifiedAt DATETIME2 NULL
    );

    CREATE INDEX IX_TeamsMessageTemplates_Name ON TeamsMessageTemplates(Name);
    CREATE INDEX IX_TeamsMessageTemplates_Category ON TeamsMessageTemplates(Category);
    CREATE INDEX IX_TeamsMessageTemplates_IsActive ON TeamsMessageTemplates(IsActive);

    PRINT 'Created TeamsMessageTemplates table';
END
GO

-- =============================================
-- Step 3: Create TeamsMessageQueue table
-- =============================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeamsMessageQueue')
BEGIN
    CREATE TABLE TeamsMessageQueue (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Recipient NVARCHAR(500) NOT NULL,
        RecipientType NVARCHAR(50) NOT NULL DEFAULT 'User',
        MessageContent NVARCHAR(MAX) NOT NULL,
        IsAdaptiveCard BIT NOT NULL DEFAULT 0,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
        RetryCount INT NOT NULL DEFAULT 0,
        MaxRetries INT NOT NULL DEFAULT 3,
        SentAt DATETIME2 NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        TemplateId UNIQUEIDENTIFIER NULL,
        RelatedEntityType NVARCHAR(100) NULL,
        RelatedEntityId UNIQUEIDENTIFIER NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ProcessedAt DATETIME2 NULL
    );

    CREATE INDEX IX_TeamsMessageQueue_Status ON TeamsMessageQueue(Status);
    CREATE INDEX IX_TeamsMessageQueue_CreatedAt ON TeamsMessageQueue(CreatedAt);
    CREATE INDEX IX_TeamsMessageQueue_TemplateId ON TeamsMessageQueue(TemplateId);
    CREATE INDEX IX_TeamsMessageQueue_RelatedEntityId ON TeamsMessageQueue(RelatedEntityId);

    PRINT 'Created TeamsMessageQueue table';
END
GO

-- =============================================
-- Step 4: Seed default Teams templates for policy violations
-- =============================================

-- Policy Violation Teams Template (Plain Text)
IF NOT EXISTS (SELECT 1 FROM TeamsMessageTemplates WHERE Name = 'POLICY_VIOLATION')
BEGIN
    INSERT INTO TeamsMessageTemplates (Id, Name, Description, MessageTemplate, UseAdaptiveCard, Category, IsActive, IsBuiltIn)
    VALUES (
        'A0000001-0000-0000-0000-000000000001',
        'POLICY_VIOLATION',
        'Notification sent when a compliance policy violation is detected',
        '**Policy Violation Detected**

**Policy:** {PolicyName}
**User:** {UserDisplayName}
**Violation:** {ViolationMessage}
**Severity:** {Severity}
**Detected:** {DetectedAt}

Please review this violation in the Compliance Center.',
        0, -- Not an adaptive card
        'Compliance',
        1,
        1
    );
    PRINT 'Seeded POLICY_VIOLATION Teams template';
END
GO

-- Policy Violation Teams Adaptive Card Template
IF NOT EXISTS (SELECT 1 FROM TeamsMessageTemplates WHERE Name = 'POLICY_VIOLATION_CARD')
BEGIN
    INSERT INTO TeamsMessageTemplates (Id, Name, Description, MessageTemplate, UseAdaptiveCard, AdaptiveCardJson, Category, IsActive, IsBuiltIn)
    VALUES (
        'A0000001-0000-0000-0000-000000000002',
        'POLICY_VIOLATION_CARD',
        'Adaptive Card notification for compliance policy violations',
        'Policy Violation: {PolicyName}',
        1, -- Is an adaptive card
        '{
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "type": "AdaptiveCard",
    "version": "1.4",
    "body": [
        {
            "type": "Container",
            "style": "warning",
            "items": [
                {
                    "type": "TextBlock",
                    "text": "Policy Violation Detected",
                    "weight": "Bolder",
                    "size": "Large"
                }
            ]
        },
        {
            "type": "FactSet",
            "facts": [
                { "title": "Policy:", "value": "{PolicyName}" },
                { "title": "User:", "value": "{UserDisplayName}" },
                { "title": "Severity:", "value": "{Severity}" },
                { "title": "Detected:", "value": "{DetectedAt}" }
            ]
        },
        {
            "type": "TextBlock",
            "text": "{ViolationMessage}",
            "wrap": true,
            "spacing": "Medium"
        }
    ],
    "actions": [
        {
            "type": "Action.OpenUrl",
            "title": "View in Portal",
            "url": "{PortalUrl}/compliance-center?tab=violations"
        }
    ]
}',
        'Compliance',
        1,
        1
    );
    PRINT 'Seeded POLICY_VIOLATION_CARD Teams adaptive card template';
END
GO

-- Access Review Reminder Teams Template
IF NOT EXISTS (SELECT 1 FROM TeamsMessageTemplates WHERE Name = 'ACCESS_REVIEW_REMINDER')
BEGIN
    INSERT INTO TeamsMessageTemplates (Id, Name, Description, MessageTemplate, UseAdaptiveCard, Category, IsActive, IsBuiltIn)
    VALUES (
        'A0000001-0000-0000-0000-000000000003',
        'ACCESS_REVIEW_REMINDER',
        'Reminder notification for pending access reviews',
        '**Access Review Reminder**

You have **{PendingCount}** pending access review(s) that require your attention.

**Campaign:** {CampaignName}
**Due Date:** {DueDate}

Please complete your reviews before the due date.',
        0,
        'AccessReview',
        1,
        1
    );
    PRINT 'Seeded ACCESS_REVIEW_REMINDER Teams template';
END
GO

-- Manager Required Violation Template (specific for missing manager policy)
IF NOT EXISTS (SELECT 1 FROM TeamsMessageTemplates WHERE Name = 'MANAGER_REQUIRED_VIOLATION')
BEGIN
    INSERT INTO TeamsMessageTemplates (Id, Name, Description, MessageTemplate, UseAdaptiveCard, Category, IsActive, IsBuiltIn)
    VALUES (
        'A0000001-0000-0000-0000-000000000004',
        'MANAGER_REQUIRED_VIOLATION',
        'Notification when a user account has no manager assigned',
        '**Manager Required - Action Needed**

The following user account has been flagged because they do not have a manager assigned:

**User:** {UserDisplayName}
**Account:** {Username}
**Department:** {Department}
**Detected:** {DetectedAt}

Please assign a manager to this user account or take appropriate action.',
        0,
        'Compliance',
        1,
        1
    );
    PRINT 'Seeded MANAGER_REQUIRED_VIOLATION Teams template';
END
GO

PRINT 'Migration complete: AddEnforcementModeAndTeamsTemplates';
GO
