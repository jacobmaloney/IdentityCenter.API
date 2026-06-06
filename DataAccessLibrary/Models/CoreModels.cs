using System;
using System.ComponentModel.DataAnnotations;

namespace DataAccessLibrary.Models
{
    public class Setting
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Key { get; set; } = string.Empty;

        [Required]
        public string Value { get; set; } = string.Empty;

        public bool IsEncrypted { get; set; }

        [MaxLength(50)]
        public string? DataType { get; set; }

        public DateTime ModifiedAt { get; set; }

        [MaxLength(256)]
        public string? ModifiedBy { get; set; }
    }

    public class IdentityProvider
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = string.Empty; // OIDC, SAML, WsFed

        public bool IsEnabled { get; set; } = true;

        public bool IsPrimary { get; set; }

        [Required]
        public string Configuration { get; set; } = string.Empty; // Encrypted JSON

        public string? Metadata { get; set; } // Encrypted

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        [MaxLength(256)]
        public string? ModifiedBy { get; set; }
    }

    public class DirectoryConnection
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string ConnectionType { get; set; } = string.Empty; // ActiveDirectory, EntraID, LDAP

        [Required]
        public string ConnectionString { get; set; } = string.Empty; // Encrypted

        [Required]
        public string Credentials { get; set; } = string.Empty; // Encrypted JSON

        public string? Configuration { get; set; } // JSON configuration specific to connection type

        public bool IsActive { get; set; } = true;

        public bool IsAuthoritative { get; set; } = false; // If true, this source is authoritative for person attributes

        public DateTime? LastSyncAt { get; set; }

        public DateTime? LastTestAt { get; set; }

        public string? LastTestResult { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedAt { get; set; }
    }

    public class AuditLog
    {
        public long Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(20)]
        public string Level { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Category { get; set; }

        [MaxLength(256)]
        public string? UserId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? EntityType { get; set; }

        [MaxLength(256)]
        public string? EntityId { get; set; }

        public string? OldValues { get; set; } // Encrypted if sensitive

        public string? NewValues { get; set; } // Encrypted if sensitive

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(500)]
        public string? UserAgent { get; set; }

        public Guid? CorrelationId { get; set; }
    }
}