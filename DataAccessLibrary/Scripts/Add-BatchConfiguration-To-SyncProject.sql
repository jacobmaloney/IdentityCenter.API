-- =====================================================
-- Add Batch Configuration to SyncProjects Table
-- =====================================================
-- Author: Claude Code
-- Date: 2025-11-04
-- Description: Adds BatchSize, MaxConcurrentBatches, and BatchTimeoutSeconds columns
--              to SyncProjects table for configurable parallel batch processing
-- =====================================================

USE [IdentityCenter]
GO

-- Check if columns already exist before adding
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SyncProjects]') AND name = 'BatchSize')
BEGIN
    ALTER TABLE [dbo].[SyncProjects]
    ADD [BatchSize] INT NOT NULL DEFAULT 500;

    PRINT '✅ Added BatchSize column with default value 500';
END
ELSE
BEGIN
    PRINT '⚠️ BatchSize column already exists';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SyncProjects]') AND name = 'MaxConcurrentBatches')
BEGIN
    ALTER TABLE [dbo].[SyncProjects]
    ADD [MaxConcurrentBatches] INT NOT NULL DEFAULT 1;

    PRINT '✅ Added MaxConcurrentBatches column with default value 1 (sequential)';
END
ELSE
BEGIN
    PRINT '⚠️ MaxConcurrentBatches column already exists';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SyncProjects]') AND name = 'BatchTimeoutSeconds')
BEGIN
    ALTER TABLE [dbo].[SyncProjects]
    ADD [BatchTimeoutSeconds] INT NOT NULL DEFAULT 120;

    PRINT '✅ Added BatchTimeoutSeconds column with default value 120';
END
ELSE
BEGIN
    PRINT '⚠️ BatchTimeoutSeconds column already exists';
END
GO

-- Display current SyncProjects with new batch configuration
SELECT
    Id,
    Name,
    BatchSize,
    MaxConcurrentBatches,
    BatchTimeoutSeconds,
    IsEnabled,
    IsRunning
FROM [dbo].[SyncProjects]
ORDER BY Name;
GO

PRINT '🎯 Migration complete! All SyncProjects now have batch configuration columns.';
PRINT 'Default values: BatchSize=500, MaxConcurrentBatches=1, BatchTimeoutSeconds=120';
PRINT '';
PRINT '📝 Recommended BatchSize values:';
PRINT '   • 500: Safe default for most scenarios';
PRINT '   • 1000: Good balance of speed and safety';
PRINT '   • 2000-5000: Fast, but requires good network to remote SQL Server';
PRINT '';
PRINT '⚡ Recommended MaxConcurrentBatches values:';
PRINT '   • 1: Sequential (safe, predictable)';
PRINT '   • 2-4: Moderate parallelism (2-4x faster)';
PRINT '   • 5-10: High parallelism (maximum speed, high load)';
GO
