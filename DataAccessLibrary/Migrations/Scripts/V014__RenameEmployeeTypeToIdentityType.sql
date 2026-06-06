-- V014: Rename EmployeeType column to IdentityType in Identities table
-- The term "Identity Type" better describes the nature of the identity record
-- (Employee, Contractor, Vendor, Service Account, Bot, etc.) vs just employment category

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EmployeeType')
BEGIN
    EXEC sp_rename 'Identities.EmployeeType', 'IdentityType', 'COLUMN';
    PRINT 'Renamed Identities.EmployeeType to IdentityType.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'IdentityType')
BEGIN
    -- Column doesn't exist yet (fresh DB), add it directly
    ALTER TABLE Identities ADD IdentityType NVARCHAR(100) NULL;
    PRINT 'Added IdentityType column to Identities table.';
END
ELSE
BEGIN
    PRINT 'IdentityType column already exists in Identities table - no action needed.';
END
