using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;
using System.Text.Json;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default approval workflow templates for common business scenarios.
/// These templates can be cloned and customized for specific organizational needs.
/// </summary>
public class DefaultApprovalWorkflowsSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<DefaultApprovalWorkflowsSeedService> _logger;

    public DefaultApprovalWorkflowsSeedService(
        IConfiguration configuration,
        ILogger<DefaultApprovalWorkflowsSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds essential approval workflow templates with visual nodes and connections
    /// </summary>
    public async Task SeedDefaultWorkflowTemplatesAsync()
    {
        _logger.LogInformation("Starting approval workflow templates seeding...");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var existingCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ApprovalWorkflows WHERE IsTemplate = 1");

        if (existingCount > 0)
        {
            _logger.LogInformation("Approval workflow templates already exist ({Count}), skipping seed", existingCount);
            return;
        }

        var templates = GetDefaultWorkflowTemplates();

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var template in templates)
            {
                // Insert workflow
                const string insertWorkflowSql = @"
                    INSERT INTO ApprovalWorkflows
                        (Id, Name, Description, ResourceType, Category, IsTemplate, IsActive, Priority, CreatedAt, CreatedBy)
                    VALUES
                        (@Id, @Name, @Description, @ResourceType, @Category, @IsTemplate, @IsActive, @Priority, @CreatedAt, @CreatedBy)";

                await connection.ExecuteAsync(insertWorkflowSql, new
                {
                    template.Id,
                    template.Name,
                    template.Description,
                    template.ResourceType,
                    template.Category,
                    template.IsTemplate,
                    template.IsActive,
                    template.Priority,
                    template.CreatedAt,
                    template.CreatedBy
                }, transaction);

                // Insert nodes
                const string insertNodeSql = @"
                    INSERT INTO ApprovalWorkflowNodes
                        (Id, WorkflowId, NodeType, NodeName, PositionX, PositionY, ConfigData, CreatedAt)
                    VALUES
                        (@Id, @WorkflowId, @NodeType, @NodeName, @PositionX, @PositionY, @ConfigData, @CreatedAt)";

                foreach (var node in template.Nodes)
                {
                    await connection.ExecuteAsync(insertNodeSql, node, transaction);
                }

                // Insert connections
                const string insertConnectionSql = @"
                    INSERT INTO ApprovalWorkflowConnections
                        (Id, WorkflowId, SourceNodeId, TargetNodeId, Label, CreatedAt)
                    VALUES
                        (@Id, @WorkflowId, @SourceNodeId, @TargetNodeId, @Label, @CreatedAt)";

                foreach (var conn in template.Connections)
                {
                    await connection.ExecuteAsync(insertConnectionSql, conn, transaction);
                }
            }

            await transaction.CommitAsync();
            _logger.LogInformation("Successfully seeded {Count} approval workflow templates", templates.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private List<ApprovalWorkflow> GetDefaultWorkflowTemplates()
    {
        return new List<ApprovalWorkflow>
        {
            // ========== ACCESS REQUEST WORKFLOWS ==========
            CreateWorkflowWithNodes(
                "Standard Access Request",
                "Single-level manager approval for standard access requests",
                "AccessRequest",
                "Access Management",
                10,
                new[] { ("Manager", "Manager Approval", 48) }
            ),

            CreateWorkflowWithNodes(
                "Elevated Access Request",
                "Two-level approval (Manager + Security) for elevated/admin access",
                "AccessRequest",
                "Access Management",
                5,
                new (string, string, int, string?)[] { ("Manager", "Manager Approval", 24, null), ("Role", "Security Review", 24, "Security Administrator") }
            ),

            CreateWorkflowWithNodes(
                "Emergency Access Request",
                "Fast-track approval with automatic escalation for urgent access needs",
                "AccessRequest",
                "Access Management",
                1,
                new (string, string, int, string?)[] { ("Role", "Security Fast-Track", 4, "Security Administrator") }
            ),

            // ========== GROUP MEMBERSHIP WORKFLOWS ==========
            CreateWorkflowWithNodes(
                "Group Membership - Manager Approval",
                "Manager approval for adding users to groups",
                "GroupMembership",
                "Group Management",
                10,
                new[] { ("Manager", "Manager Approval", 48) }
            ),

            CreateWorkflowWithNodes(
                "Sensitive Group Membership",
                "Multi-level approval for sensitive group membership (Manager + Group Owner + Security)",
                "GroupMembership",
                "Group Management",
                1,
                new (string, string, int, string?)[] { ("Manager", "Manager Approval", 24, null), ("GroupOwner", "Group Owner Approval", 24, null), ("Role", "Security Review", 24, "Security Administrator") }
            ),

            // ========== ROLE ASSIGNMENT WORKFLOWS ==========
            CreateWorkflowWithNodes(
                "Business Role Assignment",
                "Manager and HR approval for business role assignments",
                "RoleAssignment",
                "Role Management",
                10,
                new (string, string, int, string?)[] { ("Manager", "Manager Approval", 48, null), ("Role", "HR Review", 48, "HR Manager") }
            ),

            CreateWorkflowWithNodes(
                "Admin Role Assignment",
                "Multi-level approval (Manager + IT Lead + CISO) for admin role assignments",
                "RoleAssignment",
                "Role Management",
                1,
                new (string, string, int, string?)[] { ("Manager", "Manager Approval", 24, null), ("Role", "IT Lead Review", 24, "IT Lead"), ("Role", "CISO Approval", 48, "CISO") }
            ),

            // ========== ACCESS REVIEW WORKFLOWS ==========
            CreateWorkflowWithNodes(
                "Access Review - Standard",
                "Standard access review workflow with manager certification",
                "AccessReview",
                "Compliance",
                10,
                new[] { ("Manager", "Manager Certification", 168) } // 7 days
            ),

            CreateWorkflowWithNodes(
                "Access Review - SOX Compliance",
                "SOX-compliant access review with multiple approver levels and audit trail",
                "AccessReview",
                "Compliance",
                5,
                new (string, string, int, string?)[] { ("Manager", "Manager Review", 120, null), ("Role", "Compliance Review", 72, "Compliance Officer"), ("Role", "Audit Sign-off", 48, "Internal Audit") }
            ),

            // ========== PROVISIONING WORKFLOWS ==========
            CreateWorkflowWithNodes(
                "New Employee Provisioning",
                "Automated provisioning workflow for new employee onboarding",
                "Provisioning",
                "Lifecycle",
                10,
                new (string, string, int, string?)[] { ("Manager", "Manager Approval", 24, null), ("Role", "HR Confirmation", 24, "HR Manager") }
            ),

            CreateWorkflowWithNodes(
                "Employee Termination",
                "De-provisioning workflow for employee offboarding with access revocation",
                "Provisioning",
                "Lifecycle",
                1,
                new (string, string, int, string?)[] { ("Role", "HR Initiation", 4, "HR Manager"), ("Role", "IT Execution", 4, "IT Administrator") }
            ),

            CreateWorkflowWithNodes(
                "Department Transfer",
                "Access modification workflow for department transfers",
                "Provisioning",
                "Lifecycle",
                10,
                new (string, string, int, string?)[] { ("Manager", "Current Manager Approval", 48, null), ("Role", "HR Review", 48, "HR Manager"), ("Role", "IT Processing", 24, "IT Administrator") }
            ),

            // ========== EXCEPTION WORKFLOWS ==========
            CreateWorkflowWithNodes(
                "Policy Exception Request",
                "Multi-level approval for compliance policy exceptions",
                "PolicyException",
                "Compliance",
                5,
                new (string, string, int, string?)[] { ("Manager", "Manager Approval", 24, null), ("Role", "Compliance Review", 48, "Compliance Officer"), ("Role", "Risk Assessment", 48, "Risk Manager") }
            ),

            CreateWorkflowWithNodes(
                "SoD Violation Override",
                "Approval workflow for Separation of Duties violations requiring exception",
                "PolicyException",
                "Compliance",
                1,
                new (string, string, int, string?)[] { ("Manager", "Manager Justification", 24, null), ("Role", "Compliance Review", 24, "Compliance Officer"), ("Role", "CISO Approval", 48, "CISO"), ("Role", "Executive Sign-off", 72, "CFO") }
            )
        };
    }

    /// <summary>
    /// Creates a workflow with visual nodes and connections
    /// </summary>
    private ApprovalWorkflow CreateWorkflowWithNodes(
        string name,
        string description,
        string resourceType,
        string category,
        int priority,
        (string approverType, string nodeName, int timeoutHours, string? approverId)[] approvalSteps)
    {
        var workflowId = Guid.NewGuid();
        var nodes = new List<ApprovalWorkflowNode>();
        var connections = new List<ApprovalWorkflowConnection>();

        // Layout constants
        const double startX = 100;
        const double startY = 300;
        const double nodeSpacingX = 250;

        // Create Start node
        var startNode = new ApprovalWorkflowNode
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            NodeType = "Start",
            NodeName = "Start",
            PositionX = startX,
            PositionY = startY,
            CreatedAt = DateTime.UtcNow
        };
        nodes.Add(startNode);

        // Create approval nodes
        var previousNodeId = startNode.Id;
        for (int i = 0; i < approvalSteps.Length; i++)
        {
            var step = approvalSteps[i];
            var approvalNode = new ApprovalWorkflowNode
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
                CreatedAt = DateTime.UtcNow
            };
            nodes.Add(approvalNode);

            // Connect previous node to this approval node
            connections.Add(new ApprovalWorkflowConnection
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                SourceNodeId = previousNodeId,
                TargetNodeId = approvalNode.Id,
                Label = previousNodeId == startNode.Id ? null : "Approved",
                CreatedAt = DateTime.UtcNow
            });

            previousNodeId = approvalNode.Id;
        }

        // Create End node
        var endNode = new ApprovalWorkflowNode
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            NodeType = "End",
            NodeName = "Complete",
            PositionX = startX + ((approvalSteps.Length + 1) * nodeSpacingX),
            PositionY = startY,
            CreatedAt = DateTime.UtcNow
        };
        nodes.Add(endNode);

        // Connect last approval to End
        connections.Add(new ApprovalWorkflowConnection
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            SourceNodeId = previousNodeId,
            TargetNodeId = endNode.Id,
            Label = "Approved",
            CreatedAt = DateTime.UtcNow
        });

        return new ApprovalWorkflow
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
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "System"
        };
    }

    /// <summary>
    /// Helper overload for steps without approverId
    /// </summary>
    private ApprovalWorkflow CreateWorkflowWithNodes(
        string name,
        string description,
        string resourceType,
        string category,
        int priority,
        (string approverType, string nodeName, int timeoutHours)[] approvalSteps)
    {
        var stepsWithApproverId = approvalSteps
            .Select(s => (s.approverType, s.nodeName, s.timeoutHours, (string?)null))
            .ToArray();
        return CreateWorkflowWithNodes(name, description, resourceType, category, priority, stepsWithApproverId);
    }
}
