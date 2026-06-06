-- V127: Add DEFAULT constraints to SyncProjects NOT NULL columns that the
-- INSERT in SyncConfigRepository.CreateSyncProjectAsync omits.
--
-- ROOT CAUSE: SyncProjects was created in V004 with 15 NOT NULL columns that
-- have NO default constraint (IsTemplateMode, ConflictResolutionStrategy,
-- AutoCreateIdentities, EnableManagerAssignment, ProjectType, IsBuiltIn,
-- IsReadOnly, MinMatchConfidenceThreshold, PauseOnError, MaxErrorsBeforePause,
-- Priority, LogLevel, TotalExecutions, SuccessfulExecutions, FailedExecutions).
-- The repository INSERT does not list those columns, so SQL Server has no value
-- to supply and rejects the row. It fails on the FIRST such column in table
-- order -- IsTemplateMode -- which is the "Cannot insert the value NULL into
-- column 'IsTemplateMode'" error seen on a fresh-deploy UI sync-project create.
--
-- FIX: give every omitted NOT NULL column a DEFAULT matching the C# model
-- semantics (SyncProject in SyncModels.cs). This repairs EXISTING databases
-- (e.g. 192.168.1.56) on next boot AND makes fresh installs safe. The
-- repository INSERT is also updated to set the two semantically meaningful
-- values explicitly (IsTemplateMode = 0, ProjectType), but the defaults below
-- are the durable safety net for any insert path that omits a column.
--
-- IsTemplateMode defaults to 0 (false): a UI-created sync project is a real
-- project, not a template. The V045 built-in seed insert sets it to 0 as well.
--
-- Idempotent: each constraint is added only if one does not already exist for
-- the column (sys.default_constraints), so this is safe to re-run and safe on
-- both fresh and already-migrated databases.

SET NOCOUNT ON;

DECLARE @sql NVARCHAR(MAX);

-- Helper pattern repeated per column: add a named default constraint only when
-- the column currently has no default constraint of any name.

-- IsTemplateMode -> 0
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'IsTemplateMode')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_IsTemplateMode] DEFAULT (0) FOR [IsTemplateMode];
END;
GO

-- IsEnabled -> 1
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'IsEnabled')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_IsEnabled] DEFAULT (1) FOR [IsEnabled];
END;
GO

-- IsRunning -> 0
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'IsRunning')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_IsRunning] DEFAULT (0) FOR [IsRunning];
END;
GO

-- ConflictResolutionStrategy -> 'SourceWins'
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'ConflictResolutionStrategy')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_ConflictResolutionStrategy] DEFAULT (N'SourceWins') FOR [ConflictResolutionStrategy];
END;
GO

-- AutoCreateIdentities -> 1
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'AutoCreateIdentities')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_AutoCreateIdentities] DEFAULT (1) FOR [AutoCreateIdentities];
END;
GO

-- EnableManagerAssignment -> 1
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'EnableManagerAssignment')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_EnableManagerAssignment] DEFAULT (1) FOR [EnableManagerAssignment];
END;
GO

-- ProjectType -> 'ObjectSync'
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'ProjectType')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_ProjectType] DEFAULT (N'ObjectSync') FOR [ProjectType];
END;
GO

-- IsBuiltIn -> 0
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'IsBuiltIn')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_IsBuiltIn] DEFAULT (0) FOR [IsBuiltIn];
END;
GO

-- IsReadOnly -> 0
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'IsReadOnly')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_IsReadOnly] DEFAULT (0) FOR [IsReadOnly];
END;
GO

-- MinMatchConfidenceThreshold -> 75
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'MinMatchConfidenceThreshold')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_MinMatchConfidenceThreshold] DEFAULT (75) FOR [MinMatchConfidenceThreshold];
END;
GO

-- PauseOnError -> 0
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'PauseOnError')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_PauseOnError] DEFAULT (0) FOR [PauseOnError];
END;
GO

-- MaxErrorsBeforePause -> 100
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'MaxErrorsBeforePause')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_MaxErrorsBeforePause] DEFAULT (100) FOR [MaxErrorsBeforePause];
END;
GO

-- Priority -> 5
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'Priority')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_Priority] DEFAULT (5) FOR [Priority];
END;
GO

-- LogLevel -> 'Information'
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'LogLevel')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_LogLevel] DEFAULT (N'Information') FOR [LogLevel];
END;
GO

-- TotalExecutions -> 0
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'TotalExecutions')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_TotalExecutions] DEFAULT (0) FOR [TotalExecutions];
END;
GO

-- SuccessfulExecutions -> 0
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'SuccessfulExecutions')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_SuccessfulExecutions] DEFAULT (0) FOR [SuccessfulExecutions];
END;
GO

-- FailedExecutions -> 0
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.SyncProjects') AND c.name = N'FailedExecutions')
BEGIN
    ALTER TABLE [SyncProjects] ADD CONSTRAINT [DF_SyncProjects_FailedExecutions] DEFAULT (0) FOR [FailedExecutions];
END;
GO

PRINT 'V127: SyncProjects NOT NULL columns now carry DEFAULT constraints';
