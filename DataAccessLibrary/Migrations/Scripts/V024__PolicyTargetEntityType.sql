-- V024: Add TargetEntityType to CompliancePolicies
-- Determines whether the policy evaluates Objects (AD accounts) or Identities (people)
-- Default 'Object' preserves backward compatibility with all existing policies

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'TargetEntityType'
)
BEGIN
    ALTER TABLE CompliancePolicies
    ADD TargetEntityType NVARCHAR(50) NOT NULL DEFAULT 'Object';

    PRINT 'Added TargetEntityType column to CompliancePolicies table';
END
ELSE
BEGIN
    PRINT 'TargetEntityType column already exists on CompliancePolicies table - skipping';
END
