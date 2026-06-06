-- V128: Add DEFAULT constraints to NOT NULL columns across many tables whose raw
-- (Dapper) INSERTs omit those columns. Mirrors the proven V127 pattern.
--
-- ROOT CAUSE (from the V128 audit): a large set of tables were created in V004
-- with NOT NULL columns that have NO default constraint. Numerous raw INSERT
-- statements throughout the codebase do not list every NOT NULL column, so SQL
-- Server has no value to supply and rejects the row ("Cannot insert the value
-- NULL into column ..."). The worst offenders silently swallow the exception in
-- a try/catch (audit logs, remediation actions) so the rows are dropped without
-- a trace; others (e.g. SystemConfigurations first-boot insert) throw outright
-- and break a fresh deploy.
--
-- FIX: give every omitted NOT NULL column a DEFAULT matching the C# model
-- initializer semantics. This repairs EXISTING databases (e.g. 192.168.1.56) on
-- next boot AND makes fresh installs safe. The highest-value INSERTs are ALSO
-- updated in code to set the semantically meaningful values explicitly, but the
-- defaults below are the durable safety net for any insert path that omits a
-- column.
--
-- IDEMPOTENT: each constraint is added only if no default constraint already
-- exists for the column (sys.default_constraints). Safe to re-run, safe on fresh
-- and on already-migrated databases. Each ALTER is also guarded by a column
-- existence check (sys.columns) so a table whose shape differs across IC
-- versions is skipped gracefully rather than erroring.
--
-- Default values are derived from the C# model initializers. bit -> 0 (or 1
-- where the model initializer is true), int counters -> 0, status nvarchar ->
-- its documented default, CreatedAt -> GETUTCDATE(). uniqueidentifier / FK /
-- real-data datetime columns are intentionally NOT defaulted.

SET NOCOUNT ON;
GO

-- =====================================================================
-- Helper note: the repeated block below adds a named default constraint
-- only when (a) the column exists and (b) it currently has no default
-- constraint of any name.
-- =====================================================================

-- ---------------------------------------------------------------------
-- SystemConfigurations (singleton; first-boot insert previously listed
-- only 4 columns). Defaults match SystemConfiguration.cs initializers.
-- ---------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'AllowSelfRegistration')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'AllowSelfRegistration')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_AllowSelfRegistration] DEFAULT (0) FOR [AllowSelfRegistration];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'RequireEmailConfirmation')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'RequireEmailConfirmation')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_RequireEmailConfirmation] DEFAULT (0) FOR [RequireEmailConfirmation];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'AllowExternalLogins')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'AllowExternalLogins')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_AllowExternalLogins] DEFAULT (1) FOR [AllowExternalLogins];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'MinimumPasswordLength')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'MinimumPasswordLength')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_MinimumPasswordLength] DEFAULT (8) FOR [MinimumPasswordLength];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'RequireDigit')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'RequireDigit')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_RequireDigit] DEFAULT (1) FOR [RequireDigit];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'RequireLowercase')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'RequireLowercase')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_RequireLowercase] DEFAULT (1) FOR [RequireLowercase];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'RequireUppercase')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'RequireUppercase')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_RequireUppercase] DEFAULT (1) FOR [RequireUppercase];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'RequireNonAlphanumeric')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'RequireNonAlphanumeric')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_RequireNonAlphanumeric] DEFAULT (1) FOR [RequireNonAlphanumeric];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'MaxFailedAccessAttempts')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'MaxFailedAccessAttempts')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_MaxFailedAccessAttempts] DEFAULT (5) FOR [MaxFailedAccessAttempts];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'LockoutDurationMinutes')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'LockoutDurationMinutes')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_LockoutDurationMinutes] DEFAULT (30) FOR [LockoutDurationMinutes];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'SessionTimeoutMinutes')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'SessionTimeoutMinutes')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_SessionTimeoutMinutes] DEFAULT (30) FOR [SessionTimeoutMinutes];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'SlidingExpiration')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'SlidingExpiration')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_SlidingExpiration] DEFAULT (1) FOR [SlidingExpiration];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'EnableAuditLogging')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'EnableAuditLogging')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_EnableAuditLogging] DEFAULT (1) FOR [EnableAuditLogging];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'AuditRetentionDays')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'AuditRetentionDays')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_AuditRetentionDays] DEFAULT (90) FOR [AuditRetentionDays];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'PortalUrl')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'PortalUrl')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_PortalUrl] DEFAULT (N'https://localhost:7001') FOR [PortalUrl];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'PortalDisplayName')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'PortalDisplayName')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_PortalDisplayName] DEFAULT (N'Certification Center') FOR [PortalDisplayName];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'EnablePolicyNotifications')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'EnablePolicyNotifications')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_EnablePolicyNotifications] DEFAULT (1) FOR [EnablePolicyNotifications];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'EnableSyncNotifications')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'EnableSyncNotifications')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_EnableSyncNotifications] DEFAULT (1) FOR [EnableSyncNotifications];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'EnableEscalationNotifications')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'EnableEscalationNotifications')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_EnableEscalationNotifications] DEFAULT (1) FOR [EnableEscalationNotifications];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'ChatLlmEnabled')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'ChatLlmEnabled')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_ChatLlmEnabled] DEFAULT (0) FOR [ChatLlmEnabled];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'ChatLlmProvider')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'ChatLlmProvider')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_ChatLlmProvider] DEFAULT (N'Anthropic') FOR [ChatLlmProvider];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'ChatLlmEndpoint')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'ChatLlmEndpoint')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_ChatLlmEndpoint] DEFAULT (N'https://api.anthropic.com/v1') FOR [ChatLlmEndpoint];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'ChatLlmModel')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'ChatLlmModel')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_ChatLlmModel] DEFAULT (N'claude-sonnet-4-6') FOR [ChatLlmModel];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'ChatLlmMaxTokens')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'ChatLlmMaxTokens')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_ChatLlmMaxTokens] DEFAULT (500) FOR [ChatLlmMaxTokens];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'ChatLlmTemperature')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'ChatLlmTemperature')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_ChatLlmTemperature] DEFAULT (0.3) FOR [ChatLlmTemperature];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'ChatLlmTimeoutSeconds')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'ChatLlmTimeoutSeconds')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_ChatLlmTimeoutSeconds] DEFAULT (30) FOR [ChatLlmTimeoutSeconds];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'CreatedAt')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'CreatedAt')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_CreatedAt] DEFAULT (GETUTCDATE()) FOR [CreatedAt];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND name=N'ModifiedBy')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SystemConfigurations') AND c.name=N'ModifiedBy')
    ALTER TABLE [SystemConfigurations] ADD CONSTRAINT [DF_SystemConfigurations_ModifiedBy] DEFAULT (N'System') FOR [ModifiedBy];
GO

-- ---------------------------------------------------------------------
-- JobQueue (counters omitted by JobQueueRepository INSERT)
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.JobQueue') AND name=N'ItemsSucceeded')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.JobQueue') AND c.name=N'ItemsSucceeded')
    ALTER TABLE [JobQueue] ADD CONSTRAINT [DF_JobQueue_ItemsSucceeded] DEFAULT (0) FOR [ItemsSucceeded];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.JobQueue') AND name=N'ItemsFailed')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.JobQueue') AND c.name=N'ItemsFailed')
    ALTER TABLE [JobQueue] ADD CONSTRAINT [DF_JobQueue_ItemsFailed] DEFAULT (0) FOR [ItemsFailed];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.JobQueue') AND name=N'ProgressPercent')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.JobQueue') AND c.name=N'ProgressPercent')
    ALTER TABLE [JobQueue] ADD CONSTRAINT [DF_JobQueue_ProgressPercent] DEFAULT (0) FOR [ProgressPercent];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.JobQueue') AND name=N'ItemsProcessed')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.JobQueue') AND c.name=N'ItemsProcessed')
    ALTER TABLE [JobQueue] ADD CONSTRAINT [DF_JobQueue_ItemsProcessed] DEFAULT (0) FOR [ItemsProcessed];
GO

-- ---------------------------------------------------------------------
-- SyncSteps (matching/scope/batch flags omitted by several INSERT paths)
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'CreatePersonIfNotFound')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'CreatePersonIfNotFound')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_CreatePersonIfNotFound] DEFAULT (0) FOR [CreatePersonIfNotFound];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'InheritWorkflowTags')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'InheritWorkflowTags')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_InheritWorkflowTags] DEFAULT (0) FOR [InheritWorkflowTags];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'SkipPersonMatching')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'SkipPersonMatching')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_SkipPersonMatching] DEFAULT (0) FOR [SkipPersonMatching];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'EnableIdentityMatching')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'EnableIdentityMatching')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_EnableIdentityMatching] DEFAULT (0) FOR [EnableIdentityMatching];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'EnablePersonMatching')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'EnablePersonMatching')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_EnablePersonMatching] DEFAULT (0) FOR [EnablePersonMatching];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'BatchSize')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'BatchSize')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_BatchSize] DEFAULT (100) FOR [BatchSize];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'SearchScope')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'SearchScope')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_SearchScope] DEFAULT (N'Subtree') FOR [SearchScope];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'MaxExecutionTimeMinutes')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'MaxExecutionTimeMinutes')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_MaxExecutionTimeMinutes] DEFAULT (60) FOR [MaxExecutionTimeMinutes];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'UpdateExisting')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'UpdateExisting')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_UpdateExisting] DEFAULT (1) FOR [UpdateExisting];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'LdapPageSize')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'LdapPageSize')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_LdapPageSize] DEFAULT (1000) FOR [LdapPageSize];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'ProcessDeletions')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'ProcessDeletions')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_ProcessDeletions] DEFAULT (0) FOR [ProcessDeletions];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncSteps') AND name=N'ContinueOnError')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncSteps') AND c.name=N'ContinueOnError')
    ALTER TABLE [SyncSteps] ADD CONSTRAINT [DF_SyncSteps_ContinueOnError] DEFAULT (1) FOR [ContinueOnError];
GO

-- ---------------------------------------------------------------------
-- SyncWorkflows
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncWorkflows') AND name=N'ContinueOnError')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncWorkflows') AND c.name=N'ContinueOnError')
    ALTER TABLE [SyncWorkflows] ADD CONSTRAINT [DF_SyncWorkflows_ContinueOnError] DEFAULT (1) FOR [ContinueOnError];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncWorkflows') AND name=N'WorkflowType')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncWorkflows') AND c.name=N'WorkflowType')
    ALTER TABLE [SyncWorkflows] ADD CONSTRAINT [DF_SyncWorkflows_WorkflowType] DEFAULT (N'ObjectSync') FOR [WorkflowType];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SyncWorkflows') AND name=N'MaxExecutionTimeMinutes')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.SyncWorkflows') AND c.name=N'MaxExecutionTimeMinutes')
    ALTER TABLE [SyncWorkflows] ADD CONSTRAINT [DF_SyncWorkflows_MaxExecutionTimeMinutes] DEFAULT (60) FOR [MaxExecutionTimeMinutes];
GO

-- ---------------------------------------------------------------------
-- Campaigns (many counters/flags omitted across 3 INSERT paths)
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'AutoGenerated')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'AutoGenerated')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_AutoGenerated] DEFAULT (0) FOR [AutoGenerated];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'ReviewPeriodDays')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'ReviewPeriodDays')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_ReviewPeriodDays] DEFAULT (14) FOR [ReviewPeriodDays];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'OnApprovalAction')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'OnApprovalAction')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_OnApprovalAction] DEFAULT (N'Certify') FOR [OnApprovalAction];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'CampaignType')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'CampaignType')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_CampaignType] DEFAULT (N'UserAccess') FOR [CampaignType];
-- NOTE: CampaignType model initializer is string.Empty; 'UserAccess' is used as
-- the durable fallback so a row that omits it carries a valid, queryable type
-- rather than an empty string. Every code INSERT that omits it does set Type or
-- Status, so this default only fires on truly partial inserts.
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'EnableNotifications')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'EnableNotifications')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_EnableNotifications] DEFAULT (1) FOR [EnableNotifications];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'OnDenialAction')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'OnDenialAction')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_OnDenialAction] DEFAULT (N'RemoveFromGroup') FOR [OnDenialAction];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'OnIncompleteAction')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'OnIncompleteAction')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_OnIncompleteAction] DEFAULT (N'None') FOR [OnIncompleteAction];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'IncludeNestedMemberships')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'IncludeNestedMemberships')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_IncludeNestedMemberships] DEFAULT (0) FOR [IncludeNestedMemberships];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'ExtensionDays')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'ExtensionDays')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_ExtensionDays] DEFAULT (0) FOR [ExtensionDays];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'ReminderDaysBefore')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'ReminderDaysBefore')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_ReminderDaysBefore] DEFAULT (0) FOR [ReminderDaysBefore];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'MaxNestedDepth')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'MaxNestedDepth')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_MaxNestedDepth] DEFAULT (10) FOR [MaxNestedDepth];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'CompletedAssignments')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'CompletedAssignments')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_CompletedAssignments] DEFAULT (0) FOR [CompletedAssignments];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'TotalAssignments')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'TotalAssignments')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_TotalAssignments] DEFAULT (0) FOR [TotalAssignments];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'PolicyViolationFilter')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'PolicyViolationFilter')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_PolicyViolationFilter] DEFAULT (0) FOR [PolicyViolationFilter];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'CompletionActionsProcessed')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'CompletionActionsProcessed')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_CompletionActionsProcessed] DEFAULT (0) FOR [CompletionActionsProcessed];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'CompletionPercentage')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'CompletionPercentage')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_CompletionPercentage] DEFAULT (0) FOR [CompletionPercentage];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'IsRecurring')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'IsRecurring')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_IsRecurring] DEFAULT (0) FOR [IsRecurring];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Campaigns') AND name=N'AutoRemediateOnDenial')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Campaigns') AND c.name=N'AutoRemediateOnDenial')
    ALTER TABLE [Campaigns] ADD CONSTRAINT [DF_Campaigns_AutoRemediateOnDenial] DEFAULT (0) FOR [AutoRemediateOnDenial];
GO

-- ---------------------------------------------------------------------
-- WorkflowTriggers
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.WorkflowTriggers') AND name=N'CooldownMinutes')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.WorkflowTriggers') AND c.name=N'CooldownMinutes')
    ALTER TABLE [WorkflowTriggers] ADD CONSTRAINT [DF_WorkflowTriggers_CooldownMinutes] DEFAULT (0) FOR [CooldownMinutes];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.WorkflowTriggers') AND name=N'TestMode')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.WorkflowTriggers') AND c.name=N'TestMode')
    ALTER TABLE [WorkflowTriggers] ADD CONSTRAINT [DF_WorkflowTriggers_TestMode] DEFAULT (0) FOR [TestMode];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.WorkflowTriggers') AND name=N'Priority')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.WorkflowTriggers') AND c.name=N'Priority')
    ALTER TABLE [WorkflowTriggers] ADD CONSTRAINT [DF_WorkflowTriggers_Priority] DEFAULT (5) FOR [Priority];
GO

-- ---------------------------------------------------------------------
-- TriggerExecutions
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerExecutions') AND name=N'ActionsFailed')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerExecutions') AND c.name=N'ActionsFailed')
    ALTER TABLE [TriggerExecutions] ADD CONSTRAINT [DF_TriggerExecutions_ActionsFailed] DEFAULT (0) FOR [ActionsFailed];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerExecutions') AND name=N'ActionsExecuted')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerExecutions') AND c.name=N'ActionsExecuted')
    ALTER TABLE [TriggerExecutions] ADD CONSTRAINT [DF_TriggerExecutions_ActionsExecuted] DEFAULT (0) FOR [ActionsExecuted];
GO

-- ---------------------------------------------------------------------
-- TriggerActionLogs
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerActionLogs') AND name=N'WillRetry')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerActionLogs') AND c.name=N'WillRetry')
    ALTER TABLE [TriggerActionLogs] ADD CONSTRAINT [DF_TriggerActionLogs_WillRetry] DEFAULT (0) FOR [WillRetry];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerActionLogs') AND name=N'AttemptNumber')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerActionLogs') AND c.name=N'AttemptNumber')
    ALTER TABLE [TriggerActionLogs] ADD CONSTRAINT [DF_TriggerActionLogs_AttemptNumber] DEFAULT (1) FOR [AttemptNumber];
GO

-- ---------------------------------------------------------------------
-- TriggerActions
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerActions') AND name=N'CreatedAt')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerActions') AND c.name=N'CreatedAt')
    ALTER TABLE [TriggerActions] ADD CONSTRAINT [DF_TriggerActions_CreatedAt] DEFAULT (GETUTCDATE()) FOR [CreatedAt];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerActions') AND name=N'RetryDelaySeconds')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerActions') AND c.name=N'RetryDelaySeconds')
    ALTER TABLE [TriggerActions] ADD CONSTRAINT [DF_TriggerActions_RetryDelaySeconds] DEFAULT (60) FOR [RetryDelaySeconds];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerActions') AND name=N'MaxRetries')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerActions') AND c.name=N'MaxRetries')
    ALTER TABLE [TriggerActions] ADD CONSTRAINT [DF_TriggerActions_MaxRetries] DEFAULT (3) FOR [MaxRetries];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerActions') AND name=N'IsAsync')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerActions') AND c.name=N'IsAsync')
    ALTER TABLE [TriggerActions] ADD CONSTRAINT [DF_TriggerActions_IsAsync] DEFAULT (0) FOR [IsAsync];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerActions') AND name=N'DelayMinutes')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerActions') AND c.name=N'DelayMinutes')
    ALTER TABLE [TriggerActions] ADD CONSTRAINT [DF_TriggerActions_DelayMinutes] DEFAULT (0) FOR [DelayMinutes];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerActions') AND name=N'TimeoutMinutes')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerActions') AND c.name=N'TimeoutMinutes')
    ALTER TABLE [TriggerActions] ADD CONSTRAINT [DF_TriggerActions_TimeoutMinutes] DEFAULT (5) FOR [TimeoutMinutes];
GO

-- ---------------------------------------------------------------------
-- TriggerConditions
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerConditions') AND name=N'GroupOrder')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerConditions') AND c.name=N'GroupOrder')
    ALTER TABLE [TriggerConditions] ADD CONSTRAINT [DF_TriggerConditions_GroupOrder] DEFAULT (0) FOR [GroupOrder];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerConditions') AND name=N'CreatedAt')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerConditions') AND c.name=N'CreatedAt')
    ALTER TABLE [TriggerConditions] ADD CONSTRAINT [DF_TriggerConditions_CreatedAt] DEFAULT (GETUTCDATE()) FOR [CreatedAt];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerConditions') AND name=N'ValueType')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerConditions') AND c.name=N'ValueType')
    ALTER TABLE [TriggerConditions] ADD CONSTRAINT [DF_TriggerConditions_ValueType] DEFAULT (N'String') FOR [ValueType];
GO

-- ---------------------------------------------------------------------
-- TriggerEvents
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TriggerEvents') AND name=N'ProcessingAttempts')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TriggerEvents') AND c.name=N'ProcessingAttempts')
    ALTER TABLE [TriggerEvents] ADD CONSTRAINT [DF_TriggerEvents_ProcessingAttempts] DEFAULT (0) FOR [ProcessingAttempts];
GO

-- ---------------------------------------------------------------------
-- InternalSyncRuns
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.InternalSyncRuns') AND name=N'Errors')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.InternalSyncRuns') AND c.name=N'Errors')
    ALTER TABLE [InternalSyncRuns] ADD CONSTRAINT [DF_InternalSyncRuns_Errors] DEFAULT (0) FOR [Errors];
GO

-- ---------------------------------------------------------------------
-- InternalSyncStepMappings
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.InternalSyncStepMappings') AND name=N'OverwriteExisting')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.InternalSyncStepMappings') AND c.name=N'OverwriteExisting')
    ALTER TABLE [InternalSyncStepMappings] ADD CONSTRAINT [DF_InternalSyncStepMappings_OverwriteExisting] DEFAULT (1) FOR [OverwriteExisting];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.InternalSyncStepMappings') AND name=N'MappingOrder')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.InternalSyncStepMappings') AND c.name=N'MappingOrder')
    ALTER TABLE [InternalSyncStepMappings] ADD CONSTRAINT [DF_InternalSyncStepMappings_MappingOrder] DEFAULT (0) FOR [MappingOrder];
GO

-- ---------------------------------------------------------------------
-- ComplianceFrameworks
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ComplianceFrameworks') AND name=N'TotalRequirements')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ComplianceFrameworks') AND c.name=N'TotalRequirements')
    ALTER TABLE [ComplianceFrameworks] ADD CONSTRAINT [DF_ComplianceFrameworks_TotalRequirements] DEFAULT (0) FOR [TotalRequirements];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ComplianceFrameworks') AND name=N'ImplementedControls')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ComplianceFrameworks') AND c.name=N'ImplementedControls')
    ALTER TABLE [ComplianceFrameworks] ADD CONSTRAINT [DF_ComplianceFrameworks_ImplementedControls] DEFAULT (0) FOR [ImplementedControls];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ComplianceFrameworks') AND name=N'ComplianceScore')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ComplianceFrameworks') AND c.name=N'ComplianceScore')
    ALTER TABLE [ComplianceFrameworks] ADD CONSTRAINT [DF_ComplianceFrameworks_ComplianceScore] DEFAULT (0) FOR [ComplianceScore];
GO

-- ---------------------------------------------------------------------
-- CompliancePolicies
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'IsRunning')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'IsRunning')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_IsRunning] DEFAULT (0) FOR [IsRunning];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'TotalExecutions')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'TotalExecutions')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_TotalExecutions] DEFAULT (0) FOR [TotalExecutions];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'LastActionCount')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'LastActionCount')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_LastActionCount] DEFAULT (0) FOR [LastActionCount];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'DailyProcessedCount')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'DailyProcessedCount')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_DailyProcessedCount] DEFAULT (0) FOR [DailyProcessedCount];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'EnableReminderSchedule')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'EnableReminderSchedule')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_EnableReminderSchedule] DEFAULT (0) FOR [EnableReminderSchedule];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'ScopeInheritance')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'ScopeInheritance')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_ScopeInheritance] DEFAULT (N'Inherit') FOR [ScopeInheritance];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'ReminderIntervalDays')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'ReminderIntervalDays')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_ReminderIntervalDays] DEFAULT (5) FOR [ReminderIntervalDays];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'LastViolationCount')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'LastViolationCount')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_LastViolationCount] DEFAULT (0) FOR [LastViolationCount];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'EnforcementMode')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'EnforcementMode')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_EnforcementMode] DEFAULT (N'Monitor') FOR [EnforcementMode];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'CurrentScope')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'CurrentScope')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_CurrentScope] DEFAULT (0) FOR [CurrentScope];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND name=N'FirstReminderDelayDays')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicies') AND c.name=N'FirstReminderDelayDays')
    ALTER TABLE [CompliancePolicies] ADD CONSTRAINT [DF_CompliancePolicies_FirstReminderDelayDays] DEFAULT (0) FOR [FirstReminderDelayDays];
GO

-- ---------------------------------------------------------------------
-- CompliancePolicyRule
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicyRule') AND name=N'LogicalOperator')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicyRule') AND c.name=N'LogicalOperator')
    ALTER TABLE [CompliancePolicyRule] ADD CONSTRAINT [DF_CompliancePolicyRule_LogicalOperator] DEFAULT (N'AND') FOR [LogicalOperator];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CompliancePolicyRule') AND name=N'GroupOperator')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.CompliancePolicyRule') AND c.name=N'GroupOperator')
    ALTER TABLE [CompliancePolicyRule] ADD CONSTRAINT [DF_CompliancePolicyRule_GroupOperator] DEFAULT (N'AND') FOR [GroupOperator];
GO

-- ---------------------------------------------------------------------
-- ComplianceFrameworkPolicyMappings
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ComplianceFrameworkPolicyMappings') AND name=N'ComplianceStatus')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ComplianceFrameworkPolicyMappings') AND c.name=N'ComplianceStatus')
    ALTER TABLE [ComplianceFrameworkPolicyMappings] ADD CONSTRAINT [DF_CFPM_ComplianceStatus] DEFAULT (N'NotAssessed') FOR [ComplianceStatus];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ComplianceFrameworkPolicyMappings') AND name=N'CoveragePercentage')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ComplianceFrameworkPolicyMappings') AND c.name=N'CoveragePercentage')
    ALTER TABLE [ComplianceFrameworkPolicyMappings] ADD CONSTRAINT [DF_CFPM_CoveragePercentage] DEFAULT (0) FOR [CoveragePercentage];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ComplianceFrameworkPolicyMappings') AND name=N'CreatedAt')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ComplianceFrameworkPolicyMappings') AND c.name=N'CreatedAt')
    ALTER TABLE [ComplianceFrameworkPolicyMappings] ADD CONSTRAINT [DF_CFPM_CreatedAt] DEFAULT (GETUTCDATE()) FOR [CreatedAt];
GO

-- ---------------------------------------------------------------------
-- FrameworkAssignments
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND name=N'TotalPolicies')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND c.name=N'TotalPolicies')
    ALTER TABLE [FrameworkAssignments] ADD CONSTRAINT [DF_FrameworkAssignments_TotalPolicies] DEFAULT (0) FOR [TotalPolicies];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND name=N'CriticalViolations')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND c.name=N'CriticalViolations')
    ALTER TABLE [FrameworkAssignments] ADD CONSTRAINT [DF_FrameworkAssignments_CriticalViolations] DEFAULT (0) FOR [CriticalViolations];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND name=N'TotalViolations')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND c.name=N'TotalViolations')
    ALTER TABLE [FrameworkAssignments] ADD CONSTRAINT [DF_FrameworkAssignments_TotalViolations] DEFAULT (0) FOR [TotalViolations];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND name=N'FailingPolicies')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND c.name=N'FailingPolicies')
    ALTER TABLE [FrameworkAssignments] ADD CONSTRAINT [DF_FrameworkAssignments_FailingPolicies] DEFAULT (0) FOR [FailingPolicies];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND name=N'ComplianceScore')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND c.name=N'ComplianceScore')
    ALTER TABLE [FrameworkAssignments] ADD CONSTRAINT [DF_FrameworkAssignments_ComplianceScore] DEFAULT (0) FOR [ComplianceScore];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND name=N'PassingPolicies')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.FrameworkAssignments') AND c.name=N'PassingPolicies')
    ALTER TABLE [FrameworkAssignments] ADD CONSTRAINT [DF_FrameworkAssignments_PassingPolicies] DEFAULT (0) FOR [PassingPolicies];
GO

-- ---------------------------------------------------------------------
-- ObjectGroupMemberships
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ObjectGroupMemberships') AND name=N'IsPrimary')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ObjectGroupMemberships') AND c.name=N'IsPrimary')
    ALTER TABLE [ObjectGroupMemberships] ADD CONSTRAINT [DF_ObjectGroupMemberships_IsPrimary] DEFAULT (0) FOR [IsPrimary];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ObjectGroupMemberships') AND name=N'IsActive')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ObjectGroupMemberships') AND c.name=N'IsActive')
    ALTER TABLE [ObjectGroupMemberships] ADD CONSTRAINT [DF_ObjectGroupMemberships_IsActive] DEFAULT (1) FOR [IsActive];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ObjectGroupMemberships') AND name=N'IsDirect')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ObjectGroupMemberships') AND c.name=N'IsDirect')
    ALTER TABLE [ObjectGroupMemberships] ADD CONSTRAINT [DF_ObjectGroupMemberships_IsDirect] DEFAULT (1) FOR [IsDirect];
GO

-- ---------------------------------------------------------------------
-- Tags
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Tags') AND name=N'CreatedAt')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Tags') AND c.name=N'CreatedAt')
    ALTER TABLE [Tags] ADD CONSTRAINT [DF_Tags_CreatedAt] DEFAULT (GETUTCDATE()) FOR [CreatedAt];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Tags') AND name=N'IsSystem')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Tags') AND c.name=N'IsSystem')
    ALTER TABLE [Tags] ADD CONSTRAINT [DF_Tags_IsSystem] DEFAULT (0) FOR [IsSystem];
GO

-- ---------------------------------------------------------------------
-- IdentityTags
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.IdentityTags') AND name=N'IsInherited')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.IdentityTags') AND c.name=N'IsInherited')
    ALTER TABLE [IdentityTags] ADD CONSTRAINT [DF_IdentityTags_IsInherited] DEFAULT (0) FOR [IsInherited];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.IdentityTags') AND name=N'CreatedAt')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.IdentityTags') AND c.name=N'CreatedAt')
    ALTER TABLE [IdentityTags] ADD CONSTRAINT [DF_IdentityTags_CreatedAt] DEFAULT (GETUTCDATE()) FOR [CreatedAt];
GO

-- ---------------------------------------------------------------------
-- ObjectTags
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ObjectTags') AND name=N'IsInherited')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ObjectTags') AND c.name=N'IsInherited')
    ALTER TABLE [ObjectTags] ADD CONSTRAINT [DF_ObjectTags_IsInherited] DEFAULT (0) FOR [IsInherited];
GO

-- ---------------------------------------------------------------------
-- AspNetRoles
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AspNetRoles') AND name=N'CreatedAt')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.AspNetRoles') AND c.name=N'CreatedAt')
    ALTER TABLE [AspNetRoles] ADD CONSTRAINT [DF_AspNetRoles_CreatedAt] DEFAULT (GETUTCDATE()) FOR [CreatedAt];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AspNetRoles') AND name=N'IsSystem')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.AspNetRoles') AND c.name=N'IsSystem')
    ALTER TABLE [AspNetRoles] ADD CONSTRAINT [DF_AspNetRoles_IsSystem] DEFAULT (0) FOR [IsSystem];
GO

-- ---------------------------------------------------------------------
-- WorkflowTriggerTemplates
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.WorkflowTriggerTemplates') AND name=N'UsageCount')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.WorkflowTriggerTemplates') AND c.name=N'UsageCount')
    ALTER TABLE [WorkflowTriggerTemplates] ADD CONSTRAINT [DF_WorkflowTriggerTemplates_UsageCount] DEFAULT (0) FOR [UsageCount];
GO

-- ---------------------------------------------------------------------
-- Objects bit-flags (3 flags omitted by InternalSyncStepExecutor +
-- ObjectsController INSERTs)
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Objects') AND name=N'IsAdminSDHolder')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Objects') AND c.name=N'IsAdminSDHolder')
    ALTER TABLE [Objects] ADD CONSTRAINT [DF_Objects_IsAdminSDHolder] DEFAULT (0) FOR [IsAdminSDHolder];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Objects') AND name=N'PasswordNeverExpires')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Objects') AND c.name=N'PasswordNeverExpires')
    ALTER TABLE [Objects] ADD CONSTRAINT [DF_Objects_PasswordNeverExpires] DEFAULT (0) FOR [PasswordNeverExpires];
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Objects') AND name=N'IsBuiltIn')
   AND NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Objects') AND c.name=N'IsBuiltIn')
    ALTER TABLE [Objects] ADD CONSTRAINT [DF_Objects_IsBuiltIn] DEFAULT (0) FOR [IsBuiltIn];
GO

PRINT 'V128: NOT NULL columns across audited tables now carry DEFAULT constraints';
GO
