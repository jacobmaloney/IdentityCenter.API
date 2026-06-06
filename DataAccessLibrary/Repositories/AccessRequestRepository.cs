using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class AccessRequestRepository : DapperRepositoryBase, IAccessRequestRepository
{
    public AccessRequestRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<AccessRequest> CreateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            request.Id = Guid.NewGuid();
            request.RequestedAt = DateTime.UtcNow;
            request.Status = "Pending";

            if (request.DurationDays > 0)
            {
                request.ExpiresAt = DateTime.UtcNow.AddDays(request.DurationDays);
            }

            // Resolve approver: resource owner → matching admin user → any Admin role user
            if (string.IsNullOrEmpty(request.ApproverId))
            {
                request.ApproverId = await ResolveApproverAsync(connection, request.ResourceId, cancellationToken);
            }

            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO AccessRequests (Id, RequesterId, RequesterName, ResourceType, ResourceId, ResourceName,
                    Justification, DurationDays, Status, ApproverId, ExpiresAt, RequestedAt)
                VALUES (@Id, @RequesterId, @RequesterName, @ResourceType, @ResourceId, @ResourceName,
                    @Justification, @DurationDays, @Status, @ApproverId, @ExpiresAt, @RequestedAt)",
                request,
                cancellationToken: cancellationToken));

            return request;
        }, cancellationToken);
    }

    /// <summary>
    /// Resolve the approver for an access request by checking the resource owner chain.
    /// Priority: resource owner (matched to AspNetUser by email) → any Admin role user.
    /// </summary>
    private async Task<string?> ResolveApproverAsync(
        System.Data.IDbConnection connection, string resourceId, CancellationToken cancellationToken)
    {
        // Try to resolve resource owner → AspNetUser by email match
        if (Guid.TryParse(resourceId, out _))
        {
            var ownerUserId = await connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition(@"
                SELECT TOP 1 u.Id
                FROM Objects resource
                INNER JOIN Objects owner ON resource.OwnerObjectId = owner.Id
                INNER JOIN AspNetUsers u ON u.NormalizedEmail = UPPER(owner.Email)
                WHERE resource.Id = @ResourceId
                  AND resource.OwnerObjectId IS NOT NULL
                  AND owner.Email IS NOT NULL
                  AND owner.Email != ''",
                new { ResourceId = resourceId },
                cancellationToken: cancellationToken));

            if (!string.IsNullOrEmpty(ownerUserId))
                return ownerUserId;
        }

        // Fallback: assign to any user in the Admin role
        var adminUserId = await connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition(@"
            SELECT TOP 1 ur.UserId
            FROM AspNetUserRoles ur
            INNER JOIN AspNetRoles r ON ur.RoleId = r.Id
            WHERE r.NormalizedName = 'ADMIN'
            ORDER BY ur.UserId",
            cancellationToken: cancellationToken));

        return adminUserId;
    }

    public async Task<AccessRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.QueryFirstOrDefaultAsync<AccessRequest>(new CommandDefinition(@"
                SELECT * FROM AccessRequests WHERE Id = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<(List<AccessRequest> Items, int TotalCount)> GetByRequesterPagedAsync(
        string requesterId, string? statusFilter = null, int skip = 0, int take = 20,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var whereClause = "WHERE RequesterId = @RequesterId";
            if (!string.IsNullOrEmpty(statusFilter))
                whereClause += " AND Status = @StatusFilter";

            var countSql = $"SELECT COUNT(*) FROM AccessRequests {whereClause}";
            var dataSql = $@"SELECT * FROM AccessRequests {whereClause}
                ORDER BY RequestedAt DESC
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

            var parameters = new { RequesterId = requesterId, StatusFilter = statusFilter, Skip = skip, Take = take };

            var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));
            var items = await connection.QueryAsync<AccessRequest>(new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken));

            return (items.ToList(), total);
        }, cancellationToken);
    }

    public async Task<List<AccessRequest>> GetPendingAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<AccessRequest>(new CommandDefinition(@"
                SELECT TOP (@Take) * FROM AccessRequests
                WHERE Status = 'Pending'
                ORDER BY RequestedAt ASC",
                new { Take = take },
                cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM AccessRequests WHERE Status = 'Pending'",
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid id, string status, string? approverId = null, string? comments = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE AccessRequests
                SET Status = @Status,
                    ApproverId = COALESCE(@ApproverId, ApproverId),
                    ApprovedAt = CASE WHEN @ApproverId IS NOT NULL THEN GETUTCDATE() ELSE ApprovedAt END,
                    ApprovalComments = COALESCE(@Comments, ApprovalComments),
                    UpdatedAt = GETUTCDATE()
                WHERE Id = @Id",
                new { Id = id, Status = status, ApproverId = approverId, Comments = comments },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE AccessRequests SET Status = 'Cancelled', UpdatedAt = GETUTCDATE()
                WHERE Id = @Id AND Status = 'Pending'",
                new { Id = id },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }
}
