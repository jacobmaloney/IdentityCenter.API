-- V012: Expand Identities table to include all 63 columns for comprehensive identity data

-- Add missing biographic columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Suffix')
    ALTER TABLE Identities ADD Suffix NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Salutation')
    ALTER TABLE Identities ADD Salutation NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PreferredName')
    ALTER TABLE Identities ADD PreferredName NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'DateOfBirth')
    ALTER TABLE Identities ADD DateOfBirth DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Gender')
    ALTER TABLE Identities ADD Gender NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'NationalId')
    ALTER TABLE Identities ADD NationalId NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PhotoUrl')
    ALTER TABLE Identities ADD PhotoUrl NVARCHAR(2000) NULL;

-- Add missing contact columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'SecondaryEmail')
    ALTER TABLE Identities ADD SecondaryEmail NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'MobilePhone')
    ALTER TABLE Identities ADD MobilePhone NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'HomePhone')
    ALTER TABLE Identities ADD HomePhone NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Fax')
    ALTER TABLE Identities ADD Fax NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'StreetAddress')
    ALTER TABLE Identities ADD StreetAddress NVARCHAR(1000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'City')
    ALTER TABLE Identities ADD City NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'State')
    ALTER TABLE Identities ADD State NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PostalCode')
    ALTER TABLE Identities ADD PostalCode NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Country')
    ALTER TABLE Identities ADD Country NVARCHAR(200) NULL;

-- Add missing organizational columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EmployeeId')
    ALTER TABLE Identities ADD EmployeeId NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Division')
    ALTER TABLE Identities ADD Division NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Company')
    ALTER TABLE Identities ADD Company NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Office')
    ALTER TABLE Identities ADD Office NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Building')
    ALTER TABLE Identities ADD Building NVARCHAR(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Floor')
    ALTER TABLE Identities ADD Floor NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Room')
    ALTER TABLE Identities ADD Room NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CostCenter')
    ALTER TABLE Identities ADD CostCenter NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'ProfitCenter')
    ALTER TABLE Identities ADD ProfitCenter NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EmployeeType')
    ALTER TABLE Identities ADD EmployeeType NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'ContractType')
    ALTER TABLE Identities ADD ContractType NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'HireDate')
    ALTER TABLE Identities ADD HireDate DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'TerminationDate')
    ALTER TABLE Identities ADD TerminationDate DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'LastWorkDay')
    ALTER TABLE Identities ADD LastWorkDay DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Description')
    ALTER TABLE Identities ADD Description NVARCHAR(2000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'ManagerEmployeeId')
    ALTER TABLE Identities ADD ManagerEmployeeId NVARCHAR(100) NULL;

-- Add missing technical columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Username')
    ALTER TABLE Identities ADD Username NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'UserPrincipalName')
    ALTER TABLE Identities ADD UserPrincipalName NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Status')
    ALTER TABLE Identities ADD Status NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'SecurityClearance')
    ALTER TABLE Identities ADD SecurityClearance NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'RiskScore')
    ALTER TABLE Identities ADD RiskScore DECIMAL(5,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'RiskLevel')
    ALTER TABLE Identities ADD RiskLevel NVARCHAR(50) NULL;

-- Add missing localization columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PreferredLanguage')
    ALTER TABLE Identities ADD PreferredLanguage NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'TimeZone')
    ALTER TABLE Identities ADD TimeZone NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Locale')
    ALTER TABLE Identities ADD Locale NVARCHAR(50) NULL;

-- Add missing timestamp columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'LastLoginAt')
    ALTER TABLE Identities ADD LastLoginAt DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PasswordLastChangedAt')
    ALTER TABLE Identities ADD PasswordLastChangedAt DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'LastAccessReviewAt')
    ALTER TABLE Identities ADD LastAccessReviewAt DATETIME2 NULL;

-- Add missing audit columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CreatedBy')
    ALTER TABLE Identities ADD CreatedBy NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'ModifiedBy')
    ALTER TABLE Identities ADD ModifiedBy NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttributes')
    ALTER TABLE Identities ADD CustomAttributes NVARCHAR(MAX) NULL;

PRINT 'Expanded Identities table schema to include all 63 columns.';
