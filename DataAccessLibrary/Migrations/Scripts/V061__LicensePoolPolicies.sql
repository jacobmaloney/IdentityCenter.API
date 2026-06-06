-- V061: Add policy thresholds, friendly name, and notes to LicensePools
-- ─────────────────────────────────────────────────────────────────────────────

-- Policy thresholds
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'MinBufferPercent')
    ALTER TABLE LicensePools ADD MinBufferPercent INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'MaxUtilizationPercent')
    ALTER TABLE LicensePools ADD MaxUtilizationPercent INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'AlertThreshold')
    ALTER TABLE LicensePools ADD AlertThreshold NVARCHAR(20) NULL;
GO

-- Friendly display name (human-readable, e.g. "Microsoft 365 E5" instead of "ENTERPRISEPREMIUM")
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'FriendlyName')
    ALTER TABLE LicensePools ADD FriendlyName NVARCHAR(500) NULL;
GO

-- Notes/description field for admin annotations
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'Notes')
    ALTER TABLE LicensePools ADD Notes NVARCHAR(MAX) NULL;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- Seed friendly names for common Microsoft SKUs
-- ─────────────────────────────────────────────────────────────────────────────

UPDATE LicensePools SET FriendlyName = CASE SkuPartNumber
    WHEN 'ENTERPRISEPREMIUM' THEN 'Microsoft 365 E5'
    WHEN 'ENTERPRISEPACK' THEN 'Microsoft 365 E3'
    WHEN 'SPE_E5' THEN 'Microsoft 365 E5'
    WHEN 'SPE_E3' THEN 'Microsoft 365 E3'
    WHEN 'FLOW_FREE' THEN 'Power Automate Free'
    WHEN 'POWER_BI_PRO' THEN 'Power BI Pro'
    WHEN 'POWER_BI_STANDARD' THEN 'Power BI Free'
    WHEN 'POWERAPPS_VIRAL' THEN 'Power Apps Free'
    WHEN 'TEAMS_EXPLORATORY' THEN 'Teams Exploratory'
    WHEN 'STREAM' THEN 'Microsoft Stream'
    WHEN 'PROJECTPREMIUM' THEN 'Project Plan 5'
    WHEN 'PROJECTPROFESSIONAL' THEN 'Project Plan 3'
    WHEN 'VISIOONLINE_PLAN1' THEN 'Visio Plan 1'
    WHEN 'VISIOCLIENT' THEN 'Visio Plan 2'
    WHEN 'RIGHTSMANAGEMENT_ADHOC' THEN 'Rights Management Adhoc'
    WHEN 'EMS_E5' THEN 'Enterprise Mobility + Security E5'
    WHEN 'EMSPREMIUM' THEN 'Enterprise Mobility + Security E5'
    WHEN 'EMS' THEN 'Enterprise Mobility + Security E3'
    WHEN 'AAD_PREMIUM' THEN 'Entra ID P1'
    WHEN 'AAD_PREMIUM_P2' THEN 'Entra ID P2'
    WHEN 'INTUNE_A' THEN 'Microsoft Intune Plan 1'
    WHEN 'ATP_ENTERPRISE' THEN 'Microsoft Defender for Office 365 P1'
    WHEN 'THREAT_INTELLIGENCE' THEN 'Microsoft Defender for Office 365 P2'
    WHEN 'WIN_DEF_ATP' THEN 'Microsoft Defender for Endpoint P2'
    WHEN 'IDENTITY_THREAT_PROTECTION' THEN 'Microsoft Defender for Identity'
    WHEN 'EXCHANGESTANDARD' THEN 'Exchange Online Plan 1'
    WHEN 'EXCHANGEENTERPRISE' THEN 'Exchange Online Plan 2'
    WHEN 'EXCHANGEDESKLESS' THEN 'Exchange Online Kiosk'
    WHEN 'SHAREPOINTSTANDARD' THEN 'SharePoint Online Plan 1'
    WHEN 'SHAREPOINTENTERPRISE' THEN 'SharePoint Online Plan 2'
    WHEN 'O365_BUSINESS_ESSENTIALS' THEN 'Microsoft 365 Business Basic'
    WHEN 'O365_BUSINESS_PREMIUM' THEN 'Microsoft 365 Business Standard'
    WHEN 'SMB_BUSINESS' THEN 'Microsoft 365 Business Basic'
    WHEN 'SMB_BUSINESS_PREMIUM' THEN 'Microsoft 365 Business Standard'
    WHEN 'MCOSTANDARD' THEN 'Skype for Business Online Plan 2'
    WHEN 'MCOPSTN1' THEN 'Domestic Calling Plan'
    WHEN 'MCOPSTN2' THEN 'International Calling Plan'
    WHEN 'PHONESYSTEM_VIRTUALUSER' THEN 'Phone System Virtual User'
    WHEN 'MCOMEETADV' THEN 'Audio Conferencing'
    WHEN 'WINDOWS_STORE' THEN 'Windows Store for Business'
    WHEN 'WIN10_PRO_ENT_SUB' THEN 'Windows 10/11 Enterprise E3'
    WHEN 'WIN10_VDA_E5' THEN 'Windows 10/11 Enterprise E5'
    WHEN 'DEVELOPERPACK_E5' THEN 'Microsoft 365 E5 Developer'
    WHEN 'DYN365_ENTERPRISE_PLAN1' THEN 'Dynamics 365 Plan'
    WHEN 'DYN365_ENTERPRISE_SALES' THEN 'Dynamics 365 Sales Enterprise'
    WHEN 'DYN365_ENTERPRISE_TEAM_MEMBERS' THEN 'Dynamics 365 Team Members'
    WHEN 'PBI_PREMIUM_EM1_ADDON' THEN 'Power BI Premium EM1'
    ELSE NULL
END
WHERE FriendlyName IS NULL;
GO

PRINT 'V061: License pool policies and friendly names complete';
GO
