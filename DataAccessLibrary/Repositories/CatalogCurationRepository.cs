using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-backed implementation of <see cref="ICatalogCurationRepository"/>.
/// Backed by V116 schema: CatalogVisibility + CustomCatalogItems.
/// </summary>
public class CatalogCurationRepository : DapperRepositoryBase, ICatalogCurationRepository
{
    public CatalogCurationRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    // ---- Curation --------------------------------------------------------

    public Task<bool> IsHiddenAsync(Guid objectId, CancellationToken ct = default)
    {
        if (objectId == Guid.Empty)
            throw new ArgumentException("objectId is required", nameof(objectId));

        return ExecuteAsync(async conn =>
        {
            var count = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM CatalogVisibility WHERE ObjectId = @ObjectId;",
                new { ObjectId = objectId },
                cancellationToken: ct));
            return count > 0;
        }, ct);
    }

    public Task<IReadOnlyList<Guid>> GetHiddenObjectIdsAsync(CancellationToken ct = default)
    {
        return ExecuteAsync(async conn =>
        {
            var rows = await conn.QueryAsync<Guid>(new CommandDefinition(
                "SELECT ObjectId FROM CatalogVisibility;",
                cancellationToken: ct));
            return (IReadOnlyList<Guid>)rows.ToList();
        }, ct);
    }

    public Task HideAsync(Guid objectId, string? reason, string? hiddenBy, CancellationToken ct = default)
    {
        if (objectId == Guid.Empty)
            throw new ArgumentException("objectId is required", nameof(objectId));

        return ExecuteNonQueryAsync(async conn =>
        {
            // Upsert: if a row exists, refresh it; otherwise insert. Cleaner than
            // MERGE for a 4-column table and avoids MERGE's edge cases.
            var rowsAffected = await conn.ExecuteAsync(new CommandDefinition(
                @"UPDATE CatalogVisibility
                  SET IsHidden = 1,
                      HiddenAt = SYSUTCDATETIME(),
                      HiddenBy = @HiddenBy,
                      Reason   = @Reason
                  WHERE ObjectId = @ObjectId;",
                new { ObjectId = objectId, HiddenBy = hiddenBy, Reason = reason },
                cancellationToken: ct));

            if (rowsAffected == 0)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO CatalogVisibility (ObjectId, IsHidden, HiddenAt, HiddenBy, Reason)
                      VALUES (@ObjectId, 1, SYSUTCDATETIME(), @HiddenBy, @Reason);",
                    new { ObjectId = objectId, HiddenBy = hiddenBy, Reason = reason },
                    cancellationToken: ct));
            }
        }, ct);
    }

    public Task ShowAsync(Guid objectId, CancellationToken ct = default)
    {
        if (objectId == Guid.Empty)
            throw new ArgumentException("objectId is required", nameof(objectId));

        return ExecuteNonQueryAsync(async conn =>
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM CatalogVisibility WHERE ObjectId = @ObjectId;",
                new { ObjectId = objectId },
                cancellationToken: ct));
        }, ct);
    }

    // ---- Custom items ----------------------------------------------------

    public Task<IReadOnlyList<CustomCatalogItem>> GetCustomItemsAsync(bool activeOnly = true, CancellationToken ct = default)
    {
        return ExecuteAsync(async conn =>
        {
            var sql = @"SELECT Id, Name, Description, ResourceType, ExternalUrl, RiskLevel,
                               OwnerObjectId, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, IsActive
                        FROM CustomCatalogItems"
                      + (activeOnly ? " WHERE IsActive = 1" : "")
                      + " ORDER BY Name;";

            var rows = await conn.QueryAsync<CustomCatalogItem>(new CommandDefinition(
                sql, cancellationToken: ct));
            return (IReadOnlyList<CustomCatalogItem>)rows.ToList();
        }, ct);
    }

    public Task<CustomCatalogItem?> GetCustomItemAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("id is required", nameof(id));

        return ExecuteAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<CustomCatalogItem>(new CommandDefinition(
                @"SELECT Id, Name, Description, ResourceType, ExternalUrl, RiskLevel,
                         OwnerObjectId, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, IsActive
                  FROM CustomCatalogItems WHERE Id = @Id;",
                new { Id = id },
                cancellationToken: ct));
            return row;
        }, ct);
    }

    public Task<Guid> CreateCustomItemAsync(CustomCatalogItem item, CancellationToken ct = default)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (string.IsNullOrWhiteSpace(item.Name))
            throw new ArgumentException("Name is required", nameof(item));

        return ExecuteAsync(async conn =>
        {
            var newId = Guid.NewGuid();
            var inserted = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
                @"INSERT INTO CustomCatalogItems
                    (Id, Name, Description, ResourceType, ExternalUrl, RiskLevel, OwnerObjectId, CreatedAt, CreatedBy, IsActive)
                  OUTPUT INSERTED.Id
                  VALUES
                    (@Id, @Name, @Description, @ResourceType, @ExternalUrl, @RiskLevel, @OwnerObjectId, SYSUTCDATETIME(), @CreatedBy, 1);",
                new
                {
                    Id = newId,
                    item.Name,
                    item.Description,
                    ResourceType = string.IsNullOrWhiteSpace(item.ResourceType) ? "Application" : item.ResourceType,
                    item.ExternalUrl,
                    RiskLevel = string.IsNullOrWhiteSpace(item.RiskLevel) ? "Low" : item.RiskLevel,
                    item.OwnerObjectId,
                    item.CreatedBy,
                },
                cancellationToken: ct));
            return inserted;
        }, ct);
    }

    public Task UpdateCustomItemAsync(CustomCatalogItem item, CancellationToken ct = default)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (item.Id == Guid.Empty)
            throw new ArgumentException("Id is required", nameof(item));
        if (string.IsNullOrWhiteSpace(item.Name))
            throw new ArgumentException("Name is required", nameof(item));

        return ExecuteNonQueryAsync(async conn =>
        {
            await conn.ExecuteAsync(new CommandDefinition(
                @"UPDATE CustomCatalogItems
                  SET Name          = @Name,
                      Description   = @Description,
                      ResourceType  = @ResourceType,
                      ExternalUrl   = @ExternalUrl,
                      RiskLevel     = @RiskLevel,
                      OwnerObjectId = @OwnerObjectId,
                      ModifiedAt    = SYSUTCDATETIME(),
                      ModifiedBy    = @ModifiedBy
                  WHERE Id = @Id;",
                new
                {
                    item.Id,
                    item.Name,
                    item.Description,
                    ResourceType = string.IsNullOrWhiteSpace(item.ResourceType) ? "Application" : item.ResourceType,
                    item.ExternalUrl,
                    RiskLevel = string.IsNullOrWhiteSpace(item.RiskLevel) ? "Low" : item.RiskLevel,
                    item.OwnerObjectId,
                    item.ModifiedBy,
                },
                cancellationToken: ct));
        }, ct);
    }

    public Task DeleteCustomItemAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("id is required", nameof(id));

        return ExecuteNonQueryAsync(async conn =>
        {
            await conn.ExecuteAsync(new CommandDefinition(
                @"UPDATE CustomCatalogItems
                  SET IsActive   = 0,
                      ModifiedAt = SYSUTCDATETIME()
                  WHERE Id = @Id;",
                new { Id = id },
                cancellationToken: ct));
        }, ct);
    }
}
