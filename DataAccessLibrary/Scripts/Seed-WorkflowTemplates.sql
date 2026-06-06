-- ============================================================================
-- WORKFLOW TEMPLATE SEEDING SCRIPT
-- Creates pre-built workflow templates with nodes and connections
-- Run this ONCE to populate the ApprovalWorkflows table with templates
-- ============================================================================

-- Template 1: Single Manager Approval (Quick Review)
-- Simple 3-node workflow: Start → Manager → End
-- ============================================================================

DECLARE @SingleManagerWorkflowId UNIQUEIDENTIFIER = NEWID();
DECLARE @StartNodeId UNIQUEIDENTIFIER = NEWID();
DECLARE @ManagerNodeId UNIQUEIDENTIFIER = NEWID();
DECLARE @EndNodeId UNIQUEIDENTIFIER = NEWID();

-- Insert workflow definition
INSERT INTO ApprovalWorkflows (Id, Name, Description, Category, ResourceType, Priority, IsTemplate, IsActive, CreatedBy, CreatedAt)
VALUES (
    @SingleManagerWorkflowId,
    'Single Manager Approval (Quick Review)',
    'Fast approval workflow for low-risk access reviews',
    'Access Review',
    'AccessReview', -- ResourceType (required)
    1, -- Priority (required)
    1, -- IsTemplate = true
    1, -- IsActive = true
    'System',
    GETUTCDATE()
);

-- Insert Start node
INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, PositionX, PositionY, ConfigData, CreatedAt)
VALUES (
    @StartNodeId,
    @SingleManagerWorkflowId,
    0, -- NodeType.Start
    'Start',
    340,
    358,
    NULL,
    GETUTCDATE()
);

-- Insert Manager approval node
INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, PositionX, PositionY, ConfigData, CreatedAt)
VALUES (
    @ManagerNodeId,
    @SingleManagerWorkflowId,
    1, -- NodeType.Approval
    'Direct Manager',
    565,
    503,
    '{"ApproverType":"Manager","TimeoutHours":48,"RequireJustification":true,"AllowDelegation":true,"AutoEscalate":false}',
    GETUTCDATE()
);

-- Insert End node
INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, PositionX, PositionY, ConfigData, CreatedAt)
VALUES (
    @EndNodeId,
    @SingleManagerWorkflowId,
    7, -- NodeType.End
    'End',
    815,
    651,
    NULL,
    GETUTCDATE()
);

-- Insert connection: Start → Manager
INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, SourcePort, TargetPort, CreatedAt)
VALUES (
    NEWID(),
    @SingleManagerWorkflowId,
    @StartNodeId,
    @ManagerNodeId,
    'Bottom', -- Start node bottom port
    'Top',    -- Manager node top port
    GETUTCDATE()
);

-- Insert connection: Manager → End
INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, SourcePort, TargetPort, CreatedAt)
VALUES (
    NEWID(),
    @SingleManagerWorkflowId,
    @ManagerNodeId,
    @EndNodeId,
    'Bottom', -- Manager node bottom port
    'Top',    -- End node top port
    GETUTCDATE()
);

PRINT 'Template 1: Single Manager Approval created successfully';

-- ============================================================================
-- Template 2: SOX Compliance - 4 Level Approval
-- High-security workflow: Start → Manager → CFO/CTO → CISO → Dept Head → End
-- ============================================================================

DECLARE @SOXWorkflowId UNIQUEIDENTIFIER = NEWID();
DECLARE @SOXStartId UNIQUEIDENTIFIER = NEWID();
DECLARE @SOXManagerId UNIQUEIDENTIFIER = NEWID();
DECLARE @SOXCFOId UNIQUEIDENTIFIER = NEWID();
DECLARE @SOXCISOId UNIQUEIDENTIFIER = NEWID();
DECLARE @SOXDeptId UNIQUEIDENTIFIER = NEWID();
DECLARE @SOXEndId UNIQUEIDENTIFIER = NEWID();

-- Insert workflow definition
INSERT INTO ApprovalWorkflows (Id, Name, Description, Category, ResourceType, Priority, IsTemplate, IsActive, CreatedBy, CreatedAt)
VALUES (
    @SOXWorkflowId,
    'SOX Compliance - 4 Level Approval',
    'Public company high-risk access workflow (Sarbanes-Oxley compliance)',
    'Access Review',
    'AccessReview', -- ResourceType (required)
    2, -- Priority (required)
    1, -- IsTemplate = true
    1, -- IsActive = true
    'System',
    GETUTCDATE()
);

-- Insert nodes
INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, PositionX, PositionY, ConfigData, CreatedAt)
VALUES
    (@SOXStartId, @SOXWorkflowId, 0, 'Start', 100, 200, NULL, GETUTCDATE()),
    (@SOXManagerId, @SOXWorkflowId, 1, 'Manager', 350, 200, '{"ApproverType":"Manager","TimeoutHours":72,"RequireJustification":true,"AllowDelegation":true,"AutoEscalate":true}', GETUTCDATE()),
    (@SOXCFOId, @SOXWorkflowId, 1, 'CFO/CTO', 600, 200, '{"ApproverType":"RoleHolder","ApproverIdentifier":"CFO","TimeoutHours":72,"RequireJustification":true,"AllowDelegation":true,"AutoEscalate":true}', GETUTCDATE()),
    (@SOXCISOId, @SOXWorkflowId, 1, 'CISO', 850, 200, '{"ApproverType":"RoleHolder","ApproverIdentifier":"CISO","TimeoutHours":72,"RequireJustification":true,"AllowDelegation":true,"AutoEscalate":true}', GETUTCDATE()),
    (@SOXDeptId, @SOXWorkflowId, 1, 'Department Head', 1100, 200, '{"ApproverType":"RoleHolder","ApproverIdentifier":"DepartmentHead","TimeoutHours":72,"RequireJustification":true,"AllowDelegation":true,"AutoEscalate":true}', GETUTCDATE()),
    (@SOXEndId, @SOXWorkflowId, 7, 'End', 1350, 200, NULL, GETUTCDATE());

-- Insert connections
INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, SourcePort, TargetPort, CreatedAt)
VALUES
    (NEWID(), @SOXWorkflowId, @SOXStartId, @SOXManagerId, 'Bottom', 'Top', GETUTCDATE()),
    (NEWID(), @SOXWorkflowId, @SOXManagerId, @SOXCFOId, 'Bottom', 'Top', GETUTCDATE()),
    (NEWID(), @SOXWorkflowId, @SOXCFOId, @SOXCISOId, 'Bottom', 'Top', GETUTCDATE()),
    (NEWID(), @SOXWorkflowId, @SOXCISOId, @SOXDeptId, 'Bottom', 'Top', GETUTCDATE()),
    (NEWID(), @SOXWorkflowId, @SOXDeptId, @SOXEndId, 'Bottom', 'Top', GETUTCDATE());

PRINT 'Template 2: SOX Compliance - 4 Level Approval created successfully';

PRINT '';
PRINT '============================================================================';
PRINT 'Workflow templates seeded successfully!';
PRINT 'Total templates created: 2';
PRINT '  1. Single Manager Approval (Quick Review) - 3 nodes, 2 connections';
PRINT '  2. SOX Compliance - 4 Level Approval - 6 nodes, 5 connections';
PRINT '============================================================================';
