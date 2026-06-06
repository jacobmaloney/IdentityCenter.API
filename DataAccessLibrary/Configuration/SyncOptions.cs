using System;

namespace DataAccessLibrary.Configuration
{
    /// <summary>
    /// Configuration options for synchronization operations.
    /// Replaces magic numbers with configurable values for better maintainability.
    /// </summary>
    public class SyncOptions
    {
        /// <summary>
        /// Configuration section name in appsettings.json
        /// </summary>
        public const string SectionName = "SyncOptions";

        /// <summary>
        /// Database command timeout in seconds for sync operations.
        /// Default: 300 seconds (5 minutes) for large batch operations.
        /// </summary>
        public int CommandTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Default batch size for processing records when not specified per step.
        /// Default: 50 records per batch.
        /// </summary>
        public int DefaultBatchSize { get; set; } = 50;

        /// <summary>
        /// Default LDAP page size for querying large result sets when not specified per step.
        /// Default: 1000 records per page.
        /// </summary>
        public int DefaultLdapPageSize { get; set; } = 1000;

        /// <summary>
        /// Maximum number of detailed skip log entries to record per step.
        /// Default: 100 entries to prevent log explosion.
        /// </summary>
        public int MaxDetailedSkips { get; set; } = 100;

        /// <summary>
        /// Maximum number of errors before pausing sync (when PauseOnError is enabled).
        /// Default: 100 errors.
        /// </summary>
        public int MaxErrorsThreshold { get; set; } = 100;
    }

    /// <summary>
    /// Configuration options for person matching algorithms.
    /// Controls confidence thresholds for different matching methods.
    /// </summary>
    public class PersonMatchingOptions
    {
        /// <summary>
        /// Configuration section name in appsettings.json
        /// </summary>
        public const string SectionName = "PersonMatchingOptions";

        /// <summary>
        /// High confidence threshold for exact matches (e.g., email).
        /// Default: 95%
        /// </summary>
        public int HighConfidenceThreshold { get; set; } = 95;

        /// <summary>
        /// Medium confidence threshold for partial matches (e.g., name + department).
        /// Default: 80%
        /// </summary>
        public int MediumConfidenceThreshold { get; set; } = 80;

        /// <summary>
        /// Low confidence threshold for weak matches (not currently used).
        /// Default: 60%
        /// </summary>
        public int LowConfidenceThreshold { get; set; } = 60;
    }
}
