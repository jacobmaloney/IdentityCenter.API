-- V119: Rename Quartz JOB_CLASS_NAME entries after the Phase 3 namespace move
-- (commit 66f770a4 — "Phase 3: consolidate 12 Quartz jobs into Schedule project").
--
-- Before the move, jobs were registered under namespaces like "AccessReview.Jobs.X"
-- in three different assemblies (AccessReview, Processes, DataAccessLibrary). The
-- move rehomed them all under "Schedule.Jobs.X" inside the Schedule assembly.
-- The QRTZ_JOB_DETAILS rows still hold the OLD type strings, so on startup Quartz
-- crashes with TypeLoadException when it tries to recover misfired triggers.
--
-- This script rewrites JOB_CLASS_NAME for every affected job so existing scheduled
-- jobs (cron triggers, misfired triggers, durable jobs) bind to the new types.
-- Idempotent — re-running matches zero rows after the first successful run.

-- AccessReview/Jobs/* (8 jobs)
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.CampaignCompletionJob, Schedule'
    WHERE JOB_CLASS_NAME = N'AccessReview.Jobs.CampaignCompletionJob, AccessReview';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.DatabaseMaintenanceJob, Schedule'
    WHERE JOB_CLASS_NAME = N'AccessReview.Jobs.DatabaseMaintenanceJob, AccessReview';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.FrameworkComplianceRefreshJob, Schedule'
    WHERE JOB_CLASS_NAME = N'AccessReview.Jobs.FrameworkComplianceRefreshJob, AccessReview';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.LogCleanupJob, Schedule'
    WHERE JOB_CLASS_NAME = N'AccessReview.Jobs.LogCleanupJob, AccessReview';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.PolicyEvaluationJob, Schedule'
    WHERE JOB_CLASS_NAME = N'AccessReview.Jobs.PolicyEvaluationJob, AccessReview';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.ReviewReminderJob, Schedule'
    WHERE JOB_CLASS_NAME = N'AccessReview.Jobs.ReviewReminderJob, AccessReview';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.SessionCleanupJob, Schedule'
    WHERE JOB_CLASS_NAME = N'AccessReview.Jobs.SessionCleanupJob, AccessReview';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.SinglePolicyEvaluationJob, Schedule'
    WHERE JOB_CLASS_NAME = N'AccessReview.Jobs.SinglePolicyEvaluationJob, AccessReview';

-- Processes/Jobs/* (3 jobs)
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.ApprovalEscalationJob, Schedule'
    WHERE JOB_CLASS_NAME = N'Processes.Jobs.ApprovalEscalationJob, Processes';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.ProcessInstanceWorkerJob, Schedule'
    WHERE JOB_CLASS_NAME = N'Processes.Jobs.ProcessInstanceWorkerJob, Processes';
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.ProcessResumeJob, Schedule'
    WHERE JOB_CLASS_NAME = N'Processes.Jobs.ProcessResumeJob, Processes';

-- DataAccessLibrary/Jobs/* (1 job)
UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = N'Schedule.Jobs.IdentityLinkerJob, Schedule'
    WHERE JOB_CLASS_NAME = N'DataAccessLibrary.Jobs.IdentityLinkerJob, DataAccessLibrary';
GO
