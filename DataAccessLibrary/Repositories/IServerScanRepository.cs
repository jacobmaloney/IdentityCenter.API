using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IServerScanRepository
{
    // ── Local Users ─────────────────────────────────────────────────────────
    Task<List<ServerLocalUser>> GetLocalUsersAsync(Guid serverId, bool activeOnly = true);
    Task<List<ServerLocalUser>> GetLocalAdminsAsync(Guid serverId);
    Task DeactivateLocalUsersAsync(Guid serverId);
    Task<(int inserted, int adMatched)> UpsertLocalUsersAsync(Guid serverId, List<ServerLocalUser> users);

    // ── Installed Products ──────────────────────────────────────────────────
    Task<List<ServerInstalledProduct>> GetInstalledProductsAsync(Guid serverId, bool activeOnly = true);
    Task<List<ServerInstalledProduct>> GetProductsByCategoryAsync(Guid serverId, string category);
    Task DeactivateInstalledProductsAsync(Guid serverId);
    Task<int> UpsertInstalledProductsAsync(Guid serverId, List<ServerInstalledProduct> products);

    // ── WinRM Scan Status ───────────────────────────────────────────────────
    Task UpdateWinRmScanStatusAsync(Guid serverId, string status, string? message, int? durationMs);
}
