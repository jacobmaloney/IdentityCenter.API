-- V021: Campaign Owner, CC Emails, Teams Notifications, and Standing Campaign Fixes
-- Adds campaign ownership, notification CC addresses, and Teams integration.
-- Backfills existing standing campaigns with email templates.

-- Campaign owner
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'OwnerId')
BEGIN
    ALTER TABLE Campaigns ADD OwnerId uniqueidentifier NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'OwnerName')
BEGIN
    ALTER TABLE Campaigns ADD OwnerName nvarchar(256) NULL;
END

-- CC emails + Teams toggle
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'NotificationCcEmails')
BEGIN
    ALTER TABLE Campaigns ADD NotificationCcEmails nvarchar(1000) NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'EnableTeamsNotifications')
BEGIN
    ALTER TABLE Campaigns ADD EnableTeamsNotifications bit NOT NULL DEFAULT 0;
END

-- Index for per-policy campaign lookup (column added in V022, use dynamic SQL to defer column validation)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'SourcePolicyId')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Campaigns') AND name = 'IX_Campaigns_SourcePolicyId')
BEGIN
    EXEC('CREATE NONCLUSTERED INDEX IX_Campaigns_SourcePolicyId ON Campaigns(SourcePolicyId) WHERE SourcePolicyId IS NOT NULL');
END

-- Default owner setting on AccessReviewSettings
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AccessReviewSettings') AND name = 'DefaultCampaignOwnerId')
BEGIN
    ALTER TABLE AccessReviewSettings ADD DefaultCampaignOwnerId uniqueidentifier NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AccessReviewSettings') AND name = 'DefaultCampaignOwnerName')
BEGIN
    ALTER TABLE AccessReviewSettings ADD DefaultCampaignOwnerName nvarchar(256) NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AccessReviewSettings') AND name = 'DefaultNotificationCcEmails')
BEGIN
    ALTER TABLE AccessReviewSettings ADD DefaultNotificationCcEmails nvarchar(1000) NULL;
END

-- Backfill V019 standing campaign with email templates (if missing)
UPDATE Campaigns
SET AssignmentEmailTemplateId = (SELECT TOP 1 Id FROM EmailTemplates WHERE Name = 'REVIEW_ASSIGNED' AND IsActive = 1),
    ReminderEmailTemplateId = (SELECT TOP 1 Id FROM EmailTemplates WHERE Name = 'REVIEW_DUE' AND IsActive = 1)
WHERE AssignmentEmailTemplateId IS NULL
  AND Status = 'Active'
  AND IsRecurring = 1;
