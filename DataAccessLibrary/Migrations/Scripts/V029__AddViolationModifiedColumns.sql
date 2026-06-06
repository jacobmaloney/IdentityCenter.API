-- V029: Add ModifiedAt/ModifiedBy to CompliancePolicyViolations for audit tracking

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyViolations') AND name = 'ModifiedAt')
BEGIN
    ALTER TABLE [CompliancePolicyViolations] ADD [ModifiedAt] datetime2 NULL;
    PRINT 'Added ModifiedAt column to CompliancePolicyViolations';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyViolations') AND name = 'ModifiedBy')
BEGIN
    ALTER TABLE [CompliancePolicyViolations] ADD [ModifiedBy] nvarchar(256) NULL;
    PRINT 'Added ModifiedBy column to CompliancePolicyViolations';
END;
GO
