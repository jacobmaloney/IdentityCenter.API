using System;
using System.Collections.Generic;

namespace DataAccessLibrary.Models
{
    /// <summary>
    /// Represents an object class discovered from a directory (AD, Entra, etc.)
    /// </summary>
    public class ObjectClassInfo
    {
        /// <summary>
        /// Internal name of the object class (e.g., "user", "group", "computer")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Display-friendly name (e.g., "User Account", "Security Group")
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Estimated count of objects of this class in the directory
        /// </summary>
        public int EstimatedCount { get; set; }

        /// <summary>
        /// List of commonly used attributes for this object class
        /// Used for quick reference in the UI
        /// </summary>
        public List<string> CommonAttributes { get; set; } = new();

        /// <summary>
        /// Icon to display in the UI
        /// </summary>
        public string Icon { get; set; } = "fa-cube";

        /// <summary>
        /// Whether this object class is selected for synchronization
        /// </summary>
        public bool IsSelected { get; set; }
    }

    /// <summary>
    /// Represents an attribute of a directory object class
    /// </summary>
    public class AttributeInfo
    {
        /// <summary>
        /// Internal LDAP/API name of the attribute (e.g., "sAMAccountName", "mail")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Display-friendly name (e.g., "Username", "Email Address")
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Data type of the attribute (String, Integer, DateTime, Boolean, Binary)
        /// </summary>
        public string DataType { get; set; } = "String";

        /// <summary>
        /// Whether this attribute can contain multiple values
        /// </summary>
        public bool IsMultiValued { get; set; }

        /// <summary>
        /// Whether this attribute is mandatory for the object class
        /// </summary>
        public bool IsMandatory { get; set; }

        /// <summary>
        /// Whether this attribute is commonly used (for filtering UI display)
        /// </summary>
        public bool IsCommon { get; set; }

        /// <summary>
        /// Description of what this attribute represents
        /// </summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// Connection test result from directory schema discovery
    /// </summary>
    public class DirectoryConnectionTestResult
    {
        public bool IsSuccessful { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ServerInfo { get; set; }
        public DateTime TestedAt { get; set; } = DateTime.UtcNow;
    }
}
