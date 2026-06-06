-- Create JobExecutionHistory table if missing
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'JobExecutionHistory')
BEGIN
    CREATE TABLE [dbo].[JobExecutionHistory] (
        [Id] uniqueidentifier NOT NULL,
        [JobType] nvarchar(50) NOT NULL,
        [JobName] nvarchar(200) NOT NULL,
        [RelatedEntityId] uniqueidentifier NULL,
        [RelatedEntityType] nvarchar(50) NULL,
        [QuartzJobId] nvarchar(100) NULL,
        [TriggerType] nvarchar(50) NOT NULL,
        [TriggeredBy] nvarchar(200) NOT NULL,
        [StartedAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        [DurationMs] int NULL,
        [Status] nvarchar(20) NOT NULL,
        [ItemsProcessed] int NOT NULL DEFAULT 0,
        [ItemsSucceeded] int NOT NULL DEFAULT 0,
        [ItemsFailed] int NOT NULL DEFAULT 0,
        [ResultSummaryJson] nvarchar(max) NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [ExceptionDetails] nvarchar(max) NULL,
        [ExecutingServer] nvarchar(100) NULL,
        [NextScheduledRun] datetime2 NULL,
        [IsRetry] bit NOT NULL DEFAULT 0,
        [RetryCount] int NOT NULL DEFAULT 0,
        [ParentExecutionId] uniqueidentifier NULL,
        CONSTRAINT [PK_JobExecutionHistory] PRIMARY KEY ([Id])
    );
    PRINT 'Created JobExecutionHistory table'
END
ELSE
    PRINT 'JobExecutionHistory table already exists'
