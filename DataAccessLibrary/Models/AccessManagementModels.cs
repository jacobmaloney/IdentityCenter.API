using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DataAccessLibrary.Models
{
    public class AccessRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string RequesterId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? RequesterName { get; set; }

        public ApplicationUser? Requester { get; set; }

        [Required]
        [MaxLength(100)]
        public string ResourceType { get; set; } = string.Empty; // Application, Group, Role

        [Required]
        [MaxLength(256)]
        public string ResourceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string ResourceName { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string Justification { get; set; } = string.Empty;

        public int DurationDays { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Denied, Expired

        public string? ApproverId { get; set; }

        public ApplicationUser? Approver { get; set; }

        public DateTime? ApprovedAt { get; set; }

        [MaxLength(500)]
        public string? ApprovalComments { get; set; }

        public DateTime? ExpiresAt { get; set; }
    }

    public class ApprovalWorkflow
    {
        public Guid Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(100)]
        public string? Category { get; set; }

        [Required]
        [MaxLength(100)]
        public string ResourceType { get; set; } = "AccessReview";

        public int Priority { get; set; } = 1;

        public bool IsTemplate { get; set; }
        public bool IsActive { get; set; } = true;

        public string? CanvasData { get; set; }

        [MaxLength(256)]
        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        [MaxLength(256)]
        public string? ModifiedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        // Navigation properties
        public List<WorkflowStep> Steps { get; set; } = new();
        public List<ApprovalWorkflowNode> Nodes { get; set; } = new();
        public List<ApprovalWorkflowConnection> Connections { get; set; } = new();
    }

    public class ApprovalWorkflowNode
    {
        public Guid Id { get; set; }
        public Guid WorkflowId { get; set; }

        [MaxLength(100)]
        public string? NodeType { get; set; }

        [MaxLength(200)]
        public string? NodeName { get; set; }

        public double? PositionX { get; set; }
        public double? PositionY { get; set; }

        public string? ConfigData { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation
        public ApprovalWorkflow? Workflow { get; set; }
    }

    public class ApprovalWorkflowConnection
    {
        public Guid Id { get; set; }
        public Guid WorkflowId { get; set; }
        public Guid SourceNodeId { get; set; }
        public Guid TargetNodeId { get; set; }

        [MaxLength(200)]
        public string? Label { get; set; }

        [MaxLength(100)]
        public string? SourcePort { get; set; }

        [MaxLength(100)]
        public string? TargetPort { get; set; }

        public string? ConditionData { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation
        public ApprovalWorkflow? Workflow { get; set; }
        public ApprovalWorkflowNode? SourceNode { get; set; }
        public ApprovalWorkflowNode? TargetNode { get; set; }
    }

    public class WorkflowStep
    {
        public Guid Id { get; set; }
        public Guid WorkflowId { get; set; }

        [MaxLength(100)]
        public string StepType { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? StepName { get; set; }

        public int StepOrder { get; set; }

        public string? Configuration { get; set; }

        // Navigation
        public ApprovalWorkflow? Workflow { get; set; }
    }

    public class UserAccess
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        [Required]
        [MaxLength(100)]
        public string ResourceType { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string ResourceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string ResourceName { get; set; } = string.Empty;

        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        public string? GrantedBy { get; set; }

        public DateTime? ExpiresAt { get; set; }

        public bool IsActive { get; set; } = true;

        public Guid? AccessRequestId { get; set; }

        public AccessRequest? AccessRequest { get; set; }
    }

    public class AccessPolicy
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = string.Empty; // Access, Password, MFA, Compliance

        public bool IsEnabled { get; set; } = true;

        public int Priority { get; set; }

        public List<PolicyCondition> Conditions { get; set; } = new();

        public List<PolicyAction> Actions { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }
    }

    public class PolicyCondition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PolicyId { get; set; }

        public AccessPolicy? Policy { get; set; }

        [Required]
        [MaxLength(100)]
        public string ConditionType { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Operator { get; set; } = string.Empty; // Equals, Contains, GreaterThan, etc.

        [Required]
        public string Value { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }
    }

    public class PolicyAction
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PolicyId { get; set; }

        public AccessPolicy? Policy { get; set; }

        [Required]
        [MaxLength(100)]
        public string ActionType { get; set; } = string.Empty;

        [Required]
        public string Parameters { get; set; } = "{}"; // JSON

        [MaxLength(500)]
        public string? Description { get; set; }
    }
}
