using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based approval workflow template seeding.
/// Seeds workflow templates with visual nodes and connections for access requests,
/// group memberships, role assignments, and compliance processes.
/// </summary>
public class DapperApprovalWorkflowsSeedService : DapperSeedServiceBase
{
    public DapperApprovalWorkflowsSeedService(
        IConfiguration configuration,
        ILogger<DapperApprovalWorkflowsSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if workflow templates already exist
        var existingCount = await GetCountAsync(connection, transaction, "ApprovalWorkflows", "IsTemplate = 1");
        if (existingCount > 0)
        {
            _logger.LogDebug("Approval workflows already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var workflows = GetDefaultWorkflowTemplates();
        int created = 0;

        const string workflowInsertSql = @"
            INSERT INTO ApprovalWorkflows (
                Id, Name, Description, ResourceType, Category, IsTemplate, IsActive,
                Priority, CreatedAt, CreatedBy
            )
            VALUES (
                @Id, @Name, @Description, @ResourceType, @Category, @IsTemplate, @IsActive,
                @Priority, @CreatedAt, @CreatedBy
            )";

        const string nodeInsertSql = @"
            INSERT INTO ApprovalWorkflowNodes (
                Id, WorkflowId, NodeType, NodeName, PositionX, PositionY, ConfigData, CreatedAt
            )
            VALUES (
                @Id, @WorkflowId, @NodeType, @NodeName, @PositionX, @PositionY, @ConfigData, @CreatedAt
            )";

        const string connectionInsertSql = @"
            INSERT INTO ApprovalWorkflowConnections (
                Id, WorkflowId, SourceNodeId, TargetNodeId, Label, CreatedAt
            )
            VALUES (
                @Id, @WorkflowId, @SourceNodeId, @TargetNodeId, @Label, @CreatedAt
            )";

        foreach (var workflow in workflows)
        {
            // Insert workflow
            await InsertAsync(connection, transaction, workflowInsertSql, new
            {
                workflow.Id,
                workflow.Name,
                workflow.Description,
                workflow.ResourceType,
                workflow.Category,
                workflow.IsTemplate,
                workflow.IsActive,
                workflow.Priority,
                workflow.CreatedAt,
                workflow.CreatedBy
            });

            // Insert nodes
            foreach (var node in workflow.Nodes)
            {
                await InsertAsync(connection, transaction, nodeInsertSql, node);
            }

            // Insert connections
            foreach (var conn in workflow.Connections)
            {
                await InsertAsync(connection, transaction, connectionInsertSql, conn);
            }

            created++;
        }

        sw.Stop();
        LogSeedComplete("ApprovalWorkflows", created, 0, sw.Elapsed);
    }

    private static List<WorkflowDefinition> GetDefaultWorkflowTemplates()
    {
        return new List<WorkflowDefinition>
        {
            // Access Request Workflows
            CreateWorkflowWithNodes("Standard Access Request", "Single-level manager approval for standard access requests", "AccessRequest", "Access Management", 10,
                new[] { ("Manager", "Manager Approval", 48, (string?)null) }),

            CreateWorkflowWithNodes("Elevated Access Request", "Two-level approval (Manager + Security) for elevated/admin access", "AccessRequest", "Access Management", 5,
                new[] { ("Manager", "Manager Approval", 24, (string?)null), ("Role", "Security Review", 24, "Security Administrator") }),

            CreateWorkflowWithNodes("Emergency Access Request", "Fast-track approval with automatic escalation for urgent access needs", "AccessRequest", "Access Management", 1,
                new[] { ("Role", "Security Fast-Track", 4, "Security Administrator") }),

            // Group Membership Workflows
            CreateWorkflowWithNodes("Group Membership - Manager Approval", "Manager approval for adding users to groups", "GroupMembership", "Group Management", 10,
                new[] { ("Manager", "Manager Approval", 48, (string?)null) }),

            CreateWorkflowWithNodes("Sensitive Group Membership", "Multi-level approval for sensitive group membership (Manager + Group Owner + Security)", "GroupMembership", "Group Management", 1,
                new[] { ("Manager", "Manager Approval", 24, (string?)null), ("GroupOwner", "Group Owner Approval", 24, (string?)null), ("Role", "Security Review", 24, "Security Administrator") }),

            // Role Assignment Workflows
            CreateWorkflowWithNodes("Business Role Assignment", "Manager and HR approval for business role assignments", "RoleAssignment", "Role Management", 10,
                new[] { ("Manager", "Manager Approval", 48, (string?)null), ("Role", "HR Review", 48, "HR Manager") }),

            CreateWorkflowWithNodes("Admin Role Assignment", "Multi-level approval (Manager + IT Lead + CISO) for admin role assignments", "RoleAssignment", "Role Management", 1,
                new[] { ("Manager", "Manager Approval", 24, (string?)null), ("Role", "IT Lead Review", 24, "IT Lead"), ("Role", "CISO Approval", 48, "CISO") }),

            // Access Review Workflows
            CreateWorkflowWithNodes("Access Review - Standard", "Standard access review workflow with manager certification", "AccessReview", "Compliance", 10,
                new[] { ("Manager", "Manager Certification", 168, (string?)null) }),

            CreateWorkflowWithNodes("Access Review - SOX Compliance", "SOX-compliant access review with multiple approver levels and audit trail", "AccessReview", "Compliance", 5,
                new[] { ("Manager", "Manager Review", 120, (string?)null), ("Role", "Compliance Review", 72, "Compliance Officer"), ("Role", "Audit Sign-off", 48, "Internal Audit") }),

            // Provisioning Workflows
            CreateWorkflowWithNodes("New Employee Provisioning", "Automated provisioning workflow for new employee onboarding", "Provisioning", "Lifecycle", 10,
                new[] { ("Manager", "Manager Approval", 24, (string?)null), ("Role", "HR Confirmation", 24, "HR Manager") }),

            CreateWorkflowWithNodes("Employee Termination", "De-provisioning workflow for employee offboarding with access revocation", "Provisioning", "Lifecycle", 1,
                new[] { ("Role", "HR Initiation", 4, "HR Manager"), ("Role", "IT Execution", 4, "IT Administrator") }),

            CreateWorkflowWithNodes("Department Transfer", "Access modification workflow for department transfers", "Provisioning", "Lifecycle", 10,
                new[] { ("Manager", "Current Manager Approval", 48, (string?)null), ("Role", "HR Review", 48, "HR Manager"), ("Role", "IT Processing", 24, "IT Administrator") }),

            // Exception Workflows
            CreateWorkflowWithNodes("Policy Exception Request", "Multi-level approval for compliance policy exceptions", "PolicyException", "Compliance", 5,
                new[] { ("Manager", "Manager Approval", 24, (string?)null), ("Role", "Compliance Review", 48, "Compliance Officer"), ("Role", "Risk Assessment", 48, "Risk Manager") }),

            CreateWorkflowWithNodes("SoD Violation Override", "Approval workflow for Separation of Duties violations requiring exception", "PolicyException", "Compliance", 1,
                new[] { ("Manager", "Manager Justification", 24, (string?)null), ("Role", "Compliance Review", 24, "Compliance Officer"), ("Role", "CISO Approval", 48, "CISO"), ("Role", "Executive Sign-off", 72, "CFO") })
        };
    }

    private static WorkflowDefinition CreateWorkflowWithNodes(
        string name,
        string description,
        string resourceType,
        string category,
        int priority,
        (string approverType, string nodeName, int timeoutHours, string? approverId)[] approvalSteps)
    {
        var now = DateTime.UtcNow;
        var workflowId = Guid.NewGuid();
        var nodes = new List<NodeDefinition>();
        var connections = new List<ConnectionDefinition>();

        const double startX = 100;
        const double startY = 300;
        const double nodeSpacingX = 250;

        // Create Start node
        var startNode = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            NodeType = "Start",
            NodeName = "Start",
            PositionX = startX,
            PositionY = startY,
            ConfigData = (string?)null,
            CreatedAt = now
        };
        nodes.Add(startNode);

        // Create approval nodes
        var previousNodeId = startNode.Id;
        for (int i = 0; i < approvalSteps.Length; i++)
        {
            var step = approvalSteps[i];
            var approvalNode = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                NodeType = "Approval",
                NodeName = step.nodeName,
                PositionX = startX + ((i + 1) * nodeSpacingX),
                PositionY = startY,
                ConfigData = JsonSerializer.Serialize(new
                {
                    approverType = step.approverType,
                    approverId = step.approverId,
                    timeoutHours = step.timeoutHours,
                    escalationAction = "Escalate"
                }),
                CreatedAt = now
            };
            nodes.Add(approvalNode);

            // Connect previous node to this approval node
            connections.Add(new ConnectionDefinition
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                SourceNodeId = previousNodeId,
                TargetNodeId = approvalNode.Id,
                Label = previousNodeId == startNode.Id ? null : "Approved",
                CreatedAt = now
            });

            previousNodeId = approvalNode.Id;
        }

        // Create End node
        var endNode = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            NodeType = "End",
            NodeName = "Complete",
            PositionX = startX + ((approvalSteps.Length + 1) * nodeSpacingX),
            PositionY = startY,
            ConfigData = (string?)null,
            CreatedAt = now
        };
        nodes.Add(endNode);

        // Connect last approval to End
        connections.Add(new ConnectionDefinition
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            SourceNodeId = previousNodeId,
            TargetNodeId = endNode.Id,
            Label = "Approved",
            CreatedAt = now
        });

        return new WorkflowDefinition
        {
            Id = workflowId,
            Name = name,
            Description = description,
            ResourceType = resourceType,
            Category = category,
            IsTemplate = true,
            IsActive = true,
            Priority = priority,
            Nodes = nodes,
            Connections = connections,
            CreatedAt = now,
            CreatedBy = "System"
        };
    }

    private class WorkflowDefinition
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsTemplate { get; set; }
        public bool IsActive { get; set; }
        public int Priority { get; set; }
        public List<NodeDefinition> Nodes { get; set; } = new();
        public List<ConnectionDefinition> Connections { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    private class NodeDefinition
    {
        public Guid Id { get; set; }
        public Guid WorkflowId { get; set; }
        public string NodeType { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public string? ConfigData { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private class ConnectionDefinition
    {
        public Guid Id { get; set; }
        public Guid WorkflowId { get; set; }
        public Guid SourceNodeId { get; set; }
        public Guid TargetNodeId { get; set; }
        public string? Label { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
