namespace DataAccessLibrary.Models
{
    /// <summary>
    /// Defines the retention mode for audit log cleanup operations.
    /// </summary>
    public enum AuditLogRetentionMode
    {
        /// <summary>
        /// Keep records for a specified number of days (default behavior)
        /// </summary>
        ByDays = 0,

        /// <summary>
        /// Keep only the most recent N records
        /// </summary>
        ByRecordCount = 1,

        /// <summary>
        /// Keep records until the table size exceeds N megabytes
        /// </summary>
        BySize = 2
    }
}
