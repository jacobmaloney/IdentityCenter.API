namespace DataAccessLibrary.Models;

/// <summary>
/// Aggregated NHI Governance dashboard counts. Populates the stat-card row at
/// the top of /admin/nhi.
/// </summary>
public class NHISummaryStats
{
    public int TotalNHIs { get; set; }
    public int Owned { get; set; }
    public int Unowned { get; set; }
    public int WithExpiredPasswords { get; set; }
    public int WithNeverExpiringPasswords { get; set; }
    public int WithAdminRights { get; set; }
    public int WithSPNs { get; set; }

    /// <summary>NHIs not attested in the last 90 days, or never attested.</summary>
    public int AttestationOverdue { get; set; }
}
