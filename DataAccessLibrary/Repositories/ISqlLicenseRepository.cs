using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface ISqlLicenseRepository
{
    // ── Entitlements ─────────────────────────────────────────────────────────
    Task<List<SqlLicenseEntitlement>> GetEntitlementsAsync();
    Task<SqlLicenseEntitlement?> GetEntitlementAsync(Guid id);
    Task<Guid> CreateEntitlementAsync(SqlLicenseEntitlement entitlement);
    Task UpdateEntitlementAsync(SqlLicenseEntitlement entitlement);
    Task DeleteEntitlementAsync(Guid id);
    Task<int> GetTotalOwnedCoresAsync(string edition);

    // ── Inventory ────────────────────────────────────────────────────────────
    Task<List<SqlServerInventory>> GetInventoryAsync();
    Task<SqlServerInventory?> GetServerAsync(Guid id);
    Task<SqlServerInventory?> GetServerByNameAsync(string serverName, string? instanceName = null);
    Task<SqlServerInventory?> GetServerByIdAsync(Guid serverId);
    Task<SqlServerInventory?> GetServerByIpAsync(string ipAddress, string? instanceName = null);
    Task<List<SqlServerInventory>> GetAllServersAsync();
    Task UpdateServerCredentialAsync(Guid serverId, Guid? credentialId);
    Task UpdateScanStatusAsync(Guid serverId, string status, string? message, int? durationMs);
    Task<Guid> UpsertServerAsync(SqlServerInventory server);
    Task UpdateServerOwnerAsync(Guid serverId, string ownerId, string assignedBy);
    Task UpdateServerComplianceStatusAsync(Guid serverId, string status);
    Task<List<SqlDatabaseInventory>> GetDatabasesAsync(Guid serverId);
    Task UpsertDatabasesAsync(Guid serverId, List<SqlDatabaseInventory> databases);

    // ── Assignments ───────────────────────────────────────────────────────────
    Task<List<SqlLicenseAssignment>> GetAssignmentsAsync(Guid? entitlementId = null);
    Task<SqlLicenseAssignment?> GetAssignmentForServerAsync(string objectId);
    Task<Guid> CreateAssignmentAsync(SqlLicenseAssignment assignment);
    Task RemoveAssignmentAsync(Guid id, string removedBy);

    // ── Network Scan Ranges ───────────────────────────────────────────────────
    Task<List<NetworkScanRange>> GetScanRangesAsync();
    Task<Guid> CreateScanRangeAsync(NetworkScanRange range);
    Task DeleteScanRangeAsync(Guid id);
    Task UpdateScanRangeLastScanAsync(Guid id, DateTime scannedAt, int durationSeconds);

    // ── Network Scan History ─────────────────────────────────────────────────
    Task<Guid> RecordScanHistoryAsync(NetworkScanHistoryEntry entry);
    Task<List<NetworkScanHistoryEntry>> GetScanHistoryAsync(Guid? rangeId = null, int limit = 50);

    // ── Compliance ────────────────────────────────────────────────────────────
    Task<SqlLicenseComplianceSummary> GetComplianceSummaryAsync(bool excludeDemo = false);
    Task<List<LicenseComplianceViolation>> GetViolationsAsync(bool unresolvedOnly = true, string? sourceType = null);
    Task<Guid> CreateViolationAsync(LicenseComplianceViolation violation);
    Task ResolveViolationAsync(Guid id, string resolvedBy, string? note = null);
    Task LinkViolationToCertificationAsync(Guid violationId, Guid campaignId);

    // ── SQL Server Permissions (access governance) ───────────────────────────
    Task DeactivateServerPermissionsAsync(Guid serverId);
    Task<(int inserted, int adMatched)> UpsertPermissionsAsync(Guid serverId, List<SqlServerPermission> permissions);
    Task<List<SqlServerPermission>> GetServerPermissionsAsync(Guid serverId, bool activeOnly = true);
    Task<List<SqlServerPermission>> GetPermissionsForObjectAsync(Guid objectId);
    Task<List<SqlServerPermission>> GetPrivilegedPermissionsAsync();
}
