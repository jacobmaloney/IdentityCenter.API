-- =============================================
-- Workflow Integration - Campaign Schema Updates
-- =============================================
-- Purpose: Add workflow support to Campaigns and Assignments
-- Created: 2025-11-19
-- =============================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT '========================================='
PRINT 'Campaign Workflow Integration - Schema Update'
PRINT '========================================='
PRINT ''

-- =============================================
-- Step 1: Add WorkflowId to Campaigns Table
-- =============================================
PRINT 'Step 1: Adding WorkflowId to Campaigns table...'

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'WorkflowId')
BEGIN
    ALTER TABLE Campaigns ADD WorkflowId UNIQUEIDENTIFIER NULL
    PRINT '  ✓ Added WorkflowId column to Campaigns'
END
ELSE
BEGIN
    PRINT '  ⚠ WorkflowId column already exists in Campaigns'
END
GO

-- =============================================
-- Step 2: Add Foreign Key Constraint (Campaigns -> ApprovalWorkflows)
-- =============================================
PRINT 'Step 2: Adding foreign key constraint to Campaigns...'

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Campaigns_ApprovalWorkflows')
BEGIN
    ALTER TABLE Campaigns ADD CONSTRAINT FK_Campaigns_ApprovalWorkflows
        FOREIGN KEY (WorkflowId) REFERENCES ApprovalWorkflows(Id)
    PRINT '  ✓ Added FK_Campaigns_ApprovalWorkflows constraint'
END
ELSE
BEGIN
    PRINT '  ⚠ FK_Campaigns_ApprovalWorkflows constraint already exists'
END
GO

-- =============================================
-- Step 3: Add WorkflowInstanceId to Assignments Table
-- =============================================
PRINT 'Step 3: Adding WorkflowInstanceId to Assignments table...'

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Assignments') AND name = 'WorkflowInstanceId')
BEGIN
    ALTER TABLE Assignments ADD WorkflowInstanceId UNIQUEIDENTIFIER NULL
    PRINT '  ✓ Added WorkflowInstanceId column to Assignments'
END
ELSE
BEGIN
    PRINT '  ⚠ WorkflowInstanceId column already exists in Assignments'
END
GO

-- =============================================
-- Step 4: Add Foreign Key Constraint (Assignments -> ApprovalWorkflowInstances)
-- =============================================
PRINT 'Step 4: Adding foreign key constraint to Assignments...'

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Assignments_WorkflowInstances')
BEGIN
    ALTER TABLE Assignments ADD CONSTRAINT FK_Assignments_WorkflowInstances
        FOREIGN KEY (WorkflowInstanceId) REFERENCES ApprovalWorkflowInstances(Id)
    PRINT '  ✓ Added FK_Assignments_WorkflowInstances constraint'
END
ELSE
BEGIN
    PRINT '  ⚠ FK_Assignments_WorkflowInstances constraint already exists'
END
GO

-- =============================================
-- Step 5: Add Indexes for Performance
-- =============================================
PRINT 'Step 5: Creating indexes for performance...'

-- Index on Campaigns.WorkflowId for faster joins
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Campaigns_WorkflowId' AND object_id = OBJECT_ID('Campaigns'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Campaigns_WorkflowId
    ON Campaigns(WorkflowId)
    WHERE WorkflowId IS NOT NULL
    PRINT '  ✓ Created IX_Campaigns_WorkflowId index'
END
ELSE
BEGIN
    PRINT '  ⚠ IX_Campaigns_WorkflowId index already exists'
END
GO

-- Index on Assignments.WorkflowInstanceId for faster workflow queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Assignments_WorkflowInstanceId' AND object_id = OBJECT_ID('Assignments'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Assignments_WorkflowInstanceId
    ON Assignments(WorkflowInstanceId)
    WHERE WorkflowInstanceId IS NOT NULL
    PRINT '  ✓ Created IX_Assignments_WorkflowInstanceId index'
END
ELSE
BEGIN
    PRINT '  ⚠ IX_Assignments_WorkflowInstanceId index already exists'
END
GO

-- =============================================
-- Verification
-- =============================================
PRINT ''
PRINT '========================================='
PRINT 'Verification'
PRINT '========================================='
PRINT ''

-- Check Campaigns columns
SELECT
    'Campaigns' AS TableName,
    name AS ColumnName,
    TYPE_NAME(system_type_id) AS DataType,
    is_nullable AS IsNullable
FROM sys.columns
WHERE object_id = OBJECT_ID('Campaigns')
  AND name IN ('WorkflowId')

-- Check Assignments columns
SELECT
    'Assignments' AS TableName,
    name AS ColumnName,
    TYPE_NAME(system_type_id) AS DataType,
    is_nullable AS IsNullable
FROM sys.columns
WHERE object_id = OBJECT_ID('Assignments')
  AND name IN ('WorkflowInstanceId')

-- Check foreign keys
SELECT
    OBJECT_NAME(parent_object_id) AS TableName,
    name AS ConstraintName,
    OBJECT_NAME(referenced_object_id) AS ReferencedTable
FROM sys.foreign_keys
WHERE name IN ('FK_Campaigns_ApprovalWorkflows', 'FK_Assignments_WorkflowInstances')

PRINT ''
PRINT '========================================='
PRINT 'Schema Update Complete!'
PRINT '========================================='
PRINT ''
PRINT 'Summary:'
PRINT '  ✓ Campaigns.WorkflowId column added'
PRINT '  ✓ FK_Campaigns_ApprovalWorkflows constraint added'
PRINT '  ✓ Assignments.WorkflowInstanceId column added'
PRINT '  ✓ FK_Assignments_WorkflowInstances constraint added'
PRINT '  ✓ Performance indexes created'
PRINT ''
PRINT 'Next Steps:'
PRINT '  1. Update Campaign model with WorkflowId property'
PRINT '  2. Update Assignment model with WorkflowInstanceId property'
PRINT '  3. Update campaign creation UI to include workflow selector'
PRINT '  4. Update CampaignService to start workflows'
PRINT ''
PRINT '========================================='
