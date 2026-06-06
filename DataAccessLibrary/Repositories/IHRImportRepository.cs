using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IHRImportRepository
{
    // === Field Mapping CRUD ===
    Task<List<HRFieldMapping>> GetFieldMappingsAsync(Guid directoryConnectionId, CancellationToken ct = default);
    Task BulkCreateFieldMappingsAsync(List<HRFieldMapping> mappings, CancellationToken ct = default);
    Task DeleteAllFieldMappingsAsync(Guid directoryConnectionId, CancellationToken ct = default);

    // === Import Run Tracking ===
    Task<Guid> CreateImportRunAsync(HRImportRun run, CancellationToken ct = default);
    Task UpdateImportRunAsync(HRImportRun run, CancellationToken ct = default);
    Task<List<HRImportRun>> GetImportRunsAsync(Guid syncProjectId, int top = 50, CancellationToken ct = default);
    Task<HRImportRun?> GetLatestImportRunAsync(Guid syncProjectId, CancellationToken ct = default);

    // === Core Import ===
    Task<HRImportResult> BulkUpsertIdentitiesAsync(
        List<Dictionary<string, object?>> records,
        List<HRFieldMapping> mappings,
        string uniqueIdField,
        Guid connectionId,
        CancellationToken ct = default,
        HRImportStepConfig? stepConfig = null);
}
