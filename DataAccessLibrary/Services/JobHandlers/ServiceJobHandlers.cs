using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DataAccessLibrary.Services.JobHandlers;

/// <summary>
/// Handles PolicyEvaluation jobs — runs the compliance policy engine for a specific policy.
/// </summary>
public class PolicyEvaluationJobHandler : IJobTypeHandler
{
    public string JobType => "PolicyEvaluation";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();
        // PolicyEvaluationEngine is in AccessReview project — resolve dynamically
        var engineType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "IPolicyEvaluationEngine");

        if (engineType == null)
        {
            logger.LogWarning("PolicyEvaluationJobHandler: IPolicyEvaluationEngine not found in assemblies");
            return;
        }

        var engine = sp.GetService(engineType);
        if (engine == null)
        {
            logger.LogWarning("PolicyEvaluationJobHandler: IPolicyEvaluationEngine not registered in DI");
            return;
        }

        if (job.RelatedEntityId.HasValue)
        {
            // Single policy evaluation
            var method = engineType.GetMethod("ExecuteSinglePolicyAsync");
            if (method != null)
            {
                logger.LogInformation("PolicyEvaluationJobHandler: evaluating policy {PolicyId}", job.RelatedEntityId);
                await (Task)method.Invoke(engine, new object[] { job.RelatedEntityId.Value, "Distributed", ct })!;
            }
        }
        else
        {
            // All policies evaluation
            var method = engineType.GetMethod("ProcessScheduledEvaluationsAsync");
            if (method != null)
            {
                logger.LogInformation("PolicyEvaluationJobHandler: evaluating all scheduled policies");
                await (Task)method.Invoke(engine, new object[] { ct })!;
            }
        }
    }
}

/// <summary>Handles EmailQueueProcessing jobs.</summary>
public class EmailQueueJobHandler : IJobTypeHandler
{
    public string JobType => "EmailQueueProcessing";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var emailService = sp.GetRequiredService<IEmailService>();
        await emailService.ProcessEmailQueueAsync(ct);
    }
}

/// <summary>Handles LicenseThresholdMonitor jobs.</summary>
public class LicenseThresholdJobHandler : IJobTypeHandler
{
    public string JobType => "LicenseThresholdMonitor";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var monitor = sp.GetRequiredService<ILicenseThresholdMonitorService>();
        await monitor.EvaluateAllPoolsAsync(ct);
    }
}

/// <summary>Handles LicenseLifecycleEvaluation jobs.</summary>
public class LicenseLifecycleJobHandler : IJobTypeHandler
{
    public string JobType => "LicenseLifecycleEvaluation";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var lifecycle = sp.GetRequiredService<ILicenseLifecycleService>();
        await lifecycle.EvaluateStateTransitionsAsync(ct: ct);
    }
}

/// <summary>Handles SystemMaintenance jobs (cleanup, stats update).</summary>
public class SystemMaintenanceJobHandler : IJobTypeHandler
{
    public string JobType => "SystemMaintenance";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();
        var registry = sp.GetRequiredService<IExecutionServerRegistry>();

        logger.LogInformation("SystemMaintenanceJobHandler: running cleanup tasks");

        // Clean old heartbeats
        await registry.CleanupOldHeartbeatsAsync(retentionDays: 7, ct);

        logger.LogInformation("SystemMaintenanceJobHandler: cleanup complete");
    }
}

/// <summary>Handles ReportGeneration jobs.</summary>
public class ReportGenerationJobHandler : IJobTypeHandler
{
    public string JobType => "ReportGeneration";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();

        if (!job.RelatedEntityId.HasValue)
        {
            logger.LogWarning("ReportGenerationJobHandler: no RelatedEntityId (ReportId) provided");
            return;
        }

        // Resolve IReportRepository dynamically (it's in DataAccessLibrary)
        var repoType = typeof(Repositories.IReportRepository);
        var repo = sp.GetService(repoType);
        if (repo == null)
        {
            logger.LogWarning("ReportGenerationJobHandler: IReportRepository not available");
            return;
        }

        logger.LogInformation("ReportGenerationJobHandler: generating report {ReportId}", job.RelatedEntityId);
        // Report execution would be called here
        await Task.CompletedTask; // Placeholder — actual report execution needs ReportRunner service
    }
}

/// <summary>Handles Escalation jobs (overdue violations, reviews).</summary>
public class EscalationJobHandler : IJobTypeHandler
{
    public string JobType => "Escalation";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();
        logger.LogInformation("EscalationJobHandler: checking for overdue violations requiring escalation");

        // Resolve ICampaignService dynamically
        var campaignServiceType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "ICampaignService");

        if (campaignServiceType != null)
        {
            var campaignService = sp.GetService(campaignServiceType);
            if (campaignService != null)
            {
                var method = campaignServiceType.GetMethod("ProcessEscalationsAsync");
                if (method != null)
                {
                    await (Task)method.Invoke(campaignService, new object[] { ct })!;
                    return;
                }
            }
        }

        logger.LogDebug("EscalationJobHandler: ICampaignService.ProcessEscalationsAsync not available");
    }
}

/// <summary>Handles IntegrityCalculation jobs — computes data integrity scores for all identities.</summary>
public class IntegrityCalculationJobHandler : IJobTypeHandler
{
    public string JobType => "IntegrityCalculation";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();

        // Dev-loop guard (mirrors IntegrityCalculationJob.Execute): the full integrity recompute
        // is heavy against the dev DB. The Quartz job skips in Development, but it also enqueues an
        // "IntegrityCalculation" job-queue entry that lands here — so this handler must skip too,
        // otherwise the loop still pounds the dev box. Still runs in Staging/Production.
        var aspnetEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.Equals(aspnetEnv, "Development", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("IntegrityCalculationJobHandler skipped in Development environment");
            return;
        }

        var engineType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "IntegrityScoreEngine");

        if (engineType != null)
        {
            var engine = sp.GetService(engineType);
            if (engine != null)
            {
                var method = engineType.GetMethod("CalculateAllIntegritiesAsync");
                if (method != null)
                {
                    logger.LogInformation("IntegrityCalculationJobHandler: calculating integrity scores for all identities");
                    await (Task)method.Invoke(engine, new object[] { ct })!;
                    return;
                }
            }
        }
        logger.LogDebug("IntegrityCalculationJobHandler: IntegrityScoreEngine not available");
    }
}

/// <summary>Handles BulkIssueMonitor jobs — detects data quality issues across the population.</summary>
public class BulkIssueMonitorJobHandler : IJobTypeHandler
{
    public string JobType => "BulkIssueMonitor";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();
        var serviceType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "IBulkInsightService");

        if (serviceType != null)
        {
            var service = sp.GetService(serviceType);
            if (service != null)
            {
                var method = serviceType.GetMethod("GetAllBulkInsightsAsync");
                if (method != null)
                {
                    logger.LogInformation("BulkIssueMonitorJobHandler: scanning for bulk issues");
                    await (Task)method.Invoke(service, new object[] { ct })!;
                    return;
                }
            }
        }
        logger.LogDebug("BulkIssueMonitorJobHandler: IBulkInsightService not available");
    }
}

/// <summary>Handles EffectiveAccessMaterialization jobs — materializes computed access data.</summary>
public class EffectiveAccessMaterializationJobHandler : IJobTypeHandler
{
    public string JobType => "EffectiveAccessMaterialization";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();
        var serviceType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "IEffectiveAccessService");

        if (serviceType != null)
        {
            var service = sp.GetService(serviceType);
            if (service != null)
            {
                var method = serviceType.GetMethod("MaterializeAllAsync");
                if (method != null)
                {
                    logger.LogInformation("EffectiveAccessMaterializationJobHandler: materializing effective access");
                    await (Task)method.Invoke(service, new object[] { ct })!;
                    return;
                }
            }
        }
        logger.LogDebug("EffectiveAccessMaterializationJobHandler: IEffectiveAccessService not available");
    }
}

/// <summary>Handles EntropyCalculation jobs — computes access entropy and drift detection.</summary>
public class EntropyCalculationJobHandler : IJobTypeHandler
{
    public string JobType => "EntropyCalculation";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();
        var engineType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "IEntropyEngine");

        if (engineType != null)
        {
            var engine = sp.GetService(engineType);
            if (engine != null)
            {
                logger.LogInformation("EntropyCalculationJobHandler: calculating entropy and detecting drift");
                var calcMethod = engineType.GetMethod("CalculateAllEntropyAsync");
                if (calcMethod != null) await (Task)calcMethod.Invoke(engine, new object[] { ct })!;

                var driftMethod = engineType.GetMethod("DetectAllDriftAsync");
                if (driftMethod != null) await (Task)driftMethod.Invoke(engine, new object[] { ct })!;
                return;
            }
        }
        logger.LogDebug("EntropyCalculationJobHandler: IEntropyEngine not available");
    }
}

/// <summary>Handles ModelTraining jobs — trains ML.NET models and runs batch scoring.</summary>
public class ModelTrainingJobHandler : IJobTypeHandler
{
    public string JobType => "ModelTraining";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();
        var trainerType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "IModelTrainer");

        if (trainerType != null)
        {
            var trainer = sp.GetService(trainerType);
            if (trainer != null)
            {
                var method = trainerType.GetMethod("TrainAllModelsAsync");
                if (method != null)
                {
                    logger.LogInformation("ModelTrainingJobHandler: training ML models");
                    await (Task)method.Invoke(trainer, new object[] { ct })!;
                    return;
                }
            }
        }
        logger.LogDebug("ModelTrainingJobHandler: IModelTrainer not available");
    }
}

/// <summary>Handles ScheduledTrigger jobs — fires workflow triggers.</summary>
public class ScheduledTriggerJobHandler : IJobTypeHandler
{
    public string JobType => "ScheduledTrigger";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();

        if (!job.RelatedEntityId.HasValue)
        {
            logger.LogWarning("ScheduledTriggerJobHandler: no RelatedEntityId (TriggerId) provided");
            return;
        }

        var serviceType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "IWorkflowTriggerService");

        if (serviceType != null)
        {
            var service = sp.GetService(serviceType);
            if (service != null)
            {
                var method = serviceType.GetMethod("ExecuteTriggerAsync");
                if (method != null)
                {
                    logger.LogInformation("ScheduledTriggerJobHandler: firing trigger {TriggerId}", job.RelatedEntityId);
                    await (Task)method.Invoke(service, new object[] { job.RelatedEntityId.Value, ct })!;
                    return;
                }
            }
        }
        logger.LogDebug("ScheduledTriggerJobHandler: IWorkflowTriggerService not available");
    }
}

/// <summary>Handles AutoGovernance jobs — evaluates governance policies and executes actions.</summary>
public class AutoGovernanceJobHandler : IJobTypeHandler
{
    public string JobType => "AutoGovernance";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<IGlobalLogger>();
        logger.LogInformation("AutoGovernanceJobHandler: evaluating auto-governance policies");

        // AutoGovernance uses multiple services — resolve dynamically
        var evaluatorType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "IGovernancePolicyEvaluator");

        if (evaluatorType != null)
        {
            var evaluator = sp.GetService(evaluatorType);
            if (evaluator != null)
            {
                var method = evaluatorType.GetMethod("EvaluateAllAsync");
                if (method != null)
                {
                    await (Task)method.Invoke(evaluator, new object[] { ct })!;
                    return;
                }
            }
        }
        logger.LogDebug("AutoGovernanceJobHandler: IGovernancePolicyEvaluator not available");
    }
}
