-- ============================================================================
-- Migration: Add ComplianceEscalationSettings and NotificationIntegrationSettings
-- Date: 2026-01-22
-- Purpose: Add JSON columns to SystemConfigurations for storing compliance
--          escalation and notification integration settings.
-- ============================================================================

-- Add ComplianceEscalationSettings column
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.SystemConfigurations')
      AND name = 'ComplianceEscalationSettings'
)
BEGIN
    ALTER TABLE [dbo].[SystemConfigurations]
        ADD [ComplianceEscalationSettings] NVARCHAR(MAX) NULL;

    PRINT 'Added ComplianceEscalationSettings column to SystemConfigurations';
END
ELSE
BEGIN
    PRINT 'ComplianceEscalationSettings column already exists';
END
GO

-- Add NotificationIntegrationSettings column
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.SystemConfigurations')
      AND name = 'NotificationIntegrationSettings'
)
BEGIN
    ALTER TABLE [dbo].[SystemConfigurations]
        ADD [NotificationIntegrationSettings] NVARCHAR(MAX) NULL;

    PRINT 'Added NotificationIntegrationSettings column to SystemConfigurations';
END
ELSE
BEGIN
    PRINT 'NotificationIntegrationSettings column already exists';
END
GO

PRINT 'Migration 20260122_AddComplianceSettingsColumns completed successfully';
GO
