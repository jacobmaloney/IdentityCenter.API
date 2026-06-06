using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Handles breach actions defined on a LicensePool (create review, send email, notify Teams).
/// Implemented in the WebPortal layer where CampaignService, EmailService, etc. are available.
/// </summary>
public interface ILicenseBreachActionHandler
{
    Task<LicenseBreachActionResult> HandleBreachAsync(LicensePool pool, LicenseThresholdBreach breach);
    Task HandleBreachResolvedAsync(LicensePool pool, LicenseThresholdBreach breach);
}

/// <summary>
/// Result of running breach actions for a single pool/breach pair. Reports the
/// auto-created campaign id (if any) so callers can persist a back-link on
/// LicenseThresholdBreaches.CampaignId, and a circuit-breaker flag for the
/// orchestrator's per-run cap.
/// </summary>
public class LicenseBreachActionResult
{
    /// <summary>Auto-created (or reused) campaign id, when OnBreachCreateReview is on.</summary>
    public Guid? CampaignId { get; set; }

    /// <summary>True when a campaign was created (vs reused via SourcePolicyId lookup).</summary>
    public bool CampaignCreated { get; set; }

    /// <summary>True when the breach was skipped because the LicenseManagement module is disabled.</summary>
    public bool ModuleDisabled { get; set; }

    /// <summary>True when the per-run cap (MaxAutoReclaimPerRun) was already reached.</summary>
    public bool CapReached { get; set; }
}
