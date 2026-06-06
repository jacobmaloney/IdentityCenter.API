-- Create WorkflowTrigger tables for IdentityCenter13
-- From migration: 20251211000000_AddWorkflowTriggerTables

SET QUOTED_IDENTIFIER ON
SET ANSI_NULLS ON
GO

-- WorkflowTriggers
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowTriggers')
BEGIN
    CREATE TABLE [dbo].[WorkflowTriggers] (
        [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [Category] nvarchar(100) NULL,
        [TriggerType] nvarchar(50) NOT NULL,
        [EventTypes] nvarchar(max) NULL,
        [EventSourceConfig] nvarchar(max) NULL,
        [WorkflowId] uniqueidentifier NULL,
        [CronExpression] nvarchar(100) NULL,
        [NextScheduledRun] datetime2 NULL,
        [LastScheduledRun] datetime2 NULL,
        [IsActive] bit NOT NULL DEFAULT 1,
        [IsSystem] bit NOT NULL DEFAULT 0,
        [Priority] int NOT NULL DEFAULT 100,
        [CooldownMinutes] int NOT NULL DEFAULT 0,
        [TestMode] bit NOT NULL DEFAULT 0,
        [TriggerCount] int NOT NULL DEFAULT 0,
        [LastTriggeredAt] datetime2 NULL,
        [SuccessCount] int NOT NULL DEFAULT 0,
        [FailureCount] int NOT NULL DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] nvarchar(256) NULL,
        [ModifiedAt] datetime2 NULL,
        [ModifiedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_WorkflowTriggers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowTriggers_ApprovalWorkflows_WorkflowId] FOREIGN KEY ([WorkflowId]) REFERENCES [ApprovalWorkflows] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_WorkflowTriggers_Type_Active] ON [WorkflowTriggers] ([TriggerType], [IsActive]) WHERE [IsActive] = 1;
    CREATE INDEX [IX_WorkflowTriggers_WorkflowId] ON [WorkflowTriggers] ([WorkflowId]);
    PRINT 'Created WorkflowTriggers table'
END

-- TriggerConditions
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerConditions')
BEGIN
    CREATE TABLE [dbo].[TriggerConditions] (
        [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
        [TriggerId] uniqueidentifier NOT NULL,
        [ConditionType] nvarchar(100) NOT NULL,
        [FieldName] nvarchar(200) NULL,
        [Operator] nvarchar(50) NOT NULL,
        [Value] nvarchar(2000) NULL,
        [ValueType] nvarchar(50) NOT NULL DEFAULT 'String',
        [LogicalGroup] nvarchar(50) NOT NULL DEFAULT 'AND',
        [GroupOrder] int NOT NULL DEFAULT 0,
        [IsActive] bit NOT NULL DEFAULT 1,
        [SortOrder] int NOT NULL DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_TriggerConditions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TriggerConditions_WorkflowTriggers_TriggerId] FOREIGN KEY ([TriggerId]) REFERENCES [WorkflowTriggers] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_TriggerConditions_TriggerId] ON [TriggerConditions] ([TriggerId]);
    PRINT 'Created TriggerConditions table'
END

-- TriggerActions
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerActions')
BEGIN
    CREATE TABLE [dbo].[TriggerActions] (
        [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
        [TriggerId] uniqueidentifier NOT NULL,
        [ActionType] nvarchar(100) NOT NULL,
        [ActionName] nvarchar(200) NULL,
        [ActionConfig] nvarchar(max) NULL,
        [ExecutionOrder] int NOT NULL DEFAULT 0,
        [IsAsync] bit NOT NULL DEFAULT 0,
        [ContinueOnError] bit NOT NULL DEFAULT 1,
        [DelayMinutes] int NOT NULL DEFAULT 0,
        [TimeoutMinutes] int NOT NULL DEFAULT 60,
        [MaxRetries] int NOT NULL DEFAULT 3,
        [RetryDelaySeconds] int NOT NULL DEFAULT 60,
        [IsActive] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_TriggerActions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TriggerActions_WorkflowTriggers_TriggerId] FOREIGN KEY ([TriggerId]) REFERENCES [WorkflowTriggers] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_TriggerActions_TriggerId] ON [TriggerActions] ([TriggerId]);
    PRINT 'Created TriggerActions table'
END

-- TriggerEvents
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerEvents')
BEGIN
    CREATE TABLE [dbo].[TriggerEvents] (
        [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
        [EventType] nvarchar(100) NOT NULL,
        [EventSource] nvarchar(200) NOT NULL,
        [EventData] nvarchar(max) NOT NULL,
        [TargetEntityType] nvarchar(100) NULL,
        [TargetEntityId] uniqueidentifier NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT 'Pending',
        [ProcessingAttempts] int NOT NULL DEFAULT 0,
        [LastAttemptAt] datetime2 NULL,
        [ProcessedAt] datetime2 NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [IdempotencyKey] nvarchar(500) NULL,
        [OccurredAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [ExpiresAt] datetime2 NULL,
        [CorrelationId] uniqueidentifier NULL,
        [CausationId] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_TriggerEvents] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_TriggerEvents_Status_Type] ON [TriggerEvents] ([Status], [EventType], [OccurredAt]) WHERE [Status] = 'Pending';
    CREATE UNIQUE INDEX [IX_TriggerEvents_IdempotencyKey] ON [TriggerEvents] ([IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL;
    PRINT 'Created TriggerEvents table'
END

-- TriggerExecutions
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerExecutions')
BEGIN
    CREATE TABLE [dbo].[TriggerExecutions] (
        [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
        [TriggerId] uniqueidentifier NOT NULL,
        [EventId] uniqueidentifier NULL,
        [WorkflowInstanceId] uniqueidentifier NULL,
        [TargetEntityType] nvarchar(100) NULL,
        [TargetEntityId] uniqueidentifier NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT 'Running',
        [StartedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [CompletedAt] datetime2 NULL,
        [DurationMs] bigint NULL,
        [ActionsExecuted] int NOT NULL DEFAULT 0,
        [ActionsFailed] int NOT NULL DEFAULT 0,
        [ResultSummary] nvarchar(max) NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [EventDataSnapshot] nvarchar(max) NULL,
        [TriggerConfigSnapshot] nvarchar(max) NULL,
        [TriggeredBy] nvarchar(256) NULL,
        CONSTRAINT [PK_TriggerExecutions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TriggerExecutions_WorkflowTriggers_TriggerId] FOREIGN KEY ([TriggerId]) REFERENCES [WorkflowTriggers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TriggerExecutions_TriggerEvents_EventId] FOREIGN KEY ([EventId]) REFERENCES [TriggerEvents] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_TriggerExecutions_TriggerId_StartedAt] ON [TriggerExecutions] ([TriggerId], [StartedAt]);
    CREATE INDEX [IX_TriggerExecutions_EventId] ON [TriggerExecutions] ([EventId]);
    PRINT 'Created TriggerExecutions table'
END

-- TriggerActionLogs
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerActionLogs')
BEGIN
    CREATE TABLE [dbo].[TriggerActionLogs] (
        [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
        [ExecutionId] uniqueidentifier NOT NULL,
        [ActionId] uniqueidentifier NOT NULL,
        [ActionType] nvarchar(100) NOT NULL,
        [ActionName] nvarchar(200) NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT 'Pending',
        [StartedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [DurationMs] bigint NULL,
        [InputData] nvarchar(max) NULL,
        [OutputData] nvarchar(max) NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [AttemptNumber] int NOT NULL DEFAULT 1,
        [WillRetry] bit NOT NULL DEFAULT 0,
        [NextRetryAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_TriggerActionLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TriggerActionLogs_TriggerExecutions_ExecutionId] FOREIGN KEY ([ExecutionId]) REFERENCES [TriggerExecutions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TriggerActionLogs_TriggerActions_ActionId] FOREIGN KEY ([ActionId]) REFERENCES [TriggerActions] ([Id]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_TriggerActionLogs_ExecutionId] ON [TriggerActionLogs] ([ExecutionId]);
    CREATE INDEX [IX_TriggerActionLogs_ActionId] ON [TriggerActionLogs] ([ActionId]);
    PRINT 'Created TriggerActionLogs table'
END

-- WorkflowTriggerTemplates
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowTriggerTemplates')
BEGIN
    CREATE TABLE [dbo].[WorkflowTriggerTemplates] (
        [Id] uniqueidentifier NOT NULL DEFAULT NEWID(),
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [Category] nvarchar(100) NULL,
        [Icon] nvarchar(100) NULL,
        [Color] nvarchar(50) NULL,
        [IsSystem] bit NOT NULL DEFAULT 0,
        [TemplateJson] nvarchar(max) NOT NULL,
        [UsageCount] int NOT NULL DEFAULT 0,
        [SortOrder] int NOT NULL DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] nvarchar(256) NULL,
        [ModifiedAt] datetime2 NULL,
        CONSTRAINT [PK_WorkflowTriggerTemplates] PRIMARY KEY ([Id])
    );
    PRINT 'Created WorkflowTriggerTemplates table'
END

-- Add migration record
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251211000000_AddWorkflowTriggerTables')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251211000000_AddWorkflowTriggerTables', '8.0.0');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251211000001_SeedBuiltInWorkflowTriggers')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251211000001_SeedBuiltInWorkflowTriggers', '8.0.0');

PRINT 'All WorkflowTrigger tables created successfully'
