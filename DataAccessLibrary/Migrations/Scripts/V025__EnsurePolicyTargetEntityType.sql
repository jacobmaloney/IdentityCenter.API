-- V025: Ensure TargetEntityType exists on CompliancePolicies
-- Re-applies V024 if it was recorded but the column wasn't actually created

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'TargetEntityType'
)
BEGIN
    ALTER TABLE CompliancePolicies
    ADD TargetEntityType NVARCHAR(50) NOT NULL DEFAULT 'Object';

    PRINT 'Added TargetEntityType column to CompliancePolicies table (V025 recovery)';
END
ELSE
BEGIN
    PRINT 'TargetEntityType column already exists - no action needed';
END
