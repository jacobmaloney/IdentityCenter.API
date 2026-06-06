-- V111: Add recurrence columns to Campaigns for automated recurring access reviews.
--
-- Notes:
--   * Each column is guarded with IF NOT EXISTS to keep the migration idempotent
--     and safe to re-run on any environment.
--   * Three related columns (ParentCampaignId, IsRecurring, RecurrencePattern)
--     are NOT added here — they already exist in V004 (Campaigns table baseline).
--     The recurrence engine reads ParentCampaignId; IsRecurring/RecurrencePattern
--     remain available for legacy callers but new code uses the columns below.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'RecurrenceType')
BEGIN
    ALTER TABLE Campaigns ADD RecurrenceType NVARCHAR(20) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'RecurrenceInterval')
BEGIN
    ALTER TABLE Campaigns ADD RecurrenceInterval INT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'RecurrenceStartDate')
BEGIN
    ALTER TABLE Campaigns ADD RecurrenceStartDate DATE NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'AutoLaunch')
BEGIN
    ALTER TABLE Campaigns ADD AutoLaunch BIT NOT NULL CONSTRAINT DF_Campaigns_AutoLaunch DEFAULT 0;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'CampaignDurationDays')
BEGIN
    ALTER TABLE Campaigns ADD CampaignDurationDays INT NULL CONSTRAINT DF_Campaigns_DurationDays DEFAULT 30;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'NextScheduledRun')
BEGIN
    ALTER TABLE Campaigns ADD NextScheduledRun DATE NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'IsRecurrencePaused')
BEGIN
    ALTER TABLE Campaigns ADD IsRecurrencePaused BIT NOT NULL CONSTRAINT DF_Campaigns_IsRecurrencePaused DEFAULT 0;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'IsRecurrenceClone')
BEGIN
    ALTER TABLE Campaigns ADD IsRecurrenceClone BIT NOT NULL CONSTRAINT DF_Campaigns_IsRecurrenceClone DEFAULT 0;
END
GO
