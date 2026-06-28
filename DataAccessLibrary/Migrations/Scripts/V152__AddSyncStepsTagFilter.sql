-- V152: Add SyncSteps.TagFilter so a sync step can be scoped/filtered by tag.
--
-- WHY: The native sync engine already reads step.TagFilter (BuildTagFilterClause in
-- InternalSyncStepExecutor) and the step-editor UI lets an operator set a tag filter,
-- but the column was never added to the live schema. Saving a tag filter therefore
-- threw "Invalid column name 'TagFilter'". This adds the missing column so the
-- existing read/save path works.
--
-- Idempotent: guarded on COL_LENGTH so re-running (or running against a DB where the
-- column already exists) is a clean no-op. NULL-able with no default — existing rows
-- mean "no tag filter" exactly as the read code already treats NULL/empty.

IF COL_LENGTH('dbo.SyncSteps', 'TagFilter') IS NULL
BEGIN
    ALTER TABLE [dbo].[SyncSteps] ADD [TagFilter] NVARCHAR(500) NULL;
    PRINT 'V152: Added SyncSteps.TagFilter.';
END
ELSE
BEGIN
    PRINT 'V152: SyncSteps.TagFilter already present -- nothing to do.';
END
GO
