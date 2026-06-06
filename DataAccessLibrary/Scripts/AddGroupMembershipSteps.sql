-- ============================================================================
-- Add Group Membership Sync Steps to Existing Sync Projects
-- Injects GroupMembership steps into user, computer, and contact workflows
-- Date: 2025-12-05
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

PRINT 'Adding Group Membership Sync steps to existing sync projects...';
PRINT '';

-- ============================================================================
-- 1. ADD GROUP MEMBERSHIP STEP TO USER WORKFLOWS
-- ============================================================================
PRINT '1. Adding to User workflows...';

-- Find User workflows that don't already have a GroupMembership step
INSERT INTO SyncSteps (
    Id, SyncWorkflowId, Name, ObjectClass, ExecutionOrder,
    LdapFilter, SearchBase, SearchScope, StepType,
    IsEnabled, BatchSize, LdapPageSize,
    ContinueOnError, MaxExecutionTimeMinutes, ProcessDeletions, UpdateExisting,
    EnableIdentityMatching, InheritWorkflowTags, SkipPersonMatching,
    CreatedAt
)
SELECT
    NEWID() as Id,
    w.Id as SyncWorkflowId,
    'Sync Group Memberships' as Name,
    'GroupMembership' as ObjectClass,
    (SELECT ISNULL(MAX(s.ExecutionOrder), 0) + 1 FROM SyncSteps s WHERE s.SyncWorkflowId = w.Id) as ExecutionOrder,
    '' as LdapFilter,
    '' as SearchBase,
    'Subtree' as SearchScope,
    'GroupMembership' as StepType,
    1 as IsEnabled,
    500 as BatchSize,
    0 as LdapPageSize,
    0 as ContinueOnError,
    60 as MaxExecutionTimeMinutes,
    0 as ProcessDeletions,
    1 as UpdateExisting,
    0 as EnableIdentityMatching,
    0 as InheritWorkflowTags,
    0 as SkipPersonMatching,
    GETUTCDATE() as CreatedAt
FROM SyncWorkflows w
WHERE w.ObjectClass = 'user'
  AND NOT EXISTS (
      SELECT 1 FROM SyncSteps s
      WHERE s.SyncWorkflowId = w.Id
        AND s.ObjectClass = 'GroupMembership'
  );

PRINT '   Added to ' + CAST(@@ROWCOUNT AS VARCHAR) + ' User workflows';

-- ============================================================================
-- 2. ADD GROUP MEMBERSHIP STEP TO COMPUTER WORKFLOWS
-- ============================================================================
PRINT '2. Adding to Computer workflows...';

INSERT INTO SyncSteps (
    Id, SyncWorkflowId, Name, ObjectClass, ExecutionOrder,
    LdapFilter, SearchBase, SearchScope, StepType,
    IsEnabled, BatchSize, LdapPageSize,
    ContinueOnError, MaxExecutionTimeMinutes, ProcessDeletions, UpdateExisting,
    EnableIdentityMatching, InheritWorkflowTags, SkipPersonMatching,
    CreatedAt
)
SELECT
    NEWID() as Id,
    w.Id as SyncWorkflowId,
    'Sync Group Memberships' as Name,
    'GroupMembership' as ObjectClass,
    (SELECT ISNULL(MAX(s.ExecutionOrder), 0) + 1 FROM SyncSteps s WHERE s.SyncWorkflowId = w.Id) as ExecutionOrder,
    '' as LdapFilter,
    '' as SearchBase,
    'Subtree' as SearchScope,
    'GroupMembership' as StepType,
    1 as IsEnabled,
    500 as BatchSize,
    0 as LdapPageSize,
    0 as ContinueOnError,
    60 as MaxExecutionTimeMinutes,
    0 as ProcessDeletions,
    1 as UpdateExisting,
    0 as EnableIdentityMatching,
    0 as InheritWorkflowTags,
    0 as SkipPersonMatching,
    GETUTCDATE() as CreatedAt
FROM SyncWorkflows w
WHERE w.ObjectClass = 'computer'
  AND NOT EXISTS (
      SELECT 1 FROM SyncSteps s
      WHERE s.SyncWorkflowId = w.Id
        AND s.ObjectClass = 'GroupMembership'
  );

PRINT '   Added to ' + CAST(@@ROWCOUNT AS VARCHAR) + ' Computer workflows';

-- ============================================================================
-- 3. ADD GROUP MEMBERSHIP STEP TO CONTACT WORKFLOWS
-- ============================================================================
PRINT '3. Adding to Contact workflows...';

INSERT INTO SyncSteps (
    Id, SyncWorkflowId, Name, ObjectClass, ExecutionOrder,
    LdapFilter, SearchBase, SearchScope, StepType,
    IsEnabled, BatchSize, LdapPageSize,
    ContinueOnError, MaxExecutionTimeMinutes, ProcessDeletions, UpdateExisting,
    EnableIdentityMatching, InheritWorkflowTags, SkipPersonMatching,
    CreatedAt
)
SELECT
    NEWID() as Id,
    w.Id as SyncWorkflowId,
    'Sync Group Memberships' as Name,
    'GroupMembership' as ObjectClass,
    (SELECT ISNULL(MAX(s.ExecutionOrder), 0) + 1 FROM SyncSteps s WHERE s.SyncWorkflowId = w.Id) as ExecutionOrder,
    '' as LdapFilter,
    '' as SearchBase,
    'Subtree' as SearchScope,
    'GroupMembership' as StepType,
    1 as IsEnabled,
    500 as BatchSize,
    0 as LdapPageSize,
    0 as ContinueOnError,
    60 as MaxExecutionTimeMinutes,
    0 as ProcessDeletions,
    1 as UpdateExisting,
    0 as EnableIdentityMatching,
    0 as InheritWorkflowTags,
    0 as SkipPersonMatching,
    GETUTCDATE() as CreatedAt
FROM SyncWorkflows w
WHERE w.ObjectClass = 'contact'
  AND NOT EXISTS (
      SELECT 1 FROM SyncSteps s
      WHERE s.SyncWorkflowId = w.Id
        AND s.ObjectClass = 'GroupMembership'
  );

PRINT '   Added to ' + CAST(@@ROWCOUNT AS VARCHAR) + ' Contact workflows';

-- ============================================================================
-- 4. VERIFY RESULTS
-- ============================================================================
PRINT '';
PRINT '============================================';
PRINT 'Verification - GroupMembership steps added:';
PRINT '============================================';

SELECT
    p.Name as ProjectName,
    w.Name as WorkflowName,
    w.ObjectClass as WorkflowObjectClass,
    s.Name as StepName,
    s.ExecutionOrder as StepOrder
FROM SyncSteps s
JOIN SyncWorkflows w ON s.SyncWorkflowId = w.Id
JOIN SyncProjects p ON w.SyncProjectId = p.Id
WHERE s.ObjectClass = 'GroupMembership'
ORDER BY p.Name, w.ExecutionOrder, s.ExecutionOrder;

PRINT '';
PRINT 'Done - Group Membership steps injection complete!';
