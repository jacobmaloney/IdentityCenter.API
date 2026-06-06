-- Migration: RenamePolicyThrottleColumnsToPerRun
-- Date: 2026-01-19
-- Description: Rename throttle columns from "Daily" to "PerRun" semantics
--              - DailyProcessingLimit -> ProcessingLimitPerRun
--              - DailyProcessedCount -> ProcessedThisRun
--              - LastProcessingResetDate -> CurrentRunStartedAt
--
-- Why: The throttle limit should be per-execution, not per-day.
--      If a policy runs 4 times a day, each run should have its own limit.

-- =============================================
-- Step 1: Rename DailyProcessingLimit to ProcessingLimitPerRun
-- =============================================

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'DailyProcessingLimit')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'ProcessingLimitPerRun')
BEGIN
    EXEC sp_rename 'CompliancePolicies.DailyProcessingLimit', 'ProcessingLimitPerRun', 'COLUMN';
    PRINT 'Renamed DailyProcessingLimit to ProcessingLimitPerRun';
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'ProcessingLimitPerRun')
BEGIN
    -- Column doesn't exist at all, add it
    ALTER TABLE CompliancePolicies ADD ProcessingLimitPerRun INT NULL DEFAULT 10;
    PRINT 'Added ProcessingLimitPerRun column (DailyProcessingLimit did not exist)';
END
GO

-- =============================================
-- Step 2: Rename DailyProcessedCount to ProcessedThisRun
-- =============================================

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'DailyProcessedCount')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'ProcessedThisRun')
BEGIN
    EXEC sp_rename 'CompliancePolicies.DailyProcessedCount', 'ProcessedThisRun', 'COLUMN';
    PRINT 'Renamed DailyProcessedCount to ProcessedThisRun';
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'ProcessedThisRun')
BEGIN
    -- Column doesn't exist at all, add it
    ALTER TABLE CompliancePolicies ADD ProcessedThisRun INT NOT NULL DEFAULT 0;
    PRINT 'Added ProcessedThisRun column (DailyProcessedCount did not exist)';
END
GO

-- =============================================
-- Step 3: Rename LastProcessingResetDate to CurrentRunStartedAt
-- =============================================

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'LastProcessingResetDate')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'CurrentRunStartedAt')
BEGIN
    EXEC sp_rename 'CompliancePolicies.LastProcessingResetDate', 'CurrentRunStartedAt', 'COLUMN';
    PRINT 'Renamed LastProcessingResetDate to CurrentRunStartedAt';
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'CurrentRunStartedAt')
BEGIN
    -- Column doesn't exist at all, add it
    ALTER TABLE CompliancePolicies ADD CurrentRunStartedAt DATETIME2 NULL;
    PRINT 'Added CurrentRunStartedAt column (LastProcessingResetDate did not exist)';
END
GO

-- Reset all ProcessedThisRun counters to 0 (clean slate)
UPDATE CompliancePolicies SET ProcessedThisRun = 0;
PRINT 'Reset all ProcessedThisRun counters to 0';
GO

PRINT 'Migration complete: RenamePolicyThrottleColumnsToPerRun';
GO
