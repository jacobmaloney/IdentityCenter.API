-- Migration: Assign CreateOrUpdateIdentity script to existing user/contact sync steps
-- Date: 2025-12-25
-- Description: Auto-assigns the person matching script to steps that have EnableIdentityMatching=true
--              This enables script-based person matching instead of inline PersonMatchingService calls

PRINT 'Starting migration: Assign person matching script to existing steps...'

-- Constants
DECLARE @CreateOrUpdateIdentityScriptId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @PostProcessingPhase NVARCHAR(50) = 'PostProcessing';
DECLARE @ExecutionOrder INT = 1;  -- First script to run

-- First, verify the script exists
IF NOT EXISTS (SELECT 1 FROM SyncProcessingScripts WHERE Id = @CreateOrUpdateIdentityScriptId)
BEGIN
    PRINT 'WARNING: CreateOrUpdateIdentity script not found. Run the DevCenterScriptsSeedService first.';
    PRINT 'Script ID expected: ' + CAST(@CreateOrUpdateIdentityScriptId AS NVARCHAR(50));
    RETURN;
END

PRINT 'Found CreateOrUpdateIdentity script. Proceeding with assignment...'

-- Count steps that need the script assigned
DECLARE @StepsToUpdate INT;
SELECT @StepsToUpdate = COUNT(*)
FROM SyncSteps s
WHERE s.EnableIdentityMatching = 1
    AND s.ObjectClass IN ('user', 'contact')
    AND NOT EXISTS (
        SELECT 1 FROM SyncStepScripts sss
        WHERE sss.SyncStepId = s.Id
        AND sss.ScriptId = @CreateOrUpdateIdentityScriptId
    );

PRINT 'Found ' + CAST(@StepsToUpdate AS NVARCHAR(10)) + ' steps that need script assignment.';

-- Assign script to all user/contact steps with EnableIdentityMatching=true
INSERT INTO SyncStepScripts (Id, SyncStepId, ScriptId, ExecutionPhase, ExecutionOrder, IsEnabled)
SELECT
    NEWID(),
    s.Id,
    @CreateOrUpdateIdentityScriptId,
    @PostProcessingPhase,
    @ExecutionOrder,
    1  -- IsEnabled = true
FROM SyncSteps s
WHERE s.EnableIdentityMatching = 1
    AND s.ObjectClass IN ('user', 'contact')
    AND NOT EXISTS (
        SELECT 1 FROM SyncStepScripts sss
        WHERE sss.SyncStepId = s.Id
        AND sss.ScriptId = @CreateOrUpdateIdentityScriptId
    );

DECLARE @RowsAffected INT = @@ROWCOUNT;
PRINT 'Assigned script to ' + CAST(@RowsAffected AS NVARCHAR(10)) + ' steps.';

-- Verify assignment
SELECT
    p.Name AS ProjectName,
    w.Name AS WorkflowName,
    s.Name AS StepName,
    s.ObjectClass,
    s.EnableIdentityMatching,
    CASE WHEN sss.Id IS NOT NULL THEN 'Yes' ELSE 'No' END AS ScriptAssigned
FROM SyncSteps s
INNER JOIN SyncWorkflows w ON s.SyncWorkflowId = w.Id
INNER JOIN SyncProjects p ON w.SyncProjectId = p.Id
LEFT JOIN SyncStepScripts sss ON s.Id = sss.SyncStepId AND sss.ScriptId = @CreateOrUpdateIdentityScriptId
WHERE s.ObjectClass IN ('user', 'contact')
ORDER BY p.Name, w.Name, s.StepOrder;

PRINT 'Migration complete: Person matching script assignment finished.';
GO
