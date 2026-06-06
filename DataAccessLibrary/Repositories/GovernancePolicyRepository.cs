using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based repository for governance policies and quarantine records.
/// </summary>
public class GovernancePolicyRepository : DapperRepositoryBase, IGovernancePolicyRepository
{
    public GovernancePolicyRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    // === Governance Policies ===

    public async Task<List<GovernanceModels.GovernancePolicy>> GetEnabledPoliciesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [GovernancePolicies]
            WHERE [IsEnabled] = 1
            ORDER BY [Priority]";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GovernanceModels.GovernancePolicy>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<List<GovernanceModels.GovernancePolicy>> GetAllPoliciesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM [GovernancePolicies] ORDER BY [Priority], [Name]";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GovernanceModels.GovernancePolicy>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<GovernanceModels.GovernancePolicy?> GetPolicyByIdAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM [GovernancePolicies] WHERE [Id] = @PolicyId";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleOrDefaultAsync<GovernanceModels.GovernancePolicy>(
                new CommandDefinition(sql, new { PolicyId = policyId }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<Guid> InsertPolicyAsync(GovernanceModels.GovernancePolicy policy, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
            INSERT INTO [GovernancePolicies]
                ([Id], [Name], [Description], [IsEnabled], [Priority], [TriggerConditions], [ActionType],
                 [ActionConfig], [RequiresApproval], [ConfidenceThreshold], [MaxActionsPerRun],
                 [CooldownHours], [ExcludeAdminAccounts], [CreatedAt], [CreatedBy])
            VALUES
                (@NewId, @Name, @Description, @IsEnabled, @Priority, @TriggerConditions, @ActionType,
                 @ActionConfig, @RequiresApproval, @ConfidenceThreshold, @MaxActionsPerRun,
                 @CooldownHours, @ExcludeAdminAccounts, SYSUTCDATETIME(), @CreatedBy);
            SELECT @NewId;";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<Guid>(new CommandDefinition(
                sql,
                new
                {
                    policy.Name, policy.Description, policy.IsEnabled, policy.Priority,
                    policy.TriggerConditions, policy.ActionType, policy.ActionConfig,
                    policy.RequiresApproval, policy.ConfidenceThreshold, policy.MaxActionsPerRun,
                    policy.CooldownHours, policy.ExcludeAdminAccounts, policy.CreatedBy
                },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task UpdatePolicyAsync(GovernanceModels.GovernancePolicy policy, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [GovernancePolicies]
            SET [Name] = @Name, [Description] = @Description, [IsEnabled] = @IsEnabled,
                [Priority] = @Priority, [TriggerConditions] = @TriggerConditions,
                [ActionType] = @ActionType, [ActionConfig] = @ActionConfig,
                [RequiresApproval] = @RequiresApproval, [ConfidenceThreshold] = @ConfidenceThreshold,
                [MaxActionsPerRun] = @MaxActionsPerRun, [CooldownHours] = @CooldownHours,
                [ExcludeAdminAccounts] = @ExcludeAdminAccounts, [ModifiedAt] = SYSUTCDATETIME()
            WHERE [Id] = @Id";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    policy.Id, policy.Name, policy.Description, policy.IsEnabled, policy.Priority,
                    policy.TriggerConditions, policy.ActionType, policy.ActionConfig,
                    policy.RequiresApproval, policy.ConfidenceThreshold, policy.MaxActionsPerRun,
                    policy.CooldownHours, policy.ExcludeAdminAccounts
                },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task TogglePolicyAsync(Guid policyId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [GovernancePolicies]
            SET [IsEnabled] = @IsEnabled, [ModifiedAt] = SYSUTCDATETIME()
            WHERE [Id] = @PolicyId";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql, new { PolicyId = policyId, IsEnabled = isEnabled }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    // === Quarantine Records ===

    public async Task<Guid> InsertQuarantineRecordAsync(GovernanceModels.QuarantineRecord record, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
            INSERT INTO [QuarantineRecords]
                ([Id], [IdentityId], [ObjectId], [GovernancePolicyId], [QuarantineType],
                 [PreviousOU], [QuarantineOU], [PreviousEnabled], [RemovedGroupIds],
                 [Reason], [QuarantinedAt], [QuarantinedBy], [ExpiresAt], [IsActive])
            VALUES
                (@NewId, @IdentityId, @ObjectId, @GovernancePolicyId, @QuarantineType,
                 @PreviousOU, @QuarantineOU, @PreviousEnabled, @RemovedGroupIds,
                 @Reason, SYSUTCDATETIME(), @QuarantinedBy, @ExpiresAt, 1);
            SELECT @NewId;";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<Guid>(new CommandDefinition(
                sql,
                new
                {
                    record.IdentityId, record.ObjectId, record.GovernancePolicyId,
                    record.QuarantineType, record.PreviousOU, record.QuarantineOU,
                    record.PreviousEnabled, record.RemovedGroupIds, record.Reason,
                    record.QuarantinedBy, record.ExpiresAt
                },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<GovernanceModels.QuarantineRecord>> GetActiveQuarantinesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT qr.*, i.[DisplayName]
            FROM [QuarantineRecords] qr
            LEFT JOIN [Identities] i ON qr.[IdentityId] = i.[Id]
            WHERE qr.[IsActive] = 1
            ORDER BY qr.[QuarantinedAt] DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GovernanceModels.QuarantineRecord>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<GovernanceModels.QuarantineRecord?> GetQuarantineByIdentityAsync(Guid identityId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [QuarantineRecords]
            WHERE [IdentityId] = @IdentityId AND [IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleOrDefaultAsync<GovernanceModels.QuarantineRecord>(
                new CommandDefinition(sql, new { IdentityId = identityId }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task ReleaseQuarantineAsync(Guid quarantineId, string releasedBy, string? releaseReason, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [QuarantineRecords]
            SET [IsActive] = 0, [ReleasedAt] = SYSUTCDATETIME(), [ReleasedBy] = @ReleasedBy, [ReleaseReason] = @ReleaseReason
            WHERE [Id] = @Id";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql, new { Id = quarantineId, ReleasedBy = releasedBy, ReleaseReason = releaseReason }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<GovernanceModels.QuarantineRecord>> GetExpiredQuarantinesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [QuarantineRecords]
            WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL AND [ExpiresAt] <= SYSUTCDATETIME()";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GovernanceModels.QuarantineRecord>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<int> GetActiveQuarantineCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM [QuarantineRecords] WHERE [IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<int>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        }, cancellationToken);
    }
}
