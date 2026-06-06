-- V034: Expand Identities table with comprehensive workforce attributes and 20 custom columns
-- Adds missing employee/contractor fields and dedicated CustomAttribute1-20 columns

-- ============================================================
-- ORGANIZATIONAL & JOB ATTRIBUTES
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EmployeeType')
    ALTER TABLE [Identities] ADD [EmployeeType] NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'JobCode')
    ALTER TABLE [Identities] ADD [JobCode] NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'JobFamily')
    ALTER TABLE [Identities] ADD [JobFamily] NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PayGrade')
    ALTER TABLE [Identities] ADD [PayGrade] NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Organization')
    ALTER TABLE [Identities] ADD [Organization] NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'BusinessUnit')
    ALTER TABLE [Identities] ADD [BusinessUnit] NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'LegalEntity')
    ALTER TABLE [Identities] ADD [LegalEntity] NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Region')
    ALTER TABLE [Identities] ADD [Region] NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Site')
    ALTER TABLE [Identities] ADD [Site] NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'WorkSchedule')
    ALTER TABLE [Identities] ADD [WorkSchedule] NVARCHAR(100) NULL;

-- ============================================================
-- DATES
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'StartDate')
    ALTER TABLE [Identities] ADD [StartDate] DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EndDate')
    ALTER TABLE [Identities] ADD [EndDate] DATETIME2 NULL;

-- ============================================================
-- DESCRIPTION EXPANSION & NOTES
-- ============================================================

-- Expand Description from 1000 to 2000
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Description')
    ALTER TABLE [Identities] ALTER COLUMN [Description] NVARCHAR(2000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Notes')
    ALTER TABLE [Identities] ADD [Notes] NVARCHAR(4000) NULL;

-- ============================================================
-- MANAGER & SPONSOR
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'ManagerDisplayName')
    ALTER TABLE [Identities] ADD [ManagerDisplayName] NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Sponsor')
    ALTER TABLE [Identities] ADD [Sponsor] NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'SponsorEmail')
    ALTER TABLE [Identities] ADD [SponsorEmail] NVARCHAR(500) NULL;

-- ============================================================
-- CONTRACTOR / VENDOR
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'VendorName')
    ALTER TABLE [Identities] ADD [VendorName] NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PONumber')
    ALTER TABLE [Identities] ADD [PONumber] NVARCHAR(100) NULL;

-- ============================================================
-- PHYSICAL ACCESS & BADGE
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'BadgeNumber')
    ALTER TABLE [Identities] ADD [BadgeNumber] NVARCHAR(100) NULL;

-- ============================================================
-- CUSTOM ATTRIBUTE COLUMNS (1-20)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute1')
    ALTER TABLE [Identities] ADD [CustomAttribute1] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute2')
    ALTER TABLE [Identities] ADD [CustomAttribute2] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute3')
    ALTER TABLE [Identities] ADD [CustomAttribute3] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute4')
    ALTER TABLE [Identities] ADD [CustomAttribute4] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute5')
    ALTER TABLE [Identities] ADD [CustomAttribute5] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute6')
    ALTER TABLE [Identities] ADD [CustomAttribute6] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute7')
    ALTER TABLE [Identities] ADD [CustomAttribute7] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute8')
    ALTER TABLE [Identities] ADD [CustomAttribute8] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute9')
    ALTER TABLE [Identities] ADD [CustomAttribute9] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute10')
    ALTER TABLE [Identities] ADD [CustomAttribute10] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute11')
    ALTER TABLE [Identities] ADD [CustomAttribute11] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute12')
    ALTER TABLE [Identities] ADD [CustomAttribute12] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute13')
    ALTER TABLE [Identities] ADD [CustomAttribute13] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute14')
    ALTER TABLE [Identities] ADD [CustomAttribute14] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute15')
    ALTER TABLE [Identities] ADD [CustomAttribute15] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute16')
    ALTER TABLE [Identities] ADD [CustomAttribute16] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute17')
    ALTER TABLE [Identities] ADD [CustomAttribute17] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute18')
    ALTER TABLE [Identities] ADD [CustomAttribute18] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute19')
    ALTER TABLE [Identities] ADD [CustomAttribute19] NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttribute20')
    ALTER TABLE [Identities] ADD [CustomAttribute20] NVARCHAR(1000) NULL;
