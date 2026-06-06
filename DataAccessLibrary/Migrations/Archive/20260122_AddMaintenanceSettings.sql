-- ============================================================================
-- Migration: Add MaintenanceSettings table
-- Date: 2026-01-22
-- Purpose: Store configuration for automated maintenance jobs including
--          log cleanup, database optimization, and data retention policies.
-- ============================================================================

-- Create MaintenanceSettings table (singleton pattern - only one row with Id=1)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MaintenanceSettings')
BEGIN
    CREATE TABLE [dbo].[MaintenanceSettings] (
        [Id] INT NOT NULL DEFAULT 1,

        -- Log Retention Settings (in days, 0 = keep forever)
        [SyncLogRetentionDays] INT NOT NULL DEFAULT 30,
        [ChangeLogRetentionDays] INT NOT NULL DEFAULT 365,
        [SystemLogRetentionDays] INT NOT NULL DEFAULT 90,
        [JobHistoryRetentionDays] INT NOT NULL DEFAULT 30,
        [NotificationLogRetentionDays] INT NOT NULL DEFAULT 60,

        -- Database Maintenance Settings
        [EnableIndexMaintenance] BIT NOT NULL DEFAULT 1,
        [IndexReorganizeThreshold] INT NOT NULL DEFAULT 10,
        [IndexRebuildThreshold] INT NOT NULL DEFAULT 30,
        [EnableStatisticsUpdate] BIT NOT NULL DEFAULT 1,
        [StatisticsUpdateThreshold] INT NOT NULL DEFAULT 20,

        -- Session & Orphan Cleanup Settings
        [EnableSessionCleanup] BIT NOT NULL DEFAULT 1,
        [ExpiredSessionRetentionDays] INT NOT NULL DEFAULT 7,
        [EnableOrphanedDataCleanup] BIT NOT NULL DEFAULT 1,
        [OrphanedDataRetentionDays] INT NOT NULL DEFAULT 14,
        [EnableTempFileCleanup] BIT NOT NULL DEFAULT 1,
        [TempFileRetentionDays] INT NOT NULL DEFAULT 7,

        -- Job Schedules (Cron expressions)
        [LogCleanupSchedule] NVARCHAR(100) NOT NULL DEFAULT '0 0 2 * * ?',
        [IndexMaintenanceSchedule] NVARCHAR(100) NOT NULL DEFAULT '0 0 3 ? * SUN',
        [StatisticsUpdateSchedule] NVARCHAR(100) NOT NULL DEFAULT '0 30 3 * * ?',
        [SessionCleanupSchedule] NVARCHAR(100) NOT NULL DEFAULT '0 0 */6 * * ?',
        [OrphanedDataCleanupSchedule] NVARCHAR(100) NOT NULL DEFAULT '0 0 4 * * ?',

        -- Job Enabled Flags
        [LogCleanupEnabled] BIT NOT NULL DEFAULT 1,
        [IndexMaintenanceEnabled] BIT NOT NULL DEFAULT 1,
        [StatisticsUpdateEnabled] BIT NOT NULL DEFAULT 1,
        [SessionCleanupEnabled] BIT NOT NULL DEFAULT 1,
        [OrphanedDataCleanupEnabled] BIT NOT NULL DEFAULT 1,

        -- Last Run Tracking
        [LastLogCleanupRun] DATETIME2 NULL,
        [LastIndexMaintenanceRun] DATETIME2 NULL,
        [LastStatisticsUpdateRun] DATETIME2 NULL,
        [LastSessionCleanupRun] DATETIME2 NULL,
        [LastOrphanedDataCleanupRun] DATETIME2 NULL,

        -- Audit Columns
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [ModifiedAt] DATETIME2 NULL,
        [ModifiedBy] NVARCHAR(256) NOT NULL DEFAULT '',

        CONSTRAINT [PK_MaintenanceSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_MaintenanceSettings_SingleRow] CHECK ([Id] = 1)
    );

    PRINT 'Created MaintenanceSettings table';
END
ELSE
BEGIN
    PRINT 'MaintenanceSettings table already exists';
END
GO

-- Insert default settings row (singleton)
IF NOT EXISTS (SELECT 1 FROM [dbo].[MaintenanceSettings] WHERE [Id] = 1)
BEGIN
    INSERT INTO [dbo].[MaintenanceSettings] ([Id], [CreatedAt])
    VALUES (1, GETUTCDATE());

    PRINT 'Inserted default MaintenanceSettings row';
END
ELSE
BEGIN
    PRINT 'Default MaintenanceSettings row already exists';
END
GO

-- ============================================================================
-- Create stored procedure for Index Maintenance
-- This procedure analyzes index fragmentation and performs REORGANIZE or REBUILD
-- ============================================================================

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MaintenanceIndexOptimize')
    DROP PROCEDURE [dbo].[sp_MaintenanceIndexOptimize];
GO

CREATE PROCEDURE [dbo].[sp_MaintenanceIndexOptimize]
    @ReorganizeThreshold INT = 10,
    @RebuildThreshold INT = 30,
    @TableFilter NVARCHAR(128) = NULL,  -- Optional: filter to specific table
    @OnlineRebuild BIT = 1              -- Use ONLINE = ON for rebuilds (Enterprise only)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SchemaName NVARCHAR(128);
    DECLARE @TableName NVARCHAR(128);
    DECLARE @IndexName NVARCHAR(128);
    DECLARE @Fragmentation FLOAT;
    DECLARE @SQL NVARCHAR(MAX);
    DECLARE @IndexCount INT = 0;
    DECLARE @ReorganizeCount INT = 0;
    DECLARE @RebuildCount INT = 0;
    DECLARE @SkippedCount INT = 0;

    -- Create temp table for results
    CREATE TABLE #IndexActions (
        SchemaName NVARCHAR(128),
        TableName NVARCHAR(128),
        IndexName NVARCHAR(128),
        Fragmentation FLOAT,
        Action NVARCHAR(20),
        Success BIT,
        ErrorMessage NVARCHAR(MAX)
    );

    -- Cursor for indexes with fragmentation above threshold
    DECLARE index_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT
        s.name AS SchemaName,
        t.name AS TableName,
        i.name AS IndexName,
        ps.avg_fragmentation_in_percent AS Fragmentation
    FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
    INNER JOIN sys.indexes i ON ps.object_id = i.object_id AND ps.index_id = i.index_id
    INNER JOIN sys.tables t ON i.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE ps.avg_fragmentation_in_percent > @ReorganizeThreshold
      AND i.name IS NOT NULL
      AND ps.page_count > 1000  -- Only process indexes with significant pages
      AND (@TableFilter IS NULL OR t.name = @TableFilter)
    ORDER BY ps.avg_fragmentation_in_percent DESC;

    OPEN index_cursor;
    FETCH NEXT FROM index_cursor INTO @SchemaName, @TableName, @IndexName, @Fragmentation;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @IndexCount = @IndexCount + 1;

        BEGIN TRY
            IF @Fragmentation >= @RebuildThreshold
            BEGIN
                -- REBUILD for high fragmentation
                IF @OnlineRebuild = 1
                    SET @SQL = N'ALTER INDEX [' + @IndexName + N'] ON [' + @SchemaName + N'].[' + @TableName + N'] REBUILD WITH (ONLINE = ON)';
                ELSE
                    SET @SQL = N'ALTER INDEX [' + @IndexName + N'] ON [' + @SchemaName + N'].[' + @TableName + N'] REBUILD';

                EXEC sp_executesql @SQL;
                SET @RebuildCount = @RebuildCount + 1;

                INSERT INTO #IndexActions VALUES (@SchemaName, @TableName, @IndexName, @Fragmentation, 'REBUILD', 1, NULL);
            END
            ELSE
            BEGIN
                -- REORGANIZE for moderate fragmentation
                SET @SQL = N'ALTER INDEX [' + @IndexName + N'] ON [' + @SchemaName + N'].[' + @TableName + N'] REORGANIZE';
                EXEC sp_executesql @SQL;
                SET @ReorganizeCount = @ReorganizeCount + 1;

                INSERT INTO #IndexActions VALUES (@SchemaName, @TableName, @IndexName, @Fragmentation, 'REORGANIZE', 1, NULL);
            END
        END TRY
        BEGIN CATCH
            SET @SkippedCount = @SkippedCount + 1;
            INSERT INTO #IndexActions VALUES (@SchemaName, @TableName, @IndexName, @Fragmentation,
                CASE WHEN @Fragmentation >= @RebuildThreshold THEN 'REBUILD' ELSE 'REORGANIZE' END,
                0, ERROR_MESSAGE());
        END CATCH

        FETCH NEXT FROM index_cursor INTO @SchemaName, @TableName, @IndexName, @Fragmentation;
    END

    CLOSE index_cursor;
    DEALLOCATE index_cursor;

    -- Return results
    SELECT
        @IndexCount AS TotalIndexesProcessed,
        @ReorganizeCount AS ReorganizedCount,
        @RebuildCount AS RebuiltCount,
        @SkippedCount AS SkippedCount;

    SELECT * FROM #IndexActions ORDER BY Fragmentation DESC;

    DROP TABLE #IndexActions;
END
GO

PRINT 'Created sp_MaintenanceIndexOptimize stored procedure';
GO

-- ============================================================================
-- Create stored procedure for Statistics Update
-- ============================================================================

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MaintenanceUpdateStatistics')
    DROP PROCEDURE [dbo].[sp_MaintenanceUpdateStatistics];
GO

CREATE PROCEDURE [dbo].[sp_MaintenanceUpdateStatistics]
    @SamplePercent INT = 100,  -- Default to full scan
    @TableFilter NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TableName NVARCHAR(128);
    DECLARE @SchemaName NVARCHAR(128);
    DECLARE @SQL NVARCHAR(MAX);
    DECLARE @TableCount INT = 0;

    DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name AS SchemaName, t.name AS TableName
    FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE t.is_ms_shipped = 0
      AND (@TableFilter IS NULL OR t.name = @TableFilter)
    ORDER BY t.name;

    OPEN table_cursor;
    FETCH NEXT FROM table_cursor INTO @SchemaName, @TableName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            SET @SQL = N'UPDATE STATISTICS [' + @SchemaName + N'].[' + @TableName + N'] WITH SAMPLE ' + CAST(@SamplePercent AS NVARCHAR(3)) + N' PERCENT';
            EXEC sp_executesql @SQL;
            SET @TableCount = @TableCount + 1;
        END TRY
        BEGIN CATCH
            -- Log error but continue
            PRINT 'Error updating statistics for ' + @SchemaName + '.' + @TableName + ': ' + ERROR_MESSAGE();
        END CATCH

        FETCH NEXT FROM table_cursor INTO @SchemaName, @TableName;
    END

    CLOSE table_cursor;
    DEALLOCATE table_cursor;

    SELECT @TableCount AS TablesUpdated;
END
GO

PRINT 'Created sp_MaintenanceUpdateStatistics stored procedure';
GO

-- ============================================================================
-- Create stored procedure for Log Cleanup
-- ============================================================================

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MaintenanceCleanupLogs')
    DROP PROCEDURE [dbo].[sp_MaintenanceCleanupLogs];
GO

CREATE PROCEDURE [dbo].[sp_MaintenanceCleanupLogs]
    @SyncLogRetentionDays INT = 30,
    @ChangeLogRetentionDays INT = 365,
    @SystemLogRetentionDays INT = 90,
    @JobHistoryRetentionDays INT = 30,
    @NotificationLogRetentionDays INT = 60,
    @BatchSize INT = 10000
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CutoffDate DATETIME2;
    DECLARE @DeletedCount INT;
    DECLARE @RowsAffected INT;
    DECLARE @TotalDeleted INT = 0;

    -- Results table
    CREATE TABLE #CleanupResults (
        TableName NVARCHAR(128),
        RetentionDays INT,
        CutoffDate DATETIME2,
        RowsDeleted INT
    );

    -- Clean SyncAuditLogs
    IF @SyncLogRetentionDays > 0 AND EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncAuditLogs')
    BEGIN
        SET @CutoffDate = DATEADD(DAY, -@SyncLogRetentionDays, GETUTCDATE());
        SET @DeletedCount = 0;

        WHILE 1 = 1
        BEGIN
            DELETE TOP (@BatchSize) FROM [dbo].[SyncAuditLogs] WHERE [Timestamp] < @CutoffDate;
            SET @RowsAffected = @@ROWCOUNT;
            IF @RowsAffected = 0 BREAK;
            SET @DeletedCount = @DeletedCount + @RowsAffected;
        END

        INSERT INTO #CleanupResults VALUES ('SyncAuditLogs', @SyncLogRetentionDays, @CutoffDate, @DeletedCount);
        SET @TotalDeleted = @TotalDeleted + @DeletedCount;
    END

    -- Clean ChangeAuditLogs
    IF @ChangeLogRetentionDays > 0 AND EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ChangeAuditLogs')
    BEGIN
        SET @CutoffDate = DATEADD(DAY, -@ChangeLogRetentionDays, GETUTCDATE());
        SET @DeletedCount = 0;

        WHILE 1 = 1
        BEGIN
            DELETE TOP (@BatchSize) FROM [dbo].[ChangeAuditLogs] WHERE [Timestamp] < @CutoffDate;
            SET @RowsAffected = @@ROWCOUNT;
            IF @RowsAffected = 0 BREAK;
            SET @DeletedCount = @DeletedCount + @RowsAffected;
        END

        INSERT INTO #CleanupResults VALUES ('ChangeAuditLogs', @ChangeLogRetentionDays, @CutoffDate, @DeletedCount);
        SET @TotalDeleted = @TotalDeleted + @DeletedCount;
    END

    -- Clean AuditLogs (System logs)
    IF @SystemLogRetentionDays > 0 AND EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AuditLogs')
    BEGIN
        SET @CutoffDate = DATEADD(DAY, -@SystemLogRetentionDays, GETUTCDATE());
        SET @DeletedCount = 0;

        WHILE 1 = 1
        BEGIN
            DELETE TOP (@BatchSize) FROM [dbo].[AuditLogs] WHERE [Timestamp] < @CutoffDate;
            SET @RowsAffected = @@ROWCOUNT;
            IF @RowsAffected = 0 BREAK;
            SET @DeletedCount = @DeletedCount + @RowsAffected;
        END

        INSERT INTO #CleanupResults VALUES ('AuditLogs', @SystemLogRetentionDays, @CutoffDate, @DeletedCount);
        SET @TotalDeleted = @TotalDeleted + @DeletedCount;
    END

    -- Clean JobExecutionHistory
    IF @JobHistoryRetentionDays > 0 AND EXISTS (SELECT 1 FROM sys.tables WHERE name = 'JobExecutionHistory')
    BEGIN
        SET @CutoffDate = DATEADD(DAY, -@JobHistoryRetentionDays, GETUTCDATE());
        SET @DeletedCount = 0;

        WHILE 1 = 1
        BEGIN
            DELETE TOP (@BatchSize) FROM [dbo].[JobExecutionHistory] WHERE [StartedAt] < @CutoffDate;
            SET @RowsAffected = @@ROWCOUNT;
            IF @RowsAffected = 0 BREAK;
            SET @DeletedCount = @DeletedCount + @RowsAffected;
        END

        INSERT INTO #CleanupResults VALUES ('JobExecutionHistory', @JobHistoryRetentionDays, @CutoffDate, @DeletedCount);
        SET @TotalDeleted = @TotalDeleted + @DeletedCount;
    END

    -- Clean EmailQueue (sent/failed items)
    IF @NotificationLogRetentionDays > 0 AND EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmailQueue')
    BEGIN
        SET @CutoffDate = DATEADD(DAY, -@NotificationLogRetentionDays, GETUTCDATE());
        SET @DeletedCount = 0;

        WHILE 1 = 1
        BEGIN
            DELETE TOP (@BatchSize) FROM [dbo].[EmailQueue]
            WHERE [Status] IN ('Sent', 'Failed') AND [CreatedAt] < @CutoffDate;
            SET @RowsAffected = @@ROWCOUNT;
            IF @RowsAffected = 0 BREAK;
            SET @DeletedCount = @DeletedCount + @RowsAffected;
        END

        INSERT INTO #CleanupResults VALUES ('EmailQueue', @NotificationLogRetentionDays, @CutoffDate, @DeletedCount);
        SET @TotalDeleted = @TotalDeleted + @DeletedCount;
    END

    -- Clean TeamsMessageQueue (sent/failed items)
    IF @NotificationLogRetentionDays > 0 AND EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeamsMessageQueue')
    BEGIN
        SET @CutoffDate = DATEADD(DAY, -@NotificationLogRetentionDays, GETUTCDATE());
        SET @DeletedCount = 0;

        WHILE 1 = 1
        BEGIN
            DELETE TOP (@BatchSize) FROM [dbo].[TeamsMessageQueue]
            WHERE [Status] IN ('Sent', 'Failed') AND [CreatedAt] < @CutoffDate;
            SET @RowsAffected = @@ROWCOUNT;
            IF @RowsAffected = 0 BREAK;
            SET @DeletedCount = @DeletedCount + @RowsAffected;
        END

        INSERT INTO #CleanupResults VALUES ('TeamsMessageQueue', @NotificationLogRetentionDays, @CutoffDate, @DeletedCount);
        SET @TotalDeleted = @TotalDeleted + @DeletedCount;
    END

    -- Return summary
    SELECT @TotalDeleted AS TotalRowsDeleted;
    SELECT * FROM #CleanupResults;

    DROP TABLE #CleanupResults;
END
GO

PRINT 'Created sp_MaintenanceCleanupLogs stored procedure';
GO

PRINT 'Migration 20260122_AddMaintenanceSettings completed successfully';
GO
