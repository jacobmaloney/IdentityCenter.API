-- Migration: Add Rule Logic Fields to CompliancePolicyRules
-- This enables AND/OR logic between rules and rule grouping

-- Add LogicalOperator column (AND/OR between consecutive rules)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyRules') AND name = 'LogicalOperator')
BEGIN
    ALTER TABLE CompliancePolicyRules ADD LogicalOperator NVARCHAR(10) NOT NULL DEFAULT 'AND';
    PRINT 'Added LogicalOperator column to CompliancePolicyRules';
END
GO

-- Add RuleGroupId column (group rules together for evaluation)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyRules') AND name = 'RuleGroupId')
BEGIN
    ALTER TABLE CompliancePolicyRules ADD RuleGroupId INT NULL;
    PRINT 'Added RuleGroupId column to CompliancePolicyRules';
END
GO

-- Add RuleGroupName column (display name for rule groups)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyRules') AND name = 'RuleGroupName')
BEGIN
    ALTER TABLE CompliancePolicyRules ADD RuleGroupName NVARCHAR(100) NULL;
    PRINT 'Added RuleGroupName column to CompliancePolicyRules';
END
GO

-- Add GroupOperator column (AND/OR between groups)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyRules') AND name = 'GroupOperator')
BEGIN
    ALTER TABLE CompliancePolicyRules ADD GroupOperator NVARCHAR(10) NOT NULL DEFAULT 'AND';
    PRINT 'Added GroupOperator column to CompliancePolicyRules';
END
GO

-- Record migration
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20260103100000_AddRuleLogicFields')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260103100000_AddRuleLogicFields', '8.0.0');
    PRINT 'Migration recorded in history';
END
GO
