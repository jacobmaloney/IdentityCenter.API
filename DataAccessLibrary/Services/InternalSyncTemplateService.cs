using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Represents a built-in internal sync project template.
/// </summary>
public class InternalSyncTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Direction { get; set; } = "ObjectToPerson";
    public string Category { get; set; } = "General";
    public string IconClass { get; set; } = "fas fa-sync";
    public string BadgeColor { get; set; } = "#3b82f6";
    public List<InternalSyncStepTemplate> Steps { get; set; } = new();
}

/// <summary>
/// Represents a step within a template.
/// </summary>
public class InternalSyncStepTemplate
{
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Configuration { get; set; }
    public string? MappingsPreset { get; set; }
    public bool ContinueOnError { get; set; } = false;
}

/// <summary>
/// Service for managing internal sync project templates.
/// Provides built-in templates and template application functionality.
/// </summary>
public interface IInternalSyncTemplateService
{
    /// <summary>
    /// Get all available templates (built-in + custom).
    /// </summary>
    List<InternalSyncTemplate> GetAvailableTemplates();

    /// <summary>
    /// Get a specific template by ID.
    /// </summary>
    InternalSyncTemplate? GetTemplate(string templateId);

    /// <summary>
    /// Apply a template to create a new internal sync project.
    /// </summary>
    Task<SyncProject> ApplyTemplateAsync(
        string templateId,
        string projectName,
        string? description = null,
        Guid? sourceConnectionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get preset field mappings for a given preset name.
    /// </summary>
    List<InternalSyncStepMapping> GetPresetMappings(string presetName, Guid stepId);

    /// <summary>
    /// Get available source fields for a sync direction.
    /// </summary>
    List<FieldDefinition> GetSourceFields(string direction);

    /// <summary>
    /// Get available target fields for a sync direction.
    /// </summary>
    List<FieldDefinition> GetTargetFields(string direction);

    /// <summary>
    /// Get available preset names for a sync direction.
    /// </summary>
    List<string> GetPresetNames(string direction);

    /// <summary>
    /// Get preset mappings for a specific direction.
    /// </summary>
    List<InternalSyncStepMapping> GetPresetMappingsForDirection(string presetName, string direction, Guid stepId);

    /// <summary>
    /// Auto-calculate mappings by matching source and target fields with identical or well-known equivalent names.
    /// </summary>
    List<InternalSyncStepMapping> AutoCalculateMappings(string direction, Guid stepId);
}

/// <summary>
/// Represents a field that can be used in sync mappings.
/// </summary>
public class FieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = "string";
    public string? Description { get; set; }
    public bool IsRequired { get; set; } = false;
}

/// <summary>
/// Implementation of the internal sync template service.
/// </summary>
public class InternalSyncTemplateService : IInternalSyncTemplateService
{
    private readonly string _connectionString;
    private readonly ILogger<InternalSyncTemplateService> _logger;

    private static readonly List<InternalSyncTemplate> _builtInTemplates = new()
    {
        new InternalSyncTemplate
        {
            Id = "standard-person-sync",
            Name = "Full Sync",
            Description = "Complete Object→Person pipeline: create identities, sync fields, resolve managers",
            Direction = "ObjectToPerson",
            Category = "Identity",
            IconClass = "fas fa-users",
            BadgeColor = "#10b981",
            Steps = new()
            {
                new InternalSyncStepTemplate
                {
                    Order = 1,
                    Name = "Create Identities",
                    StepType = "ObjectToPersonCreate",
                    Description = "Create identity records for unlinked objects"
                },
                new InternalSyncStepTemplate
                {
                    Order = 2,
                    Name = "Sync Fields to Identities",
                    StepType = "ObjectToPersonFieldSync",
                    Description = "Sync all fields from objects to identities",
                    MappingsPreset = "full"
                },
                new InternalSyncStepTemplate
                {
                    Order = 3,
                    Name = "Resolve Object Managers",
                    StepType = "ManagerResolve",
                    Description = "Resolve manager DN references to object IDs"
                },
                new InternalSyncStepTemplate
                {
                    Order = 4,
                    Name = "Assign Identity Managers",
                    StepType = "ManagerAssign",
                    Description = "Assign manager relationships at the identity level"
                }
            }
        },
        new InternalSyncTemplate
        {
            Id = "create-only",
            Name = "Bulk Create",
            Description = "Create identities for all unlinked objects, then sync fields",
            Direction = "ObjectToPerson",
            Category = "Identity",
            IconClass = "fas fa-user-plus",
            BadgeColor = "#8b5cf6",
            Steps = new()
            {
                new InternalSyncStepTemplate
                {
                    Order = 1,
                    Name = "Create Identities",
                    StepType = "ObjectToPersonCreate",
                    Description = "Create identity records for all unlinked objects"
                },
                new InternalSyncStepTemplate
                {
                    Order = 2,
                    Name = "Sync Fields",
                    StepType = "ObjectToPersonFieldSync",
                    Description = "Sync all fields from objects to newly created identities",
                    MappingsPreset = "full"
                }
            }
        },
        new InternalSyncTemplate
        {
            Id = "hr-import",
            Name = "HR Import",
            Description = "Create identities from HR system objects with employee-centric fields",
            Direction = "ObjectToPerson",
            Category = "Identity",
            IconClass = "fas fa-id-card",
            BadgeColor = "#ec4899",
            Steps = new()
            {
                new InternalSyncStepTemplate
                {
                    Order = 1,
                    Name = "Sync HR Fields",
                    StepType = "ObjectToPersonFieldSync",
                    Description = "Sync all fields from HR source to identities",
                    MappingsPreset = "full"
                }
            }
        },
        new InternalSyncTemplate
        {
            Id = "manager-sync",
            Name = "Manager Sync Only",
            Description = "Sync manager relationships from objects to identities",
            Direction = "ObjectToPerson",
            Category = "Relationships",
            IconClass = "fas fa-sitemap",
            BadgeColor = "#f59e0b",
            Steps = new()
            {
                new InternalSyncStepTemplate
                {
                    Order = 1,
                    Name = "Resolve Object Managers",
                    StepType = "ManagerResolve",
                    Description = "Resolve manager DN references to object IDs"
                },
                new InternalSyncStepTemplate
                {
                    Order = 2,
                    Name = "Assign Identity Managers",
                    StepType = "ManagerAssign",
                    Description = "Assign manager relationships at the identity level"
                }
            }
        },
        new InternalSyncTemplate
        {
            Id = "ad-provisioning",
            Name = "AD Provisioning",
            Description = "Create AD user accounts from identity records",
            Direction = "PersonToObject",
            Category = "Provisioning",
            IconClass = "fas fa-user-cog",
            BadgeColor = "#f97316",
            Steps = new()
            {
                new InternalSyncStepTemplate
                {
                    Order = 1,
                    Name = "Provision AD Accounts",
                    StepType = "PersonToObjectCreate",
                    Description = "Create AD user objects for identities without linked accounts"
                },
                new InternalSyncStepTemplate
                {
                    Order = 2,
                    Name = "Push Identity Data",
                    StepType = "PersonToObjectFieldSync",
                    Description = "Sync all identity fields to provisioned AD accounts",
                    MappingsPreset = "full"
                }
            }
        },
        new InternalSyncTemplate
        {
            Id = "identity-writeback",
            Name = "Identity Writeback",
            Description = "Push identity changes back to linked objects",
            Direction = "PersonToObject",
            Category = "Provisioning",
            IconClass = "fas fa-cloud-upload-alt",
            BadgeColor = "#6366f1",
            Steps = new()
            {
                new InternalSyncStepTemplate
                {
                    Order = 1,
                    Name = "Push Identity Data",
                    StepType = "PersonToObjectFieldSync",
                    Description = "Sync all identity fields to linked objects",
                    MappingsPreset = "full"
                }
            }
        },
        new InternalSyncTemplate
        {
            Id = "deprovision",
            Name = "Deprovisioning",
            Description = "Disable objects for inactive identities",
            Direction = "PersonToObject",
            Category = "Provisioning",
            IconClass = "fas fa-user-slash",
            BadgeColor = "#ef4444",
            Steps = new()
            {
                new InternalSyncStepTemplate
                {
                    Order = 1,
                    Name = "Deprovision Inactive Accounts",
                    StepType = "PersonToObjectDeprovision",
                    Description = "Disable objects linked to inactive identities"
                }
            }
        },
        new InternalSyncTemplate
        {
            Id = "custom",
            Name = "Custom",
            Description = "Start with an empty project and configure steps manually",
            Direction = "ObjectToPerson",
            Category = "Custom",
            IconClass = "fas fa-code",
            BadgeColor = "#6b7280",
            Steps = new() // Empty - user adds steps
        }
    };

    // ========================================================================
    // FIELD MAPPING PRESETS - COMPREHENSIVE IDENTITY MANAGEMENT FIELDS
    // ========================================================================
    // Object table (IdentityObject): Comprehensive AD/LDAP attributes
    // Identity table: Full identity management schema
    // ========================================================================

    // Object → Identity (Person) mappings
    private static readonly Dictionary<string, List<(string Source, string Target, bool Overwrite)>> _objectToIdentityMappings = new()
    {
        // FULL preset - ALL mappable fields between Objects and Identities, always overwrite (authoritative source)
        ["full"] = new()
        {
            // Core Biographic
            ("DisplayName", "DisplayName", true),
            ("FirstName", "FirstName", true),
            ("LastName", "LastName", true),
            ("MiddleName", "MiddleName", true),

            // Contact Information
            ("Email", "PrimaryEmail", true),
            ("Phone", "PrimaryPhone", true),
            ("MobilePhone", "MobilePhone", true),
            ("HomePhone", "HomePhone", true),
            ("Fax", "Fax", true),

            // Address
            ("StreetAddress", "StreetAddress", true),
            ("City", "City", true),
            ("State", "State", true),
            ("PostalCode", "PostalCode", true),
            ("Country", "Country", true),

            // Organizational
            ("EmployeeId", "EmployeeId", true),
            ("JobTitle", "JobTitle", true),
            ("Department", "Department", true),
            ("Division", "Division", true),
            ("Company", "Company", true),
            ("Office", "Office", true),
            ("CostCenter", "CostCenter", true),
            ("EmployeeType", "IdentityType", true),
            ("Description", "Description", true),

            // Technical / Account
            ("Username", "Username", true),
            ("UserPrincipalName", "UserPrincipalName", true),
            ("IsActive", "IsActive", true)
        },

        // STANDARD preset - Common fields, preserve existing unless empty
        ["standard"] = new()
        {
            ("DisplayName", "DisplayName", false),
            ("FirstName", "FirstName", false),
            ("LastName", "LastName", false),
            ("Email", "PrimaryEmail", false),
            ("Phone", "PrimaryPhone", false),
            ("MobilePhone", "MobilePhone", false),
            ("Department", "Department", true),
            ("JobTitle", "JobTitle", true),
            ("Company", "Company", false),
            ("CostCenter", "CostCenter", false),
            ("Office", "Office", true),
            ("EmployeeId", "EmployeeId", false),
            ("Username", "Username", false),
            ("UserPrincipalName", "UserPrincipalName", false)
        },

        // HR preset - HR system as authoritative source
        ["hr"] = new()
        {
            ("DisplayName", "DisplayName", true),
            ("FirstName", "FirstName", true),
            ("LastName", "LastName", true),
            ("MiddleName", "MiddleName", true),
            ("Email", "PrimaryEmail", false),  // HR may not have email
            ("Phone", "PrimaryPhone", true),
            ("MobilePhone", "MobilePhone", true),
            ("EmployeeId", "EmployeeId", true),
            ("JobTitle", "JobTitle", true),
            ("Department", "Department", true),
            ("Division", "Division", true),
            ("Company", "Company", true),
            ("Office", "Office", true),
            ("CostCenter", "CostCenter", true),
            ("EmployeeType", "IdentityType", true),
            ("StreetAddress", "StreetAddress", true),
            ("City", "City", true),
            ("State", "State", true),
            ("PostalCode", "PostalCode", true),
            ("Country", "Country", true)
        },

        // MINIMAL preset - Just email and display name
        ["minimal"] = new()
        {
            ("Email", "PrimaryEmail", false),
            ("DisplayName", "DisplayName", false)
        },

        // NAMES-ONLY preset - Name fields only
        ["names-only"] = new()
        {
            ("DisplayName", "DisplayName", false),
            ("FirstName", "FirstName", false),
            ("LastName", "LastName", false),
            ("MiddleName", "MiddleName", false)
        },

        // CONTACT-INFO preset - All contact fields
        ["contact-info"] = new()
        {
            ("Email", "PrimaryEmail", true),
            ("Phone", "PrimaryPhone", true),
            ("MobilePhone", "MobilePhone", true),
            ("HomePhone", "HomePhone", true),
            ("Fax", "Fax", true),
            ("StreetAddress", "StreetAddress", true),
            ("City", "City", true),
            ("State", "State", true),
            ("PostalCode", "PostalCode", true),
            ("Country", "Country", true)
        },

        // ORG-INFO preset - Organizational fields
        ["org-info"] = new()
        {
            ("EmployeeId", "EmployeeId", true),
            ("JobTitle", "JobTitle", true),
            ("Department", "Department", true),
            ("Division", "Division", true),
            ("Company", "Company", true),
            ("Office", "Office", true),
            ("CostCenter", "CostCenter", true),
            ("EmployeeType", "IdentityType", true)
        },

        // TECHNICAL preset - Account/System fields
        ["technical"] = new()
        {
            ("Username", "Username", true),
            ("UserPrincipalName", "UserPrincipalName", true),
            ("Email", "PrimaryEmail", true)
        }
    };

    // Identity (Person) → Object mappings (for provisioning/writeback)
    private static readonly Dictionary<string, List<(string Source, string Target, bool Overwrite)>> _identityToObjectMappings = new()
    {
        // FULL provisioning - all fields
        ["full"] = new()
        {
            // Core Biographic
            ("DisplayName", "DisplayName", true),
            ("FirstName", "FirstName", true),
            ("LastName", "LastName", true),
            ("MiddleName", "MiddleName", true),

            // Contact Information
            ("PrimaryEmail", "Email", true),
            ("PrimaryPhone", "Phone", true),
            ("MobilePhone", "MobilePhone", true),
            ("HomePhone", "HomePhone", true),
            ("Fax", "Fax", true),

            // Address
            ("StreetAddress", "StreetAddress", true),
            ("City", "City", true),
            ("State", "State", true),
            ("PostalCode", "PostalCode", true),
            ("Country", "Country", true),

            // Organizational
            ("EmployeeId", "EmployeeId", true),
            ("JobTitle", "JobTitle", true),
            ("Department", "Department", true),
            ("Division", "Division", true),
            ("Company", "Company", true),
            ("Office", "Office", true),
            ("IdentityType", "EmployeeType", true),
            ("Description", "Description", true),

            // Technical / Account
            ("Username", "Username", true),
            ("UserPrincipalName", "UserPrincipalName", true),
            ("IsActive", "IsActive", true)
        },

        ["provisioning"] = new()
        {
            ("PrimaryEmail", "Email", true),
            ("PrimaryPhone", "Phone", true),
            ("MobilePhone", "MobilePhone", true),
            ("DisplayName", "DisplayName", true),
            ("FirstName", "FirstName", true),
            ("LastName", "LastName", true),
            ("MiddleName", "MiddleName", true),
            ("Department", "Department", true),
            ("JobTitle", "JobTitle", true),
            ("Company", "Company", true),
            ("Office", "Office", true),
            ("EmployeeId", "EmployeeId", true),
            ("Username", "Username", true),
            ("UserPrincipalName", "UserPrincipalName", true)
        },
        ["writeback"] = new()
        {
            ("PrimaryEmail", "Email", false),
            ("PrimaryPhone", "Phone", false),
            ("MobilePhone", "MobilePhone", false),
            ("DisplayName", "DisplayName", false),
            ("FirstName", "FirstName", false),
            ("LastName", "LastName", false),
            ("Department", "Department", false),
            ("JobTitle", "JobTitle", false),
            ("Company", "Company", false),
            ("Office", "Office", false)
        },
        ["minimal"] = new()
        {
            ("PrimaryEmail", "Email", true),
            ("DisplayName", "DisplayName", true)
        },
        ["names-only"] = new()
        {
            ("DisplayName", "DisplayName", true),
            ("FirstName", "FirstName", true),
            ("LastName", "LastName", true),
            ("MiddleName", "MiddleName", true)
        },
        ["contact-info"] = new()
        {
            ("PrimaryEmail", "Email", true),
            ("PrimaryPhone", "Phone", true),
            ("MobilePhone", "MobilePhone", true),
            ("HomePhone", "HomePhone", true),
            ("Fax", "Fax", true),
            ("StreetAddress", "StreetAddress", true),
            ("City", "City", true),
            ("State", "State", true),
            ("PostalCode", "PostalCode", true),
            ("Country", "Country", true)
        },
        ["org-info"] = new()
        {
            ("EmployeeId", "EmployeeId", true),
            ("Department", "Department", true),
            ("Division", "Division", true),
            ("JobTitle", "JobTitle", true),
            ("Company", "Company", true),
            ("Office", "Office", true),
            ("CostCenter", "CostCenter", true),
            ("EmployeeType", "IdentityType", true)
        },
        ["technical"] = new()
        {
            ("Username", "Username", true),
            ("UserPrincipalName", "UserPrincipalName", true),
            ("PrimaryEmail", "Email", true)
        }
    };

    // Legacy compatibility - maps old preset names to new structure
    private static readonly Dictionary<string, List<(string Source, string Target, bool Overwrite)>> _presetMappings = new()
    {
        // Object → Identity presets (default direction)
        ["standard"] = _objectToIdentityMappings["standard"],
        ["full"] = _objectToIdentityMappings["full"],
        ["hr"] = _objectToIdentityMappings["hr"],
        ["minimal"] = _objectToIdentityMappings["minimal"],

        // Identity → Object presets
        ["provisioning"] = _identityToObjectMappings["provisioning"],
        ["writeback"] = _identityToObjectMappings["writeback"]
    };

    // ========================================================================
    // FIELD DEFINITIONS - COMPREHENSIVE IDENTITY MANAGEMENT
    // ========================================================================

    /// <summary>
    /// All mappable fields from the Objects (IdentityObject) table
    /// </summary>
    private static readonly List<FieldDefinition> _objectFields = new()
    {
        // Core Biographic
        new() { Name = "DisplayName", DisplayName = "Display Name", DataType = "string", Description = "Full display name" },
        new() { Name = "FirstName", DisplayName = "First Name", DataType = "string", Description = "Given name" },
        new() { Name = "LastName", DisplayName = "Last Name", DataType = "string", Description = "Surname" },
        new() { Name = "MiddleName", DisplayName = "Middle Name", DataType = "string", Description = "Middle name" },

        // Contact Information
        new() { Name = "Email", DisplayName = "Email", DataType = "string", Description = "Email address" },
        new() { Name = "Phone", DisplayName = "Phone", DataType = "string", Description = "Office phone number" },
        new() { Name = "MobilePhone", DisplayName = "Mobile Phone", DataType = "string", Description = "Mobile phone number" },
        new() { Name = "HomePhone", DisplayName = "Home Phone", DataType = "string", Description = "Home phone number" },
        new() { Name = "Fax", DisplayName = "Fax", DataType = "string", Description = "Fax number" },

        // Address
        new() { Name = "StreetAddress", DisplayName = "Street Address", DataType = "string", Description = "Street address" },
        new() { Name = "City", DisplayName = "City", DataType = "string", Description = "City" },
        new() { Name = "State", DisplayName = "State", DataType = "string", Description = "State/Province" },
        new() { Name = "PostalCode", DisplayName = "Postal Code", DataType = "string", Description = "ZIP/Postal code" },
        new() { Name = "Country", DisplayName = "Country", DataType = "string", Description = "Country" },

        // Organizational
        new() { Name = "EmployeeId", DisplayName = "Employee ID", DataType = "string", Description = "HR employee identifier" },
        new() { Name = "JobTitle", DisplayName = "Job Title", DataType = "string", Description = "Job title/position" },
        new() { Name = "Department", DisplayName = "Department", DataType = "string", Description = "Department name" },
        new() { Name = "Division", DisplayName = "Division", DataType = "string", Description = "Division" },
        new() { Name = "Company", DisplayName = "Company", DataType = "string", Description = "Company/Organization" },
        new() { Name = "Office", DisplayName = "Office", DataType = "string", Description = "Physical office location" },
        new() { Name = "IdentityType", DisplayName = "Identity Type", DataType = "string", Description = "Employee, Contractor, Vendor, Bot, etc." },
        new() { Name = "Description", DisplayName = "Description", DataType = "string", Description = "Account description" },

        // Technical
        new() { Name = "Username", DisplayName = "Username", DataType = "string", Description = "sAMAccountName (e.g. jsmith)" },
        new() { Name = "UserPrincipalName", DisplayName = "UPN", DataType = "string", Description = "User Principal Name (e.g. user@domain.local)" },
        new() { Name = "DN", DisplayName = "Distinguished Name", DataType = "string", Description = "Full LDAP path" },
        new() { Name = "CN", DisplayName = "Common Name", DataType = "string", Description = "Short name from DN" },
        new() { Name = "ObjectClass", DisplayName = "Object Class", DataType = "string", Description = "AD object class" },
        new() { Name = "SourceUniqueId", DisplayName = "Source Unique ID", DataType = "string", Description = "ObjectGuid from source" },
        new() { Name = "SourceType", DisplayName = "Source Type", DataType = "string", Description = "Type of source system" },
        new() { Name = "ManagerSourceId", DisplayName = "Manager DN", DataType = "string", Description = "Manager's distinguished name" },

        // Status
        new() { Name = "IsActive", DisplayName = "Is Active", DataType = "bool", Description = "Account enabled status" },
        new() { Name = "IsBuiltIn", DisplayName = "Is Built-In", DataType = "bool", Description = "Built-in AD account" },
        new() { Name = "IsAdminSDHolder", DisplayName = "AdminSDHolder", DataType = "bool", Description = "Protected admin account" }
    };

    /// <summary>
    /// All mappable fields from the Identities (Person) table - COMPREHENSIVE
    /// </summary>
    private static readonly List<FieldDefinition> _identityFields = new()
    {
        // Core Biographic
        new() { Name = "DisplayName", DisplayName = "Display Name", DataType = "string", Description = "Full display name", IsRequired = true },
        new() { Name = "FirstName", DisplayName = "First Name", DataType = "string", Description = "Given name" },
        new() { Name = "LastName", DisplayName = "Last Name", DataType = "string", Description = "Surname" },
        new() { Name = "MiddleName", DisplayName = "Middle Name", DataType = "string", Description = "Middle name" },
        new() { Name = "Suffix", DisplayName = "Suffix", DataType = "string", Description = "Jr., Sr., III, PhD" },
        new() { Name = "Salutation", DisplayName = "Salutation", DataType = "string", Description = "Mr., Mrs., Dr." },
        new() { Name = "PreferredName", DisplayName = "Preferred Name", DataType = "string", Description = "Nickname" },
        new() { Name = "DateOfBirth", DisplayName = "Date of Birth", DataType = "datetime", Description = "Birth date" },
        new() { Name = "Gender", DisplayName = "Gender", DataType = "string", Description = "Gender identity" },

        // Contact Information
        new() { Name = "PrimaryEmail", DisplayName = "Primary Email", DataType = "string", Description = "Primary email address" },
        new() { Name = "SecondaryEmail", DisplayName = "Secondary Email", DataType = "string", Description = "Personal email" },
        new() { Name = "PrimaryPhone", DisplayName = "Primary Phone", DataType = "string", Description = "Office phone" },
        new() { Name = "MobilePhone", DisplayName = "Mobile Phone", DataType = "string", Description = "Mobile phone" },
        new() { Name = "HomePhone", DisplayName = "Home Phone", DataType = "string", Description = "Home phone" },
        new() { Name = "Fax", DisplayName = "Fax", DataType = "string", Description = "Fax number" },

        // Address
        new() { Name = "StreetAddress", DisplayName = "Street Address", DataType = "string", Description = "Street address" },
        new() { Name = "City", DisplayName = "City", DataType = "string", Description = "City" },
        new() { Name = "State", DisplayName = "State", DataType = "string", Description = "State/Province" },
        new() { Name = "PostalCode", DisplayName = "Postal Code", DataType = "string", Description = "ZIP/Postal code" },
        new() { Name = "Country", DisplayName = "Country", DataType = "string", Description = "Country" },

        // Organizational
        new() { Name = "EmployeeId", DisplayName = "Employee ID", DataType = "string", Description = "HR employee identifier" },
        new() { Name = "JobTitle", DisplayName = "Job Title", DataType = "string", Description = "Job title/position" },
        new() { Name = "Department", DisplayName = "Department", DataType = "string", Description = "Department name" },
        new() { Name = "Division", DisplayName = "Division", DataType = "string", Description = "Division" },
        new() { Name = "Company", DisplayName = "Company", DataType = "string", Description = "Company/Organization" },
        new() { Name = "Office", DisplayName = "Office", DataType = "string", Description = "Physical office location" },
        new() { Name = "Building", DisplayName = "Building", DataType = "string", Description = "Building name/number" },
        new() { Name = "Floor", DisplayName = "Floor", DataType = "string", Description = "Floor number" },
        new() { Name = "Room", DisplayName = "Room", DataType = "string", Description = "Room number" },
        new() { Name = "CostCenter", DisplayName = "Cost Center", DataType = "string", Description = "Financial cost center" },
        new() { Name = "IdentityType", DisplayName = "Identity Type", DataType = "string", Description = "Employee, Contractor, Vendor, Bot, etc." },
        new() { Name = "ContractType", DisplayName = "Contract Type", DataType = "string", Description = "Permanent, Temporary, etc." },
        new() { Name = "HireDate", DisplayName = "Hire Date", DataType = "datetime", Description = "Employment start date" },
        new() { Name = "TerminationDate", DisplayName = "Termination Date", DataType = "datetime", Description = "Employment end date" },
        new() { Name = "Description", DisplayName = "Description", DataType = "string", Description = "Bio or notes" },

        // Technical
        new() { Name = "Username", DisplayName = "Username (sAMAccountName)", DataType = "string", Description = "sAMAccountName (e.g. jsmith)" },
        new() { Name = "UserPrincipalName", DisplayName = "UPN", DataType = "string", Description = "User Principal Name (e.g. user@domain.local)" },
        new() { Name = "Status", DisplayName = "Status", DataType = "string", Description = "Active, Inactive, Suspended, etc." },
        new() { Name = "SecurityClearance", DisplayName = "Security Clearance", DataType = "string", Description = "Security level" },
        new() { Name = "RiskScore", DisplayName = "Risk Score", DataType = "int", Description = "Calculated risk (0-100)" },
        new() { Name = "RiskLevel", DisplayName = "Risk Level", DataType = "string", Description = "Low, Medium, High, Critical" },
        new() { Name = "IsActive", DisplayName = "Is Active", DataType = "bool", Description = "Identity active status" },

        // Localization
        new() { Name = "PreferredLanguage", DisplayName = "Preferred Language", DataType = "string", Description = "ISO language code" },
        new() { Name = "TimeZone", DisplayName = "Time Zone", DataType = "string", Description = "IANA timezone" },
        new() { Name = "Locale", DisplayName = "Locale", DataType = "string", Description = "Locale code" }
    };

    public InternalSyncTemplateService(
        IConfiguration configuration,
        ILogger<InternalSyncTemplateService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    public List<InternalSyncTemplate> GetAvailableTemplates()
    {
        return _builtInTemplates.ToList();
    }

    public InternalSyncTemplate? GetTemplate(string templateId)
    {
        return _builtInTemplates.FirstOrDefault(t => t.Id == templateId);
    }

    public List<InternalSyncStepMapping> GetPresetMappings(string presetName, Guid stepId)
    {
        if (!_presetMappings.TryGetValue(presetName, out var mappings))
        {
            _logger.LogWarning("Preset mappings '{PresetName}' not found", presetName);
            return new List<InternalSyncStepMapping>();
        }

        return mappings.Select((m, index) => new InternalSyncStepMapping
        {
            Id = Guid.NewGuid(),
            InternalSyncStepId = stepId,
            SourceField = m.Source,
            TargetField = m.Target,
            OverwriteExisting = m.Overwrite,
            MappingOrder = index,
            IsEnabled = true
        }).ToList();
    }

    /// <summary>
    /// Get available source fields for a sync direction.
    /// ObjectToPerson: source = Object fields
    /// PersonToObject: source = Identity fields
    /// </summary>
    public List<FieldDefinition> GetSourceFields(string direction)
    {
        return direction?.ToLowerInvariant() switch
        {
            "objecttoperson" or "objecttoidentity" => _objectFields.ToList(),
            "persontoobject" or "identitytoobject" => _identityFields.ToList(),
            _ => _objectFields.ToList() // Default to Object→Identity
        };
    }

    /// <summary>
    /// Get available target fields for a sync direction.
    /// ObjectToPerson: target = Identity fields
    /// PersonToObject: target = Object fields
    /// </summary>
    public List<FieldDefinition> GetTargetFields(string direction)
    {
        return direction?.ToLowerInvariant() switch
        {
            "objecttoperson" or "objecttoidentity" => _identityFields.ToList(),
            "persontoobject" or "identitytoobject" => _objectFields.ToList(),
            _ => _identityFields.ToList() // Default to Object→Identity
        };
    }

    /// <summary>
    /// Get available preset names for a sync direction.
    /// </summary>
    public List<string> GetPresetNames(string direction)
    {
        return direction?.ToLowerInvariant() switch
        {
            "objecttoperson" or "objecttoidentity" => _objectToIdentityMappings.Keys.ToList(),
            "persontoobject" or "identitytoobject" => _identityToObjectMappings.Keys.ToList(),
            _ => _objectToIdentityMappings.Keys.ToList() // Default
        };
    }

    /// <summary>
    /// Get preset mappings for a specific direction.
    /// </summary>
    public List<InternalSyncStepMapping> GetPresetMappingsForDirection(string presetName, string direction, Guid stepId)
    {
        var mappingDict = direction?.ToLowerInvariant() switch
        {
            "objecttoperson" or "objecttoidentity" => _objectToIdentityMappings,
            "persontoobject" or "identitytoobject" => _identityToObjectMappings,
            _ => _objectToIdentityMappings // Default
        };

        if (!mappingDict.TryGetValue(presetName, out var mappings))
        {
            _logger.LogWarning("Preset mappings '{PresetName}' not found for direction '{Direction}'", presetName, direction);
            return new List<InternalSyncStepMapping>();
        }

        return mappings.Select((m, index) => new InternalSyncStepMapping
        {
            Id = Guid.NewGuid(),
            InternalSyncStepId = stepId,
            SourceField = m.Source,
            TargetField = m.Target,
            OverwriteExisting = m.Overwrite,
            MappingOrder = index,
            IsEnabled = true
        }).ToList();
    }

    /// <inheritdoc />
    public List<InternalSyncStepMapping> AutoCalculateMappings(string direction, Guid stepId)
    {
        var sourceFields = GetSourceFields(direction);
        var targetFields = GetTargetFields(direction);

        // Build lookup of target field names (case-insensitive)
        var targetByName = targetFields.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

        // Well-known equivalences: source name → target name
        var equivalences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Email"] = "PrimaryEmail",
            ["Phone"] = "PrimaryPhone",
            ["PrimaryEmail"] = "Email",
            ["PrimaryPhone"] = "Phone",
            ["EmployeeType"] = "IdentityType",
            ["IdentityType"] = "EmployeeType",
        };

        var mappings = new List<InternalSyncStepMapping>();
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int order = 0;

        foreach (var source in sourceFields)
        {
            string? matchedTarget = null;

            // 1. Exact name match
            if (targetByName.ContainsKey(source.Name) && !usedTargets.Contains(source.Name))
            {
                matchedTarget = source.Name;
            }
            // 2. Well-known equivalence
            else if (equivalences.TryGetValue(source.Name, out var equiv) &&
                     targetByName.ContainsKey(equiv) && !usedTargets.Contains(equiv))
            {
                matchedTarget = equiv;
            }

            if (matchedTarget != null)
            {
                usedTargets.Add(matchedTarget);
                mappings.Add(new InternalSyncStepMapping
                {
                    Id = Guid.NewGuid(),
                    InternalSyncStepId = stepId,
                    SourceField = source.Name,
                    TargetField = matchedTarget,
                    OverwriteExisting = true,
                    MappingOrder = order++,
                    IsEnabled = true
                });
            }
        }

        return mappings;
    }

    public async Task<SyncProject> ApplyTemplateAsync(
        string templateId,
        string projectName,
        string? description = null,
        Guid? sourceConnectionId = null,
        CancellationToken cancellationToken = default)
    {
        var template = GetTemplate(templateId);
        if (template == null)
        {
            throw new ArgumentException($"Template '{templateId}' not found", nameof(templateId));
        }

        _logger.LogInformation("Applying template '{TemplateId}' to create project '{ProjectName}'",
            templateId, projectName);

        // Create the project
        var project = new SyncProject
        {
            Id = Guid.NewGuid(),
            Name = projectName,
            Description = description ?? template.Description,
            ProjectType = template.Direction == "ObjectToPerson" ? "InternalSync" : "Provisioning",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            // Store template info in project for reference
            IdentityMatchingStrategy = template.Id
        };

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            // Insert project
            await connection.ExecuteAsync(@"
                INSERT INTO SyncProjects (Id, Name, Description, ProjectType, IsEnabled, CreatedAt, ModifiedAt, IdentityMatchingStrategy)
                VALUES (@Id, @Name, @Description, @ProjectType, @IsEnabled, @CreatedAt, @ModifiedAt, @IdentityMatchingStrategy)",
                project, transaction);

            // Create steps from template
            foreach (var stepTemplate in template.Steps)
            {
                var step = new InternalSyncStep
                {
                    Id = Guid.NewGuid(),
                    SyncProjectId = project.Id,
                    Name = stepTemplate.Name,
                    Description = stepTemplate.Description,
                    ExecutionOrder = stepTemplate.Order,
                    Direction = template.Direction,
                    StepType = stepTemplate.StepType,
                    ObjectClassFilter = "user",
                    IsEnabled = true,
                    ContinueOnError = stepTemplate.ContinueOnError,
                    Configuration = stepTemplate.Configuration,
                    SourceConnectionId = sourceConnectionId,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                };

                await connection.ExecuteAsync(@"
                    INSERT INTO InternalSyncSteps (Id, SyncProjectId, Name, Description, ExecutionOrder, Direction,
                        StepType, ObjectClassFilter, IsEnabled, ContinueOnError, Configuration, SourceConnectionId, CreatedAt, ModifiedAt)
                    VALUES (@Id, @SyncProjectId, @Name, @Description, @ExecutionOrder, @Direction,
                        @StepType, @ObjectClassFilter, @IsEnabled, @ContinueOnError, @Configuration, @SourceConnectionId, @CreatedAt, @ModifiedAt)",
                    step, transaction);

                // Add preset mappings if specified
                if (!string.IsNullOrEmpty(stepTemplate.MappingsPreset))
                {
                    var mappings = GetPresetMappings(stepTemplate.MappingsPreset, step.Id);
                    foreach (var mapping in mappings)
                    {
                        await connection.ExecuteAsync(@"
                            INSERT INTO InternalSyncStepMappings (Id, InternalSyncStepId, SourceField, TargetField,
                                TransformationType, TransformationConfig, IsEnabled, IsRequired, DefaultValue, ExecutionOrder)
                            VALUES (@Id, @InternalSyncStepId, @SourceField, @TargetField,
                                @TransformationType, @TransformationConfig, @IsEnabled, @IsRequired, @DefaultValue, @ExecutionOrder)",
                            mapping, transaction);
                    }
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        _logger.LogInformation("Created project '{ProjectName}' with {StepCount} steps from template '{TemplateId}'",
            projectName, template.Steps.Count, templateId);

        return project;
    }
}
