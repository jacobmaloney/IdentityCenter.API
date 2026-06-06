namespace DataAccessLibrary.Services;

/// <summary>
/// Service for executing license write-back operations against Entra ID
/// and keeping the local database in sync.
/// </summary>
public interface ILicenseWriteService
{
    /// <summary>
    /// Removes a license assignment from the user in Entra ID and deactivates the
    /// local LicenseAssignment record. Also marks any related pending recommendation as Applied.
    /// </summary>
    /// <param name="objectId">Internal Objects.Id for the user</param>
    /// <param name="licensePoolId">LicensePool.Id identifying the SKU to remove</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success flag and a human-readable message</returns>
    Task<(bool Success, string Message)> RemoveUserLicenseAsync(
        Guid objectId,
        Guid licensePoolId,
        CancellationToken ct = default);

    /// <summary>
    /// Assigns a license SKU to a user in Entra ID and activates the local LicenseAssignment record.
    /// </summary>
    Task<(bool Success, string Message)> AssignUserLicenseAsync(
        Guid objectId,
        Guid licensePoolId,
        string? usageLocation = null,
        CancellationToken ct = default);
}
