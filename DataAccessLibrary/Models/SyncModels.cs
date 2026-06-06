using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace DataAccessLibrary.Models
{
    // ==========================================
    // CORE IDENTITY MODELS (REFACTORED)
    // Identity (formerly Person) = Real person who may have multiple objects
    // IdentityObject (formerly Identity) = Individual account from source systems
    // ==========================================

    /// <summary>
    /// Represents a real person/identity who may have multiple objects (accounts) across different systems.
    /// Comprehensive identity model following industry standards (One Identity, SailPoint, etc.)
    /// </summary>
    public class Identity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Auto-generated unique identifier for this identity (e.g., "IC-00001").
        /// Used as the template key when objects are created from this identity.
        /// </summary>
        [MaxLength(50)]
        public string? CentralId { get; set; }

        // ============================================================
        // CORE BIOGRAPHIC & PERSONAL DATA
        // ============================================================

        [Required]
        [MaxLength(500)]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? MiddleName { get; set; }

        [MaxLength(50)]
        public string? Suffix { get; set; }  // Jr., Sr., III, PhD, etc.

        [MaxLength(50)]
        public string? Salutation { get; set; }  // Mr., Mrs., Dr., etc.

        [MaxLength(500)]
        public string? PreferredName { get; set; }  // Nickname or preferred display

        public DateTime? DateOfBirth { get; set; }

        [MaxLength(50)]
        public string? Gender { get; set; }  // Male, Female, Non-Binary, Prefer Not to Say

        [MaxLength(500)]
        public string? NationalId { get; set; }  // SSN/National ID (should be encrypted at rest)

        [MaxLength(2000)]
        public string? PhotoUrl { get; set; }  // URL to profile photo

        // ============================================================
        // CONTACT INFORMATION
        // ============================================================

        [MaxLength(500)]
        public string? PrimaryEmail { get; set; }

        [MaxLength(500)]
        public string? SecondaryEmail { get; set; }  // Personal email

        [MaxLength(50)]
        public string? PrimaryPhone { get; set; }  // Office phone

        [MaxLength(50)]
        public string? MobilePhone { get; set; }

        [MaxLength(50)]
        public string? HomePhone { get; set; }

        [MaxLength(50)]
        public string? Fax { get; set; }

        // Address fields
        [MaxLength(500)]
        public string? StreetAddress { get; set; }

        [MaxLength(200)]
        public string? City { get; set; }

        [MaxLength(200)]
        public string? State { get; set; }

        [MaxLength(50)]
        public string? PostalCode { get; set; }

        [MaxLength(200)]
        public string? Country { get; set; }

        // ============================================================
        // ORGANIZATIONAL & JOB ATTRIBUTES
        // ============================================================

        [MaxLength(100)]
        public string? EmployeeId { get; set; }  // HR identifier / Personnel number

        [MaxLength(500)]
        public string? JobTitle { get; set; }

        [MaxLength(500)]
        public string? Department { get; set; }

        [MaxLength(500)]
        public string? Division { get; set; }

        [MaxLength(500)]
        public string? Company { get; set; }  // Organization name

        [MaxLength(200)]
        public string? Office { get; set; }  // Physical office location

        [MaxLength(200)]
        public string? Building { get; set; }

        [MaxLength(50)]
        public string? Floor { get; set; }

        [MaxLength(50)]
        public string? Room { get; set; }

        [MaxLength(100)]
        public string? CostCenter { get; set; }  // Financial tracking

        [MaxLength(100)]
        public string? ProfitCenter { get; set; }

        [MaxLength(100)]
        public string? IdentityType { get; set; }  // Employee, Contractor, Vendor, Service Account, Bot, etc.

        [MaxLength(100)]
        public string? EmployeeType { get; set; }  // Full-Time, Part-Time, Intern, Temporary, Seasonal

        [MaxLength(100)]
        public string? ContractType { get; set; }  // Permanent, Temporary, Fixed-term

        [MaxLength(100)]
        public string? JobCode { get; set; }  // HR job classification code

        [MaxLength(200)]
        public string? JobFamily { get; set; }  // Job family / category grouping

        [MaxLength(200)]
        public string? PayGrade { get; set; }  // Compensation grade / level

        [MaxLength(500)]
        public string? Organization { get; set; }  // Top-level organization unit

        [MaxLength(500)]
        public string? BusinessUnit { get; set; }  // Business unit within organization

        [MaxLength(500)]
        public string? LegalEntity { get; set; }  // Legal employing entity

        [MaxLength(200)]
        public string? Region { get; set; }  // Geographic region (e.g., EMEA, APAC, NA)

        [MaxLength(200)]
        public string? Site { get; set; }  // Physical site / campus name

        [MaxLength(100)]
        public string? WorkSchedule { get; set; }  // e.g., "9-5 M-F", "Shift A", "Flex"

        public DateTime? HireDate { get; set; }

        public DateTime? TerminationDate { get; set; }

        public DateTime? LastWorkDay { get; set; }

        public DateTime? StartDate { get; set; }  // Position / assignment start

        public DateTime? EndDate { get; set; }  // Contract / assignment end

        [MaxLength(2000)]
        public string? Description { get; set; }  // Bio or notes

        [MaxLength(4000)]
        public string? Notes { get; set; }  // Free-text admin notes

        // ============================================================
        // MANAGER & SPONSOR
        // ============================================================

        /// <summary>
        /// Manager reference (person-level manager relationship)
        /// </summary>
        public Guid? ManagerIdentityId { get; set; }

        /// <summary>
        /// Staging field: Manager's EmployeeId from HR source.
        /// Resolved to ManagerIdentityId by the IdentityManagerLookup step.
        /// </summary>
        [MaxLength(100)]
        public string? ManagerEmployeeId { get; set; }

        [MaxLength(500)]
        public string? ManagerDisplayName { get; set; }  // Denormalized for display

        [MaxLength(500)]
        public string? Sponsor { get; set; }  // Sponsor name (for contractors / vendors)

        [MaxLength(500)]
        public string? SponsorEmail { get; set; }  // Sponsor email

        // ============================================================
        // CONTRACTOR / VENDOR
        // ============================================================

        [MaxLength(500)]
        public string? VendorName { get; set; }  // Vendor / staffing agency

        [MaxLength(100)]
        public string? PONumber { get; set; }  // Purchase order number

        // ============================================================
        // PHYSICAL ACCESS & BADGE
        // ============================================================

        [MaxLength(100)]
        public string? BadgeNumber { get; set; }  // Physical access badge ID

        // ============================================================
        // TECHNICAL & SECURITY ATTRIBUTES
        // ============================================================

        [MaxLength(200)]
        public string? Username { get; set; }  // Primary sAMAccountName

        [MaxLength(500)]
        public string? UserPrincipalName { get; set; }  // Primary UPN

        /// <summary>
        /// Account lifecycle status: Active, Inactive, Suspended, Locked, Terminated, Pending
        /// </summary>
        [MaxLength(50)]
        public string? Status { get; set; } = "Active";

        public bool IsActive { get; set; } = true;

        [MaxLength(50)]
        public string? SecurityClearance { get; set; }  // None, Confidential, Secret, Top Secret

        /// <summary>
        /// Risk score (0-100) calculated from access patterns, violations, etc.
        /// </summary>
        public int? RiskScore { get; set; }

        [MaxLength(50)]
        public string? RiskLevel { get; set; }  // Low, Medium, High, Critical

        /// <summary>
        /// Identifier of the most authoritative object source for this identity
        /// </summary>
        public Guid? AuthoritativeSourceId { get; set; }

        // ============================================================
        // LOCALIZATION & PREFERENCES
        // ============================================================

        [MaxLength(20)]
        public string? PreferredLanguage { get; set; }  // ISO language code (en-US, fr-FR, etc.)

        [MaxLength(100)]
        public string? TimeZone { get; set; }  // IANA timezone (America/New_York, etc.)

        [MaxLength(10)]
        public string? Locale { get; set; }  // Locale code for formatting

        // ============================================================
        // AUDIT & LIFECYCLE
        // ============================================================

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }

        public DateTime? LastSeenAt { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public DateTime? PasswordLastChangedAt { get; set; }

        public DateTime? LastAccessReviewAt { get; set; }

        [MaxLength(200)]
        public string? CreatedBy { get; set; }

        [MaxLength(200)]
        public string? ModifiedBy { get; set; }

        // ============================================================
        // CUSTOM ATTRIBUTE COLUMNS (user-defined, mappable, searchable)
        // ============================================================

        [MaxLength(1000)]
        public string? CustomAttribute1 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute2 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute3 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute4 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute5 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute6 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute7 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute8 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute9 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute10 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute11 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute12 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute13 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute14 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute15 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute16 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute17 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute18 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute19 { get; set; }
        [MaxLength(1000)]
        public string? CustomAttribute20 { get; set; }

        // ============================================================
        // CUSTOM / EXTENSION FIELDS (JSON)
        // ============================================================

        /// <summary>
        /// JSON blob for custom attributes beyond the 20 dedicated columns
        /// </summary>
        public string? CustomAttributes { get; set; }

        // ============================================================
        // NAVIGATION PROPERTIES
        // ============================================================

        [ForeignKey(nameof(ManagerIdentityId))]
        public virtual Identity? Manager { get; set; }

        public virtual ICollection<Identity> DirectReports { get; set; } = new List<Identity>();

        public virtual ICollection<IdentityObject> Objects { get; set; } = new List<IdentityObject>();

        public virtual ICollection<IdentityGroupMembership> GroupMemberships { get; set; } = new List<IdentityGroupMembership>();

        public virtual ICollection<IdentityTag> Tags { get; set; } = new List<IdentityTag>();
    }

    /// <summary>
    /// Represents an individual object (account) from a specific source system (AD, Azure AD, Okta, etc.)
    /// RENAMED FROM: Identity → IdentityObject
    /// </summary>
    public class IdentityObject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the Identity (person) this object belongs to
        /// NULL for built-in accounts that should not have Identity records
        /// </summary>
        public Guid? IdentityId { get; set; }

        /// <summary>
        /// Reference to the source connection this object came from
        /// </summary>
        [Required]
        public Guid SourceConnectionId { get; set; }

        /// <summary>
        /// The unique identifier from the source system (objectGuid, immutableId, etc.)
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string SourceUniqueId { get; set; } = string.Empty;

        /// <summary>
        /// The type of source system (ActiveDirectory, AzureAD, Okta, Google, etc.)
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string SourceType { get; set; } = string.Empty;

        /// <summary>
        /// Upstream source the row originated from before it was projected through
        /// an intermediate sync engine (e.g. Conduit). When non-null and different
        /// from SourceType, the row was synced TO IdentityCenter VIA an intermediary;
        /// auditors need both legs of the chain. NULL for native syncs.
        /// </summary>
        [MaxLength(100)]
        public string? OriginalSource { get; set; }

        /// <summary>
        /// Object class type: User, Computer, Group, Contact, OrganizationalUnit, etc.
        /// Determines how the object is displayed and categorized in the UI
        /// </summary>
        [MaxLength(100)]
        public string? ObjectClass { get; set; }

        /// <summary>
        /// Display name from this source
        /// </summary>
        [MaxLength(500)]
        public string? DisplayName { get; set; }

        /// <summary>
        /// Email from this source
        /// </summary>
        [MaxLength(500)]
        public string? Email { get; set; }

        /// <summary>
        /// Username — stores sAMAccountName from this source
        /// </summary>
        [MaxLength(500)]
        public string? Username { get; set; }

        /// <summary>
        /// First name from this source
        /// </summary>
        [MaxLength(500)]
        public string? FirstName { get; set; }

        /// <summary>
        /// Last name from this source
        /// </summary>
        [MaxLength(500)]
        public string? LastName { get; set; }

        /// <summary>
        /// Department from this source
        /// </summary>
        [MaxLength(500)]
        public string? Department { get; set; }

        /// <summary>
        /// Job title from this source
        /// </summary>
        [MaxLength(500)]
        public string? JobTitle { get; set; }

        /// <summary>
        /// Middle name from this source
        /// </summary>
        [MaxLength(200)]
        public string? MiddleName { get; set; }

        /// <summary>
        /// Phone number from this source (office/primary)
        /// </summary>
        [MaxLength(50)]
        public string? Phone { get; set; }

        /// <summary>
        /// Mobile phone number
        /// </summary>
        [MaxLength(50)]
        public string? MobilePhone { get; set; }

        /// <summary>
        /// Home phone number
        /// </summary>
        [MaxLength(50)]
        public string? HomePhone { get; set; }

        /// <summary>
        /// Fax number
        /// </summary>
        [MaxLength(50)]
        public string? Fax { get; set; }

        /// <summary>
        /// Street address
        /// </summary>
        [MaxLength(500)]
        public string? StreetAddress { get; set; }

        /// <summary>
        /// City
        /// </summary>
        [MaxLength(200)]
        public string? City { get; set; }

        /// <summary>
        /// State or province
        /// </summary>
        [MaxLength(200)]
        public string? State { get; set; }

        /// <summary>
        /// Postal/ZIP code
        /// </summary>
        [MaxLength(50)]
        public string? PostalCode { get; set; }

        /// <summary>
        /// Country
        /// </summary>
        [MaxLength(200)]
        public string? Country { get; set; }

        /// <summary>
        /// Company/Organization name
        /// </summary>
        [MaxLength(500)]
        public string? Company { get; set; }

        /// <summary>
        /// Division within the organization
        /// </summary>
        [MaxLength(500)]
        public string? Division { get; set; }

        /// <summary>
        /// Physical office location
        /// </summary>
        [MaxLength(200)]
        public string? Office { get; set; }

        /// <summary>
        /// Employee ID / Personnel number
        /// </summary>
        [MaxLength(100)]
        public string? EmployeeId { get; set; }

        /// <summary>
        /// Employee type (Full-time, Contractor, etc.)
        /// </summary>
        [MaxLength(100)]
        public string? EmployeeType { get; set; }

        /// <summary>
        /// Cost center for financial tracking and chargebacks
        /// </summary>
        [MaxLength(100)]
        public string? CostCenter { get; set; }

        /// <summary>
        /// User Principal Name (UPN) - separate from sAMAccountName
        /// </summary>
        [MaxLength(500)]
        public string? UserPrincipalName { get; set; }

        /// <summary>
        /// Description/notes about the object
        /// </summary>
        [MaxLength(2000)]
        public string? Description { get; set; }

        /// <summary>
        /// Distinguished Name (DN) from Active Directory or LDAP
        /// Full path in directory (e.g., CN=User,OU=Users,DC=domain,DC=com)
        /// </summary>
        [MaxLength(2000)]
        public string? DN { get; set; }

        /// <summary>
        /// Common Name (CN) from Active Directory or LDAP
        /// Short name extracted from DN or cn attribute
        /// </summary>
        [MaxLength(500)]
        public string? CN { get; set; }

        /// <summary>
        /// Whether this object is considered high-risk (e.g., admin group, privileged account).
        /// Used for risk-based access review scoping and approval routing.
        /// </summary>
        public bool IsHighRisk { get; set; }

        /// <summary>
        /// Manager's identifier in the source system (DN, objectGuid, etc.)
        /// </summary>
        [MaxLength(500)]
        public string? ManagerSourceId { get; set; }

        /// <summary>
        /// NEW: Reference to manager object (resolved from ManagerSourceId during sync)
        /// Object-level manager relationship within the same source system
        /// </summary>
        public Guid? ManagerObjectId { get; set; }

        /// <summary>
        /// PHASE 2: Resolved manager relationship
        /// This is the actual Object ID of the manager, resolved from ManagerSourceId (DN) in PostSyncTask
        /// Enables manager hierarchy navigation: Object -> Manager Object -> Manager's Person
        /// </summary>
        public Guid? ManagerId { get; set; }

        /// <summary>
        /// Owner object ID for groups (resolved from managedBy attribute)
        /// Used to track who manages/owns a group in Active Directory
        /// </summary>
        public Guid? OwnerObjectId { get; set; }

        /// <summary>
        /// Owner identity ID for groups (resolved from OwnerObjectId or assigned directly)
        /// Links to the Identity record that owns/manages this group
        /// Used for governance and access review workflows
        /// </summary>
        public Guid? OwnerIdentityId { get; set; }

        /// <summary>
        /// Whether this object is currently active in the source system
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Whether this object is considered authoritative for the identity
        /// </summary>
        public bool IsAuthoritative { get; set; } = false;

        /// <summary>
        /// Confidence score of the identity match (0-100)
        /// </summary>
        public int MatchConfidence { get; set; } = 100;

        /// <summary>
        /// The matching method used to link this object to the identity
        /// </summary>
        [MaxLength(100)]
        public string? MatchMethod { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }

        public DateTime FirstSyncedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastSeenAt { get; set; }

        public DateTime? DeletedAt { get; set; }

        /// <summary>
        /// When the password was last set/changed in Active Directory.
        /// Synced from AD pwdLastSet attribute.
        /// Used by PasswordAge policy rule type to evaluate password expiration.
        /// </summary>
        public DateTime? PasswordLastSet { get; set; }

        /// <summary>
        /// Whether this is a built-in Active Directory account (e.g., Administrator, Guest)
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Whether this account is a member of AdminSDHolder (highly privileged AD accounts)
        /// </summary>
        public bool IsAdminSDHolder { get; set; } = false;

        /// <summary>
        /// Whether this account has "Password Never Expires" flag set.
        /// Synced from AD userAccountControl attribute (ADS_UF_DONT_EXPIRE_PASSWD = 0x10000).
        /// Used by security reports and compliance policies.
        /// </summary>
        public bool PasswordNeverExpires { get; set; } = false;

        /// <summary>
        /// Raw userAccountControl value from Active Directory.
        /// Contains bitmask flags for account status, password policies, etc.
        /// See https://docs.microsoft.com/en-us/troubleshoot/windows-server/identity/useraccountcontrol-manipulate-account-properties
        /// </summary>
        public int? UserAccountControl { get; set; }

        /// <summary>
        /// NEW: Navigation property to manager object
        /// </summary>
        [ForeignKey(nameof(ManagerObjectId))]
        public virtual IdentityObject? Manager { get; set; }

        /// <summary>
        /// NEW: Navigation property to direct reports
        /// </summary>
        public virtual ICollection<IdentityObject> DirectReports { get; set; } = new List<IdentityObject>();

        /// <summary>
        /// Navigation property to the identity (person)
        /// </summary>
        [ForeignKey(nameof(IdentityId))]
        public virtual Identity? Identity { get; set; }

        /// <summary>
        /// Navigation property to the owner identity (for groups)
        /// </summary>
        [ForeignKey(nameof(OwnerIdentityId))]
        public virtual Identity? OwnerIdentity { get; set; }

        /// <summary>
        /// Navigation property to the source connection
        /// </summary>
        [ForeignKey(nameof(SourceConnectionId))]
        public virtual DirectoryConnection SourceConnection { get; set; } = null!;

        /// <summary>
        /// Extended attributes specific to this source type
        /// </summary>
        public virtual ICollection<ObjectAttribute> Attributes { get; set; } = new List<ObjectAttribute>();

        /// <summary>
        /// Group memberships for this specific object
        /// </summary>
        public virtual ICollection<ObjectGroupMembership> GroupMemberships { get; set; } = new List<ObjectGroupMembership>();

        /// <summary>
        /// NEW: Tags assigned to this object (Service Account, Privileged, etc.)
        /// </summary>
        public virtual ICollection<ObjectTag> Tags { get; set; } = new List<ObjectTag>();
    }

    /// <summary>
    /// Stores extended attributes for objects (flexible key-value storage for source-specific data)
    /// RENAMED FROM: IdentityAttribute → ObjectAttribute
    /// </summary>
    public class ObjectAttribute
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ObjectId { get; set; }

        /// <summary>
        /// Attribute name (e.g., "employeeId", "costCenter", "extensionAttribute1")
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string AttributeName { get; set; } = string.Empty;

        /// <summary>
        /// Attribute value (stored as string, can be JSON for complex types)
        /// </summary>
        public string? AttributeValue { get; set; }

        /// <summary>
        /// Data type hint (string, int, bool, json, etc.)
        /// </summary>
        [MaxLength(50)]
        public string? DataType { get; set; }

        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(ObjectId))]
        public virtual IdentityObject Object { get; set; } = null!;
    }

    /// <summary>
    /// Represents a group from any source system
    /// NO RENAME - Groups table stays the same
    /// PHASE 1 ENHANCEMENT: Added access review, risk assessment, and compliance fields`n    /// </summary>
    public class Group
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the source connection this group came from
        /// </summary>
        [Required]
        public Guid SourceConnectionId { get; set; }

        /// <summary>
        /// The unique identifier from the source system
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string SourceUniqueId { get; set; } = string.Empty;

        /// <summary>
        /// The type of source system (ActiveDirectory, AzureAD, Okta, Google, etc.)
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string SourceType { get; set; } = string.Empty;

        /// <summary>
        /// Group name from the source system
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Group description from the source system
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Distinguished name or full path in the source system
        /// </summary>
        [MaxLength(2000)]
        public string? DistinguishedName { get; set; }

        /// <summary>
        /// Group type (Security, Distribution, etc.) - source-specific
        /// </summary>
        [MaxLength(100)]
        public string? GroupType { get; set; }

        /// <summary>
        /// Email address for the group (if applicable)
        /// </summary>
        [MaxLength(500)]
        public string? Email { get; set; }

        /// <summary>
        /// Whether the group is mail-enabled
        /// </summary>
        public bool IsMailEnabled { get; set; } = false;

        /// <summary>
        /// The managedBy DN from AD - stores the raw DN of the group owner
        /// This is resolved to OwnerId during the Resolve Group Owners step
        /// </summary>
        [MaxLength(500)]
        public string? ManagedBy { get; set; }

        /// <summary>
        /// PHASE 2: Group owner relationship
        /// This is the actual Object ID of the owner, resolved from managedBy attribute (DN) in PostSyncTask
        /// Enables owner navigation: Group -> Owner Object -> Owner's Person
        /// </summary>
        public Guid? OwnerId { get; set; }

        /// <summary>
        /// Whether this group is currently active in the source system
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime FirstSyncedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastSeenAt { get; set; }

        public DateTime? DeletedAt { get; set; }

        
// ==========================================
        // PHASE 1 ENHANCEMENT: ACCESS REVIEW FIELDS
        // UC-GRP-01: Group Management with Access Review Capabilities
        // ==========================================

        /// <summary>
        /// Date of the last completed access review for this group
        /// NULL if group has never been reviewed
        /// </summary>
        public DateTime? LastReviewDate { get; set; }

        /// <summary>
        /// Next scheduled access review date
        /// Calculated as LastReviewDate + ReviewFrequencyDays
        /// NULL if no review schedule configured
        /// </summary>
        public DateTime? NextReviewDate { get; set; }

        /// <summary>
        /// How often (in days) this group should be reviewed
        /// Default: 90 days (quarterly review)
        /// Range: 30-365 days
        /// </summary>
        public int ReviewFrequencyDays { get; set; } = 90;

        /// <summary>
        /// Whether this group requires periodic access reviews for compliance
        /// Typically TRUE for sensitive or privileged groups
        /// </summary>
        public bool RequiresReview { get; set; } = false;

        /// <summary>
        /// Identity ID of the person responsible for conducting access reviews
        /// Falls back to OwnerId if not specified
        /// NULL if no reviewer assigned
        /// </summary>
        public Guid? ReviewOwnerId { get; set; }

        // ==========================================
        // PHASE 1 ENHANCEMENT: RISK ASSESSMENT FIELDS
        // ML.NET-powered risk scoring for proactive security
        // ==========================================

        /// <summary>
        /// Calculated risk score for this group (0-100)
        /// 0 = No risk, 100 = Critical risk
        /// Calculated by ML.NET risk assessment service based on multiple factors
        /// </summary>
        public decimal RiskScore { get; set; } = 0;

        /// <summary>
        /// Human-readable risk level derived from RiskScore
        /// Values: "Unknown", "Low", "Medium", "High", "Critical"
        /// - Unknown: Not yet assessed
        /// - Low: 0-25
        /// - Medium: 26-50
        /// - High: 51-75
        /// - Critical: 76-100
        /// </summary>
        [MaxLength(20)]
        public string RiskLevel { get; set; } = "Unknown";

        /// <summary>
        /// Timestamp of the last risk assessment calculation
        /// Updated by background job (daily) or on-demand
        /// </summary>
        public DateTime? LastRiskAssessment { get; set; }

        /// <summary>
        /// JSON array of risk factors contributing to the RiskScore
        /// Example: ["NoOwner", "OverdueReview", "HighPrivilegeMembers", "LargeMemberCount"]
        /// Used for detailed risk analysis and remediation guidance
        /// </summary>
        public string? RiskFactors { get; set; }

        // ==========================================
        // PHASE 1 ENHANCEMENT: COMPLIANCE & GOVERNANCE FIELDS
        // Support for SOX, HIPAA, SOC2, GDPR, PCI compliance
        // ==========================================

        /// <summary>
        /// Whether this group contains sensitive data or provides privileged access
        /// Sensitive groups require justification for membership changes
        /// Sensitive groups have higher review frequency
        /// </summary>
        public bool IsSensitive { get; set; } = false;

        /// <summary>
        /// Whether membership changes require justification
        /// Automatically TRUE for sensitive groups
        /// Can be enabled for any group requiring audit trail
        /// </summary>
        public bool RequiresJustification { get; set; } = false;

        /// <summary>
        /// JSON array of compliance framework tags
        /// Example: ["SOX", "HIPAA", "PCI", "GDPR", "SOC2"]
        /// Used for compliance reporting and review prioritization
        /// </summary>
        public string? ComplianceTags { get; set; }

        /// <summary>
        /// Identity ID of the business owner responsible for this group
        /// Used for access review approvals and business decisions
        /// Can be same as OwnerId or different person
        /// </summary>
        public Guid? BusinessOwnerId { get; set; }

        /// <summary>
        /// Identity ID of the technical owner responsible for this group
        /// Used for technical issues, AD management, provisioning
        /// Can be same as OwnerId or different person
        /// </summary>
        public Guid? TechnicalOwnerId { get; set; }

        // ==========================================
        // DOMAIN METHODS (Nexus-style business logic)
        // ==========================================

        /// <summary>
        /// Determines if this group is overdue for access review
        /// Returns TRUE if NextReviewDate is in the past
        /// </summary>
        public bool IsOverdueForReview()
        {
            return NextReviewDate.HasValue && NextReviewDate.Value < DateTime.UtcNow;
        }

        /// <summary>
        /// Calculates risk score based on multiple factors
        /// This is a basic implementation - will be replaced with ML.NET model in Phase 4
        /// </summary>
        public void UpdateRiskScore()
        {
            decimal score = 0.3m; // Base risk

            // Factor: No owner assigned (+20%)
            if (!OwnerId.HasValue && !BusinessOwnerId.HasValue && !TechnicalOwnerId.HasValue)
                score += 0.2m;

            // Factor: Overdue for review (+30%)
            if (IsOverdueForReview())
                score += 0.3m;

            // Factor: Sensitive group (+20%)
            if (IsSensitive)
                score += 0.2m;

            // Factor: Never reviewed (+15%)
            if (!LastReviewDate.HasValue && RequiresReview)
                score += 0.15m;

            // Cap at 1.0 (100%)
            RiskScore = Math.Min(100m, score * 100m);

            // Set risk level
            if (RiskScore == 0)
                RiskLevel = "Unknown";
            else if (RiskScore <= 25)
                RiskLevel = "Low";
            else if (RiskScore <= 50)
                RiskLevel = "Medium";
            else if (RiskScore <= 75)
                RiskLevel = "High";
            else
                RiskLevel = "Critical";

            LastRiskAssessment = DateTime.UtcNow;
        }

        /// <summary>
        /// Calculates the next review date based on current date and review frequency
        /// </summary>
        public void CalculateNextReviewDate()
        {
            if (RequiresReview)
            {
                var baseDate = LastReviewDate ?? DateTime.UtcNow;
                NextReviewDate = baseDate.AddDays(ReviewFrequencyDays);
            }
            else
            {
                NextReviewDate = null;
            }
        }

        /// <summary>
        /// Navigation property to the source connection
        /// </summary>
        [ForeignKey(nameof(SourceConnectionId))]
        public virtual DirectoryConnection SourceConnection { get; set; } = null!;

        /// <summary>
        /// Extended attributes specific to this source type
        /// </summary>
        public virtual ICollection<GroupAttribute> Attributes { get; set; } = new List<GroupAttribute>();

        /// <summary>
        /// Members of this group
        /// </summary>
        public virtual ICollection<ObjectGroupMembership> Members { get; set; } = new List<ObjectGroupMembership>();
    }

    /// <summary>
    /// Stores extended attributes for groups (flexible key-value storage for source-specific data)
    /// NO RENAME
    /// </summary>
    public class GroupAttribute
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid GroupId { get; set; }

        [Required]
        [MaxLength(200)]
        public string AttributeName { get; set; } = string.Empty;

        public string? AttributeValue { get; set; }

        [MaxLength(50)]
        public string? DataType { get; set; }

        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(GroupId))]
        public virtual Group Group { get; set; } = null!;
    }

    /// <summary>
    /// Links objects to groups (source-specific membership)
    /// RENAMED FROM: IdentityGroupMembership → ObjectGroupMembership
    /// </summary>
    public class ObjectGroupMembership
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ObjectId { get; set; }

        [Required]
        public Guid GroupId { get; set; }

        /// <summary>
        /// Whether this is a direct membership or nested/inherited
        /// </summary>
        public bool IsDirect { get; set; } = true;

        /// <summary>
        /// Whether this is the user's PRIMARY group (from AD primaryGroupID attribute).
        /// Primary group membership is NOT stored in memberOf - it must be resolved via SID.
        /// </summary>
        public bool IsPrimary { get; set; } = false;

        /// <summary>
        /// For nested memberships, the path of groups
        /// </summary>
        [MaxLength(2000)]
        public string? MembershipPath { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        public DateTime? RemovedAt { get; set; }
        // ==========================================
        // PHASE 1.5 ENHANCEMENT: ACCESS REVIEW & JUSTIFICATION TRACKING
        // UC-GRP-01-03: Manage Group Members with Justification
        // UC-GRP-01-04: Conduct Access Review
        // ==========================================

        /// <summary>
        /// Whether this membership is currently active
        /// FALSE = soft-deleted (member removed but history retained)
        /// Used for access review workflow and audit trail
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// User who added this member to the group
        /// Captured for audit trail and compliance
        /// </summary>
        [MaxLength(256)]
        public string? AddedBy { get; set; }

        /// <summary>
        /// Justification for adding this member
        /// REQUIRED when Group.RequiresJustification = true
        /// Used for access reviews and compliance reporting
        /// </summary>
        public string? Justification { get; set; }

        /// <summary>
        /// Optional expiration date for temporary access
        /// When set, background job will auto-remove member after this date
        /// NULL = permanent membership
        /// </summary>
        public DateTime? ExpirationDate { get; set; }

        /// <summary>
        /// User who removed this member from the group
        /// Only set when IsActive = false
        /// </summary>
        [MaxLength(256)]
        public string? RemovedBy { get; set; }

        /// <summary>
        /// Reason for removing this member
        /// Captured for audit trail (e.g., "Access no longer needed", "Failed review")
        /// </summary>
        public string? RemovalReason { get; set; }

        [ForeignKey(nameof(ObjectId))]
        public virtual IdentityObject Object { get; set; } = null!;

        [ForeignKey(nameof(GroupId))]
        public virtual Group Group { get; set; } = null!;

        /// <summary>
        /// NEW: Tags for this specific membership (e.g., "Temporary Access", "Requires Approval")
        /// </summary>
        public virtual ICollection<MembershipTag> Tags { get; set; } = new List<MembershipTag>();
    }

    /// <summary>
    /// Aggregated view of identity's group memberships across all objects
    /// RENAMED FROM: PersonGroupMembership → IdentityGroupMembership
    /// </summary>
    [Table("IdentityGroupMemberships")]
    public class IdentityGroupMembership
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid IdentityId { get; set; }

        [Required]
        public Guid GroupId { get; set; }

        /// <summary>
        /// The object that contributed this membership
        /// TODO: Add this column to database in future migration
        /// </summary>
        // public Guid? SourceObjectId { get; set; }

        /// <summary>
        /// Whether this is the user's PRIMARY group (from AD primaryGroupID attribute).
        /// Primary group membership is NOT stored in memberOf - it must be resolved via SID.
        /// </summary>
        public bool IsPrimary { get; set; } = false;

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        public DateTime? RemovedAt { get; set; }
        // ==========================================
        // PHASE 1.5 ENHANCEMENT: ACCESS REVIEW & JUSTIFICATION TRACKING
        // UC-GRP-01-03: Manage Group Members with Justification
        // UC-GRP-01-04: Conduct Access Review
        // ==========================================

        /// <summary>
        /// Whether this membership is currently active
        /// FALSE = soft-deleted (member removed but history retained)
        /// Used for access review workflow and audit trail
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// User who added this member to the group
        /// Captured for audit trail and compliance
        /// </summary>
        [MaxLength(256)]
        public string? AddedBy { get; set; }

        /// <summary>
        /// Justification for adding this member
        /// REQUIRED when Group.RequiresJustification = true
        /// Used for access reviews and compliance reporting
        /// </summary>
        public string? Justification { get; set; }

        /// <summary>
        /// Optional expiration date for temporary access
        /// When set, background job will auto-remove member after this date
        /// NULL = permanent membership
        /// </summary>
        public DateTime? ExpirationDate { get; set; }

        /// <summary>
        /// User who removed this member from the group
        /// Only set when IsActive = false
        /// </summary>
        [MaxLength(256)]
        public string? RemovedBy { get; set; }

        /// <summary>
        /// Reason for removing this member
        /// Captured for audit trail (e.g., "Access no longer needed", "Failed review")
        /// </summary>
        public string? RemovalReason { get; set; }

        [ForeignKey(nameof(IdentityId))]
        public virtual Identity Identity { get; set; } = null!;

        [ForeignKey(nameof(GroupId))]
        public virtual Group Group { get; set; } = null!;

        // TODO: Re-enable after adding SourceObjectId column to database
        // [ForeignKey(nameof(SourceObjectId))]
        // public virtual IdentityObject? SourceObject { get; set; }
    }

    /// <summary>
    /// Tracks sync executions for auditing and monitoring
    /// NO RENAME - SyncExecution stays the same
    /// </summary>
    public class SyncExecution
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid DirectoryConnectionId { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = "Running"; // Running, Completed, Failed, Cancelled

        public int? IdentitiesAdded { get; set; }

        public int? IdentitiesUpdated { get; set; }

        public int? IdentitiesDeleted { get; set; }

        public int? GroupsAdded { get; set; }

        public int? GroupsUpdated { get; set; }

        public int? GroupsDeleted { get; set; }

        public int? MembershipsAdded { get; set; }

        public int? MembershipsRemoved { get; set; }

        public int? PersonsCreated { get; set; }

        public int? PersonsUpdated { get; set; }

        /// <summary>
        /// Error details if sync failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Detailed log of the sync execution
        /// </summary>
        public string? ExecutionLog { get; set; }

        [ForeignKey(nameof(DirectoryConnectionId))]
        public virtual DirectoryConnection DirectoryConnection { get; set; } = null!;
    }

    /// <summary>
    /// Tracks identity matching decisions for auditing and confidence scoring
    /// RENAMED FROM: PersonMatchLog → IdentityMatchLog
    /// </summary>
    public class IdentityMatchLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid IdentityId { get; set; }

        [Required]
        public Guid ObjectId { get; set; }

        public DateTime MatchedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string MatchMethod { get; set; } = string.Empty; // Email, NameDepartment, Manual

        public int MatchConfidence { get; set; } = 100;

        /// <summary>
        /// JSON containing the matching criteria used
        /// </summary>
        public string? MatchCriteria { get; set; }

        /// <summary>
        /// Whether this match was manually confirmed by an admin
        /// </summary>
        public bool IsManualMatch { get; set; } = false;

        [MaxLength(256)]
        public string? MatchedBy { get; set; }

        [ForeignKey(nameof(IdentityId))]
        public virtual Identity Identity { get; set; } = null!;

        [ForeignKey(nameof(ObjectId))]
        public virtual IdentityObject Object { get; set; } = null!;
    }

    // ==========================================
    // NEW TAGGING SYSTEM MODELS
    // Support tagging at Object, Identity, and Membership levels
    // ==========================================

    /// <summary>
    /// NEW: Links tags to objects (Service Account, Privileged, Test Account, etc.)
    /// </summary>
    public class ObjectTag
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ObjectId { get; set; }

        [Required]
        public Guid TagId { get; set; }

        /// <summary>
        /// Whether this tag was inherited from workflow or manually assigned
        /// </summary>
        public bool IsInherited { get; set; } = false;

        /// <summary>
        /// If inherited, which workflow assigned it
        /// </summary>
        public Guid? InheritedFromWorkflowId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        [ForeignKey(nameof(ObjectId))]
        public virtual IdentityObject Object { get; set; } = null!;

        [ForeignKey(nameof(TagId))]
        public virtual Tag Tag { get; set; } = null!;
    }

    /// <summary>
    /// NEW: Links tags to sync steps for auto-assignment during sync.
    /// Many-to-many: A step can assign multiple tags (Employee + Finance + VPN User)
    /// </summary>
    public class SyncStepTag
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid SyncStepId { get; set; }

        [Required]
        public Guid TagId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(SyncStepId))]
        public virtual SyncStep SyncStep { get; set; } = null!;

        [ForeignKey(nameof(TagId))]
        public virtual Tag Tag { get; set; } = null!;
    }

    /// <summary>
    /// NEW: Links tags to identities (Vendor, Contractor, Employee, VIP, etc.)
    /// </summary>
    public class IdentityTag
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid IdentityId { get; set; }

        [Required]
        public Guid TagId { get; set; }

        /// <summary>
        /// Whether this tag was inherited from objects or manually assigned
        /// </summary>
        public bool IsInherited { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        [ForeignKey(nameof(IdentityId))]
        public virtual Identity Identity { get; set; } = null!;

        [ForeignKey(nameof(TagId))]
        public virtual Tag Tag { get; set; } = null!;
    }

    /// <summary>
    /// NEW: Links tags to group memberships (Temporary Access, Pending Removal, etc.)
    /// </summary>
    public class MembershipTag
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid MembershipId { get; set; }

        [Required]
        public Guid TagId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        [ForeignKey(nameof(MembershipId))]
        public virtual ObjectGroupMembership Membership { get; set; } = null!;

        [ForeignKey(nameof(TagId))]
        public virtual Tag Tag { get; set; } = null!;
    }

    // ==========================================
    // SYNC PROJECT MODELS (MOSTLY UNCHANGED)
    // Multi-Step Sync Projects with Workflows
    // ==========================================

    /// <summary>
    /// Represents a multi-step synchronization project that orchestrates complex sync workflows.
    /// UC-SYNC-03: Multi-Step Sync Projects
    /// </summary>
    public class SyncProject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Human-readable project name
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of what this project synchronizes
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Reference to the source directory connection this project uses
        /// NULL for internal sync projects that don't use external connections
        /// </summary>
        public Guid? SourceConnectionId { get; set; }

        /// <summary>
        /// Optional reference to target connection (null = local Certification Center)
        /// </summary>
        public Guid? TargetConnectionId { get; set; }

        /// <summary>
        /// Direction of synchronization: Inbound, Outbound, Bidirectional
        /// </summary>
        [MaxLength(50)]
        public string SyncDirection { get; set; } = "Inbound";

        /// <summary>
        /// Whether this project uses built-in templates or manual configuration.
        /// Defaults to false: a normal, user/UI-created sync project is a real
        /// project, not a template (matches the V045 built-in seed which sets 0
        /// and the DF_SyncProjects_IsTemplateMode default added in V127).
        /// </summary>
        public bool IsTemplateMode { get; set; } = false;

        /// <summary>
        /// Identity matching strategy: Email, EmployeeId, UPN
        /// </summary>
        [MaxLength(50)]
        public string? IdentityMatchingStrategy { get; set; }

        /// <summary>
        /// Cron expression for scheduling (e.g., "0 0 2 * * ?" for 2 AM daily)
        /// </summary>
        [MaxLength(100)]
        public string? CronSchedule { get; set; }

        /// <summary>
        /// Whether this project is enabled for scheduled execution
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Whether this project is currently running
        /// </summary>
        public bool IsRunning { get; set; } = false;

        /// <summary>
        /// ID of the currently running sync run (null if not running).
        /// Not persisted to database - populated at runtime.
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public Guid? CurrentRunId { get; set; }

        /// <summary>
        /// Conflict resolution strategy: SourceWins, TargetWins, Manual, MostRecent
        /// </summary>
        [MaxLength(50)]
        public string ConflictResolutionStrategy { get; set; } = "SourceWins";

        /// <summary>
        /// Whether to automatically create identities for unmatched objects
        /// </summary>
        public bool AutoCreateIdentities { get; set; } = true;

        /// <summary>
        /// Whether to run Identity Manager Assignment post-sync task.
        /// When enabled, copies ManagerObjectId from Objects to ManagerIdentityId on Identities.
        /// Required for "Manager Required" compliance policies to work correctly.
        /// </summary>
        public bool EnableManagerAssignment { get; set; } = true;

        /// <summary>
        /// Whether to run database index optimization before sync execution.
        /// Rebuilds fragmented indexes and updates statistics on sync-related tables.
        /// Recommended for large full sync projects to improve bulk insert performance.
        /// </summary>
        public bool EnablePreSyncIndexing { get; set; } = false;

        // ==========================================
        // SYNC PROJECT TYPES & CHAINING
        // Support for modular sync: ObjectSync, PersonMatch, PersonCreate, HRImport
        // ==========================================

        /// <summary>
        /// Type of sync project that determines behavior:
        /// - ObjectSync: Import from AD/LDAP to Objects table (default, current behavior)
        /// - PersonMatch: Match Objects to existing Identities (no creation)
        /// - PersonCreate: Match Objects to Identities + create new if not found
        /// - HRImport: Import HR data directly to Identity table
        /// </summary>
        [MaxLength(50)]
        public string ProjectType { get; set; } = "ObjectSync";

        /// <summary>
        /// For PersonMatch/PersonCreate projects: which sync project's objects to process.
        /// References another SyncProject that populates the Objects table.
        /// </summary>
        public Guid? SourceSyncProjectId { get; set; }

        /// <summary>
        /// Navigation property to the source sync project (for PersonMatch/PersonCreate)
        /// </summary>
        [ForeignKey(nameof(SourceSyncProjectId))]
        public virtual SyncProject? SourceSyncProject { get; set; }

        /// <summary>
        /// Whether this is a built-in system template (cannot be deleted, like compliance policies).
        /// Built-in templates are seeded at application startup and serve as read-only references.
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Whether this project is read-only (cannot be modified).
        /// Built-in templates are read-only - users must copy them to customize.
        /// </summary>
        public bool IsReadOnly { get; set; } = false;

        /// <summary>
        /// Minimum confidence score required for automatic identity matching (0-100)
        /// </summary>
        public int MinMatchConfidenceThreshold { get; set; } = 75;

        /// <summary>
        /// Whether to pause execution if errors occur
        /// </summary>
        public bool PauseOnError { get; set; } = false;

        /// <summary>
        /// Maximum number of errors before auto-pausing
        /// </summary>
        public int MaxErrorsBeforePause { get; set; } = 100;

        /// <summary>
        /// Priority for execution queue (1-10, higher = more important)
        /// </summary>
        public int Priority { get; set; } = 5;

        /// <summary>
        /// Logging level for this sync project: Error, Warning, Information, Debug, Trace
        /// Controls verbosity of logging output during sync execution
        /// </summary>
        [MaxLength(20)]
        public string LogLevel { get; set; } = "Information";

        /// <summary>
        /// Last successful execution timestamp
        /// </summary>
        public DateTime? LastSuccessfulRunAt { get; set; }

        /// <summary>
        /// Last execution timestamp (successful or failed)
        /// </summary>
        public DateTime? LastRunAt { get; set; }

        /// <summary>
        /// Next scheduled execution timestamp
        /// </summary>
        public DateTime? NextScheduledRunAt { get; set; }

        /// <summary>
        /// Total number of executions (successful + failed)
        /// </summary>
        public int TotalExecutions { get; set; } = 0;

        /// <summary>
        /// Number of successful executions
        /// </summary>
        public int SuccessfulExecutions { get; set; } = 0;

        /// <summary>
        /// Number of failed executions
        /// </summary>
        public int FailedExecutions { get; set; } = 0;

        /// <summary>
        /// Total objects synced across all successful runs (not persisted, calculated at load time)
        /// </summary>
        [NotMapped]
        public int TotalObjectsSynced { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        [MaxLength(256)]
        public string? ModifiedBy { get; set; }

        /// <summary>
        /// Navigation property to the source directory connection
        /// </summary>
        [ForeignKey(nameof(SourceConnectionId))]
        public virtual DirectoryConnection SourceConnection { get; set; } = null!;

        /// <summary>
        /// Navigation property to the target connection (if external target)
        /// </summary>
        [ForeignKey(nameof(TargetConnectionId))]
        public virtual DirectoryConnection? TargetConnection { get; set; }

        /// <summary>
        /// Collection of workflows that belong to this project
        /// </summary>
        public virtual ICollection<SyncWorkflow> Workflows { get; set; } = new List<SyncWorkflow>();

        /// <summary>
        /// Collection of execution runs for this project
        /// </summary>
        public virtual ICollection<SyncProjectRun> Runs { get; set; } = new List<SyncProjectRun>();

        /// <summary>
        /// Collection of chain links where this project is the source (triggers next projects)
        /// </summary>
        public virtual ICollection<SyncProjectChain> OutgoingChains { get; set; } = new List<SyncProjectChain>();

        /// <summary>
        /// Collection of chain links where this project is the target (triggered by other projects)
        /// </summary>
        public virtual ICollection<SyncProjectChain> IncomingChains { get; set; } = new List<SyncProjectChain>();
    }

    /// <summary>
    /// Defines a chain link between sync projects for automatic execution.
    /// Enables multi-project workflows: ProjectA -> ProjectB -> ProjectC
    /// Example: AD Users Sync -> Person Match -> AD Groups Sync
    /// </summary>
    public class SyncProjectChain
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The project that triggers this chain (when it completes)
        /// </summary>
        [Required]
        public Guid SourceProjectId { get; set; }

        /// <summary>
        /// The project to run next (target of the chain)
        /// </summary>
        [Required]
        public Guid TargetProjectId { get; set; }

        /// <summary>
        /// Execution order if multiple chains from same source
        /// Lower numbers execute first
        /// </summary>
        public int ExecutionOrder { get; set; } = 0;

        /// <summary>
        /// When to trigger the target project:
        /// - OnSuccess: Only if source completes successfully
        /// - OnCompletion: Regardless of success/failure
        /// - OnFailure: Only if source fails
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string TriggerCondition { get; set; } = "OnSuccess";

        /// <summary>
        /// Whether this chain link is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Optional delay in seconds before triggering target project
        /// </summary>
        public int DelaySeconds { get; set; } = 0;

        /// <summary>
        /// Optional description of this chain link
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        /// <summary>
        /// Navigation property to the source project
        /// </summary>
        [ForeignKey(nameof(SourceProjectId))]
        public virtual SyncProject SourceProject { get; set; } = null!;

        /// <summary>
        /// Navigation property to the target project
        /// </summary>
        [ForeignKey(nameof(TargetProjectId))]
        public virtual SyncProject TargetProject { get; set; } = null!;
    }

    /// <summary>
    /// Represents a workflow within a sync project (e.g., "User Sync", "Group Sync").
    /// Each workflow contains multiple ordered steps that execute together.
    /// This allows complex workflows like creating users first, then assigning managers.
    /// </summary>
    public class SyncWorkflow
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the parent sync project
        /// </summary>
        [Required]
        public Guid SyncProjectId { get; set; }

        /// <summary>
        /// Workflow name (e.g., "User Full Sync", "Group Delta Sync")
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of this workflow's purpose
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Object class this workflow processes: User, Group, Computer, Contact, OrganizationalUnit, etc.
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string ObjectClass { get; set; } = "User";

        /// <summary>
        /// Type of workflow: FullSync, DeltaSync, AdHoc
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string WorkflowType { get; set; } = "FullSync";

        /// <summary>
        /// Execution order within the project (lower numbers execute first)
        /// </summary>
        public int ExecutionOrder { get; set; }

        /// <summary>
        /// Whether this workflow is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Whether to continue project execution if this workflow fails
        /// </summary>
        public bool ContinueOnError { get; set; } = false;

        /// <summary>
        /// Maximum execution time in minutes (0 = unlimited)
        /// </summary>
        public int MaxExecutionTimeMinutes { get; set; } = 60;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }

        /// <summary>
        /// Navigation property to the parent sync project
        /// </summary>
        [ForeignKey(nameof(SyncProjectId))]
        public virtual SyncProject SyncProject { get; set; } = null!;

        /// <summary>
        /// Collection of steps that make up this workflow
        /// </summary>
        public virtual ICollection<SyncStep> Steps { get; set; } = new List<SyncStep>();

        /// <summary>
        /// Tags associated with this workflow for categorization and policy application
        /// </summary>
        public virtual ICollection<WorkflowTag> WorkflowTags { get; set; } = new List<WorkflowTag>();
    }

    /// <summary>
    /// Represents a tag for categorizing workflows and entities (SOX, Privileged, Service Account, Vendor, etc.)
    /// Used for policies, access reviews, and compliance tracking
    /// UPDATED to support broader tagging (workflows, objects, identities, memberships)
    /// </summary>
    public class Tag
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Tag name (e.g., "SOX Compliance", "Privileged Accounts", "Service Accounts", "Vendor", "Contractor")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of this tag's purpose
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Color for UI display (hex code or CSS color name)
        /// </summary>
        [MaxLength(50)]
        public string? Color { get; set; }

        /// <summary>
        /// Font Awesome icon class (e.g., "fa-shield-alt", "fa-user-shield")
        /// </summary>
        [MaxLength(50)]
        public string? Icon { get; set; }

        /// <summary>
        /// Whether this is a system tag (cannot be deleted by users)
        /// </summary>
        public bool IsSystem { get; set; } = false;

        /// <summary>
        /// Category for grouping tags (e.g., "Compliance", "Account Type", "Security Level", "Employment Type")
        /// </summary>
        [MaxLength(100)]
        public string? Category { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        [MaxLength(256)]
        public string? ModifiedBy { get; set; }

        /// <summary>
        /// Navigation property to workflow associations
        /// </summary>
        public virtual ICollection<WorkflowTag> WorkflowTags { get; set; } = new List<WorkflowTag>();

        /// <summary>
        /// NEW: Navigation property to object tag associations
        /// </summary>
        public virtual ICollection<ObjectTag> ObjectTags { get; set; } = new List<ObjectTag>();

        /// <summary>
        /// NEW: Navigation property to identity tag associations
        /// </summary>
        public virtual ICollection<IdentityTag> IdentityTags { get; set; } = new List<IdentityTag>();

        /// <summary>
        /// NEW: Navigation property to membership tag associations
        /// </summary>
        public virtual ICollection<MembershipTag> MembershipTags { get; set; } = new List<MembershipTag>();
    }

    /// <summary>
    /// Join table linking workflows to tags (many-to-many relationship)
    /// Allows workflows to be categorized with multiple tags for policy application
    /// NO CHANGES
    /// </summary>
    public class WorkflowTag
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the workflow being tagged
        /// </summary>
        [Required]
        public Guid SyncWorkflowId { get; set; }

        /// <summary>
        /// Reference to the tag being applied
        /// </summary>
        [Required]
        public Guid TagId { get; set; }

        /// <summary>
        /// When this tag was applied to the workflow
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// User who applied this tag
        /// </summary>
        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        /// <summary>
        /// Navigation property to the workflow
        /// </summary>
        [ForeignKey(nameof(SyncWorkflowId))]
        public virtual SyncWorkflow SyncWorkflow { get; set; } = null!;

        /// <summary>
        /// Navigation property to the tag
        /// </summary>
        [ForeignKey(nameof(TagId))]
        public virtual Tag Tag { get; set; } = null!;
    }

    // ==========================================
    // NEW: WORKFLOW TEMPLATE MODELS
    // Support for system and user-defined workflow templates
    // ==========================================

    /// <summary>
    /// NEW: Stores workflow templates for quick sync project creation
    /// </summary>
    public class SyncProjectTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Template name (e.g., "Active Directory - Standard User Sync", "Service Accounts Sync")
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Template description
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Template category (e.g., "Active Directory", "Azure AD", "Okta", "Custom")
        /// </summary>
        [MaxLength(100)]
        public string? Category { get; set; }

        /// <summary>
        /// Whether this is a system template (cannot be modified/deleted)
        /// </summary>
        public bool IsSystem { get; set; } = false;

        /// <summary>
        /// JSON representation of the template configuration
        /// Stores: default workflows, steps, attribute mappings, settings
        /// </summary>
        public string TemplateJson { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        [MaxLength(256)]
        public string? ModifiedBy { get; set; }

        /// <summary>
        /// Collection of workflow templates within this project template
        /// </summary>
        public virtual ICollection<SyncWorkflowTemplate> WorkflowTemplates { get; set; } = new List<SyncWorkflowTemplate>();
    }

    /// <summary>
    /// NEW: Stores workflow-level templates within project templates
    /// </summary>
    public class SyncWorkflowTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to parent project template
        /// </summary>
        [Required]
        public Guid ProjectTemplateId { get; set; }

        /// <summary>
        /// Workflow template name (e.g., "User Full Sync", "Service Account Sync")
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Object class for this workflow template
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string ObjectClass { get; set; } = "User";

        /// <summary>
        /// JSON representation of workflow and step configuration
        /// </summary>
        public string TemplateJson { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Navigation property to parent template
        /// </summary>
        [ForeignKey(nameof(ProjectTemplateId))]
        public virtual SyncProjectTemplate ProjectTemplate { get; set; } = null!;
    }

    // ==========================================
    // SYNC STEP AND EXECUTION MODELS
    // (Minor updates for renamed entities)
    // ==========================================

    /// <summary>
    /// Represents a single step in a sync workflow.
    /// Steps execute in order with dependency support.
    /// MINOR UPDATES for refactored naming
    /// </summary>
    public class SyncStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the parent sync workflow
        /// </summary>
        [Required]
        public Guid SyncWorkflowId { get; set; }

        /// <summary>
        /// Human-readable step name
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of what this step does
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Execution order (lower numbers execute first)
        /// </summary>
        public int ExecutionOrder { get; set; }

        /// <summary>
        /// Object class this step processes: User, Group, Computer, Contact, OrganizationalUnit, etc.
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string ObjectClass { get; set; } = "User";

        /// <summary>
        /// Type of workflow step: FullSync, DeltaSync, AdHoc, CustomWorkflow
        /// </summary>
        [MaxLength(50)]
        public string? StepType { get; set; }

        /// <summary>
        /// What this step marks objects as: Identity, Object, Group, Device, etc.
        /// </summary>
        [MaxLength(100)]
        public string? MarkAsType { get; set; }

        /// <summary>
        /// LDAP filter to apply when querying source directory (e.g., "(objectClass=user)")
        /// </summary>
        public string? LdapFilter { get; set; }

        /// <summary>
        /// Search base DN override (if different from connection default)
        /// </summary>
        [MaxLength(2000)]
        public string? SearchBase { get; set; }

        /// <summary>
        /// Multiple search base DNs for multi-scope synchronization (JSON array)
        /// Stored as JSON: ["OU=Users,DC=example,DC=com", "OU=Employees,DC=example,DC=com"]
        /// If null or empty, falls back to SearchBase or connection default
        /// </summary>
        [MaxLength(4000)]
        public string? SearchBases { get; set; }

        /// <summary>
        /// Excluded/blocked search base DNs (JSON array)
        /// Objects under these DNs will be excluded from synchronization
        /// Stored as JSON: ["OU=Blocked,DC=example,DC=com", "OU=Exclude,DC=example,DC=com"]
        /// </summary>
        [MaxLength(4000)]
        public string? ExcludedSearchBases { get; set; }

        /// <summary>
        /// Search scope: Base, OneLevel, Subtree
        /// </summary>
        [MaxLength(20)]
        public string SearchScope { get; set; } = "Subtree";

        /// <summary>
        /// Helper method to get search bases as a list.
        /// Returns list from SearchBases JSON, or single SearchBase, or empty list.
        /// </summary>
        public List<string> GetSearchBaseList()
        {
            // Priority 1: SearchBases JSON array
            if (!string.IsNullOrWhiteSpace(SearchBases))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(SearchBases);
                    if (list != null && list.Count > 0)
                        return list;
                }
                catch
                {
                    // If deserialization fails, fall through to SearchBase
                }
            }

            // Priority 2: Single SearchBase
            if (!string.IsNullOrWhiteSpace(SearchBase))
                return new List<string> { SearchBase };

            // Priority 3: Empty list (will use connection default)
            return new List<string>();
        }

        /// <summary>
        /// Helper method to set search bases from a list.
        /// Stores as JSON in SearchBases property.
        /// </summary>
        public void SetSearchBaseList(List<string> searchBases)
        {
            if (searchBases == null || searchBases.Count == 0)
            {
                SearchBases = null;
                return;
            }

            // Remove empty/null entries
            var validBases = searchBases.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            if (validBases.Count == 0)
            {
                SearchBases = null;
            }
            else if (validBases.Count == 1)
            {
                // Single entry: store in both for backwards compatibility
                SearchBase = validBases[0];
                SearchBases = null;
            }
            else
            {
                // Multiple entries: store as JSON
                SearchBases = JsonSerializer.Serialize(validBases);
                SearchBase = validBases[0]; // Keep first one for backwards compatibility
            }
        }

        /// <summary>
        /// Helper method to get excluded search bases as a list.
        /// Returns list from ExcludedSearchBases JSON, or empty list.
        /// </summary>
        public List<string> GetExcludedSearchBaseList()
        {
            if (!string.IsNullOrWhiteSpace(ExcludedSearchBases))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(ExcludedSearchBases);
                    if (list != null && list.Count > 0)
                        return list;
                }
                catch
                {
                    // If deserialization fails, return empty list
                }
            }

            return new List<string>();
        }

        /// <summary>
        /// Helper method to set excluded search bases from a list.
        /// Stores as JSON in ExcludedSearchBases property.
        /// </summary>
        public void SetExcludedSearchBaseList(List<string> excludedSearchBases)
        {
            if (excludedSearchBases == null || excludedSearchBases.Count == 0)
            {
                ExcludedSearchBases = null;
                return;
            }

            // Remove empty/null entries
            var validBases = excludedSearchBases.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            if (validBases.Count == 0)
            {
                ExcludedSearchBases = null;
            }
            else
            {
                // Store as JSON
                ExcludedSearchBases = JsonSerializer.Serialize(validBases);
            }
        }

        /// <summary>
        /// Whether this step is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Whether to continue workflow if this step fails
        /// </summary>
        public bool ContinueOnError { get; set; } = false;

        /// <summary>
        /// Maximum execution time in minutes (0 = unlimited)
        /// </summary>
        public int MaxExecutionTimeMinutes { get; set; } = 60;

        /// <summary>
        /// Comma-separated list of step IDs this step depends on
        /// </summary>
        [MaxLength(1000)]
        public string? DependsOnStepIds { get; set; }

        /// <summary>
        /// Whether this step should process deletions (soft delete objects not found in source)
        /// </summary>
        public bool ProcessDeletions { get; set; } = true;

        /// <summary>
        /// Whether to update existing objects or only create new ones
        /// </summary>
        public bool UpdateExisting { get; set; } = true;

        /// <summary>
        /// Batch size for processing objects (affects performance)
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// LDAP page size for directory queries (how many records per LDAP request)
        /// Higher = fewer round-trips but more memory per page
        /// Default: 1000 records per page
        /// </summary>
        public int LdapPageSize { get; set; } = 1000;

        /// <summary>
        /// JSON configuration specific to this step type
        /// </summary>
        public string? Configuration { get; set; }

        /// <summary>
        /// Whether to enable identity-centric matching for this step (typically only for User object class)
        /// </summary>
        public bool EnableIdentityMatching { get; set; } = false;

        /// <summary>
        /// Attribute to use for identity matching: Email, EmployeeId, UPN, or custom attribute name
        /// </summary>
        [MaxLength(200)]
        public string? IdentityMatchingAttribute { get; set; }

        /// <summary>
        /// NEW: Whether to inherit workflow tags and apply them to created/updated objects
        /// </summary>
        public bool InheritWorkflowTags { get; set; } = false;

        /// <summary>
        /// Tags to automatically assign to all objects synced by this step (many-to-many).
        /// Useful for tagging by OU (e.g., Employees OU -> "Employee" + "Full-Time" tags)
        /// </summary>
        public virtual ICollection<SyncStepTag> StepTags { get; set; } = new List<SyncStepTag>();

        /// <summary>
        /// PERFORMANCE: Skip person matching during sync (decouple for speed).
        /// When TRUE: Objects are created with IdentityId=NULL, person matching happens in post-sync task.
        /// When FALSE: Person matching happens during sync (legacy behavior, slower).
        /// Default: FALSE (legacy behavior for backward compatibility)
        /// </summary>
        public bool SkipPersonMatching { get; set; } = false;

        /// <summary>
        /// Whether to enable person matching for objects synced by this step.
        /// When FALSE: Objects will be synced without creating/linking to Identity records.
        /// Useful for service accounts, built-in accounts, or non-person object classes.
        /// Default: TRUE for user/contact object classes
        /// </summary>
        public bool EnablePersonMatching { get; set; } = true;

        /// <summary>
        /// Whether to create new Identity (person) records when no match is found.
        /// When FALSE: Objects remain orphaned (IdentityId=NULL) until manually matched.
        /// When TRUE: New Identity records are auto-created for unmatched objects.
        /// Default: TRUE
        /// </summary>
        public bool CreatePersonIfNotFound { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }

        /// <summary>
        /// Navigation property to the parent sync workflow
        /// </summary>
        [ForeignKey(nameof(SyncWorkflowId))]
        public virtual SyncWorkflow SyncWorkflow { get; set; } = null!;

        /// <summary>
        /// Attribute mappings for this step
        /// </summary>
        public virtual ICollection<AttributeMapping> AttributeMappings { get; set; } = new List<AttributeMapping>();

        /// <summary>
        /// History records for this step's executions
        /// </summary>
        public virtual ICollection<SyncStepRun> StepRuns { get; set; } = new List<SyncStepRun>();

        /// <summary>
        /// Processing scripts attached to this step (pre/post processing)
        /// </summary>
        public virtual ICollection<SyncStepScript> StepScripts { get; set; } = new List<SyncStepScript>();
    }

    /// <summary>
    /// Defines how source attributes map to target attributes for a sync step.
    /// Supports transformations and conditional logic.
    /// MINOR UPDATE: TargetType now supports "ObjectColumn" instead of "IdentityColumn"
    /// </summary>
    public class AttributeMapping
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the sync step this mapping belongs to
        /// </summary>
        [Required]
        public Guid SyncStepId { get; set; }

        /// <summary>
        /// Source attribute name from directory (e.g., "sAMAccountName", "mail", "givenName")
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string SourceAttribute { get; set; } = string.Empty;

        /// <summary>
        /// Display-friendly name for the source attribute (from schema discovery)
        /// </summary>
        [MaxLength(200)]
        public string SourceDisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Data type of the source attribute: String, Integer, DateTime, Boolean, Binary
        /// </summary>
        [MaxLength(50)]
        public string DataType { get; set; } = "String";

        /// <summary>
        /// Type of target: "ObjectColumn" for IdentityObject table columns, "ExtendedAttribute" for ObjectAttribute table
        /// </summary>
        [MaxLength(50)]
        public string TargetType { get; set; } = "ObjectColumn";

        /// <summary>
        /// Target attribute name in IdentityCenter (e.g., "Username", "Email", "FirstName")
        /// For ObjectColumn: Must match IdentityObject model property name
        /// For ExtendedAttribute: Can be any string (e.g., "samAccountName", "lastLogon")
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string TargetAttribute { get; set; } = string.Empty;

        /// <summary>
        /// Transformation type: Direct, ToUpper, ToLower, Trim, Concat, Substring, Regex, JavaScript, etc.
        /// </summary>
        [MaxLength(50)]
        public string TransformationType { get; set; } = "Direct";

        /// <summary>
        /// Transformation expression or script (format depends on TransformationType)
        /// </summary>
        public string? TransformationExpression { get; set; }

        /// <summary>
        /// Default value if source is null/empty
        /// </summary>
        [MaxLength(500)]
        public string? DefaultValue { get; set; }

        /// <summary>
        /// Whether this attribute is required (mapping fails if source is null)
        /// </summary>
        public bool IsRequired { get; set; } = false;

        /// <summary>
        /// Whether to use this attribute for identity matching
        /// </summary>
        public bool UseForMatching { get; set; } = false;

        /// <summary>
        /// Weight for identity matching algorithm (higher = more important)
        /// </summary>
        public int MatchWeight { get; set; } = 10;

        /// <summary>
        /// Enable fuzzy/approximate matching for this attribute (allows typos, nicknames, partial matches)
        /// </summary>
        public bool UseFuzzyMatch { get; set; } = false;

        /// <summary>
        /// Minimum similarity threshold for fuzzy matching (0.0-1.0, where 1.0 = exact match)
        /// Recommended: 0.8 for names, 0.95 for IDs
        /// </summary>
        public double FuzzyMatchThreshold { get; set; } = 0.85;

        /// <summary>
        /// Fuzzy match algorithm: Levenshtein, Soundex, Metaphone, JaroWinkler
        /// </summary>
        [MaxLength(50)]
        public string FuzzyMatchAlgorithm { get; set; } = "Levenshtein";

        /// <summary>
        /// Execution order within the step (lower numbers process first)
        /// </summary>
        public int ExecutionOrder { get; set; }

        /// <summary>
        /// Whether this mapping is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }

        /// <summary>
        /// Navigation property to the parent sync step
        /// </summary>
        [ForeignKey(nameof(SyncStepId))]
        public virtual SyncStep SyncStep { get; set; } = null!;
    }

    /// <summary>
    /// Tracks execution of a sync project (one project can have many runs).
    /// Replaces/enhances SyncExecution with project-based tracking.
    /// NO CHANGES
    /// </summary>
    public class SyncProjectRun
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the sync project being executed
        /// </summary>
        [Required]
        public Guid SyncProjectId { get; set; }

        /// <summary>
        /// Trigger type: Scheduled, Manual, API, EventDriven
        /// </summary>
        [MaxLength(50)]
        public string TriggerType { get; set; } = "Manual";

        /// <summary>
        /// User who triggered manual execution
        /// </summary>
        [MaxLength(256)]
        public string? TriggeredBy { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Execution status: Queued, Running, Completed, Failed, Cancelled, Paused
        /// </summary>
        [MaxLength(50)]
        public string Status { get; set; } = "Queued";

        /// <summary>
        /// Overall progress percentage (0-100)
        /// </summary>
        public int ProgressPercentage { get; set; } = 0;

        /// <summary>
        /// Current step being executed
        /// </summary>
        [MaxLength(200)]
        public string? CurrentStep { get; set; }

        /// <summary>
        /// Total number of steps in this run
        /// </summary>
        public int TotalSteps { get; set; } = 0;

        /// <summary>
        /// Number of completed steps
        /// </summary>
        public int CompletedSteps { get; set; } = 0;

        /// <summary>
        /// Number of failed steps
        /// </summary>
        public int FailedSteps { get; set; } = 0;

        /// <summary>
        /// Number of skipped steps
        /// </summary>
        public int SkippedSteps { get; set; } = 0;

        /// <summary>
        /// Total objects processed across all steps
        /// </summary>
        public int TotalObjectsProcessed { get; set; } = 0;

        /// <summary>
        /// Total objects created across all steps
        /// </summary>
        public int TotalObjectsCreated { get; set; } = 0;

        /// <summary>
        /// Total objects updated across all steps
        /// </summary>
        public int TotalObjectsUpdated { get; set; } = 0;

        /// <summary>
        /// Total objects deleted across all steps
        /// </summary>
        public int TotalObjectsDeleted { get; set; } = 0;

        /// <summary>
        /// Total errors encountered across all steps
        /// </summary>
        public int TotalErrors { get; set; } = 0;

        /// <summary>
        /// Total person (Identity) records created during this run
        /// </summary>
        public int TotalPersonsCreated { get; set; } = 0;

        /// <summary>
        /// Error message if execution failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Detailed execution log (JSON or text)
        /// </summary>
        public string? ExecutionLog { get; set; }

        /// <summary>
        /// Duration in seconds
        /// </summary>
        public int? DurationSeconds { get; set; }

        /// <summary>
        /// Navigation property to the sync project
        /// </summary>
        [ForeignKey(nameof(SyncProjectId))]
        public virtual SyncProject SyncProject { get; set; } = null!;

        /// <summary>
        /// Collection of step execution details
        /// </summary>
        public virtual ICollection<SyncStepRun> StepRuns { get; set; } = new List<SyncStepRun>();

        /// <summary>
        /// Collection of post-sync background tasks for this run
        /// </summary>
        public virtual ICollection<PostSyncTask> PostSyncTasks { get; set; } = new List<PostSyncTask>();
    }

    /// <summary>
    /// Tracks execution of individual steps within a project run.
    /// Provides detailed metrics for each step's performance.
    /// NO CHANGES
    /// </summary>
    public class SyncStepRun
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the parent project run
        /// </summary>
        [Required]
        public Guid SyncProjectRunId { get; set; }

        /// <summary>
        /// Reference to the step definition (nullable for internal sync steps which use InternalSyncSteps table)
        /// </summary>
        public Guid? SyncStepId { get; set; }

        /// <summary>
        /// Step name (cached for historical purposes)
        /// </summary>
        [MaxLength(200)]
        public string StepName { get; set; } = string.Empty;

        /// <summary>
        /// Object class processed (cached)
        /// </summary>
        [MaxLength(100)]
        public string ObjectClass { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Step execution status: Pending, Running, Completed, Failed, Skipped, Cancelled
        /// </summary>
        [MaxLength(50)]
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Number of objects queried from source
        /// </summary>
        public int ObjectsQueried { get; set; } = 0;

        /// <summary>
        /// Number of objects processed
        /// </summary>
        public int ObjectsProcessed { get; set; } = 0;

        /// <summary>
        /// Number of objects created
        /// </summary>
        public int ObjectsCreated { get; set; } = 0;

        /// <summary>
        /// Number of objects updated
        /// </summary>
        public int ObjectsUpdated { get; set; } = 0;

        /// <summary>
        /// Number of objects deleted (soft delete)
        /// </summary>
        public int ObjectsDeleted { get; set; } = 0;

        /// <summary>
        /// Number of objects skipped (no changes)
        /// </summary>
        public int ObjectsSkipped { get; set; } = 0;

        /// <summary>
        /// Number of errors encountered
        /// </summary>
        public int ErrorCount { get; set; } = 0;

        /// <summary>
        /// Number of persons matched to existing Identity records (person matching)
        /// </summary>
        public int PersonsMatched { get; set; } = 0;

        /// <summary>
        /// Number of new Identity records created (person matching)
        /// </summary>
        public int PersonsCreated { get; set; } = 0;

        /// <summary>
        /// Number of objects skipped for person matching (e.g., built-in accounts, groups)
        /// </summary>
        public int PersonMatchingSkipped { get; set; } = 0;

        /// <summary>
        /// Error message if step failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Detailed step execution log
        /// </summary>
        public string? ExecutionLog { get; set; }

        /// <summary>
        /// Duration in seconds
        /// </summary>
        public int? DurationSeconds { get; set; }

        /// <summary>
        /// Average processing time per object (milliseconds)
        /// </summary>
        public decimal? AvgProcessingTimeMs { get; set; }

        /// <summary>
        /// Navigation property to the project run
        /// </summary>
        [ForeignKey(nameof(SyncProjectRunId))]
        public virtual SyncProjectRun SyncProjectRun { get; set; } = null!;

        /// <summary>
        /// Navigation property to the step definition
        /// </summary>
        [ForeignKey(nameof(SyncStepId))]
        public virtual SyncStep SyncStep { get; set; } = null!;

        /// <summary>
        /// Collection of audit log entries for individual changes made during this step run
        /// </summary>
        public virtual ICollection<SyncAuditLog> AuditLogs { get; set; } = new List<SyncAuditLog>();
    }

    /// <summary>
    /// Tracks individual object-level changes during sync operations for detailed audit trail.
    /// Allows viewing exactly what changed for each object during a sync run.
    /// UPDATED: Now references IdentityObject instead of Identity
    /// </summary>
    public class SyncAuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the step run this audit entry belongs to
        /// </summary>
        [Required]
        public Guid SyncStepRunId { get; set; }

        /// <summary>
        /// Reference to the object that was affected
        /// </summary>
        public Guid? ObjectId { get; set; }

        /// <summary>
        /// Type of operation performed: Created, Updated, Skipped, Deleted, Error
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string OperationType { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the object for quick reference
        /// </summary>
        [MaxLength(500)]
        public string? ObjectDisplayName { get; set; }

        /// <summary>
        /// Source unique ID (objectGuid, immutableId, etc.)
        /// </summary>
        [MaxLength(500)]
        public string? SourceUniqueId { get; set; }

        /// <summary>
        /// Email address of the object
        /// </summary>
        [MaxLength(500)]
        public string? Email { get; set; }

        /// <summary>
        /// Username of the object
        /// </summary>
        [MaxLength(500)]
        public string? Username { get; set; }

        /// <summary>
        /// User Principal Name (UPN) of the object
        /// </summary>
        [MaxLength(500)]
        public string? UserPrincipalName { get; set; }

        /// <summary>
        /// JSON representation of changed fields (before/after values)
        /// Example: [{"Field":"Email","Before":"old@example.com","After":"new@example.com"}]
        /// </summary>
        public string? ChangeDetails { get; set; }

        /// <summary>
        /// Number of fields that changed
        /// </summary>
        public int ChangeCount { get; set; } = 0;

        /// <summary>
        /// Error message if operation failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When this operation was performed
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Processing time for this specific object (milliseconds)
        /// </summary>
        public decimal? ProcessingTimeMs { get; set; }

        /// <summary>
        /// Navigation property to the step run
        /// </summary>
        [ForeignKey(nameof(SyncStepRunId))]
        public virtual SyncStepRun SyncStepRun { get; set; } = null!;

        /// <summary>
        /// Navigation property to the object (may be null if object creation failed)
        /// </summary>
        [ForeignKey(nameof(ObjectId))]
        public virtual IdentityObject? Object { get; set; }
    }

    /// <summary>
    /// DTO for manager resolution audit logging.
    /// Contains object details and whether the manager was resolved.
    /// </summary>
    public class ManagerResolutionAuditItem
    {
        public Guid ObjectId { get; set; }
        public string? DisplayName { get; set; }
        public string? SourceUniqueId { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? UserPrincipalName { get; set; }
        public string? ManagerSourceId { get; set; }
        public Guid? ManagerObjectId { get; set; }
        public string? ManagerDisplayName { get; set; }

        /// <summary>
        /// True if manager was resolved (ManagerObjectId is set), false if skipped
        /// </summary>
        public bool WasResolved => ManagerObjectId.HasValue;
    }

    /// <summary>
    /// Lightweight DTO for sync projects list (no nested data)
    /// Used by Dapper for FAST project list loading (600x faster than EF Core)
    /// </summary>
    public class SyncProjectListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsBuiltIn { get; set; }
        public bool IsReadOnly { get; set; }
        public string ProjectType { get; set; } = "ObjectSync";
        public Guid? SourceConnectionId { get; set; }
        // Target = write destination. NULL => internal IdentityCenter identity store.
        public Guid? TargetConnectionId { get; set; }
        public string? CronSchedule { get; set; }
        public string? LogLevel { get; set; }
        public int TotalExecutions { get; set; }
        public int SuccessfulExecutions { get; set; }
        public int FailedExecutions { get; set; }
        public DateTime? LastRunAt { get; set; }
        public DateTime? NextScheduledRunAt { get; set; }
        public bool IsRunning { get; set; }
        public Guid? CurrentRunId { get; set; }  // ID of the currently running sync run (null if not running)

        // Joined from DirectoryConnections
        public string SourceConnectionName { get; set; } = string.Empty;
        public string SourceConnectionType { get; set; } = string.Empty;
        // Target connection join (null/empty when TargetConnectionId is null => identity store)
        public string? TargetConnectionName { get; set; }
        public string? TargetConnectionType { get; set; }

        // Aggregated counts (loaded separately)
        public int WorkflowCount { get; set; }
        public int StepCount { get; set; }

        // Total objects synced across all successful runs
        public int TotalObjectsSynced { get; set; }
    }

    /// <summary>
    /// Full sync project with all nested details (loaded via separate Dapper queries)
    /// Used for editing sync projects (2,400x faster than EF Core Include/ThenInclude)
    /// </summary>
    public class SyncProjectDetails : SyncProject
    {
        public string SourceConnectionName { get; set; } = string.Empty;
        public string SourceConnectionType { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO for workflow tags with tag details
    /// Used by Dapper to load tags in a single query
    /// </summary>
    public class WorkflowTagDetails
    {
        public Guid SyncWorkflowId { get; set; }
        public Guid TagId { get; set; }
        public string TagName { get; set; } = string.Empty;
        public string TagCategory { get; set; } = string.Empty;
        public string? TagColor { get; set; }
    }

    /// <summary>
    /// DTO for step tags with tag details
    /// Used by Dapper to load tags in a single query (FAST)
    /// </summary>
    public class StepTagDetails
    {
        public Guid SyncStepId { get; set; }
        public Guid TagId { get; set; }
        public string TagName { get; set; } = string.Empty;
        public string TagCategory { get; set; } = string.Empty;
        public string? TagColor { get; set; }
    }

    /// <summary>
    /// Post-sync background task for processing expensive operations after sync completes.
    /// Enables "lightning fast" sync by decoupling person matching, manager assignment, etc.
    /// Tasks are processed by PostSyncTaskService background service.
    /// </summary>
    public class PostSyncTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to the sync run that created this task
        /// </summary>
        [Required]
        public Guid SyncProjectRunId { get; set; }

        /// <summary>
        /// Type of task to execute:
        /// - "PersonMatching": Match objects to persons (resolve IdentityId)
        /// - "ManagerAssignment": Resolve manager relationships
        /// - "GroupOwnerAssignment": Resolve group owner relationships
        /// - "ComputerOwnerAssignment": Resolve computer owner relationships
        /// - "OUOwnerAssignment": Resolve OU owner relationships
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string TaskType { get; set; } = string.Empty;

        /// <summary>
        /// Phase of execution: PreSync (runs before sync steps) or PostSync (runs after sync steps).
        /// PreSync tasks include DatabaseOptimization.
        /// PostSync tasks include PersonMatching, ManagerAssignment, etc.
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string TaskPhase { get; set; } = "PostSync";

        /// <summary>
        /// Current status: Pending, Running, Completed, Failed
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Priority for task execution (lower number = higher priority)
        /// PersonMatching should run first (priority 1), then assignments (priority 100)
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// Number of objects processed so far
        /// </summary>
        public int ObjectsProcessed { get; set; } = 0;

        /// <summary>
        /// Total number of objects to process (set after task starts)
        /// </summary>
        public int? ObjectsTotal { get; set; }

        /// <summary>
        /// Number of objects skipped (already processed/mapped in previous syncs)
        /// </summary>
        public int ObjectsSkipped { get; set; } = 0;

        /// <summary>
        /// Error message if task failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When task started executing
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// When task finished (success or failure)
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Duration in seconds (calculated when task completes)
        /// </summary>
        public int? DurationSeconds { get; set; }

        /// <summary>
        /// When this task was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Navigation property to the parent sync run
        /// </summary>
        [ForeignKey(nameof(SyncProjectRunId))]
        public virtual SyncProjectRun? SyncProjectRun { get; set; }
    }

    /// <summary>
    /// Represents an identity that needs manager assignment during post-sync processing.
    /// Used by SyncRepository.GetIdentitiesNeedingManagerAssignmentAsync()
    /// </summary>
    public class IdentityManagerInfo
    {
        /// <summary>
        /// The identity (person) ID that needs manager assignment
        /// </summary>
        public Guid IdentityId { get; set; }

        /// <summary>
        /// Display name of the identity
        /// </summary>
        public string? IdentityDisplayName { get; set; }

        /// <summary>
        /// The identity's authoritative object ID
        /// </summary>
        public Guid AuthoritativeObjectId { get; set; }

        /// <summary>
        /// The manager object ID from the authoritative object
        /// </summary>
        public Guid? ManagerObjectId { get; set; }

        /// <summary>
        /// The manager's identity ID (person ID) to be assigned
        /// </summary>
        public Guid? ManagerIdentityId { get; set; }
    }

    // ==========================================
    // SCHEDULE TEMPLATE MODELS
    // Built-in schedule presets for sync projects
    // ==========================================

    /// <summary>
    /// Predefined schedule templates for sync project scheduling.
    /// Provides common scheduling patterns (daily, weekly, monthly, etc.) as reusable presets.
    /// </summary>
    public class ScheduleTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Display name for the schedule (e.g., "Daily at 2 AM", "Every Monday at 6 AM")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of when this schedule runs
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Category for grouping (Hourly, Daily, Weekly, Monthly, Quarterly, Yearly, Custom)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Quartz cron expression (6-part: seconds minutes hours day month weekday)
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string CronExpression { get; set; } = string.Empty;

        /// <summary>
        /// Sort order within category for UI display
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Whether this is a system template (cannot be deleted)
        /// </summary>
        public bool IsSystem { get; set; } = true;

        /// <summary>
        /// Whether this template is active and available for selection
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Icon class for UI display (FontAwesome class)
        /// </summary>
        [MaxLength(50)]
        public string? IconClass { get; set; }

        /// <summary>
        /// Color for UI display (hex or CSS color)
        /// </summary>
        [MaxLength(20)]
        public string? Color { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ==========================================
    // INTERNAL SYNC CENTER - FIELD MAPPINGS
    // ==========================================

    /// <summary>
    /// Configures field mappings for internal sync operations (Object to Identity, Identity to Object).
    /// Used by Internal Sync Center for Object-to-Person matching and field synchronization.
    /// </summary>
    public class InternalFieldMapping
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Type of mapping: "ObjectToIdentity" or "IdentityToObject"
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string MappingType { get; set; } = "ObjectToIdentity";

        /// <summary>
        /// Source field name (e.g., "Email", "Username", "FirstName")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string SourceField { get; set; } = string.Empty;

        /// <summary>
        /// Target field name (e.g., "PrimaryEmail", "Username", "FirstName")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string TargetField { get; set; } = string.Empty;

        /// <summary>
        /// Whether this mapping is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Display order in the UI
        /// </summary>
        public int DisplayOrder { get; set; } = 0;

        /// <summary>
        /// Optional transformation expression (e.g., "{FirstName} {LastName}" for DisplayName)
        /// </summary>
        [MaxLength(500)]
        public string? TransformExpression { get; set; }

        /// <summary>
        /// Whether this is a default mapping that cannot be deleted
        /// </summary>
        public bool IsDefault { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedAt { get; set; }
    }

    /// <summary>
    /// Stores configuration for Internal Sync Center operations.
    /// Persists user preferences for matching strategy, options, etc.
    /// </summary>
    public class InternalSyncConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Configuration key (e.g., "MatchStrategy", "CreateNewIdentities")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string ConfigKey { get; set; } = string.Empty;

        /// <summary>
        /// Configuration value (stored as string, parsed by caller)
        /// </summary>
        [Required]
        public string ConfigValue { get; set; } = string.Empty;

        /// <summary>
        /// Data type hint for parsing (string, bool, int, json)
        /// </summary>
        [MaxLength(20)]
        public string DataType { get; set; } = "string";

        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Logs internal sync operation runs for history and auditing.
    /// </summary>
    public class InternalSyncRun
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Type of operation: "ObjectToPersonMatch", "ManagerResolution", "MembershipAggregation", "RunAll"
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string OperationType { get; set; } = string.Empty;

        /// <summary>
        /// Matching strategy used (for ObjectToPersonMatch)
        /// </summary>
        [MaxLength(50)]
        public string? MatchStrategy { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Status: Running, Completed, Failed, Cancelled
        /// </summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Running";

        public int TotalProcessed { get; set; } = 0;
        public int Matched { get; set; } = 0;
        public int Created { get; set; } = 0;
        public int Skipped { get; set; } = 0;
        public int Errors { get; set; } = 0;

        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Reference to the sync project that triggered this run (optional for backward compatibility)
        /// </summary>
        public Guid? SyncProjectId { get; set; }

        /// <summary>
        /// Navigation to step execution records
        /// </summary>
        public virtual ICollection<InternalSyncStepRun> StepRuns { get; set; } = new List<InternalSyncStepRun>();
    }

    #region Internal Sync Steps (Granular Step-Based Execution)

    /// <summary>
    /// Granular step types for internal sync operations.
    /// Supports both Object-to-Person and Person-to-Object directions.
    /// </summary>
    public enum InternalSyncStepType
    {
        // === Object to Person Direction ===

        /// <summary>Match Objects to existing Identities by configured strategy (no create)</summary>
        ObjectToPersonMatch,

        /// <summary>Create Identities for unmatched Objects</summary>
        ObjectToPersonCreate,

        /// <summary>Link Objects to Identities without creating new ones</summary>
        ObjectToPersonLink,

        /// <summary>Sync specific fields from Object to Identity using step mappings</summary>
        ObjectToPersonFieldSync,

        /// <summary>Resolve Object.ManagerSourceId (DN) to ManagerObjectId (Guid)</summary>
        ManagerResolve,

        /// <summary>Assign Identity.ManagerIdentityId from linked Object's ManagerObjectId</summary>
        ManagerAssign,

        /// <summary>Aggregate ObjectGroupMemberships to IdentityGroupMemberships (deprecated)</summary>
        [Obsolete("GroupAggregate step has been removed. Keep for DB backward compatibility.")]
        GroupAggregate,

        /// <summary>Aggregate tags from Objects to Identities</summary>
        TagAggregate,

        // === Person to Object Direction ===

        /// <summary>Create Objects from Identities (provisioning new accounts)</summary>
        PersonToObjectCreate,

        /// <summary>Push Identity field changes to linked Objects. Superseded by PersonToObjectFieldSync.</summary>
        [Obsolete("Use PersonToObjectFieldSync instead. Kept for DB backward compatibility.")]
        PersonToObjectUpdate,

        /// <summary>Link existing Objects to Identities</summary>
        PersonToObjectLink,

        /// <summary>Sync specific fields from Identity to Object using step mappings</summary>
        PersonToObjectFieldSync,

        /// <summary>Deprovision/disable Objects for inactive Identities</summary>
        PersonToObjectDeprovision,

        /// <summary>Provision AD user accounts from Objects created by HR import</summary>
        PersonToObjectProvisionAD
    }

    /// <summary>
    /// Defines a granular step within an internal sync project.
    /// Supports bidirectional sync: Object-to-Person and Person-to-Object.
    /// </summary>
    public class InternalSyncStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to parent sync project (SyncProjects table with ProjectType=PersonMatch|PersonCreate|InternalSync)
        /// </summary>
        [Required]
        public Guid SyncProjectId { get; set; }

        /// <summary>
        /// Human-readable step name (e.g., "Match by Email", "Create Identities", "Resolve Managers")
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional description of what this step does
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Execution order within the project (lower numbers execute first)
        /// </summary>
        public int ExecutionOrder { get; set; } = 0;

        /// <summary>
        /// Sync direction: "ObjectToPerson" or "PersonToObject"
        /// </summary>
        [Required]
        [MaxLength(30)]
        public string Direction { get; set; } = "ObjectToPerson";

        /// <summary>
        /// Granular step type (see InternalSyncStepType enum)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string StepType { get; set; } = "ObjectToPersonMatch";

        /// <summary>
        /// Object class filter (e.g., "user", "contact", "*" for all). Only processes objects of this class.
        /// </summary>
        [MaxLength(100)]
        public string? ObjectClassFilter { get; set; } = "user";

        /// <summary>
        /// Whether this step is enabled and should execute
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Continue project execution if this step fails
        /// </summary>
        public bool ContinueOnError { get; set; } = false;

        /// <summary>
        /// Step-specific configuration (JSON).
        /// Examples:
        /// - Match step: {"matchingStrategy": "Email", "minConfidence": 90}
        /// - Create step: {"defaultStatus": "Active", "setAuthoritative": true}
        /// - FieldSync step: {"overwriteNulls": false}
        /// </summary>
        public string? Configuration { get; set; }

        /// <summary>
        /// Optional source connection filter - only process objects from this connection
        /// </summary>
        public Guid? SourceConnectionId { get; set; }

        /// <summary>
        /// Comma-separated tag names to filter objects (e.g., "Employee,Contractor").
        /// Only process objects that have at least one of these tags.
        /// Empty or null means no tag filtering (process all objects).
        /// </summary>
        [MaxLength(500)]
        public string? TagFilter { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey(nameof(SyncProjectId))]
        public virtual SyncProject? SyncProject { get; set; }

        /// <summary>
        /// Field mappings for FieldSync step types
        /// </summary>
        public virtual ICollection<InternalSyncStepMapping> Mappings { get; set; } = new List<InternalSyncStepMapping>();

        /// <summary>
        /// Execution history for this step
        /// </summary>
        public virtual ICollection<InternalSyncStepRun> StepRuns { get; set; } = new List<InternalSyncStepRun>();
    }

    /// <summary>
    /// Field mapping for an internal sync step.
    /// Defines which fields to sync between Objects and Identities.
    /// </summary>
    public class InternalSyncStepMapping
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to parent step
        /// </summary>
        [Required]
        public Guid InternalSyncStepId { get; set; }

        /// <summary>
        /// Source field name.
        /// For ObjectToPerson: Object/ObjectAttribute field (e.g., "Email", "Department", "extensionAttribute1")
        /// For PersonToObject: Identity field (e.g., "Email", "DisplayName")
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string SourceField { get; set; } = string.Empty;

        /// <summary>
        /// Target field name.
        /// For ObjectToPerson: Identity field
        /// For PersonToObject: Object/ObjectAttribute field
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string TargetField { get; set; } = string.Empty;

        /// <summary>
        /// Whether to overwrite existing non-null values in the target
        /// </summary>
        public bool OverwriteExisting { get; set; } = false;

        /// <summary>
        /// Whether this is a required field (step fails if source is null)
        /// </summary>
        public bool IsRequired { get; set; } = false;

        /// <summary>
        /// Default value to use if source is null
        /// </summary>
        [MaxLength(500)]
        public string? DefaultValue { get; set; }

        /// <summary>
        /// Optional transformation expression (future use: ToUpper, ToLower, Trim, Format, etc.)
        /// </summary>
        public string? Transformation { get; set; }

        /// <summary>
        /// Order to apply mappings (lower numbers first)
        /// </summary>
        public int MappingOrder { get; set; } = 0;

        /// <summary>
        /// Whether this mapping is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        // Navigation
        [ForeignKey(nameof(InternalSyncStepId))]
        public virtual InternalSyncStep? Step { get; set; }
    }

    /// <summary>
    /// Execution history for individual internal sync steps.
    /// Tracks metrics and status for each step within a run.
    /// </summary>
    public class InternalSyncStepRun
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Reference to parent run
        /// </summary>
        [Required]
        public Guid InternalSyncRunId { get; set; }

        /// <summary>
        /// Reference to the step definition
        /// </summary>
        [Required]
        public Guid InternalSyncStepId { get; set; }

        /// <summary>
        /// Step name at time of execution (copied for history)
        /// </summary>
        [MaxLength(200)]
        public string StepName { get; set; } = string.Empty;

        /// <summary>
        /// Step type at time of execution
        /// </summary>
        [MaxLength(50)]
        public string StepType { get; set; } = string.Empty;

        /// <summary>
        /// Execution order at time of execution
        /// </summary>
        public int ExecutionOrder { get; set; } = 0;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Status: Pending, Running, Completed, Failed, Skipped, Cancelled
        /// </summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Total records processed
        /// </summary>
        public int Processed { get; set; } = 0;

        /// <summary>
        /// Records matched to existing entities
        /// </summary>
        public int Matched { get; set; } = 0;

        /// <summary>
        /// Records created
        /// </summary>
        public int Created { get; set; } = 0;

        /// <summary>
        /// Records updated
        /// </summary>
        public int Updated { get; set; } = 0;

        /// <summary>
        /// Records skipped (no match, no action needed)
        /// </summary>
        public int Skipped { get; set; } = 0;

        /// <summary>
        /// Records with errors
        /// </summary>
        public int Errors { get; set; } = 0;

        /// <summary>
        /// Error message if step failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Execution duration in seconds
        /// </summary>
        public double? DurationSeconds { get; set; }

        // Navigation
        [ForeignKey(nameof(InternalSyncRunId))]
        public virtual InternalSyncRun? Run { get; set; }

        [ForeignKey(nameof(InternalSyncStepId))]
        public virtual InternalSyncStep? Step { get; set; }
    }

    #endregion

    // ==========================================
    // FIELD LOOKUP VALUES
    // ==========================================

    /// <summary>
    /// Managed lookup value for identity field dropdowns (Department, Division, IdentityType, etc.)
    /// </summary>
    public class FieldLookupValue
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string FieldName { get; set; } = "";

        [Required]
        [MaxLength(500)]
        public string Value { get; set; } = "";

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }
    }
}
