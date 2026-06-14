-- =============================================
-- Workflow Template Library - Seeding Script (FIXED)
-- =============================================
-- Purpose: Populate ApprovalWorkflows table with 8 pre-built templates
-- Categories: Compliance (SOX, HIPAA, PCI-DSS) and Organizational
-- Created: 2025-11-19
-- Fixed: Removed [Order] column references
-- =============================================

SET NOCOUNT ON;

PRINT '========================================='
PRINT 'Workflow Template Library - Seeding (FIXED)'
PRINT '========================================='
PRINT ''

-- =============================================
-- Template 1: SOX Compliance - 4 Level Approval
-- =============================================
PRINT 'Creating Template 1: SOX Compliance - 4 Level Approval'

DECLARE @WorkflowId1 UNIQUEIDENTIFIER = NEWID()

-- Insert workflow
INSERT INTO ApprovalWorkflows
    (Id, Name, Description, ResourceType, IsActive, Category, IsTemplate, Priority, CreatedAt, CreatedBy)
VALUES
    (@WorkflowId1,
     'SOX Compliance - 4 Level Approval',
     'Public company high-risk access workflow (Sarbanes-Oxley compliance)',
     'AccessReview',
     1,
     'Compliance',
     1,
     10,
     GETUTCDATE(),
     'System')

-- Insert nodes with variables to capture IDs
DECLARE @Node1_Start UNIQUEIDENTIFIER = NEWID()
DECLARE @Node1_Manager UNIQUEIDENTIFIER = NEWID()
DECLARE @Node1_DeptHead UNIQUEIDENTIFIER = NEWID()
DECLARE @Node1_CISO UNIQUEIDENTIFIER = NEWID()
DECLARE @Node1_CFO UNIQUEIDENTIFIER = NEWID()
DECLARE @Node1_End UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, ConfigData, CreatedAt)
VALUES
    (@Node1_Start, @WorkflowId1, 'Start', 'Start', NULL, GETUTCDATE()),
    (@Node1_Manager, @WorkflowId1, 'Approval', 'Direct Manager',
     '{"ApproverType":"Manager","TimeoutHours":48,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node1_DeptHead, @WorkflowId1, 'Approval', 'Department Head',
     '{"ApproverType":"DepartmentHead","TimeoutHours":72,"RequireJustification":true,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node1_CISO, @WorkflowId1, 'Approval', 'CISO',
     '{"ApproverType":"RoleHolder","ApproverIdentifier":"CISO","TimeoutHours":96,"RequireJustification":true,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node1_CFO, @WorkflowId1, 'Approval', 'CFO/CTO',
     '{"ApproverType":"RoleHolder","ApproverIdentifier":"CFO","TimeoutHours":120,"RequireJustification":true,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node1_End, @WorkflowId1, 'End', 'End', NULL, GETUTCDATE())

-- Insert connections
INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, CreatedAt)
VALUES
    (NEWID(), @WorkflowId1, @Node1_Start, @Node1_Manager, GETUTCDATE()),
    (NEWID(), @WorkflowId1, @Node1_Manager, @Node1_DeptHead, GETUTCDATE()),
    (NEWID(), @WorkflowId1, @Node1_DeptHead, @Node1_CISO, GETUTCDATE()),
    (NEWID(), @WorkflowId1, @Node1_CISO, @Node1_CFO, GETUTCDATE()),
    (NEWID(), @WorkflowId1, @Node1_CFO, @Node1_End, GETUTCDATE())

PRINT '✓ Created template: SOX Compliance - 4 Level Approval'
PRINT ''

-- =============================================
-- Template 2: HIPAA Compliance - 3 Level Approval
-- =============================================
PRINT 'Creating Template 2: HIPAA Compliance - 3 Level Approval'

DECLARE @WorkflowId2 UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflows
    (Id, Name, Description, ResourceType, IsActive, Category, IsTemplate, Priority, CreatedAt, CreatedBy)
VALUES
    (@WorkflowId2,
     'HIPAA Compliance - 3 Level Approval',
     'Healthcare PHI/PII access review workflow (HIPAA compliance)',
     'AccessReview',
     1,
     'Compliance',
     1,
     20,
     GETUTCDATE(),
     'System')

DECLARE @Node2_Start UNIQUEIDENTIFIER = NEWID()
DECLARE @Node2_Manager UNIQUEIDENTIFIER = NEWID()
DECLARE @Node2_Privacy UNIQUEIDENTIFIER = NEWID()
DECLARE @Node2_Compliance UNIQUEIDENTIFIER = NEWID()
DECLARE @Node2_End UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, ConfigData, CreatedAt)
VALUES
    (@Node2_Start, @WorkflowId2, 'Start', 'Start', NULL, GETUTCDATE()),
    (@Node2_Manager, @WorkflowId2, 'Approval', 'Direct Manager',
     '{"ApproverType":"Manager","TimeoutHours":48,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node2_Privacy, @WorkflowId2, 'Approval', 'Privacy Officer',
     '{"ApproverType":"RoleHolder","ApproverIdentifier":"PrivacyOfficer","TimeoutHours":72,"RequireJustification":true,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node2_Compliance, @WorkflowId2, 'Approval', 'Chief Compliance Officer',
     '{"ApproverType":"RoleHolder","ApproverIdentifier":"ComplianceOfficer","TimeoutHours":96,"RequireJustification":true,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node2_End, @WorkflowId2, 'End', 'End', NULL, GETUTCDATE())

INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, CreatedAt)
VALUES
    (NEWID(), @WorkflowId2, @Node2_Start, @Node2_Manager, GETUTCDATE()),
    (NEWID(), @WorkflowId2, @Node2_Manager, @Node2_Privacy, GETUTCDATE()),
    (NEWID(), @WorkflowId2, @Node2_Privacy, @Node2_Compliance, GETUTCDATE()),
    (NEWID(), @WorkflowId2, @Node2_Compliance, @Node2_End, GETUTCDATE())

PRINT '✓ Created template: HIPAA Compliance - 3 Level Approval'
PRINT ''

-- =============================================
-- Template 3: Standard 2-Level Manager Approval
-- =============================================
PRINT 'Creating Template 3: Standard 2-Level Manager Approval'

DECLARE @WorkflowId3 UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflows
    (Id, Name, Description, ResourceType, IsActive, Category, IsTemplate, Priority, CreatedAt, CreatedBy)
VALUES
    (@WorkflowId3,
     'Standard 2-Level Manager Approval',
     'Normal access reviews with manager and manager''s manager approval',
     'AccessReview',
     1,
     'Organizational',
     1,
     30,
     GETUTCDATE(),
     'System')

DECLARE @Node3_Start UNIQUEIDENTIFIER = NEWID()
DECLARE @Node3_Manager UNIQUEIDENTIFIER = NEWID()
DECLARE @Node3_ManagerMgr UNIQUEIDENTIFIER = NEWID()
DECLARE @Node3_End UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, ConfigData, CreatedAt)
VALUES
    (@Node3_Start, @WorkflowId3, 'Start', 'Start', NULL, GETUTCDATE()),
    (@Node3_Manager, @WorkflowId3, 'Approval', 'Direct Manager',
     '{"ApproverType":"Manager","TimeoutHours":48,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node3_ManagerMgr, @WorkflowId3, 'Approval', 'Manager of Manager',
     '{"ApproverType":"ManagerOfManager","TimeoutHours":72,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node3_End, @WorkflowId3, 'End', 'End', NULL, GETUTCDATE())

INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, CreatedAt)
VALUES
    (NEWID(), @WorkflowId3, @Node3_Start, @Node3_Manager, GETUTCDATE()),
    (NEWID(), @WorkflowId3, @Node3_Manager, @Node3_ManagerMgr, GETUTCDATE()),
    (NEWID(), @WorkflowId3, @Node3_ManagerMgr, @Node3_End, GETUTCDATE())

PRINT '✓ Created template: Standard 2-Level Manager Approval'
PRINT ''

-- =============================================
-- Template 4: PCI-DSS High Security
-- =============================================
PRINT 'Creating Template 4: PCI-DSS High Security'

DECLARE @WorkflowId4 UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflows
    (Id, Name, Description, ResourceType, IsActive, Category, IsTemplate, Priority, CreatedAt, CreatedBy)
VALUES
    (@WorkflowId4,
     'PCI-DSS High Security',
     'Payment card data access review workflow (PCI-DSS compliance)',
     'AccessReview',
     1,
     'Compliance',
     1,
     40,
     GETUTCDATE(),
     'System')

DECLARE @Node4_Start UNIQUEIDENTIFIER = NEWID()
DECLARE @Node4_Manager UNIQUEIDENTIFIER = NEWID()
DECLARE @Node4_Security UNIQUEIDENTIFIER = NEWID()
DECLARE @Node4_CISO UNIQUEIDENTIFIER = NEWID()
DECLARE @Node4_End UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, ConfigData, CreatedAt)
VALUES
    (@Node4_Start, @WorkflowId4, 'Start', 'Start', NULL, GETUTCDATE()),
    (@Node4_Manager, @WorkflowId4, 'Approval', 'Direct Manager',
     '{"ApproverType":"Manager","TimeoutHours":48,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node4_Security, @WorkflowId4, 'Approval', 'Security Team',
     '{"ApproverType":"RoleHolder","ApproverIdentifier":"Security","TimeoutHours":72,"RequireJustification":true,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node4_CISO, @WorkflowId4, 'Approval', 'CISO',
     '{"ApproverType":"RoleHolder","ApproverIdentifier":"CISO","TimeoutHours":96,"RequireJustification":true,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node4_End, @WorkflowId4, 'End', 'End', NULL, GETUTCDATE())

INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, CreatedAt)
VALUES
    (NEWID(), @WorkflowId4, @Node4_Start, @Node4_Manager, GETUTCDATE()),
    (NEWID(), @WorkflowId4, @Node4_Manager, @Node4_Security, GETUTCDATE()),
    (NEWID(), @WorkflowId4, @Node4_Security, @Node4_CISO, GETUTCDATE()),
    (NEWID(), @WorkflowId4, @Node4_CISO, @Node4_End, GETUTCDATE())

PRINT '✓ Created template: PCI-DSS High Security'
PRINT ''

-- =============================================
-- Template 5: Resource Owner + Manager
-- =============================================
PRINT 'Creating Template 5: Resource Owner + Manager'

DECLARE @WorkflowId5 UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflows
    (Id, Name, Description, ResourceType, IsActive, Category, IsTemplate, Priority, CreatedAt, CreatedBy)
VALUES
    (@WorkflowId5,
     'Resource Owner + Manager',
     'Application/data access reviews requiring both owner and manager approval',
     'AccessReview',
     1,
     'Organizational',
     1,
     50,
     GETUTCDATE(),
     'System')

DECLARE @Node5_Start UNIQUEIDENTIFIER = NEWID()
DECLARE @Node5_Owner UNIQUEIDENTIFIER = NEWID()
DECLARE @Node5_Manager UNIQUEIDENTIFIER = NEWID()
DECLARE @Node5_End UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, ConfigData, CreatedAt)
VALUES
    (@Node5_Start, @WorkflowId5, 'Start', 'Start', NULL, GETUTCDATE()),
    (@Node5_Owner, @WorkflowId5, 'Approval', 'Resource Owner',
     '{"ApproverType":"Owner","TimeoutHours":48,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node5_Manager, @WorkflowId5, 'Approval', 'Direct Manager',
     '{"ApproverType":"Manager","TimeoutHours":72,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node5_End, @WorkflowId5, 'End', 'End', NULL, GETUTCDATE())

INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, CreatedAt)
VALUES
    (NEWID(), @WorkflowId5, @Node5_Start, @Node5_Owner, GETUTCDATE()),
    (NEWID(), @WorkflowId5, @Node5_Owner, @Node5_Manager, GETUTCDATE()),
    (NEWID(), @WorkflowId5, @Node5_Manager, @Node5_End, GETUTCDATE())

PRINT '✓ Created template: Resource Owner + Manager'
PRINT ''

-- =============================================
-- Template 6: Helpdesk + Manager Approval
-- =============================================
PRINT 'Creating Template 6: Helpdesk + Manager Approval'

DECLARE @WorkflowId6 UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflows
    (Id, Name, Description, ResourceType, IsActive, Category, IsTemplate, Priority, CreatedAt, CreatedBy)
VALUES
    (@WorkflowId6,
     'Helpdesk + Manager Approval',
     'IT helpdesk managed resources with manager oversight',
     'AccessReview',
     1,
     'Organizational',
     1,
     60,
     GETUTCDATE(),
     'System')

DECLARE @Node6_Start UNIQUEIDENTIFIER = NEWID()
DECLARE @Node6_Helpdesk UNIQUEIDENTIFIER = NEWID()
DECLARE @Node6_Manager UNIQUEIDENTIFIER = NEWID()
DECLARE @Node6_End UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, ConfigData, CreatedAt)
VALUES
    (@Node6_Start, @WorkflowId6, 'Start', 'Start', NULL, GETUTCDATE()),
    (@Node6_Helpdesk, @WorkflowId6, 'Approval', 'Helpdesk Team',
     '{"ApproverType":"RoleHolder","ApproverIdentifier":"Helpdesk","TimeoutHours":48,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node6_Manager, @WorkflowId6, 'Approval', 'Direct Manager',
     '{"ApproverType":"Manager","TimeoutHours":72,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node6_End, @WorkflowId6, 'End', 'End', NULL, GETUTCDATE())

INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, CreatedAt)
VALUES
    (NEWID(), @WorkflowId6, @Node6_Start, @Node6_Helpdesk, GETUTCDATE()),
    (NEWID(), @WorkflowId6, @Node6_Helpdesk, @Node6_Manager, GETUTCDATE()),
    (NEWID(), @WorkflowId6, @Node6_Manager, @Node6_End, GETUTCDATE())

PRINT '✓ Created template: Helpdesk + Manager Approval'
PRINT ''

-- =============================================
-- Template 7: Department Head Chain
-- =============================================
PRINT 'Creating Template 7: Department Head Chain'

DECLARE @WorkflowId7 UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflows
    (Id, Name, Description, ResourceType, IsActive, Category, IsTemplate, Priority, CreatedAt, CreatedBy)
VALUES
    (@WorkflowId7,
     'Department Head Chain',
     'Department-specific approvals for organizational resources',
     'AccessReview',
     1,
     'Organizational',
     1,
     70,
     GETUTCDATE(),
     'System')

DECLARE @Node7_Start UNIQUEIDENTIFIER = NEWID()
DECLARE @Node7_Manager UNIQUEIDENTIFIER = NEWID()
DECLARE @Node7_DeptHead UNIQUEIDENTIFIER = NEWID()
DECLARE @Node7_End UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, ConfigData, CreatedAt)
VALUES
    (@Node7_Start, @WorkflowId7, 'Start', 'Start', NULL, GETUTCDATE()),
    (@Node7_Manager, @WorkflowId7, 'Approval', 'Direct Manager',
     '{"ApproverType":"Manager","TimeoutHours":48,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node7_DeptHead, @WorkflowId7, 'Approval', 'Department Head',
     '{"ApproverType":"DepartmentHead","TimeoutHours":72,"RequireJustification":true,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node7_End, @WorkflowId7, 'End', 'End', NULL, GETUTCDATE())

INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, CreatedAt)
VALUES
    (NEWID(), @WorkflowId7, @Node7_Start, @Node7_Manager, GETUTCDATE()),
    (NEWID(), @WorkflowId7, @Node7_Manager, @Node7_DeptHead, GETUTCDATE()),
    (NEWID(), @WorkflowId7, @Node7_DeptHead, @Node7_End, GETUTCDATE())

PRINT '✓ Created template: Department Head Chain'
PRINT ''

-- =============================================
-- Template 8: Single Manager Approval (Quick Review)
-- =============================================
PRINT 'Creating Template 8: Single Manager Approval (Quick Review)'

DECLARE @WorkflowId8 UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflows
    (Id, Name, Description, ResourceType, IsActive, Category, IsTemplate, Priority, CreatedAt, CreatedBy)
VALUES
    (@WorkflowId8,
     'Single Manager Approval (Quick Review)',
     'Fast approval workflow for low-risk access reviews',
     'AccessReview',
     1,
     'Organizational',
     1,
     80,
     GETUTCDATE(),
     'System')

DECLARE @Node8_Start UNIQUEIDENTIFIER = NEWID()
DECLARE @Node8_Manager UNIQUEIDENTIFIER = NEWID()
DECLARE @Node8_End UNIQUEIDENTIFIER = NEWID()

INSERT INTO ApprovalWorkflowNodes (Id, WorkflowId, NodeType, NodeName, ConfigData, CreatedAt)
VALUES
    (@Node8_Start, @WorkflowId8, 'Start', 'Start', NULL, GETUTCDATE()),
    (@Node8_Manager, @WorkflowId8, 'Approval', 'Direct Manager',
     '{"ApproverType":"Manager","TimeoutHours":48,"RequireJustification":false,"AllowDelegation":true}',
     GETUTCDATE()),
    (@Node8_End, @WorkflowId8, 'End', 'End', NULL, GETUTCDATE())

INSERT INTO ApprovalWorkflowConnections (Id, WorkflowId, SourceNodeId, TargetNodeId, CreatedAt)
VALUES
    (NEWID(), @WorkflowId8, @Node8_Start, @Node8_Manager, GETUTCDATE()),
    (NEWID(), @WorkflowId8, @Node8_Manager, @Node8_End, GETUTCDATE())

PRINT '✓ Created template: Single Manager Approval (Quick Review)'
PRINT ''

-- =============================================
-- Summary
-- =============================================
PRINT ''
PRINT '========================================='
PRINT 'Template Library Seeding Complete!'
PRINT '========================================='
PRINT ''
PRINT 'Templates Created:'
PRINT '  ✓ 1. SOX Compliance - 4 Level Approval'
PRINT '  ✓ 2. HIPAA Compliance - 3 Level Approval'
PRINT '  ✓ 3. Standard 2-Level Manager Approval'
PRINT '  ✓ 4. PCI-DSS High Security'
PRINT '  ✓ 5. Resource Owner + Manager'
PRINT '  ✓ 6. Helpdesk + Manager Approval'
PRINT '  ✓ 7. Department Head Chain'
PRINT '  ✓ 8. Single Manager Approval (Quick Review)'
PRINT ''
PRINT 'Verification Query:'
PRINT 'SELECT Name, Category, IsTemplate FROM ApprovalWorkflows WHERE IsTemplate = 1'
PRINT ''
PRINT '========================================='
