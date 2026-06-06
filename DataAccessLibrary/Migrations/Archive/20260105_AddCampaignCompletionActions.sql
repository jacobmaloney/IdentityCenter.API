-- Migration: Add Campaign Completion Actions Configuration
-- Allows configuring what happens when campaigns end or decisions are made

-- Add OnDenialAction column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'OnDenialAction')
BEGIN
    ALTER TABLE Campaigns ADD OnDenialAction NVARCHAR(50) NOT NULL DEFAULT 'RemoveFromGroup';
    PRINT 'Added OnDenialAction column';
END
GO

-- Add AutoRemediateOnDenial column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'AutoRemediateOnDenial')
BEGIN
    ALTER TABLE Campaigns ADD AutoRemediateOnDenial BIT NOT NULL DEFAULT 1;
    PRINT 'Added AutoRemediateOnDenial column';
END
GO

-- Add OnIncompleteAction column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'OnIncompleteAction')
BEGIN
    ALTER TABLE Campaigns ADD OnIncompleteAction NVARCHAR(50) NOT NULL DEFAULT 'None';
    PRINT 'Added OnIncompleteAction column';
END
GO

-- Add EscalationReviewerId column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'EscalationReviewerId')
BEGIN
    ALTER TABLE Campaigns ADD EscalationReviewerId UNIQUEIDENTIFIER NULL;
    PRINT 'Added EscalationReviewerId column';
END
GO

-- Add ExtensionDays column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'ExtensionDays')
BEGIN
    ALTER TABLE Campaigns ADD ExtensionDays INT NOT NULL DEFAULT 7;
    PRINT 'Added ExtensionDays column';
END
GO

-- Add OnApprovalAction column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'OnApprovalAction')
BEGIN
    ALTER TABLE Campaigns ADD OnApprovalAction NVARCHAR(50) NOT NULL DEFAULT 'Certify';
    PRINT 'Added OnApprovalAction column';
END
GO

-- Add CompletionActionsProcessed column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'CompletionActionsProcessed')
BEGIN
    ALTER TABLE Campaigns ADD CompletionActionsProcessed BIT NOT NULL DEFAULT 0;
    PRINT 'Added CompletionActionsProcessed column';
END
GO

-- Add CompletionActionsProcessedAt column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'CompletionActionsProcessedAt')
BEGIN
    ALTER TABLE Campaigns ADD CompletionActionsProcessedAt DATETIME2 NULL;
    PRINT 'Added CompletionActionsProcessedAt column';
END
GO

-- Record migration
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20260105_AddCampaignCompletionActions')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260105_AddCampaignCompletionActions', '8.0.0');
    PRINT 'Migration recorded in history';
END
GO

PRINT 'Migration complete: Campaign completion actions configuration added!';
GO
