using System;
using System.ComponentModel.DataAnnotations;

namespace DataAccessLibrary.Models
{
    /// <summary>
    /// System-wide configuration settings
    /// </summary>
    public class SystemConfiguration
    {
        [Key]
        public int Id { get; set; } = 1; // Singleton record

        // Authentication Settings
        public bool AllowSelfRegistration { get; set; } = false;
        public bool RequireEmailConfirmation { get; set; } = false;
        public bool AllowExternalLogins { get; set; } = true;

        // Password Policy
        public int MinimumPasswordLength { get; set; } = 8;
        public bool RequireDigit { get; set; } = true;
        public bool RequireLowercase { get; set; } = true;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireNonAlphanumeric { get; set; } = true;

        // Lockout Policy
        public int MaxFailedAccessAttempts { get; set; } = 5;
        public int LockoutDurationMinutes { get; set; } = 30;

        // Session Settings
        public int SessionTimeoutMinutes { get; set; } = 30;
        public bool SlidingExpiration { get; set; } = true;

        // Audit Settings
        public bool EnableAuditLogging { get; set; } = true;
        public int AuditRetentionDays { get; set; } = 90;

        // Application Settings
        public string PortalUrl { get; set; } = "https://localhost:7001";
        public string PortalDisplayName { get; set; } = "Certification Center";

        // Notification Settings
        public string? AdminNotificationEmail { get; set; }
        public bool EnablePolicyNotifications { get; set; } = true;
        public bool EnableSyncNotifications { get; set; } = true;
        public bool EnableEscalationNotifications { get; set; } = true;

        // Chat & AI Settings
        public bool ChatLlmEnabled { get; set; } = false;
        public string ChatLlmProvider { get; set; } = "Anthropic"; // OpenAI, Azure, Anthropic, Local
        public string ChatLlmEndpoint { get; set; } = "https://api.anthropic.com/v1";
        public string? ChatLlmApiKey { get; set; }
        public string ChatLlmModel { get; set; } = "claude-sonnet-4-6";
        public int ChatLlmMaxTokens { get; set; } = 500;
        public double ChatLlmTemperature { get; set; } = 0.3;
        public int ChatLlmTimeoutSeconds { get; set; } = 30;

        // Compliance & Escalation Settings (stored as JSON)
        public string? ComplianceEscalationSettings { get; set; }
        public string? NotificationIntegrationSettings { get; set; }

        // System Information
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedAt { get; set; }
        public string ModifiedBy { get; set; } = string.Empty;
    }
}
