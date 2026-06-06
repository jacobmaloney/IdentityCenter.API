-- Migration: Add PolicyType column to CompliancePolicies table
-- Date: 2026-01-17
-- Description: Adds PolicyType column for categorizing policy behavior (Detection, Enforcement, etc.)

-- Add PolicyType column if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'PolicyType')
BEGIN
    ALTER TABLE CompliancePolicies ADD PolicyType NVARCHAR(50) NOT NULL DEFAULT 'Detection';
    PRINT 'Added PolicyType column to CompliancePolicies table';
END
ELSE
BEGIN
    PRINT 'PolicyType column already exists in CompliancePolicies table';
END
GO
