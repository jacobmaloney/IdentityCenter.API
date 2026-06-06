using System.Data;
using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// High-performance Dapper repository for compliance policy data access
/// EF Core is ONLY for migrations - Dapper for ALL queries for speed
/// </summary>
public class PolicyRepository : DapperRepositoryBase, IPolicyRepository
{
    public PolicyRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<List<CompliancePolicy>> GetAllPoliciesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetAllPoliciesAsync));

        try
        {
            _logger.LogDebug("Opening database connection for policy query");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, Name, Description, Category, PolicyType, TargetEntityType, Severity, Priority, IsActive, IsBuiltIn,
                    EvaluationFrequencyHours, LastEvaluationDate, NextEvaluationDate,
                    ComplianceFramework, CurrentScope, LastViolationCount, LastActionCount,
                    IsRunning, LastRunAt, TotalExecutions,
                    ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds, ScopeInheritance,
                    RemoveOutOfScopeViolations, EnforcementMode,
                    ProcessingLimitPerRun,
                    ProcessedThisRun,
                    SlaCriticalHours, SlaHighHours, SlaMediumHours, SlaLowHours,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM CompliancePolicies
                ORDER BY Name";

            var policies = await connection.QueryAsync<CompliancePolicy>(
                new CommandDefinition(
                    sql,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = policies.ToList();

            _logger.LogInformation("Retrieved {Count} compliance policies", list.Count);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetAllPoliciesAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAllPoliciesAsync));
        }
    }

    public async Task<List<CompliancePolicy>> GetActivePoliciesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetActivePoliciesAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, Name, Description, Category, PolicyType, TargetEntityType, Severity, Priority, IsActive, IsBuiltIn,
                    EvaluationFrequencyHours, LastEvaluationDate, NextEvaluationDate,
                    ComplianceFramework, CurrentScope, LastViolationCount, LastActionCount,
                    ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds, ScopeInheritance,
                    RemoveOutOfScopeViolations, EnforcementMode,
                    ProcessingLimitPerRun,
                    ProcessedThisRun,
                    SlaCriticalHours, SlaHighHours, SlaMediumHours, SlaLowHours,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM CompliancePolicies
                WHERE IsActive = 1
                ORDER BY Severity, Name";

            var policies = await connection.QueryAsync<CompliancePolicy>(
                new CommandDefinition(
                    sql,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = policies.ToList();

            _logger.LogInformation("Retrieved {Count} active compliance policies", list.Count);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetActivePoliciesAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetActivePoliciesAsync));
        }
    }

    public async Task<CompliancePolicy?> GetPolicyByIdAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        if (policyId == Guid.Empty)
            throw new ArgumentException("Policy ID cannot be empty", nameof(policyId));

        _logger.LogMethodEntry(nameof(GetPolicyByIdAsync), new { policyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, Name, Description, Category, PolicyType, TargetEntityType, Severity, Priority, IsActive, IsBuiltIn,
                    EvaluationFrequencyHours, LastEvaluationDate, NextEvaluationDate,
                    ComplianceFramework, CurrentScope, LastViolationCount, LastActionCount,
                    ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds, ScopeInheritance,
                    RemoveOutOfScopeViolations, EnforcementMode,
                    ProcessingLimitPerRun,
                    ProcessedThisRun,
                    SlaCriticalHours, SlaHighHours, SlaMediumHours, SlaLowHours,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM CompliancePolicies
                WHERE Id = @PolicyId";

            var policy = await connection.QueryFirstOrDefaultAsync<CompliancePolicy>(
                new CommandDefinition(
                    sql,
                    new { PolicyId = policyId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            if (policy != null)
            {
                _logger.LogDebug("Found policy {PolicyId}: {PolicyName}", policyId, policy.Name);
            }

            return policy;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetPolicyByIdAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetPolicyByIdAsync));
        }
    }

    public async Task<List<CompliancePolicy>> GetPoliciesByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category cannot be null or empty", nameof(category));

        _logger.LogMethodEntry(nameof(GetPoliciesByCategoryAsync), new { category });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, Name, Description, Category, PolicyType, TargetEntityType, Severity, Priority, IsActive, IsBuiltIn,
                    EvaluationFrequencyHours, LastEvaluationDate, NextEvaluationDate,
                    ComplianceFramework, CurrentScope, LastViolationCount, LastActionCount,
                    ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds, ScopeInheritance,
                    RemoveOutOfScopeViolations, EnforcementMode,
                    ProcessingLimitPerRun,
                    ProcessedThisRun,
                    SlaCriticalHours, SlaHighHours, SlaMediumHours, SlaLowHours,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM CompliancePolicies
                WHERE Category = @Category
                ORDER BY Severity, Name";

            var policies = await connection.QueryAsync<CompliancePolicy>(
                new CommandDefinition(
                    sql,
                    new { Category = category },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = policies.ToList();

            _logger.LogInformation("Retrieved {Count} policies in category {Category}", list.Count, category);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetPoliciesByCategoryAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetPoliciesByCategoryAsync));
        }
    }

    public async Task<CompliancePolicy> CreatePolicyAsync(CompliancePolicy policy, CancellationToken cancellationToken = default)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));

        _logger.LogMethodEntry(nameof(CreatePolicyAsync), new { policyName = policy.Name });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                INSERT INTO CompliancePolicies
                (Id, Name, Description, Category, PolicyType, TargetEntityType, Severity, Priority, IsActive, IsBuiltIn, IsRunning, TotalExecutions,
                 EvaluationFrequencyHours, ComplianceFramework, CurrentScope, LastViolationCount, LastActionCount,
                 EnforcementMode, DailyProcessedCount, FirstReminderDelayDays, ReminderIntervalDays, EnableReminderSchedule,
                 RemoveOutOfScopeViolations, ProcessingLimitPerRun,
                 SlaCriticalHours, SlaHighHours, SlaMediumHours, SlaLowHours,
                 ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds, ScopeInheritance,
                 CreatedAt, CreatedBy)
                VALUES
                (@Id, @Name, @Description, @Category, @PolicyType, @TargetEntityType, @Severity, @Priority, @IsActive, @IsBuiltIn, @IsRunning, @TotalExecutions,
                 @EvaluationFrequencyHours, @ComplianceFramework, @CurrentScope, @LastViolationCount, @LastActionCount,
                 @EnforcementMode, @DailyProcessedCount, @FirstReminderDelayDays, @ReminderIntervalDays, @EnableReminderSchedule,
                 @RemoveOutOfScopeViolations, @ProcessingLimitPerRun,
                 @SlaCriticalHours, @SlaHighHours, @SlaMediumHours, @SlaLowHours,
                 @ScopeConnectionIds, @ScopeTags, @ScopeAttributeQuery, @ScopeGroupIds, @ScopeInheritance,
                 @CreatedAt, @CreatedBy)";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    policy,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Created compliance policy {PolicyId}: {PolicyName}", policy.Id, policy.Name);

            return policy;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreatePolicyAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreatePolicyAsync));
        }
    }

    public async Task<CompliancePolicy> UpdatePolicyAsync(CompliancePolicy policy, CancellationToken cancellationToken = default)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));

        _logger.LogMethodEntry(nameof(UpdatePolicyAsync), new { policyId = policy.Id });

        try
        {
            policy.ModifiedAt = DateTime.UtcNow;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE CompliancePolicies
                SET Name = @Name,
                    Description = @Description,
                    Category = @Category,
                    PolicyType = @PolicyType,
                    TargetEntityType = @TargetEntityType,
                    Severity = @Severity,
                    Priority = @Priority,
                    IsActive = @IsActive,
                    EvaluationFrequencyHours = @EvaluationFrequencyHours,
                    ComplianceFramework = @ComplianceFramework,
                    ScopeConnectionIds = @ScopeConnectionIds,
                    ScopeTags = @ScopeTags,
                    ScopeAttributeQuery = @ScopeAttributeQuery,
                    ScopeGroupIds = @ScopeGroupIds,
                    ScopeInheritance = @ScopeInheritance,
                    RemoveOutOfScopeViolations = @RemoveOutOfScopeViolations,
                    EnforcementMode = @EnforcementMode,
                    ProcessingLimitPerRun = @ProcessingLimitPerRun,
                    SlaCriticalHours = @SlaCriticalHours,
                    SlaHighHours = @SlaHighHours,
                    SlaMediumHours = @SlaMediumHours,
                    SlaLowHours = @SlaLowHours,
                    ModifiedAt = @ModifiedAt,
                    ModifiedBy = @ModifiedBy
                WHERE Id = @Id";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    policy,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Updated compliance policy {PolicyId}: {PolicyName}", policy.Id, policy.Name);

            return policy;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdatePolicyAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdatePolicyAsync));
        }
    }

    public async Task DeletePolicyAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        if (policyId == Guid.Empty)
            throw new ArgumentException("Policy ID cannot be empty", nameof(policyId));

        _logger.LogMethodEntry(nameof(DeletePolicyAsync), new { policyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            try
            {
                // Delete in correct order to avoid FK constraint violations
                // 1. Delete violations
                var violationsDeleted = await connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM CompliancePolicyViolations WHERE CompliancePolicyId = @PolicyId",
                        new { PolicyId = policyId },
                        transaction: transaction,
                        commandTimeout: 60,
                        cancellationToken: cancellationToken));
                _logger.LogDebug("Deleted {Count} violations for policy {PolicyId}", violationsDeleted, policyId);

                // 2. Delete execution history
                var executionsDeleted = await connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM CompliancePolicyExecutions WHERE CompliancePolicyId = @PolicyId",
                        new { PolicyId = policyId },
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                _logger.LogDebug("Deleted {Count} executions for policy {PolicyId}", executionsDeleted, policyId);

                // 3. Delete framework mappings
                var mappingsDeleted = await connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM ComplianceFrameworkPolicyMappings WHERE CompliancePolicyId = @PolicyId",
                        new { PolicyId = policyId },
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                _logger.LogDebug("Deleted {Count} framework mappings for policy {PolicyId}", mappingsDeleted, policyId);

                // 4. Delete actions
                var actionsDeleted = await connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM CompliancePolicyAction WHERE CompliancePolicyId = @PolicyId",
                        new { PolicyId = policyId },
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                _logger.LogDebug("Deleted {Count} actions for policy {PolicyId}", actionsDeleted, policyId);

                // 5. Delete rules
                var rulesDeleted = await connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM CompliancePolicyRule WHERE CompliancePolicyId = @PolicyId",
                        new { PolicyId = policyId },
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                _logger.LogDebug("Deleted {Count} rules for policy {PolicyId}", rulesDeleted, policyId);

                // 6. Delete the policy itself
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM CompliancePolicies WHERE Id = @PolicyId",
                        new { PolicyId = policyId },
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Deleted compliance policy {PolicyId} with {Violations} violations, {Executions} executions, {Mappings} mappings, {Actions} actions, {Rules} rules",
                    policyId, violationsDeleted, executionsDeleted, mappingsDeleted, actionsDeleted, rulesDeleted);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeletePolicyAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeletePolicyAsync));
        }
    }

    public async Task<List<CompliancePolicyRule>> GetPolicyRulesAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetPolicyRulesAsync), new { policyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, CompliancePolicyId, Name, Description, RuleType,
                    FieldName, Operator, ComparisonValue, DaysOffset,
                    Weight, SortOrder, IsActive, CreatedAt
                FROM CompliancePolicyRule
                WHERE CompliancePolicyId = @PolicyId
                    AND IsActive = 1
                ORDER BY SortOrder";

            var rules = await connection.QueryAsync<CompliancePolicyRule>(
                new CommandDefinition(
                    sql,
                    new { PolicyId = policyId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = rules.ToList();
            _logger.LogInformation("Retrieved {Count} rules for policy {PolicyId}", list.Count, policyId);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetPolicyRulesAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetPolicyRulesAsync));
        }
    }

    public async Task<List<CompliancePolicyAction>> GetPolicyActionsAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetPolicyActionsAsync), new { policyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, CompliancePolicyId, Name, Description, ActionType,
                    ExecutionTiming, DelayMinutes, RequiresApproval,
                    MaxExecutions, Priority, Configuration, IsActive,
                    CreatedAt
                FROM CompliancePolicyAction
                WHERE CompliancePolicyId = @PolicyId
                    AND IsActive = 1
                ORDER BY Priority";

            var actions = await connection.QueryAsync<CompliancePolicyAction>(
                new CommandDefinition(
                    sql,
                    new { PolicyId = policyId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = actions.ToList();
            _logger.LogInformation("Retrieved {Count} actions for policy {PolicyId}", list.Count, policyId);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetPolicyActionsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetPolicyActionsAsync));
        }
    }

    public async Task<CompliancePolicyRule> CreatePolicyRuleAsync(CompliancePolicyRule rule, CancellationToken cancellationToken = default)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        _logger.LogMethodEntry(nameof(CreatePolicyRuleAsync), new { ruleName = rule.Name, policyId = rule.CompliancePolicyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                INSERT INTO CompliancePolicyRule
                (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator,
                 ComparisonValue, DaysOffset, Weight, SortOrder, IsActive,
                 LogicalOperator, GroupOperator, RuleGroupId, RuleGroupName, CreatedAt)
                VALUES
                (@Id, @CompliancePolicyId, @Name, @Description, @RuleType, @FieldName, @Operator,
                 @ComparisonValue, @DaysOffset, @Weight, @SortOrder, @IsActive,
                 @LogicalOperator, @GroupOperator, @RuleGroupId, @RuleGroupName, @CreatedAt)";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    rule,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Created policy rule {RuleId}: {RuleName} for policy {PolicyId}", rule.Id, rule.Name, rule.CompliancePolicyId);

            return rule;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreatePolicyRuleAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreatePolicyRuleAsync));
        }
    }

    public async Task<CompliancePolicyRule> UpdatePolicyRuleAsync(CompliancePolicyRule rule, CancellationToken cancellationToken = default)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        _logger.LogMethodEntry(nameof(UpdatePolicyRuleAsync), new { ruleId = rule.Id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE CompliancePolicyRule
                SET Name = @Name,
                    Description = @Description,
                    RuleType = @RuleType,
                    FieldName = @FieldName,
                    Operator = @Operator,
                    ComparisonValue = @ComparisonValue,
                    DaysOffset = @DaysOffset,
                    Weight = @Weight,
                    SortOrder = @SortOrder,
                    IsActive = @IsActive,
                    LogicalOperator = @LogicalOperator,
                    GroupOperator = @GroupOperator,
                    RuleGroupId = @RuleGroupId,
                    RuleGroupName = @RuleGroupName
                WHERE Id = @Id";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    rule,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Updated policy rule {RuleId}: {RuleName}", rule.Id, rule.Name);

            return rule;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdatePolicyRuleAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdatePolicyRuleAsync));
        }
    }

    public async Task DeletePolicyRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(DeletePolicyRuleAsync), new { ruleId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = "DELETE FROM CompliancePolicyRule WHERE Id = @RuleId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { RuleId = ruleId },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Deleted policy rule {RuleId}", ruleId);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeletePolicyRuleAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeletePolicyRuleAsync));
        }
    }

    public async Task<CompliancePolicyAction> CreatePolicyActionAsync(CompliancePolicyAction action, CancellationToken cancellationToken = default)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        _logger.LogMethodEntry(nameof(CreatePolicyActionAsync), new { actionName = action.Name, policyId = action.CompliancePolicyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                INSERT INTO CompliancePolicyAction
                (Id, CompliancePolicyId, Name, Description, ActionType, ExecutionTiming,
                 DelayMinutes, RequiresApproval, MaxExecutions, Priority, Configuration, IsActive, CreatedAt)
                VALUES
                (@Id, @CompliancePolicyId, @Name, @Description, @ActionType, @ExecutionTiming,
                 @DelayMinutes, @RequiresApproval, @MaxExecutions, @Priority, @Configuration, @IsActive, @CreatedAt)";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    action,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Created policy action {ActionId}: {ActionName} for policy {PolicyId}", action.Id, action.Name, action.CompliancePolicyId);

            return action;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreatePolicyActionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreatePolicyActionAsync));
        }
    }

    public async Task<CompliancePolicyAction> UpdatePolicyActionAsync(CompliancePolicyAction action, CancellationToken cancellationToken = default)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        _logger.LogMethodEntry(nameof(UpdatePolicyActionAsync), new { actionId = action.Id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE CompliancePolicyAction
                SET Name = @Name,
                    Description = @Description,
                    ActionType = @ActionType,
                    ExecutionTiming = @ExecutionTiming,
                    DelayMinutes = @DelayMinutes,
                    RequiresApproval = @RequiresApproval,
                    MaxExecutions = @MaxExecutions,
                    Priority = @Priority,
                    Configuration = @Configuration,
                    IsActive = @IsActive
                WHERE Id = @Id";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    action,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Updated policy action {ActionId}: {ActionName}", action.Id, action.Name);

            return action;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdatePolicyActionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdatePolicyActionAsync));
        }
    }

    public async Task DeletePolicyActionAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(DeletePolicyActionAsync), new { actionId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = "DELETE FROM CompliancePolicyAction WHERE Id = @ActionId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { ActionId = actionId },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Deleted policy action {ActionId}", actionId);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeletePolicyActionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeletePolicyActionAsync));
        }
    }

    public async Task UpdatePolicyIsRunningAsync(Guid policyId, bool isRunning, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdatePolicyIsRunningAsync), new { policyId, isRunning });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE CompliancePolicies
                SET IsRunning = @IsRunning
                WHERE Id = @PolicyId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { PolicyId = policyId, IsRunning = isRunning },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Updated policy {PolicyId} IsRunning={IsRunning}", policyId, isRunning);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdatePolicyIsRunningAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdatePolicyIsRunningAsync));
        }
    }

    public async Task<CompliancePolicyExecution> CreatePolicyExecutionAsync(CompliancePolicyExecution execution, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(CreatePolicyExecutionAsync), new { execution.CompliancePolicyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                INSERT INTO CompliancePolicyExecutions (
                    Id, CompliancePolicyId, Status, StartedAt, CompletedAt,
                    DurationMs, UsersEvaluated, ViolationsFound, ActionsExecuted,
                    ErrorMessage, StackTrace, TriggerType, TriggeredBy
                )
                VALUES (
                    @Id, @CompliancePolicyId, @Status, @StartedAt, @CompletedAt,
                    @DurationMs, @UsersEvaluated, @ViolationsFound, @ActionsExecuted,
                    @ErrorMessage, @StackTrace, @TriggerType, @TriggeredBy
                )";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    execution,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Created policy execution {ExecutionId} for policy {PolicyId}",
                execution.Id, execution.CompliancePolicyId);

            return execution;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreatePolicyExecutionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreatePolicyExecutionAsync));
        }
    }

    public async Task UpdatePolicyExecutionAsync(CompliancePolicyExecution execution, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdatePolicyExecutionAsync), new { execution.Id });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE CompliancePolicyExecutions
                SET Status = @Status,
                    CompletedAt = @CompletedAt,
                    DurationMs = @DurationMs,
                    UsersEvaluated = @UsersEvaluated,
                    ViolationsFound = @ViolationsFound,
                    ActionsExecuted = @ActionsExecuted,
                    ErrorMessage = @ErrorMessage,
                    StackTrace = @StackTrace
                WHERE Id = @Id";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    execution,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            // Also update the policy with latest stats
            var updatePolicySql = @"
                UPDATE CompliancePolicies
                SET LastRunAt = @CompletedAt,
                    TotalExecutions = TotalExecutions + 1,
                    LastViolationCount = @ViolationsFound,
                    LastActionCount = @ActionsExecuted
                WHERE Id = @PolicyId";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    updatePolicySql,
                    new
                    {
                        PolicyId = execution.CompliancePolicyId,
                        CompletedAt = execution.CompletedAt,
                        ViolationsFound = execution.ViolationsFound,
                        ActionsExecuted = execution.ActionsExecuted
                    },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Updated policy execution {ExecutionId}", execution.Id);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdatePolicyExecutionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdatePolicyExecutionAsync));
        }
    }

    public async Task<CompliancePolicyExecution?> GetLatestExecutionAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetLatestExecutionAsync), new { policyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT TOP 1
                    Id, CompliancePolicyId, Status, StartedAt, CompletedAt,
                    DurationMs, UsersEvaluated, ViolationsFound, ActionsExecuted,
                    ErrorMessage, StackTrace, TriggerType, TriggeredBy
                FROM CompliancePolicyExecutions
                WHERE CompliancePolicyId = @PolicyId
                ORDER BY StartedAt DESC";

            var execution = await connection.QueryFirstOrDefaultAsync<CompliancePolicyExecution>(
                new CommandDefinition(
                    sql,
                    new { PolicyId = policyId },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Retrieved latest execution for policy {PolicyId}: {ExecutionId}",
                policyId, execution?.Id);

            return execution;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetLatestExecutionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetLatestExecutionAsync));
        }
    }

    public async Task<CompliancePolicy?> GetPolicyWithDetailsAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        if (policyId == Guid.Empty)
            throw new ArgumentException("Policy ID cannot be empty", nameof(policyId));

        _logger.LogMethodEntry(nameof(GetPolicyWithDetailsAsync), new { policyId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get policy
            var policy = await GetPolicyByIdAsync(policyId, cancellationToken);
            if (policy == null)
                return null;

            // Get rules
            policy.Rules = await GetPolicyRulesAsync(policyId, cancellationToken);

            // Get actions
            policy.Actions = await GetPolicyActionsAsync(policyId, cancellationToken);

            _logger.LogInformation("Retrieved policy {PolicyId} with {RuleCount} rules and {ActionCount} actions",
                policyId, policy.Rules.Count, policy.Actions.Count);

            return policy;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetPolicyWithDetailsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetPolicyWithDetailsAsync));
        }
    }

    public async Task<List<CompliancePolicy>> GetPoliciesByIdsAsync(IEnumerable<Guid> policyIds, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetPoliciesByIdsAsync));

        try
        {
            var ids = policyIds?.Where(id => id != Guid.Empty).ToList();
            if (ids == null || ids.Count == 0)
                return new List<CompliancePolicy>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                SELECT
                    Id, Name, Description, Category, PolicyType, TargetEntityType, Severity, Priority, IsActive, IsBuiltIn,
                    EvaluationFrequencyHours, LastEvaluationDate, NextEvaluationDate,
                    ComplianceFramework, CurrentScope, LastViolationCount, LastActionCount,
                    IsRunning, LastRunAt, TotalExecutions,
                    ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds, ScopeInheritance,
                    RemoveOutOfScopeViolations, EnforcementMode,
                    ProcessingLimitPerRun,
                    ProcessedThisRun,
                    SlaCriticalHours, SlaHighHours, SlaMediumHours, SlaLowHours,
                    CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                FROM CompliancePolicies
                WHERE Id IN @Ids
                ORDER BY Name";

            var policies = await connection.QueryAsync<CompliancePolicy>(
                new CommandDefinition(
                    sql,
                    new { Ids = ids },
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

            var list = policies.ToList();
            _logger.LogInformation("Retrieved {Count} policies by IDs", list.Count);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetPoliciesByIdsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetPoliciesByIdsAsync));
        }
    }

    public async Task<List<CompliancePolicy>> GetAllPoliciesWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetAllPoliciesWithDetailsAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get all policies
            var policies = await GetAllPoliciesAsync(cancellationToken);

            // Get all rules and actions in bulk
            var allRulesSql = @"
                SELECT
                    Id, CompliancePolicyId, Name, Description, RuleType,
                    FieldName, Operator, ComparisonValue, DaysOffset,
                    Weight, SortOrder, IsActive, LogicalOperator, GroupOperator,
                    RuleGroupId, RuleGroupName, CreatedAt
                FROM CompliancePolicyRule
                WHERE IsActive = 1
                ORDER BY CompliancePolicyId, SortOrder";

            var allActionsSql = @"
                SELECT
                    Id, CompliancePolicyId, Name, Description, ActionType,
                    ExecutionTiming, DelayMinutes, RequiresApproval,
                    MaxExecutions, Priority, Configuration, IsActive,
                    CreatedAt
                FROM CompliancePolicyAction
                WHERE IsActive = 1
                ORDER BY CompliancePolicyId, Priority";

            var allRules = (await connection.QueryAsync<CompliancePolicyRule>(
                new CommandDefinition(allRulesSql, commandTimeout: 30, cancellationToken: cancellationToken)))
                .ToList();

            var allActions = (await connection.QueryAsync<CompliancePolicyAction>(
                new CommandDefinition(allActionsSql, commandTimeout: 30, cancellationToken: cancellationToken)))
                .ToList();

            // Group by policy ID
            var rulesByPolicy = allRules.GroupBy(r => r.CompliancePolicyId).ToDictionary(g => g.Key, g => g.ToList());
            var actionsByPolicy = allActions.GroupBy(a => a.CompliancePolicyId).ToDictionary(g => g.Key, g => g.ToList());

            // Attach to policies
            foreach (var policy in policies)
            {
                policy.Rules = rulesByPolicy.TryGetValue(policy.Id, out var rules) ? rules : new List<CompliancePolicyRule>();
                policy.Actions = actionsByPolicy.TryGetValue(policy.Id, out var actions) ? actions : new List<CompliancePolicyAction>();
            }

            _logger.LogInformation("Retrieved {Count} policies with details", policies.Count);

            return policies;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetAllPoliciesWithDetailsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetAllPoliciesWithDetailsAsync));
        }
    }

    public async Task<CompliancePolicy> CopyPolicyAsync(Guid sourcePolicyId, string newName, bool enabled, string createdBy, CancellationToken cancellationToken = default)
    {
        if (sourcePolicyId == Guid.Empty)
            throw new ArgumentException("Source policy ID cannot be empty", nameof(sourcePolicyId));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("New name cannot be empty", nameof(newName));

        _logger.LogMethodEntry(nameof(CopyPolicyAsync), new { sourcePolicyId, newName, enabled });

        try
        {
            // Get source policy with details
            var source = await GetPolicyWithDetailsAsync(sourcePolicyId, cancellationToken);
            if (source == null)
                throw new InvalidOperationException($"Source policy {sourcePolicyId} not found");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            try
            {
                // Create the copy
                var copyId = Guid.NewGuid();
                var now = DateTime.UtcNow;

                var insertPolicySql = @"
                    INSERT INTO CompliancePolicies
                    (Id, Name, Description, Category, PolicyType, TargetEntityType, Severity, Priority, IsActive, IsBuiltIn, IsRunning, TotalExecutions,
                     EvaluationFrequencyHours, ComplianceFramework, CurrentScope, LastViolationCount, LastActionCount,
                     ScopeConnectionIds, ScopeTags, ScopeAttributeQuery, ScopeGroupIds, ScopeInheritance,
                     RemoveOutOfScopeViolations, EnforcementMode, ProcessingLimitPerRun, ProcessedThisRun,
                     FirstReminderDelayDays, ReminderIntervalDays, MaxReminderCount, EnableReminderSchedule,
                     SlaCriticalHours, SlaHighHours, SlaMediumHours, SlaLowHours,
                     DailyProcessedCount, CreatedAt, CreatedBy)
                    VALUES
                    (@Id, @Name, @Description, @Category, @PolicyType, @TargetEntityType, @Severity, @Priority, @IsActive, 0, 0, 0,
                     @EvaluationFrequencyHours, @ComplianceFramework, @CurrentScope, 0, 0,
                     @ScopeConnectionIds, @ScopeTags, @ScopeAttributeQuery, @ScopeGroupIds, @ScopeInheritance,
                     @RemoveOutOfScopeViolations, @EnforcementMode, @ProcessingLimitPerRun, 0,
                     @FirstReminderDelayDays, @ReminderIntervalDays, @MaxReminderCount, @EnableReminderSchedule,
                     @SlaCriticalHours, @SlaHighHours, @SlaMediumHours, @SlaLowHours,
                     0, @CreatedAt, @CreatedBy)";

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertPolicySql,
                        new
                        {
                            Id = copyId,
                            Name = newName.Trim(),
                            source.Description,
                            source.Category,
                            source.PolicyType,
                            source.TargetEntityType,
                            source.Severity,
                            source.Priority,
                            IsActive = enabled,
                            source.EvaluationFrequencyHours,
                            source.ComplianceFramework,
                            source.CurrentScope,
                            source.ScopeConnectionIds,
                            source.ScopeTags,
                            source.ScopeAttributeQuery,
                            source.ScopeGroupIds,
                            source.ScopeInheritance,
                            source.RemoveOutOfScopeViolations,
                            source.EnforcementMode,
                            ProcessingLimitPerRun = source.ProcessingLimitPerRun ?? 10,
                            source.FirstReminderDelayDays,
                            source.ReminderIntervalDays,
                            source.MaxReminderCount,
                            source.EnableReminderSchedule,
                            source.SlaCriticalHours,
                            source.SlaHighHours,
                            source.SlaMediumHours,
                            source.SlaLowHours,
                            CreatedAt = now,
                            CreatedBy = createdBy
                        },
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                // Copy rules
                if (source.Rules?.Any() == true)
                {
                    var insertRuleSql = @"
                        INSERT INTO CompliancePolicyRule
                        (Id, CompliancePolicyId, Name, Description, RuleType, FieldName, Operator,
                         ComparisonValue, DaysOffset, Weight, SortOrder, IsActive, LogicalOperator, GroupOperator,
                         RuleGroupId, RuleGroupName, CreatedAt)
                        VALUES
                        (@Id, @CompliancePolicyId, @Name, @Description, @RuleType, @FieldName, @Operator,
                         @ComparisonValue, @DaysOffset, @Weight, @SortOrder, @IsActive, @LogicalOperator, @GroupOperator,
                         @RuleGroupId, @RuleGroupName, @CreatedAt)";

                    foreach (var rule in source.Rules)
                    {
                        await connection.ExecuteAsync(
                            new CommandDefinition(
                                insertRuleSql,
                                new
                                {
                                    Id = Guid.NewGuid(),
                                    CompliancePolicyId = copyId,
                                    rule.Name,
                                    rule.Description,
                                    rule.RuleType,
                                    rule.FieldName,
                                    rule.Operator,
                                    rule.ComparisonValue,
                                    rule.DaysOffset,
                                    rule.Weight,
                                    rule.SortOrder,
                                    rule.IsActive,
                                    LogicalOperator = rule.LogicalOperator ?? "AND",
                                    GroupOperator = rule.GroupOperator ?? "AND",
                                    rule.RuleGroupId,
                                    rule.RuleGroupName,
                                    CreatedAt = now
                                },
                                transaction: transaction,
                                commandTimeout: 30,
                                cancellationToken: cancellationToken));
                    }
                }

                // Copy actions
                if (source.Actions?.Any() == true)
                {
                    var insertActionSql = @"
                        INSERT INTO CompliancePolicyAction
                        (Id, CompliancePolicyId, Name, Description, ActionType, ExecutionTiming,
                         DelayMinutes, RequiresApproval, MaxExecutions, Priority, Configuration, IsActive, CreatedAt)
                        VALUES
                        (@Id, @CompliancePolicyId, @Name, @Description, @ActionType, @ExecutionTiming,
                         @DelayMinutes, @RequiresApproval, @MaxExecutions, @Priority, @Configuration, @IsActive, @CreatedAt)";

                    foreach (var action in source.Actions)
                    {
                        await connection.ExecuteAsync(
                            new CommandDefinition(
                                insertActionSql,
                                new
                                {
                                    Id = Guid.NewGuid(),
                                    CompliancePolicyId = copyId,
                                    action.Name,
                                    action.Description,
                                    action.ActionType,
                                    action.ExecutionTiming,
                                    action.DelayMinutes,
                                    action.RequiresApproval,
                                    action.MaxExecutions,
                                    action.Priority,
                                    action.Configuration,
                                    action.IsActive,
                                    CreatedAt = now
                                },
                                transaction: transaction,
                                commandTimeout: 30,
                                cancellationToken: cancellationToken));
                    }
                }

                // Copy standing campaign if source policy has a CreateAccessReview action
                var hasAccessReviewAction = source.Actions?.Any(a => a.ActionType == "CreateAccessReview") == true;
                if (hasAccessReviewAction)
                {
                    var sourceCampaign = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                        SELECT TOP 1 * FROM Campaigns
                        WHERE SourcePolicyId = @PolicyId AND Status NOT IN ('Deleted', 'Archived')
                        ORDER BY CreatedAt DESC",
                        new { PolicyId = sourcePolicyId },
                        transaction);

                    var campaignId = Guid.NewGuid();
                    var campaignName = string.Concat(newName.Trim(), " - Standing Review");

                    await connection.ExecuteAsync(
                        new CommandDefinition(@"
                        INSERT INTO Campaigns (
                            Id, Name, Description, CampaignType, ReviewType, Status,
                            StartDate, EndDate, DueDate, ReviewPeriodDays,
                            CompletionPercentage, TotalAssignments, CompletedAssignments,
                            AutoGenerated, IsRecurring, RecurrencePattern,
                            EnableNotifications, ReminderDaysBefore,
                            AssignmentEmailTemplateId, ReminderEmailTemplateId,
                            SourcePolicyId,
                            OwnerId, OwnerName, NotificationCcEmails, EnableTeamsNotifications,
                            OnDenialAction, AutoRemediateOnDenial, OnIncompleteAction, OnApprovalAction, ExtensionDays,
                            CompletionActionsProcessed, CreatedBy, CreatedAt,
                            PolicyViolationFilter, IncludeNestedMemberships, MaxNestedDepth
                        ) VALUES (
                            @Id, @Name, @Description, 'ComplianceReview', 'UserAccess', 'Active',
                            @StartDate, @EndDate, @DueDate, @ReviewPeriodDays,
                            0, 0, 0,
                            1, 1, 'Continuous',
                            1, 3,
                            @AssignmentEmailTemplateId, @ReminderEmailTemplateId,
                            @SourcePolicyId,
                            @OwnerId, @OwnerName, @NotificationCcEmails, @EnableTeamsNotifications,
                            @OnDenialAction, @AutoRemediateOnDenial, @OnIncompleteAction, @OnApprovalAction, @ExtensionDays,
                            0, @CreatedBy, @CreatedAt,
                            0, 0, 10
                        )",
                        new
                        {
                            Id = campaignId,
                            Name = campaignName,
                            Description = string.Concat("Continuous campaign for ", newName.Trim(), " violation reviews. Cases are added automatically when violations are detected."),
                            StartDate = now,
                            EndDate = now.AddYears(1),
                            DueDate = sourceCampaign != null ? (DateTime?)sourceCampaign.DueDate ?? now.AddYears(1) : now.AddYears(1),
                            ReviewPeriodDays = sourceCampaign != null ? (int?)sourceCampaign.ReviewPeriodDays ?? 14 : 14,
                            AssignmentEmailTemplateId = sourceCampaign?.AssignmentEmailTemplateId,
                            ReminderEmailTemplateId = sourceCampaign?.ReminderEmailTemplateId,
                            SourcePolicyId = copyId,
                            OwnerId = sourceCampaign?.OwnerId,
                            OwnerName = (string?)(sourceCampaign?.OwnerName),
                            NotificationCcEmails = (string?)(sourceCampaign?.NotificationCcEmails),
                            EnableTeamsNotifications = sourceCampaign != null ? (bool)sourceCampaign.EnableTeamsNotifications : false,
                            OnDenialAction = (string?)(sourceCampaign?.OnDenialAction) ?? "RemoveFromGroup",
                            AutoRemediateOnDenial = sourceCampaign != null ? (bool)sourceCampaign.AutoRemediateOnDenial : true,
                            OnIncompleteAction = (string?)(sourceCampaign?.OnIncompleteAction) ?? "None",
                            OnApprovalAction = (string?)(sourceCampaign?.OnApprovalAction) ?? "Certify",
                            ExtensionDays = sourceCampaign != null ? (int?)sourceCampaign.ExtensionDays ?? 7 : 7,
                            CreatedBy = createdBy,
                            CreatedAt = now
                        },
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                    _logger.LogInformation("Created standing campaign {CampaignId} for copied policy {CopyId}", campaignId, copyId);
                }

                transaction.Commit();

                _logger.LogInformation("Copied policy {SourceId} to {CopyId} with name '{NewName}'",
                    sourcePolicyId, copyId, newName);

                // Return the new policy with details
                return await GetPolicyWithDetailsAsync(copyId, cancellationToken)
                    ?? throw new InvalidOperationException("Failed to retrieve copied policy");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CopyPolicyAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CopyPolicyAsync));
        }
    }
}
