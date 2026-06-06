using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DataAccessLibrary.Models
{
    /// <summary>
    /// Represents a chat session with a user across any channel (Web, Teams, etc.)
    /// Sessions are persisted selectively based on archiving rules:
    /// - Sessions with command executions
    /// - Sessions with errors
    /// - Sessions longer than 30 minutes
    /// </summary>
    public class ChatSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? UserDisplayName { get; set; }

        [Required]
        [MaxLength(50)]
        public string ChannelType { get; set; } = string.Empty; // Web, Teams, Slack, etc.

        [MaxLength(500)]
        public string? ChannelMetadata { get; set; } // JSON - channel-specific data (conversation ID, etc.)

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? EndedAt { get; set; }

        public int MessageCount { get; set; }

        public int CommandsExecuted { get; set; }

        public bool HasErrors { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = "Active"; // Active, Ended, Archived

        public bool IsArchived { get; set; }

        public DateTime? ArchivedAt { get; set; }

        [MaxLength(500)]
        public string? ArchiveReason { get; set; }

        // Navigation properties
        public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    /// <summary>
    /// Represents a single message in a chat session
    /// Stored only for sessions that meet archiving criteria
    /// </summary>
    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid SessionId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(50)]
        public string Direction { get; set; } = string.Empty; // UserToBot, BotToUser

        [Required]
        public string Content { get; set; } = string.Empty;

        public string? Metadata { get; set; } // JSON - attachments, formatting, etc.

        public bool IsCommand { get; set; }

        public Guid? CommandExecutionId { get; set; }

        // Navigation properties
        public virtual ChatSession Session { get; set; } = null!;
        public virtual ChatCommandExecution? CommandExecution { get; set; }
    }

    /// <summary>
    /// Represents the execution of a structured command
    /// Always persisted for audit and analytics
    /// </summary>
    public class ChatCommandExecution
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid SessionId { get; set; }

        public Guid MessageId { get; set; }

        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string CommandName { get; set; } = string.Empty;

        public string? Parameters { get; set; } // JSON - command parameters

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = string.Empty; // Success, Failed, PartialSuccess

        public string? Result { get; set; } // JSON - command result

        public string? ErrorMessage { get; set; }

        public int ExecutionTimeMs { get; set; }

        [MaxLength(100)]
        public string ExecutedBy { get; set; } = string.Empty;

        public string? AuditData { get; set; } // JSON - what changed, what was accessed

        // Navigation properties
        public virtual ChatSession Session { get; set; } = null!;
        public virtual ChatMessage Message { get; set; } = null!;
    }

    /// <summary>
    /// Defines available bot commands with their syntax and permissions
    /// Allows dynamic command registration and ML.NET training data collection
    /// </summary>
    public class ChatCommand
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty; // User, Group, Security, Sync, etc.

        public string? SyntaxPattern { get; set; } // Regex or structured pattern

        public string? Parameters { get; set; } // JSON schema for parameters

        public string? Examples { get; set; } // JSON array of example usages

        [MaxLength(100)]
        public string? RequiredRole { get; set; }

        [MaxLength(100)]
        public string? RequiredPermission { get; set; }

        public bool IsEnabled { get; set; } = true;

        public bool IsSystem { get; set; } = true; // System vs user-defined

        public int Priority { get; set; } = 0; // Higher priority commands matched first

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }

        [MaxLength(256)]
        public string? CreatedBy { get; set; }
    }

    /// <summary>
    /// Tracks user feedback on bot responses for ML.NET training (Phase 2)
    /// Helps improve natural language understanding over time
    /// </summary>
    public class ChatFeedback
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid SessionId { get; set; }

        public Guid MessageId { get; set; }

        public Guid? CommandExecutionId { get; set; }

        public DateTime ProvidedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(50)]
        public string FeedbackType { get; set; } = string.Empty; // Helpful, NotHelpful, Incorrect, Suggestion

        public int? Rating { get; set; } // 1-5 stars

        public string? Comment { get; set; }

        [MaxLength(100)]
        public string UserId { get; set; } = string.Empty;

        public bool IsProcessed { get; set; } // For ML.NET retraining pipeline

        // Navigation properties
        public virtual ChatSession Session { get; set; } = null!;
        public virtual ChatMessage Message { get; set; } = null!;
        public virtual ChatCommandExecution? CommandExecution { get; set; }
    }

    /// <summary>
    /// Stores analytics and metrics for bot performance monitoring
    /// Aggregated data for dashboards and insights
    /// </summary>
    public class ChatAnalytics
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime PeriodStart { get; set; }

        public DateTime PeriodEnd { get; set; }

        [Required]
        [MaxLength(50)]
        public string MetricType { get; set; } = string.Empty; // SessionCount, AvgDuration, CommandCount, etc.

        [Required]
        [MaxLength(50)]
        public string Channel { get; set; } = string.Empty; // Web, Teams, All

        public double Value { get; set; }

        public string? Metadata { get; set; } // JSON - breakdown by command, user, etc.

        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    }
}
