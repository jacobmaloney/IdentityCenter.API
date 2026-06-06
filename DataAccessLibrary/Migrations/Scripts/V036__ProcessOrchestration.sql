-- ============================================================================
-- V036: Process Orchestration Engine
-- Adds process execution infrastructure to the existing workflow system.
-- All changes are additive - no existing data modified.
-- ============================================================================

-- ============================================================================
-- 1. Add ProcessType discriminator to ApprovalWorkflows
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ApprovalWorkflows') AND name = 'ProcessType')
BEGIN
    ALTER TABLE ApprovalWorkflows ADD ProcessType NVARCHAR(50) NOT NULL CONSTRAINT DF_ApprovalWorkflows_ProcessType DEFAULT 'ApprovalWorkflow';
    PRINT 'Added ProcessType column to ApprovalWorkflows';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ApprovalWorkflows') AND name = 'TriggerEventTypes')
BEGIN
    ALTER TABLE ApprovalWorkflows ADD TriggerEventTypes NVARCHAR(MAX) NULL;
    PRINT 'Added TriggerEventTypes column to ApprovalWorkflows';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ApprovalWorkflows') AND name = 'TargetEntityType')
BEGIN
    ALTER TABLE ApprovalWorkflows ADD TargetEntityType NVARCHAR(100) NULL;
    PRINT 'Added TargetEntityType column to ApprovalWorkflows';
END

-- ============================================================================
-- 2. ProcessInstances table (runtime tracking for process execution)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ProcessInstances')
BEGIN
    CREATE TABLE ProcessInstances (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        WorkflowId UNIQUEIDENTIFIER NOT NULL REFERENCES ApprovalWorkflows(Id),
        CurrentNodeId UNIQUEIDENTIFIER NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Running',
        TargetEntityType NVARCHAR(100) NULL,
        TargetEntityId UNIQUEIDENTIFIER NULL,
        TargetEntityName NVARCHAR(500) NULL,
        ContextData NVARCHAR(MAX) NULL,
        TriggeredBy NVARCHAR(256) NULL,
        TriggerEventId UNIQUEIDENTIFIER NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt DATETIME2 NULL,
        ResumeAt DATETIME2 NULL,
        WaitCondition NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'Created ProcessInstances table';

    -- Active instances (Running, Waiting*) - most common query
    CREATE NONCLUSTERED INDEX IX_ProcessInstances_Status
        ON ProcessInstances (Status)
        WHERE Status IN ('Running', 'WaitingForApproval', 'WaitingForDuration', 'WaitingForCondition');

    -- Lookup by workflow
    CREATE NONCLUSTERED INDEX IX_ProcessInstances_WorkflowId
        ON ProcessInstances (WorkflowId);

    -- Lookup by target entity
    CREATE NONCLUSTERED INDEX IX_ProcessInstances_TargetEntityId
        ON ProcessInstances (TargetEntityId)
        WHERE TargetEntityId IS NOT NULL;

    -- Timer resume (ProcessResumeJob queries this)
    CREATE NONCLUSTERED INDEX IX_ProcessInstances_ResumeAt
        ON ProcessInstances (ResumeAt)
        WHERE ResumeAt IS NOT NULL AND Status = 'WaitingForDuration';

    PRINT 'Created ProcessInstances indexes';
END

-- ============================================================================
-- 3. ProcessStepLogs table (per-step audit trail)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ProcessStepLogs')
BEGIN
    CREATE TABLE ProcessStepLogs (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        ProcessInstanceId UNIQUEIDENTIFIER NOT NULL REFERENCES ProcessInstances(Id) ON DELETE CASCADE,
        NodeId UNIQUEIDENTIFIER NOT NULL,
        NodeType NVARCHAR(50) NOT NULL,
        NodeName NVARCHAR(200) NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Running',
        InputData NVARCHAR(MAX) NULL,
        OutputData NVARCHAR(MAX) NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt DATETIME2 NULL,
        DurationMs BIGINT NULL,
        ApprovedBy NVARCHAR(256) NULL,
        ApprovalComments NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'Created ProcessStepLogs table';

    -- Lookup step logs by process instance
    CREATE NONCLUSTERED INDEX IX_ProcessStepLogs_ProcessInstanceId
        ON ProcessStepLogs (ProcessInstanceId);

    PRINT 'Created ProcessStepLogs indexes';
END

PRINT 'V036 ProcessOrchestration migration completed successfully';
