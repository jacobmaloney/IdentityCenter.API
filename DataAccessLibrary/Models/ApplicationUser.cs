using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ManagerId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
        public string Source { get; set; } = "Local";
        public string ExternalId { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public bool IsSystem { get; set; } = false;

        // Person Integration (UC-AUTH-04)
        public Guid? PersonId { get; set; } = null;

        // Notification Preferences
        public bool EmailNotifications { get; set; } = true;
        public bool TeamsNotifications { get; set; } = false;
        public bool SystemAlerts { get; set; } = true;

        // User Preferences
        public string TimeZone { get; set; } = "UTC";
        public string Language { get; set; } = "en-US";
        public string Theme { get; set; } = "classic";

        [NotMapped]
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    public class ApplicationRole : IdentityRole
    {
        public string Description { get; set; } = string.Empty;
        public string Permissions { get; set; } = string.Empty;
        public string AdGroupMappings { get; set; } = string.Empty;
        public string EntraIdGroupMappings { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsSystem { get; set; } = false;
    }
}