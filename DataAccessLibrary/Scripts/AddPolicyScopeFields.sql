-- Add scope fields to CompliancePolicies table
-- Run this script manually to add the new scope fields

USE IdentityCenter;
GO

-- Add ScopeConnectionIds column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CompliancePolicies]') AND name = 'ScopeConnectionIds')
BEGIN
    ALTER TABLE [dbo].[CompliancePolicies]
    ADD [ScopeConnectionIds] NVARCHAR(2000) NULL;
    PRINT 'Added ScopeConnectionIds column';
END
ELSE
BEGIN
    PRINT 'ScopeConnectionIds column already exists';
END
GO

-- Add ScopeTags column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CompliancePolicies]') AND name = 'ScopeTags')
BEGIN
    ALTER TABLE [dbo].[CompliancePolicies]
    ADD [ScopeTags] NVARCHAR(2000) NULL;
    PRINT 'Added ScopeTags column';
END
ELSE
BEGIN
    PRINT 'ScopeTags column already exists';
END
GO

-- Add ScopeAttributeQuery column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CompliancePolicies]') AND name = 'ScopeAttributeQuery')
BEGIN
    ALTER TABLE [dbo].[CompliancePolicies]
    ADD [ScopeAttributeQuery] NVARCHAR(4000) NULL;
    PRINT 'Added ScopeAttributeQuery column';
END
ELSE
BEGIN
    PRINT 'ScopeAttributeQuery column already exists';
END
GO

PRINT 'Policy scope fields migration complete!';
GO
