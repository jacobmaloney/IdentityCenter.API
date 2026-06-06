-- =====================================================
-- Add Group Membership Sync Workflow Template
-- =====================================================
-- Makes it easy to add "Sync Group Memberships" step in wizard
-- =====================================================

USE [IdentityCenter]
GO

-- Insert workflow template for Group Membership Sync
-- Check if it already exists first
IF NOT EXISTS (SELECT 1 FROM SyncWorkflowTemplates WHERE Name = 'Sync Group Memberships')
BEGIN
    INSERT INTO SyncWorkflowTemplates (
        Id,
        Name,
        Description,
        ObjectClass,
        IsEnabled,
        ExecutionOrder,
        CreatedAt
    )
    VALUES (
        NEWID(),
        'Sync Group Memberships',
        'Syncs group memberships from AD member attribute and primary groups (Domain Users). Fast, clean, reliable - adds missing memberships and removes ones that disappeared from AD.',
        'group',
        1,
        400, -- Execute after group objects are synced
        GETUTCDATE()
    );

    PRINT '✅ Added "Sync Group Memberships" workflow template';
END
ELSE
BEGIN
    PRINT '⚠️ "Sync Group Memberships" workflow template already exists';
END
GO

-- Show all workflow templates
SELECT
    Name,
    Description,
    ObjectClass,
    ExecutionOrder,
    IsEnabled
FROM SyncWorkflowTemplates
ORDER BY ObjectClass, ExecutionOrder;
GO

PRINT '';
PRINT '📝 Usage: In the Sync Project Wizard, select "Sync Group Memberships" step when configuring group sync.';
PRINT 'This will automatically sync all group members and primary group memberships (Domain Users, etc.)';
GO
