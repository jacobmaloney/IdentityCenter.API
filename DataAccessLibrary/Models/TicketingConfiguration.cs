using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

/// <summary>
/// Configuration for external ticketing/ITSM system integration
/// Supports ServiceNow, Jira, Azure DevOps, and generic webhook
/// </summary>
[Table("TicketingConfigurations")]
public class TicketingConfiguration
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for this ticketing configuration
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ticketing system type: ServiceNow, Jira, AzureDevOps, Webhook
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string SystemType { get; set; } = "Webhook";

    /// <summary>
    /// Whether this integration is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// Whether this is the default/primary ticketing system
    /// </summary>
    public bool IsDefault { get; set; } = false;

    // ========================================
    // CONNECTION SETTINGS
    // ========================================

    /// <summary>
    /// Base URL for the ticketing system API
    /// e.g., https://company.service-now.com, https://company.atlassian.net
    /// </summary>
    [MaxLength(500)]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// API endpoint path for creating tickets
    /// e.g., /api/now/table/incident, /rest/api/3/issue
    /// </summary>
    [MaxLength(200)]
    public string? ApiEndpoint { get; set; }

    /// <summary>
    /// Authentication type: Basic, Bearer, ApiKey, OAuth2
    /// </summary>
    [MaxLength(50)]
    public string AuthenticationType { get; set; } = "Bearer";

    /// <summary>
    /// Username for Basic auth or API key name
    /// </summary>
    [MaxLength(200)]
    public string? Username { get; set; }

    /// <summary>
    /// Encrypted password, API key, or bearer token
    /// </summary>
    public string? EncryptedCredential { get; set; }

    /// <summary>
    /// OAuth2 client ID (if using OAuth)
    /// </summary>
    [MaxLength(200)]
    public string? ClientId { get; set; }

    /// <summary>
    /// Encrypted OAuth2 client secret
    /// </summary>
    public string? EncryptedClientSecret { get; set; }

    /// <summary>
    /// OAuth2 token endpoint
    /// </summary>
    [MaxLength(500)]
    public string? TokenEndpoint { get; set; }

    // ========================================
    // TICKET CREATION SETTINGS
    // ========================================

    /// <summary>
    /// Default ticket category/type
    /// e.g., "Incident", "Service Request", "Task"
    /// </summary>
    [MaxLength(100)]
    public string DefaultCategory { get; set; } = "Access Management";

    /// <summary>
    /// Default assignment group
    /// </summary>
    [MaxLength(200)]
    public string? DefaultAssignmentGroup { get; set; }

    /// <summary>
    /// Default assignee (if direct assignment)
    /// </summary>
    [MaxLength(200)]
    public string? DefaultAssignee { get; set; }

    /// <summary>
    /// JSON template for ticket creation payload
    /// Supports placeholders: {{Title}}, {{Description}}, {{Priority}}, {{Category}}, etc.
    /// </summary>
    public string? PayloadTemplate { get; set; }

    /// <summary>
    /// Custom HTTP headers as JSON
    /// e.g., {"X-Custom-Header": "value"}
    /// </summary>
    public string? CustomHeaders { get; set; }

    // ========================================
    // PRIORITY MAPPING
    // ========================================

    /// <summary>
    /// JSON mapping of Certification Center priorities to ticketing system values
    /// e.g., {"Critical": "1", "High": "2", "Medium": "3", "Low": "4"}
    /// </summary>
    public string? PriorityMapping { get; set; }

    // ========================================
    // RESPONSE PARSING
    // ========================================

    /// <summary>
    /// JSONPath to extract ticket ID from response
    /// e.g., $.result.sys_id, $.id, $.key
    /// </summary>
    [MaxLength(200)]
    public string? TicketIdPath { get; set; } = "$.id";

    /// <summary>
    /// JSONPath to extract ticket number from response
    /// e.g., $.result.number, $.key
    /// </summary>
    [MaxLength(200)]
    public string? TicketNumberPath { get; set; } = "$.key";

    /// <summary>
    /// URL template for viewing tickets
    /// e.g., https://company.service-now.com/nav_to.do?uri=incident.do?sys_id={{TicketId}}
    /// </summary>
    [MaxLength(500)]
    public string? TicketUrlTemplate { get; set; }

    // ========================================
    // AUDIT & METADATA
    // ========================================

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Last successful connection test
    /// </summary>
    public DateTime? LastTestedAt { get; set; }

    /// <summary>
    /// Result of last connection test
    /// </summary>
    public bool? LastTestSuccessful { get; set; }
}

/// <summary>
/// Log of tickets created through the integration
/// </summary>
[Table("TicketingLogs")]
public class TicketingLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the ticketing configuration used
    /// </summary>
    public Guid ConfigurationId { get; set; }

    /// <summary>
    /// External ticket ID returned by the system
    /// </summary>
    [MaxLength(100)]
    public string? ExternalTicketId { get; set; }

    /// <summary>
    /// External ticket number (human-readable)
    /// </summary>
    [MaxLength(50)]
    public string? ExternalTicketNumber { get; set; }

    /// <summary>
    /// URL to view the ticket
    /// </summary>
    [MaxLength(500)]
    public string? TicketUrl { get; set; }

    /// <summary>
    /// Ticket title/summary
    /// </summary>
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Type of ticket: AccessDenial, PolicyViolation, General
    /// </summary>
    [MaxLength(50)]
    public string TicketType { get; set; } = string.Empty;

    /// <summary>
    /// Priority sent to ticketing system
    /// </summary>
    [MaxLength(20)]
    public string? Priority { get; set; }

    /// <summary>
    /// Related entity type in Certification Center
    /// </summary>
    [MaxLength(50)]
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// Related entity ID in Certification Center
    /// </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary>
    /// Whether ticket creation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if creation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Full request payload sent (for debugging)
    /// </summary>
    public string? RequestPayload { get; set; }

    /// <summary>
    /// Full response received (for debugging)
    /// </summary>
    public string? ResponsePayload { get; set; }

    /// <summary>
    /// HTTP status code received
    /// </summary>
    public int? HttpStatusCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }
}
