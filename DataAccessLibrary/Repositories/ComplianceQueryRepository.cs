using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class ComplianceQueryRepository : DapperRepositoryBase, IComplianceQueryRepository
{
    public ComplianceQueryRepository(IConfiguration configuration, IGlobalLogger logger) : base(configuration, logger) { }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<(List<CompliancePolicyViolation> Items, int TotalCount)> GetViolationsPagedAsync(
        string? status = null, Guid? policyId = null, string? severity = null,
        int page = 1, int pageSize = 50)
    {
        using var conn = CreateConnection();
        var where = "WHERE 1=1";
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(status)) { where += " AND v.Status = @Status"; p.Add("Status", status); }
        if (policyId.HasValue) { where += " AND v.CompliancePolicyId = @PolicyId"; p.Add("PolicyId", policyId.Value); }
        if (!string.IsNullOrEmpty(severity)) { where += " AND v.Severity = @Severity"; p.Add("Severity", severity); }

        var totalCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM CompliancePolicyViolations v {where}", p).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);
        var items = (await conn.QueryAsync<CompliancePolicyViolation>(
            $"SELECT v.* FROM CompliancePolicyViolations v {where} ORDER BY v.DetectedAt DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            p).ConfigureAwait(false)).ToList();

        return (items, totalCount);
    }

    public async Task<List<CompliancePolicyViolation>> GetEscalationCandidatesAsync(int olderThanDays)
    {
        using var conn = CreateConnection();
        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
        var result = await conn.QueryAsync<CompliancePolicyViolation, CompliancePolicy, CompliancePolicyViolation>(@"
            SELECT v.*, p.*
            FROM CompliancePolicyViolations v
            INNER JOIN CompliancePolicies p ON v.CompliancePolicyId = p.Id
            WHERE v.Status = 'Open'
              AND v.DetectedAt < @CutoffDate
            ORDER BY v.DetectedAt",
            (violation, policy) =>
            {
                violation.CompliancePolicy = policy;
                return violation;
            },
            new { CutoffDate = cutoffDate },
            splitOn: "Id").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task UpdateViolationStatusAsync(Guid id, string newStatus)
    {
        using var conn = CreateConnection();
        var sql = "UPDATE CompliancePolicyViolations SET Status = @Status";

        if (newStatus == "Acknowledged")
            sql += ", AcknowledgedAt = GETUTCDATE()";
        else if (newStatus == "Remediated")
            sql += ", RemediatedAt = GETUTCDATE()";
        else if (newStatus == "Closed")
            sql += ", ClosedAt = GETUTCDATE()";

        sql += " WHERE Id = @Id";

        await conn.ExecuteAsync(sql, new { Id = id, Status = newStatus }).ConfigureAwait(false);
    }

    public async Task<int> BulkUpdateViolationStatusAsync(List<Guid> ids, string newStatus)
    {
        if (!ids.Any()) return 0;

        using var conn = CreateConnection();
        var sql = "UPDATE CompliancePolicyViolations SET Status = @Status";

        if (newStatus == "Acknowledged")
            sql += ", AcknowledgedAt = GETUTCDATE()";
        else if (newStatus == "Remediated")
            sql += ", RemediatedAt = GETUTCDATE()";
        else if (newStatus == "Closed")
            sql += ", ClosedAt = GETUTCDATE()";

        sql += " WHERE Id IN @Ids";

        return await conn.ExecuteAsync(sql, new { Ids = ids, Status = newStatus }).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, int>> GetViolationCountsByStatusAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<(string Status, int Count)>(
            "SELECT Status, COUNT(*) as [Count] FROM CompliancePolicyViolations GROUP BY Status").ConfigureAwait(false);
        return result.ToDictionary(x => x.Status, x => x.Count);
    }

    public async Task<List<CompliancePolicyViolation>> GetViolationsForPolicyAsync(Guid policyId, string? status = null)
    {
        using var conn = CreateConnection();
        var sql = "SELECT * FROM CompliancePolicyViolations WHERE CompliancePolicyId = @PolicyId";
        if (!string.IsNullOrEmpty(status))
            sql += " AND Status = @Status";
        sql += " ORDER BY DetectedAt DESC";

        var result = await conn.QueryAsync<CompliancePolicyViolation>(sql,
            new { PolicyId = policyId, Status = status }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<CompliancePolicyViolation>> GetViolationsForEntityAsync(Guid entityId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<CompliancePolicyViolation>(
            "SELECT * FROM CompliancePolicyViolations WHERE EntityId = @EntityId ORDER BY DetectedAt DESC",
            new { EntityId = entityId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<int> GetBusinessRoleCountAsync()
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BusinessRoles").ConfigureAwait(false);
    }

    public async Task<List<CompliancePolicyViolation>> GetActiveViolationsAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<CompliancePolicyViolation>(
            "SELECT * FROM CompliancePolicyViolations WHERE Status IN ('Open', 'Pending') ORDER BY DetectedAt DESC").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<CompliancePolicyViolation>> GetViolationsByStatusAsync(params string[] statuses)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<CompliancePolicyViolation>(
            "SELECT * FROM CompliancePolicyViolations WHERE Status IN @Statuses ORDER BY DetectedAt DESC",
            new { Statuses = statuses }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<CompliancePolicyViolation>> GetRecentViolationsAsync(int days = 7, int limit = 10)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<CompliancePolicyViolation>(@"
            SELECT TOP (@Limit) * FROM CompliancePolicyViolations
            WHERE DetectedAt >= DATEADD(DAY, -@Days, GETUTCDATE())
            ORDER BY DetectedAt DESC",
            new { Days = days, Limit = limit }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<CompliancePolicyViolation?> GetViolationAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CompliancePolicyViolation>(
            "SELECT * FROM CompliancePolicyViolations WHERE Id = @Id",
            new { Id = id }).ConfigureAwait(false);
    }

    public async Task UpdateViolationAsync(CompliancePolicyViolation violation)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE CompliancePolicyViolations
            SET Status = @Status, Severity = @Severity, RemediatedBy = @RemediatedBy,
                RemediatedAt = @RemediatedAt, RemediationNotes = @RemediationNotes,
                AcknowledgedAt = @AcknowledgedAt, AcknowledgedBy = @AcknowledgedBy,
                ClosedAt = @ClosedAt
            WHERE Id = @Id", violation).ConfigureAwait(false);
    }

    public async Task CreateViolationAsync(CompliancePolicyViolation violation)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO CompliancePolicyViolations (Id, CompliancePolicyId, EntityId, EntityType, EntityDisplayName,
                Severity, Status, ViolationScore, Message, Description, DisplayName,
                DetectedAt, ActionsExecuted, ActionCount, NotificationCount)
            VALUES (@Id, @CompliancePolicyId, @EntityId, @EntityType, @EntityDisplayName,
                @Severity, @Status, @ViolationScore, @Message, @Description, @DisplayName,
                @DetectedAt, @ActionsExecuted, @ActionCount, @NotificationCount)",
            violation).ConfigureAwait(false);
    }

    public async Task DeleteViolationsAsync(List<Guid> ids)
    {
        if (!ids.Any()) return;
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM CompliancePolicyViolations WHERE Id IN @Ids",
            new { Ids = ids }).ConfigureAwait(false);
    }

    public async Task<int> BulkUpdateViolationFieldsAsync(List<Guid> ids, string status, string? remediatedBy = null, DateTime? remediatedAt = null)
    {
        if (!ids.Any()) return 0;
        using var conn = CreateConnection();
        return await conn.ExecuteAsync(@"
            UPDATE CompliancePolicyViolations
            SET Status = @Status, RemediatedBy = @RemediatedBy, RemediatedAt = @RemediatedAt
            WHERE Id IN @Ids",
            new { Ids = ids, Status = status, RemediatedBy = remediatedBy, RemediatedAt = remediatedAt }).ConfigureAwait(false);
    }

    public async Task<int> GetPolicyCountAsync()
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CompliancePolicies").ConfigureAwait(false);
    }

    public async Task<Identity?> GetIdentityAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Identity>(
            "SELECT * FROM Identities WHERE Id = @Id",
            new { Id = id }).ConfigureAwait(false);
    }

    public async Task<IdentityObject?> GetIdentityObjectByIdentityIdAsync(Guid identityId)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<IdentityObject>(
            "SELECT TOP 1 * FROM Objects WHERE IdentityId = @IdentityId ORDER BY IsActive DESC, LastSyncedAt DESC",
            new { IdentityId = identityId }).ConfigureAwait(false);
    }

    public async Task<List<IdentityObject>> GetIdentityObjectsByIdentityIdsAsync(List<Guid> identityIds)
    {
        if (!identityIds.Any()) return new List<IdentityObject>();
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<IdentityObject>(
            "SELECT * FROM Objects WHERE IdentityId IN @IdentityIds",
            new { IdentityIds = identityIds }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<Identity>> SearchIdentitiesAsync(string? searchTerm = null, int limit = 50)
    {
        using var conn = CreateConnection();
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            var result = await conn.QueryAsync<Identity>(@"
                SELECT TOP (@Limit) * FROM Identities ORDER BY DisplayName",
                new { Limit = limit }).ConfigureAwait(false);
            return result.ToList();
        }
        else
        {
            var result = await conn.QueryAsync<Identity>(@"
                SELECT TOP (@Limit) * FROM Identities
                WHERE DisplayName LIKE @Query OR Email LIKE @Query
                ORDER BY DisplayName",
                new { Limit = limit, Query = $"%{searchTerm}%" }).ConfigureAwait(false);
            return result.ToList();
        }
    }

    public async Task<Tag?> GetOrCreateTagAsync(string name, string? category = null)
    {
        using var conn = CreateConnection();
        var existing = await conn.QueryFirstOrDefaultAsync<Tag>(
            "SELECT * FROM Tags WHERE Name = @Name",
            new { Name = name }).ConfigureAwait(false);

        if (existing != null) return existing;

        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = category ?? "System",
            CreatedAt = DateTime.UtcNow
        };

        await conn.ExecuteAsync(@"
            INSERT INTO Tags (Id, Name, Category, CreatedAt)
            VALUES (@Id, @Name, @Category, @CreatedAt)", tag).ConfigureAwait(false);

        return tag;
    }

    public async Task AddIdentityTagAsync(Guid identityId, Guid tagId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO IdentityTags (Id, IdentityId, TagId, CreatedAt, IsInherited, CreatedBy)
            VALUES (@Id, @IdentityId, @TagId, @CreatedAt, 0, 'System')",
            new { Id = Guid.NewGuid(), IdentityId = identityId, TagId = tagId, CreatedAt = DateTime.UtcNow }).ConfigureAwait(false);
    }

    public async Task<bool> IdentityTagExistsAsync(Guid identityId, Guid tagId)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM IdentityTags WHERE IdentityId = @IdentityId AND TagId = @TagId",
            new { IdentityId = identityId, TagId = tagId }).ConfigureAwait(false) > 0;
    }
}
