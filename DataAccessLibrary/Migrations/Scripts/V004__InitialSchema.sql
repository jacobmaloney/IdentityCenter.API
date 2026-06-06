-- V004: Complete Initial Schema
-- Auto-generated from EF Core migrations, converted to idempotent Dapper migration
-- This script creates ALL tables, indexes, foreign keys, and constraints
-- All statements are wrapped in IF NOT EXISTS checks for idempotency

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AccessPolicies')
BEGIN
    CREATE TABLE [AccessPolicies] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(500) NULL,
            [Type] nvarchar(50) NOT NULL,
            [IsEnabled] bit NOT NULL,
            [Priority] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_AccessPolicies] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AccessReviewAssignments')
BEGIN
    CREATE TABLE [AccessReviewAssignments] (
            [Id] uniqueidentifier NOT NULL,
            [CampaignId] uniqueidentifier NOT NULL,
            [ReviewerId] uniqueidentifier NOT NULL,
            [ReviewerEmail] nvarchar(256) NULL,
            [ReviewerName] nvarchar(200) NULL,
            [ReviewTargetId] uniqueidentifier NOT NULL,
            [ReviewTargetType] nvarchar(50) NOT NULL,
            [ReviewTargetName] nvarchar(200) NULL,
            [ContextData] nvarchar(max) NULL,
            [RiskScore] int NOT NULL,
            [RiskLevel] nvarchar(50) NULL,
            [LastAccessDate] datetime2 NULL,
            [AccessFrequency] nvarchar(50) NULL,
            [ReasonForAccess] nvarchar(max) NULL,
            [Status] nvarchar(50) NOT NULL,
            [AssignedAt] datetime2 NOT NULL,
            [DueDate] datetime2 NULL,
            [CompletedAt] datetime2 NULL,
            [Decision] nvarchar(50) NULL,
            [Justification] nvarchar(max) NULL,
            [Comments] nvarchar(max) NULL,
            [IsDelegated] bit NOT NULL,
            [DelegatedTo] uniqueidentifier NULL,
            [DelegatedAt] datetime2 NULL,
            [DelegationReason] nvarchar(max) NULL,
            [IsEscalated] bit NOT NULL,
            [EscalatedTo] uniqueidentifier NULL,
            [EscalatedAt] datetime2 NULL,
            [EscalationReason] nvarchar(max) NULL,
            [RemindersSent] int NOT NULL,
            [LastReminderSent] datetime2 NULL,
            [IpAddress] nvarchar(max) NULL,
            [UserAgent] nvarchar(max) NULL,
            [WorkflowInstanceId] uniqueidentifier NULL,
            [PeerGroupId] uniqueidentifier NULL,
            [CompletedByPeerId] uniqueidentifier NULL,
            [CompletedByPeerName] nvarchar(500) NULL,
            [RemediationStatus] nvarchar(50) NULL,
            [RemediationCompletedAt] datetime2 NULL,
            [RemediationDetails] nvarchar(max) NULL,
            CONSTRAINT [PK_AccessReviewAssignments] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AccessReviewSettings')
BEGIN
    CREATE TABLE [AccessReviewSettings] (
            [Id] uniqueidentifier NOT NULL,
            [DefaultReviewPeriodDays] int NOT NULL,
            [DefaultReminderFrequencyDays] int NOT NULL,
            [RequireJustification] bit NOT NULL,
            [EnableNotifications] bit NOT NULL,
            [AutoRevokeOnExpiry] bit NOT NULL,
            [AllowBulkApproval] bit NOT NULL,
            [DefaultComplianceFramework] nvarchar(100) NULL,
            [EscalationDays] int NOT NULL,
            [AutoCreatePolicyReviews] bit NOT NULL,
            [AutoReviewNewUsers] bit NOT NULL,
            [NewUserReviewDays] int NOT NULL,
            [AutoReviewHighRisk] bit NOT NULL,
            [HighRiskReviewFrequency] nvarchar(50) NOT NULL,
            [AutoReviewGroupMemberships] bit NOT NULL,
            [GroupReviewFrequency] nvarchar(50) NOT NULL,
            [NotificationFromEmail] nvarchar(256) NULL,
            [NotificationFromName] nvarchar(200) NULL,
            [NotificationSubjectTemplate] nvarchar(500) NULL,
            [NotificationBodyTemplate] nvarchar(max) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_AccessReviewSettings] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AdminNotifications')
BEGIN
    CREATE TABLE [AdminNotifications] (
            [Id] uniqueidentifier NOT NULL,
            [NotificationType] nvarchar(50) NOT NULL,
            [Category] nvarchar(50) NOT NULL,
            [Severity] nvarchar(20) NOT NULL,
            [Title] nvarchar(200) NOT NULL,
            [Message] nvarchar(max) NOT NULL,
            [ActionUrl] nvarchar(500) NULL,
            [ActionText] nvarchar(50) NULL,
            [RelatedEntityId] uniqueidentifier NULL,
            [RelatedEntityType] nvarchar(50) NULL,
            [Source] nvarchar(100) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [IsRead] bit NOT NULL,
            [ReadAt] datetime2 NULL,
            [ReadBy] nvarchar(256) NULL,
            [IsDismissed] bit NOT NULL,
            [DismissedAt] datetime2 NULL,
            [Metadata] nvarchar(max) NULL,
            CONSTRAINT [PK_AdminNotifications] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ApprovalWorkflows')
BEGIN
    CREATE TABLE [ApprovalWorkflows] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(500) NULL,
            [ResourceType] nvarchar(100) NOT NULL,
            [IsActive] bit NOT NULL,
            [Category] nvarchar(100) NULL,
            [IsTemplate] bit NOT NULL,
            [Priority] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [CreatedBy] nvarchar(100) NULL,
            [ModifiedBy] nvarchar(100) NULL,
            CONSTRAINT [PK_ApprovalWorkflows] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetRoles')
BEGIN
    CREATE TABLE [AspNetRoles] (
            [Id] nvarchar(450) NOT NULL,
            [Description] nvarchar(max) NOT NULL,
            [Permissions] nvarchar(max) NOT NULL,
            [AdGroupMappings] nvarchar(max) NOT NULL,
            [EntraIdGroupMappings] nvarchar(max) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [IsSystem] bit NOT NULL,
            [Name] nvarchar(256) NULL,
            [NormalizedName] nvarchar(256) NULL,
            [ConcurrencyStamp] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUsers')
BEGIN
    CREATE TABLE [AspNetUsers] (
            [Id] nvarchar(450) NOT NULL,
            [DisplayName] nvarchar(max) NOT NULL,
            [FirstName] nvarchar(max) NOT NULL,
            [LastName] nvarchar(max) NOT NULL,
            [Department] nvarchar(max) NOT NULL,
            [Title] nvarchar(max) NOT NULL,
            [ManagerId] nvarchar(max) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [LastLoginAt] datetime2 NULL,
            [Source] nvarchar(max) NOT NULL,
            [ExternalId] nvarchar(max) NOT NULL,
            [IsActive] bit NOT NULL,
            [IsSystem] bit NOT NULL,
            [PersonId] uniqueidentifier NULL,
            [EmailNotifications] bit NOT NULL,
            [TeamsNotifications] bit NOT NULL,
            [SystemAlerts] bit NOT NULL,
            [TimeZone] nvarchar(max) NOT NULL,
            [Language] nvarchar(max) NOT NULL,
            [Theme] nvarchar(max) NOT NULL,
            [UserName] nvarchar(256) NULL,
            [NormalizedUserName] nvarchar(256) NULL,
            [Email] nvarchar(256) NULL,
            [NormalizedEmail] nvarchar(256) NULL,
            [EmailConfirmed] bit NOT NULL,
            [PasswordHash] nvarchar(max) NULL,
            [SecurityStamp] nvarchar(max) NULL,
            [ConcurrencyStamp] nvarchar(max) NULL,
            [PhoneNumber] nvarchar(max) NULL,
            [PhoneNumberConfirmed] bit NOT NULL,
            [TwoFactorEnabled] bit NOT NULL,
            [LockoutEnd] datetimeoffset NULL,
            [LockoutEnabled] bit NOT NULL,
            [AccessFailedCount] int NOT NULL,
            CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AuditLogs')
BEGIN
    CREATE TABLE [AuditLogs] (
            [Id] bigint NOT NULL IDENTITY,
            [Timestamp] datetime2 NOT NULL,
            [Level] nvarchar(20) NOT NULL,
            [Category] nvarchar(100) NULL,
            [UserId] nvarchar(256) NULL,
            [Action] nvarchar(200) NOT NULL,
            [EntityType] nvarchar(100) NULL,
            [EntityId] nvarchar(256) NULL,
            [OldValues] nvarchar(max) NULL,
            [NewValues] nvarchar(max) NULL,
            [IpAddress] nvarchar(45) NULL,
            [UserAgent] nvarchar(500) NULL,
            [CorrelationId] uniqueidentifier NULL,
            CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BusinessRoleCategories')
BEGIN
    CREATE TABLE [BusinessRoleCategories] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Icon] nvarchar(50) NULL,
            [ColorStart] nvarchar(20) NULL,
            [ColorEnd] nvarchar(20) NULL,
            [SortOrder] int NOT NULL,
            [IsSystem] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            CONSTRAINT [PK_BusinessRoleCategories] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Campaigns')
BEGIN
    CREATE TABLE [Campaigns] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(max) NULL,
            [CampaignType] nvarchar(50) NOT NULL,
            [ReviewType] nvarchar(50) NULL,
            [ConnectionScope] nvarchar(max) NULL,
            [ObjectClassFilter] nvarchar(100) NULL,
            [DepartmentFilter] nvarchar(max) NULL,
            [RiskLevelFilter] nvarchar(50) NULL,
            [PolicyViolationFilter] bit NOT NULL,
            [CustomScopeFilter] nvarchar(max) NULL,
            [IncludeNestedMemberships] bit NOT NULL,
            [MaxNestedDepth] int NOT NULL,
            [SelectedPolicyIds] nvarchar(max) NULL,
            [StartDate] datetime2 NOT NULL,
            [EndDate] datetime2 NOT NULL,
            [DueDate] datetime2 NULL,
            [ReviewPeriodDays] int NOT NULL,
            [Status] nvarchar(50) NOT NULL,
            [CompletionPercentage] decimal(5,2) NOT NULL,
            [TotalAssignments] int NOT NULL,
            [CompletedAssignments] int NOT NULL,
            [AutoGenerated] bit NOT NULL,
            [SourcePolicyExecutionId] uniqueidentifier NULL,
            [ParentCampaignId] uniqueidentifier NULL,
            [IsRecurring] bit NOT NULL,
            [RecurrencePattern] nvarchar(max) NULL,
            [AssignmentStrategy] nvarchar(50) NULL,
            [NotificationSettings] nvarchar(max) NULL,
            [EscalationSettings] nvarchar(max) NULL,
            [EnableNotifications] bit NOT NULL,
            [ReminderDaysBefore] int NOT NULL,
            [OnDenialAction] nvarchar(50) NOT NULL,
            [AutoRemediateOnDenial] bit NOT NULL,
            [OnIncompleteAction] nvarchar(50) NOT NULL,
            [EscalationReviewerId] uniqueidentifier NULL,
            [ExtensionDays] int NOT NULL,
            [OnApprovalAction] nvarchar(50) NOT NULL,
            [CompletionActionsProcessed] bit NOT NULL,
            [CompletionActionsProcessedAt] datetime2 NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            [ComplianceFramework] nvarchar(100) NULL,
            [WorkflowId] uniqueidentifier NULL,
            [AssignmentEmailTemplateId] uniqueidentifier NULL,
            [ReminderEmailTemplateId] uniqueidentifier NULL,
            CONSTRAINT [PK_Campaigns] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CampaignTemplates')
BEGIN
    CREATE TABLE [CampaignTemplates] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(max) NULL,
            [TemplateType] nvarchar(50) NOT NULL,
            [ComplianceFramework] nvarchar(100) NULL,
            [DefaultConfiguration] nvarchar(max) NULL,
            [ScopeConfiguration] nvarchar(max) NULL,
            [NotificationConfiguration] nvarchar(max) NULL,
            [DefaultReviewPeriodDays] int NOT NULL,
            [RecurrencePattern] nvarchar(50) NULL,
            [IsBuiltIn] bit NOT NULL,
            [IsActive] bit NOT NULL,
            [UsageCount] int NOT NULL,
            [LastUsedAt] datetime2 NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(200) NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(200) NULL,
            CONSTRAINT [PK_CampaignTemplates] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChangeAuditLogs')
BEGIN
    CREATE TABLE [ChangeAuditLogs] (
            [Id] bigint NOT NULL IDENTITY,
            [Timestamp] datetime2 NOT NULL,
            [UserId] nvarchar(256) NULL,
            [UserDisplayName] nvarchar(256) NULL,
            [UserEmail] nvarchar(256) NULL,
            [IpAddress] nvarchar(45) NULL,
            [OperationType] int NOT NULL,
            [EntityType] nvarchar(50) NULL,
            [EntityId] uniqueidentifier NULL,
            [EntityDisplayName] nvarchar(256) NULL,
            [PropertyName] nvarchar(100) NULL,
            [OldValue] nvarchar(2000) NULL,
            [NewValue] nvarchar(2000) NULL,
            [RelatedEntityId] uniqueidentifier NULL,
            [RelatedEntityName] nvarchar(256) NULL,
            [Reason] nvarchar(500) NULL,
            [TicketNumber] nvarchar(100) NULL,
            [ApprovedBy] uniqueidentifier NULL,
            [ApproverName] nvarchar(256) NULL,
            [Success] bit NOT NULL,
            [ErrorMessage] nvarchar(1000) NULL,
            [CorrelationId] uniqueidentifier NULL,
            [Source] nvarchar(50) NULL,
            CONSTRAINT [PK_ChangeAuditLogs] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ComplianceFrameworks')
BEGIN
    CREATE TABLE [ComplianceFrameworks] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Code] nvarchar(50) NOT NULL,
            [Description] nvarchar(2000) NULL,
            [Category] nvarchar(50) NOT NULL,
            [Authority] nvarchar(200) NULL,
            [Jurisdiction] nvarchar(100) NULL,
            [Industry] nvarchar(100) NULL,
            [Version] nvarchar(50) NULL,
            [PublishedDate] datetime2 NULL,
            [IsActive] bit NOT NULL,
            [IsBuiltIn] bit NOT NULL,
            [ComplianceScore] decimal(5,2) NOT NULL,
            [TotalRequirements] int NOT NULL,
            [ImplementedControls] int NOT NULL,
            [LastAssessmentDate] datetime2 NULL,
            [Color] nvarchar(20) NOT NULL,
            [Icon] nvarchar(50) NULL,
            [ScopeConnectionIds] nvarchar(2000) NULL,
            [ScopeTags] nvarchar(2000) NULL,
            [ScopeAttributeQuery] nvarchar(4000) NULL,
            [ScopeGroupIds] nvarchar(4000) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_ComplianceFrameworks] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CompliancePolicies')
BEGIN
    CREATE TABLE [CompliancePolicies] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [DisplayName] nvarchar(500) NULL,
            [Description] nvarchar(1000) NULL,
            [Category] nvarchar(50) NOT NULL,
            [Severity] int NOT NULL,
            [Priority] int NOT NULL,
            [IsActive] bit NOT NULL,
            [IsBuiltIn] bit NOT NULL,
            [EvaluationFrequencyHours] int NOT NULL,
            [LastEvaluationDate] datetime2 NULL,
            [NextEvaluationDate] datetime2 NULL,
            [ComplianceFramework] nvarchar(100) NULL,
            [CurrentScope] int NOT NULL,
            [LastViolationCount] int NOT NULL,
            [LastActionCount] int NOT NULL,
            [IsRunning] bit NOT NULL,
            [LastRunAt] datetime2 NULL,
            [TotalExecutions] int NOT NULL,
            [EnforcementMode] nvarchar(20) NOT NULL,
            [DailyProcessingLimit] int NULL,
            [DailyProcessedCount] int NOT NULL,
            [LastProcessingResetDate] datetime2 NULL,
            [FirstReminderDelayDays] int NOT NULL,
            [ReminderIntervalDays] int NOT NULL,
            [MaxReminderCount] int NULL,
            [EnableReminderSchedule] bit NOT NULL,
            [ScopeConnectionIds] nvarchar(2000) NULL,
            [ScopeTags] nvarchar(2000) NULL,
            [ScopeAttributeQuery] nvarchar(4000) NULL,
            [ScopeGroupIds] nvarchar(4000) NULL,
            [ScopeInheritance] nvarchar(20) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_CompliancePolicies] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DirectoryConnections')
BEGIN
    CREATE TABLE [DirectoryConnections] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [ConnectionType] nvarchar(50) NOT NULL,
            [ConnectionString] nvarchar(max) NOT NULL,
            [Credentials] nvarchar(max) NOT NULL,
            [Configuration] nvarchar(max) NULL,
            [IsActive] bit NOT NULL,
            [IsAuthoritative] bit NOT NULL,
            [LastSyncAt] datetime2 NULL,
            [LastTestAt] datetime2 NULL,
            [LastTestResult] nvarchar(max) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_DirectoryConnections] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmailQueue')
BEGIN
    CREATE TABLE [EmailQueue] (
            [Id] uniqueidentifier NOT NULL,
            [ToAddress] nvarchar(255) NOT NULL,
            [ToDisplayName] nvarchar(200) NULL,
            [Subject] nvarchar(500) NOT NULL,
            [Body] nvarchar(max) NOT NULL,
            [IsHtml] bit NOT NULL,
            [Status] nvarchar(max) NOT NULL,
            [RetryCount] int NOT NULL,
            [MaxRetries] int NOT NULL,
            [SentAt] datetime2 NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [TemplateId] nvarchar(max) NULL,
            [RelatedEntityType] nvarchar(max) NULL,
            [RelatedEntityId] uniqueidentifier NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ProcessedAt] datetime2 NULL,
            CONSTRAINT [PK_EmailQueue] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmailTemplates')
BEGIN
    CREATE TABLE [EmailTemplates] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Subject] nvarchar(500) NOT NULL,
            [Body] nvarchar(max) NOT NULL,
            [Category] nvarchar(100) NULL,
            [IsActive] bit NOT NULL,
            [IsBuiltIn] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_EmailTemplates] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Identities')
BEGIN
    CREATE TABLE [Identities] (
            [Id] uniqueidentifier NOT NULL,
            [DisplayName] nvarchar(500) NOT NULL,
            [FirstName] nvarchar(500) NULL,
            [LastName] nvarchar(500) NULL,
            [MiddleName] nvarchar(500) NULL,
            [PrimaryEmail] nvarchar(500) NULL,
            [PrimaryPhone] nvarchar(50) NULL,
            [Department] nvarchar(500) NULL,
            [JobTitle] nvarchar(500) NULL,
            [AuthoritativeSourceId] uniqueidentifier NULL,
            [ManagerIdentityId] uniqueidentifier NULL,
            [IsActive] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [LastSeenAt] datetime2 NULL,
            CONSTRAINT [PK_Identities] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_Identities_Identities_ManagerIdentityId] FOREIGN KEY ([ManagerIdentityId]) REFERENCES [Identities] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'IdentityProviders')
BEGIN
    CREATE TABLE [IdentityProviders] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Type] nvarchar(50) NOT NULL,
            [IsEnabled] bit NOT NULL,
            [IsPrimary] bit NOT NULL,
            [Configuration] nvarchar(max) NOT NULL,
            [Metadata] nvarchar(max) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_IdentityProviders] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'InternalSyncRuns')
BEGIN
    CREATE TABLE [InternalSyncRuns] (
            [Id] uniqueidentifier NOT NULL,
            [OperationType] nvarchar(50) NOT NULL,
            [MatchStrategy] nvarchar(50) NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [Status] nvarchar(20) NOT NULL,
            [TotalProcessed] int NOT NULL,
            [Matched] int NOT NULL,
            [Created] int NOT NULL,
            [Skipped] int NOT NULL,
            [Errors] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [SyncProjectId] uniqueidentifier NULL,
            CONSTRAINT [PK_InternalSyncRuns] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'JobExecutionHistory')
BEGIN
    CREATE TABLE [JobExecutionHistory] (
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
            [ItemsProcessed] int NOT NULL,
            [ItemsSucceeded] int NOT NULL,
            [ItemsFailed] int NOT NULL,
            [ResultSummaryJson] nvarchar(max) NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [ExceptionDetails] nvarchar(max) NULL,
            [ExecutingServer] nvarchar(100) NULL,
            [NextScheduledRun] datetime2 NULL,
            [IsRetry] bit NOT NULL,
            [RetryCount] int NOT NULL,
            [ParentExecutionId] uniqueidentifier NULL,
            CONSTRAINT [PK_JobExecutionHistory] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'OrganizationalFolders')
BEGIN
    CREATE TABLE [OrganizationalFolders] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(1000) NULL,
            [ParentId] uniqueidentifier NULL,
            [FolderType] nvarchar(50) NOT NULL,
            [QueryFilter] nvarchar(max) NULL,
            [IconClass] nvarchar(100) NULL,
            [SortOrder] int NOT NULL,
            [IsSystem] bit NOT NULL,
            [IsActive] bit NOT NULL,
            [ManagerIdentityId] uniqueidentifier NULL,
            [MemberCount] int NOT NULL,
            [MemberCountUpdatedAt] datetime2 NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(200) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(200) NULL,
            CONSTRAINT [PK_OrganizationalFolders] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_OrganizationalFolders_OrganizationalFolders_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [OrganizationalFolders] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'RemoteAgents')
BEGIN
    CREATE TABLE [RemoteAgents] (
            [Id] uniqueidentifier NOT NULL,
            [AgentName] nvarchar(200) NOT NULL,
            [Description] nvarchar(500) NOT NULL,
            [MachineName] nvarchar(200) NOT NULL,
            [IpAddress] nvarchar(50) NULL,
            [Version] nvarchar(50) NOT NULL,
            [OperatingSystem] nvarchar(200) NULL,
            [ApiKeyHash] nvarchar(500) NOT NULL,
            [Status] nvarchar(20) NOT NULL,
            [SupportedJobTypes] nvarchar(500) NOT NULL,
            [MaxConcurrentJobs] int NOT NULL,
            [CurrentJobCount] int NOT NULL,
            [LastHeartbeat] datetime2 NULL,
            [LastJobClaimed] datetime2 NULL,
            [LastJobCompleted] datetime2 NULL,
            [TotalJobsProcessed] int NOT NULL,
            [TotalJobsFailed] int NOT NULL,
            [IsEnabled] bit NOT NULL,
            [RegisteredAt] datetime2 NOT NULL,
            [ConfigUpdatedAt] datetime2 NULL,
            [ConfigurationJson] nvarchar(max) NULL,
            [Tags] nvarchar(500) NULL,
            [Priority] int NOT NULL,
            CONSTRAINT [PK_RemoteAgents] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Reports')
BEGIN
    CREATE TABLE [Reports] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [DisplayName] nvarchar(200) NOT NULL,
            [Description] nvarchar(1000) NOT NULL,
            [Category] nvarchar(50) NOT NULL,
            [SubCategory] nvarchar(50) NOT NULL,
            [Icon] nvarchar(50) NOT NULL,
            [QueryDefinition] nvarchar(max) NOT NULL,
            [ConfigurationJson] nvarchar(max) NOT NULL,
            [DefaultFilters] nvarchar(max) NOT NULL,
            [ParametersJson] nvarchar(max) NOT NULL,
            [IsBuiltIn] bit NOT NULL,
            [IsActive] bit NOT NULL,
            [IsPublic] bit NOT NULL,
            [RequiredRole] nvarchar(50) NULL,
            [Tags] nvarchar(500) NOT NULL,
            [SortOrder] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(max) NULL,
            CONSTRAINT [PK_Reports] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ReportTemplates')
BEGIN
    CREATE TABLE [ReportTemplates] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Description] nvarchar(500) NOT NULL,
            [Category] nvarchar(50) NOT NULL,
            [Icon] nvarchar(50) NOT NULL,
            [ConfigurationTemplate] nvarchar(max) NOT NULL,
            [IsBuiltIn] bit NOT NULL,
            [IsActive] bit NOT NULL,
            [SortOrder] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_ReportTemplates] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ReviewDecisionHistory')
BEGIN
    CREATE TABLE [ReviewDecisionHistory] (
            [Id] uniqueidentifier NOT NULL,
            [AssignmentId] uniqueidentifier NOT NULL,
            [CampaignId] uniqueidentifier NOT NULL,
            [Decision] nvarchar(50) NOT NULL,
            [PreviousDecision] nvarchar(50) NULL,
            [Justification] nvarchar(max) NULL,
            [Comments] nvarchar(max) NULL,
            [DecisionMakerId] uniqueidentifier NOT NULL,
            [DecisionMakerName] nvarchar(200) NOT NULL,
            [DecisionMakerEmail] nvarchar(256) NULL,
            [DecisionDate] datetime2 NOT NULL,
            [IpAddress] nvarchar(max) NULL,
            [UserAgent] nvarchar(max) NULL,
            [DecisionContext] nvarchar(max) NULL,
            [RiskScoreAtDecision] int NOT NULL,
            [RiskLevelAtDecision] nvarchar(50) NULL,
            [ComplianceFramework] nvarchar(100) NULL,
            [WasEscalated] bit NOT NULL,
            [WasDelegated] bit NOT NULL,
            CONSTRAINT [PK_ReviewDecisionHistory] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ScheduleTemplates')
BEGIN
    CREATE TABLE [ScheduleTemplates] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Description] nvarchar(500) NULL,
            [Category] nvarchar(50) NOT NULL,
            [CronExpression] nvarchar(100) NOT NULL,
            [SortOrder] int NOT NULL,
            [IsSystem] bit NOT NULL,
            [IsActive] bit NOT NULL,
            [IconClass] nvarchar(50) NULL,
            [Color] nvarchar(20) NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_ScheduleTemplates] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Settings')
BEGIN
    CREATE TABLE [Settings] (
            [Id] int NOT NULL IDENTITY,
            [Category] nvarchar(100) NOT NULL,
            [Key] nvarchar(200) NOT NULL,
            [Value] nvarchar(max) NOT NULL,
            [IsEncrypted] bit NOT NULL,
            [DataType] nvarchar(50) NULL,
            [ModifiedAt] datetime2 NOT NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SMTPConfiguration')
BEGIN
    CREATE TABLE [SMTPConfiguration] (
            [Id] uniqueidentifier NOT NULL,
            [DisplayName] nvarchar(200) NOT NULL,
            [Description] nvarchar(500) NULL,
            [IsDefault] bit NOT NULL,
            [IsActive] bit NOT NULL,
            [Server] nvarchar(max) NOT NULL,
            [Port] int NOT NULL,
            [EnableSsl] bit NOT NULL,
            [Username] nvarchar(max) NOT NULL,
            [Password] nvarchar(max) NOT NULL,
            [FromAddress] nvarchar(255) NOT NULL,
            [FromDisplayName] nvarchar(200) NULL,
            [ReplyToAddress] nvarchar(255) NULL,
            [ReplyToDisplayName] nvarchar(200) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(255) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(255) NULL,
            [LastTestDate] datetime2 NULL,
            [LastTestResult] nvarchar(max) NULL,
            [LastTestSuccess] bit NULL,
            CONSTRAINT [PK_SMTPConfiguration] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncProcessingScripts')
BEGIN
    CREATE TABLE [SyncProcessingScripts] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(2000) NULL,
            [ScriptType] nvarchar(50) NOT NULL,
            [ScriptCode] nvarchar(max) NOT NULL,
            [IsSystem] bit NOT NULL,
            [IsEnabled] bit NOT NULL,
            [Version] int NOT NULL,
            [Category] nvarchar(100) NOT NULL,
            [CompilationStatus] nvarchar(50) NOT NULL,
            [CompilationError] nvarchar(max) NULL,
            [LastCompiledAt] datetime2 NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            [CopiedFromScriptId] uniqueidentifier NULL,
            CONSTRAINT [PK_SyncProcessingScripts] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncProjectTemplates')
BEGIN
    CREATE TABLE [SyncProjectTemplates] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(max) NULL,
            [Category] nvarchar(100) NULL,
            [IsSystem] bit NOT NULL,
            [TemplateJson] nvarchar(max) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_SyncProjectTemplates] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SystemConfigurations')
BEGIN
    CREATE TABLE [SystemConfigurations] (
            [Id] int NOT NULL IDENTITY,
            [AllowSelfRegistration] bit NOT NULL,
            [RequireEmailConfirmation] bit NOT NULL,
            [AllowExternalLogins] bit NOT NULL,
            [MinimumPasswordLength] int NOT NULL,
            [RequireDigit] bit NOT NULL,
            [RequireLowercase] bit NOT NULL,
            [RequireUppercase] bit NOT NULL,
            [RequireNonAlphanumeric] bit NOT NULL,
            [MaxFailedAccessAttempts] int NOT NULL,
            [LockoutDurationMinutes] int NOT NULL,
            [SessionTimeoutMinutes] int NOT NULL,
            [SlidingExpiration] bit NOT NULL,
            [EnableAuditLogging] bit NOT NULL,
            [AuditRetentionDays] int NOT NULL,
            [PortalUrl] nvarchar(max) NOT NULL,
            [PortalDisplayName] nvarchar(max) NOT NULL,
            [AdminNotificationEmail] nvarchar(max) NULL,
            [EnablePolicyNotifications] bit NOT NULL,
            [EnableSyncNotifications] bit NOT NULL,
            [EnableEscalationNotifications] bit NOT NULL,
            [ChatLlmEnabled] bit NOT NULL,
            [ChatLlmProvider] nvarchar(max) NOT NULL,
            [ChatLlmEndpoint] nvarchar(max) NOT NULL,
            [ChatLlmApiKey] nvarchar(max) NULL,
            [ChatLlmModel] nvarchar(max) NOT NULL,
            [ChatLlmMaxTokens] int NOT NULL,
            [ChatLlmTemperature] float NOT NULL,
            [ChatLlmTimeoutSeconds] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(max) NOT NULL,
            [ComplianceEscalationSettings] nvarchar(max) NULL,
            [NotificationIntegrationSettings] nvarchar(max) NULL,
            CONSTRAINT [PK_SystemConfigurations] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Tags')
BEGIN
    CREATE TABLE [Tags] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Description] nvarchar(500) NULL,
            [Color] nvarchar(50) NULL,
            [Icon] nvarchar(50) NULL,
            [IsSystem] bit NOT NULL,
            [Category] nvarchar(100) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_Tags] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TeamsMessageQueue')
BEGIN
    CREATE TABLE [TeamsMessageQueue] (
            [Id] uniqueidentifier NOT NULL,
            [Recipient] nvarchar(500) NOT NULL,
            [RecipientType] nvarchar(50) NOT NULL,
            [MessageContent] nvarchar(max) NOT NULL,
            [IsAdaptiveCard] bit NOT NULL,
            [Status] nvarchar(50) NOT NULL,
            [RetryCount] int NOT NULL,
            [MaxRetries] int NOT NULL,
            [SentAt] datetime2 NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [TemplateId] uniqueidentifier NULL,
            [RelatedEntityType] nvarchar(max) NULL,
            [RelatedEntityId] uniqueidentifier NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ProcessedAt] datetime2 NULL,
            CONSTRAINT [PK_TeamsMessageQueue] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TeamsMessageTemplates')
BEGIN
    CREATE TABLE [TeamsMessageTemplates] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Description] nvarchar(500) NULL,
            [MessageTemplate] nvarchar(max) NOT NULL,
            [UseAdaptiveCard] bit NOT NULL,
            [AdaptiveCardJson] nvarchar(max) NULL,
            [Category] nvarchar(100) NULL,
            [IsActive] bit NOT NULL,
            [IsBuiltIn] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_TeamsMessageTemplates] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TicketingConfigurations')
BEGIN
    CREATE TABLE [TicketingConfigurations] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [SystemType] nvarchar(50) NOT NULL,
            [IsEnabled] bit NOT NULL,
            [IsDefault] bit NOT NULL,
            [BaseUrl] nvarchar(500) NULL,
            [ApiEndpoint] nvarchar(200) NULL,
            [AuthenticationType] nvarchar(50) NOT NULL,
            [Username] nvarchar(200) NULL,
            [EncryptedCredential] nvarchar(max) NULL,
            [ClientId] nvarchar(200) NULL,
            [EncryptedClientSecret] nvarchar(max) NULL,
            [TokenEndpoint] nvarchar(500) NULL,
            [DefaultCategory] nvarchar(100) NOT NULL,
            [DefaultAssignmentGroup] nvarchar(200) NULL,
            [DefaultAssignee] nvarchar(200) NULL,
            [PayloadTemplate] nvarchar(max) NULL,
            [CustomHeaders] nvarchar(max) NULL,
            [PriorityMapping] nvarchar(max) NULL,
            [TicketIdPath] nvarchar(200) NULL,
            [TicketNumberPath] nvarchar(200) NULL,
            [TicketUrlTemplate] nvarchar(500) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedBy] nvarchar(256) NULL,
            [LastTestedAt] datetime2 NULL,
            [LastTestSuccessful] bit NULL,
            CONSTRAINT [PK_TicketingConfigurations] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TicketingLogs')
BEGIN
    CREATE TABLE [TicketingLogs] (
            [Id] uniqueidentifier NOT NULL,
            [ConfigurationId] uniqueidentifier NOT NULL,
            [ExternalTicketId] nvarchar(100) NULL,
            [ExternalTicketNumber] nvarchar(50) NULL,
            [TicketUrl] nvarchar(500) NULL,
            [Title] nvarchar(500) NOT NULL,
            [TicketType] nvarchar(50) NOT NULL,
            [Priority] nvarchar(20) NULL,
            [RelatedEntityType] nvarchar(50) NULL,
            [RelatedEntityId] uniqueidentifier NULL,
            [Success] bit NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [RequestPayload] nvarchar(max) NULL,
            [ResponsePayload] nvarchar(max) NULL,
            [HttpStatusCode] int NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_TicketingLogs] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerEvents')
BEGIN
    CREATE TABLE [TriggerEvents] (
            [Id] uniqueidentifier NOT NULL,
            [EventType] nvarchar(100) NOT NULL,
            [EventSource] nvarchar(200) NOT NULL,
            [EventData] nvarchar(max) NOT NULL,
            [TargetEntityType] nvarchar(100) NULL,
            [TargetEntityId] uniqueidentifier NULL,
            [Status] nvarchar(50) NOT NULL,
            [ProcessingAttempts] int NOT NULL,
            [LastAttemptAt] datetime2 NULL,
            [ProcessedAt] datetime2 NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [IdempotencyKey] nvarchar(500) NULL,
            [OccurredAt] datetime2 NOT NULL,
            [ExpiresAt] datetime2 NULL,
            [CorrelationId] uniqueidentifier NULL,
            [CausationId] uniqueidentifier NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_TriggerEvents] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowTriggers')
BEGIN
    CREATE TABLE [WorkflowTriggers] (
            [Id] uniqueidentifier NOT NULL,
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
            [IsActive] bit NOT NULL,
            [IsSystem] bit NOT NULL,
            [Priority] int NOT NULL,
            [CooldownMinutes] int NOT NULL,
            [TestMode] bit NOT NULL,
            [TriggerCount] int NOT NULL,
            [LastTriggeredAt] datetime2 NULL,
            [SuccessCount] int NOT NULL,
            [FailureCount] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_WorkflowTriggers] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowTriggerTemplates')
BEGIN
    CREATE TABLE [WorkflowTriggerTemplates] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(2000) NULL,
            [Category] nvarchar(100) NULL,
            [Icon] nvarchar(100) NULL,
            [Color] nvarchar(50) NULL,
            [IsSystem] bit NOT NULL,
            [TemplateJson] nvarchar(max) NOT NULL,
            [UsageCount] int NOT NULL,
            [SortOrder] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_WorkflowTriggerTemplates] PRIMARY KEY ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PolicyActions')
BEGIN
    CREATE TABLE [PolicyActions] (
            [Id] uniqueidentifier NOT NULL,
            [PolicyId] uniqueidentifier NOT NULL,
            [ActionType] nvarchar(100) NOT NULL,
            [Parameters] nvarchar(max) NOT NULL,
            [Description] nvarchar(500) NULL,
            CONSTRAINT [PK_PolicyActions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_PolicyActions_AccessPolicies_PolicyId] FOREIGN KEY ([PolicyId]) REFERENCES [AccessPolicies] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PolicyConditions')
BEGIN
    CREATE TABLE [PolicyConditions] (
            [Id] uniqueidentifier NOT NULL,
            [PolicyId] uniqueidentifier NOT NULL,
            [ConditionType] nvarchar(100) NOT NULL,
            [Operator] nvarchar(50) NOT NULL,
            [Value] nvarchar(max) NOT NULL,
            [Description] nvarchar(500) NULL,
            CONSTRAINT [PK_PolicyConditions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_PolicyConditions_AccessPolicies_PolicyId] FOREIGN KEY ([PolicyId]) REFERENCES [AccessPolicies] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ApprovalWorkflowNodes')
BEGIN
    CREATE TABLE [ApprovalWorkflowNodes] (
            [Id] uniqueidentifier NOT NULL,
            [WorkflowId] uniqueidentifier NOT NULL,
            [NodeType] nvarchar(50) NOT NULL,
            [NodeName] nvarchar(200) NOT NULL,
            [ConfigData] nvarchar(max) NULL,
            [PositionX] float NULL,
            [PositionY] float NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_ApprovalWorkflowNodes] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ApprovalWorkflowNodes_ApprovalWorkflows_WorkflowId] FOREIGN KEY ([WorkflowId]) REFERENCES [ApprovalWorkflows] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowStep')
BEGIN
    CREATE TABLE [WorkflowStep] (
            [Id] uniqueidentifier NOT NULL,
            [WorkflowId] uniqueidentifier NOT NULL,
            [StepOrder] int NOT NULL,
            [ApproverType] nvarchar(100) NOT NULL,
            [ApproverId] nvarchar(256) NULL,
            [RequireAllApprovers] bit NOT NULL,
            [TimeoutHours] int NOT NULL,
            [EscalationAction] nvarchar(50) NOT NULL,
            CONSTRAINT [PK_WorkflowStep] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_WorkflowStep_ApprovalWorkflows_WorkflowId] FOREIGN KEY ([WorkflowId]) REFERENCES [ApprovalWorkflows] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetRoleClaims')
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
            [Id] int NOT NULL IDENTITY,
            [RoleId] nvarchar(450) NOT NULL,
            [ClaimType] nvarchar(max) NULL,
            [ClaimValue] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AccessRequests')
BEGIN
    CREATE TABLE [AccessRequests] (
            [Id] uniqueidentifier NOT NULL,
            [RequesterId] nvarchar(450) NOT NULL,
            [ResourceType] nvarchar(100) NOT NULL,
            [ResourceId] nvarchar(256) NOT NULL,
            [ResourceName] nvarchar(200) NOT NULL,
            [Justification] nvarchar(1000) NOT NULL,
            [DurationDays] int NOT NULL,
            [RequestedAt] datetime2 NOT NULL,
            [Status] nvarchar(50) NOT NULL,
            [ApproverId] nvarchar(450) NULL,
            [ApprovedAt] datetime2 NULL,
            [ApprovalComments] nvarchar(500) NULL,
            [ExpiresAt] datetime2 NULL,
            CONSTRAINT [PK_AccessRequests] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_AccessRequests_AspNetUsers_ApproverId] FOREIGN KEY ([ApproverId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_AccessRequests_AspNetUsers_RequesterId] FOREIGN KEY ([RequesterId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUserClaims')
BEGIN
    CREATE TABLE [AspNetUserClaims] (
            [Id] int NOT NULL IDENTITY,
            [UserId] nvarchar(450) NOT NULL,
            [ClaimType] nvarchar(max) NULL,
            [ClaimValue] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUserLogins')
BEGIN
    CREATE TABLE [AspNetUserLogins] (
            [LoginProvider] nvarchar(450) NOT NULL,
            [ProviderKey] nvarchar(450) NOT NULL,
            [ProviderDisplayName] nvarchar(max) NULL,
            [UserId] nvarchar(450) NOT NULL,
            CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
            CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUserRoles')
BEGIN
    CREATE TABLE [AspNetUserRoles] (
            [UserId] nvarchar(450) NOT NULL,
            [RoleId] nvarchar(450) NOT NULL,
            CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
            CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUserTokens')
BEGIN
    CREATE TABLE [AspNetUserTokens] (
            [UserId] nvarchar(450) NOT NULL,
            [LoginProvider] nvarchar(450) NOT NULL,
            [Name] nvarchar(450) NOT NULL,
            [Value] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
            CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ComplianceFrameworkPolicyMappings')
BEGIN
    CREATE TABLE [ComplianceFrameworkPolicyMappings] (
            [Id] uniqueidentifier NOT NULL,
            [FrameworkId] uniqueidentifier NOT NULL,
            [CompliancePolicyId] uniqueidentifier NOT NULL,
            [RequirementId] nvarchar(100) NULL,
            [RequirementDescription] nvarchar(2000) NULL,
            [ComplianceStatus] nvarchar(50) NOT NULL,
            [CoveragePercentage] decimal(5,2) NOT NULL,
            [Evidence] nvarchar(max) NULL,
            [LastValidated] datetime2 NULL,
            [GapDescription] nvarchar(max) NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_ComplianceFrameworkPolicyMappings] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ComplianceFrameworkPolicyMappings_ComplianceFrameworks_FrameworkId] FOREIGN KEY ([FrameworkId]) REFERENCES [ComplianceFrameworks] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_ComplianceFrameworkPolicyMappings_CompliancePolicies_CompliancePolicyId] FOREIGN KEY ([CompliancePolicyId]) REFERENCES [CompliancePolicies] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CompliancePolicyAction')
BEGIN
    CREATE TABLE [CompliancePolicyAction] (
            [Id] uniqueidentifier NOT NULL,
            [CompliancePolicyId] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(1000) NULL,
            [ActionType] nvarchar(50) NOT NULL,
            [ExecutionTiming] nvarchar(50) NOT NULL,
            [DelayMinutes] int NULL,
            [RequiresApproval] bit NOT NULL,
            [MaxExecutions] int NULL,
            [Priority] int NOT NULL,
            [Configuration] nvarchar(max) NULL,
            [IsActive] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_CompliancePolicyAction] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_CompliancePolicyAction_CompliancePolicies_CompliancePolicyId] FOREIGN KEY ([CompliancePolicyId]) REFERENCES [CompliancePolicies] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CompliancePolicyExecutions')
BEGIN
    CREATE TABLE [CompliancePolicyExecutions] (
            [Id] uniqueidentifier NOT NULL,
            [CompliancePolicyId] uniqueidentifier NOT NULL,
            [Status] nvarchar(50) NOT NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [DurationMs] bigint NULL,
            [UsersEvaluated] int NOT NULL,
            [ViolationsFound] int NOT NULL,
            [ActionsExecuted] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [StackTrace] nvarchar(max) NULL,
            [TriggerType] nvarchar(50) NOT NULL,
            [TriggeredBy] nvarchar(256) NULL,
            CONSTRAINT [PK_CompliancePolicyExecutions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_CompliancePolicyExecutions_CompliancePolicies_CompliancePolicyId] FOREIGN KEY ([CompliancePolicyId]) REFERENCES [CompliancePolicies] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CompliancePolicyRule')
BEGIN
    CREATE TABLE [CompliancePolicyRule] (
            [Id] uniqueidentifier NOT NULL,
            [CompliancePolicyId] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(1000) NULL,
            [RuleType] nvarchar(50) NOT NULL,
            [FieldName] nvarchar(100) NOT NULL,
            [Operator] nvarchar(50) NOT NULL,
            [ComparisonValue] nvarchar(500) NULL,
            [DaysOffset] int NULL,
            [Weight] decimal(5,2) NOT NULL,
            [SortOrder] int NOT NULL,
            [IsActive] bit NOT NULL,
            [LogicalOperator] nvarchar(10) NOT NULL,
            [RuleGroupId] int NULL,
            [RuleGroupName] nvarchar(100) NULL,
            [GroupOperator] nvarchar(10) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_CompliancePolicyRule] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_CompliancePolicyRule_CompliancePolicies_CompliancePolicyId] FOREIGN KEY ([CompliancePolicyId]) REFERENCES [CompliancePolicies] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Groups')
BEGIN
    CREATE TABLE [Groups] (
            [Id] uniqueidentifier NOT NULL,
            [SourceConnectionId] uniqueidentifier NOT NULL,
            [SourceUniqueId] nvarchar(500) NOT NULL,
            [SourceType] nvarchar(100) NOT NULL,
            [Name] nvarchar(500) NOT NULL,
            [Description] nvarchar(max) NULL,
            [DistinguishedName] nvarchar(2000) NULL,
            [GroupType] nvarchar(100) NULL,
            [Email] nvarchar(500) NULL,
            [IsMailEnabled] bit NOT NULL,
            [ManagedBy] nvarchar(500) NULL,
            [OwnerId] uniqueidentifier NULL,
            [IsActive] bit NOT NULL,
            [FirstSyncedAt] datetime2 NOT NULL,
            [LastSyncedAt] datetime2 NOT NULL,
            [LastSeenAt] datetime2 NULL,
            [DeletedAt] datetime2 NULL,
            [LastReviewDate] datetime2 NULL,
            [NextReviewDate] datetime2 NULL,
            [ReviewFrequencyDays] int NOT NULL,
            [RequiresReview] bit NOT NULL,
            [ReviewOwnerId] uniqueidentifier NULL,
            [RiskScore] decimal(5,2) NOT NULL,
            [RiskLevel] nvarchar(20) NOT NULL,
            [LastRiskAssessment] datetime2 NULL,
            [RiskFactors] nvarchar(max) NULL,
            [IsSensitive] bit NOT NULL,
            [RequiresJustification] bit NOT NULL,
            [ComplianceTags] nvarchar(max) NULL,
            [BusinessOwnerId] uniqueidentifier NULL,
            [TechnicalOwnerId] uniqueidentifier NULL,
            CONSTRAINT [PK_Groups] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_Groups_DirectoryConnections_SourceConnectionId] FOREIGN KEY ([SourceConnectionId]) REFERENCES [DirectoryConnections] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncExecutions')
BEGIN
    CREATE TABLE [SyncExecutions] (
            [Id] uniqueidentifier NOT NULL,
            [DirectoryConnectionId] uniqueidentifier NOT NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [Status] nvarchar(50) NOT NULL,
            [IdentitiesAdded] int NULL,
            [IdentitiesUpdated] int NULL,
            [IdentitiesDeleted] int NULL,
            [GroupsAdded] int NULL,
            [GroupsUpdated] int NULL,
            [GroupsDeleted] int NULL,
            [MembershipsAdded] int NULL,
            [MembershipsRemoved] int NULL,
            [PersonsCreated] int NULL,
            [PersonsUpdated] int NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [ExecutionLog] nvarchar(max) NULL,
            CONSTRAINT [PK_SyncExecutions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncExecutions_DirectoryConnections_DirectoryConnectionId] FOREIGN KEY ([DirectoryConnectionId]) REFERENCES [DirectoryConnections] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncProjects')
BEGIN
    CREATE TABLE [SyncProjects] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(max) NULL,
            [SourceConnectionId] uniqueidentifier NULL,
            [TargetConnectionId] uniqueidentifier NULL,
            [IsTemplateMode] bit NOT NULL,
            [IdentityMatchingStrategy] nvarchar(50) NULL,
            [CronSchedule] nvarchar(100) NULL,
            [IsEnabled] bit NOT NULL,
            [IsRunning] bit NOT NULL,
            [ConflictResolutionStrategy] nvarchar(50) NOT NULL,
            [AutoCreateIdentities] bit NOT NULL,
            [EnableManagerAssignment] bit NOT NULL,
            [ProjectType] nvarchar(50) NOT NULL,
            [SourceSyncProjectId] uniqueidentifier NULL,
            [IsBuiltIn] bit NOT NULL,
            [IsReadOnly] bit NOT NULL,
            [MinMatchConfidenceThreshold] int NOT NULL,
            [PauseOnError] bit NOT NULL,
            [MaxErrorsBeforePause] int NOT NULL,
            [Priority] int NOT NULL,
            [LogLevel] nvarchar(20) NOT NULL,
            [LastSuccessfulRunAt] datetime2 NULL,
            [LastRunAt] datetime2 NULL,
            [NextScheduledRunAt] datetime2 NULL,
            [TotalExecutions] int NOT NULL,
            [SuccessfulExecutions] int NOT NULL,
            [FailedExecutions] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_SyncProjects] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncProjects_DirectoryConnections_SourceConnectionId] FOREIGN KEY ([SourceConnectionId]) REFERENCES [DirectoryConnections] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_SyncProjects_DirectoryConnections_TargetConnectionId] FOREIGN KEY ([TargetConnectionId]) REFERENCES [DirectoryConnections] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_SyncProjects_SyncProjects_SourceSyncProjectId] FOREIGN KEY ([SourceSyncProjectId]) REFERENCES [SyncProjects] ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CompliancePolicyViolations')
BEGIN
    CREATE TABLE [CompliancePolicyViolations] (
            [Id] uniqueidentifier NOT NULL,
            [CompliancePolicyId] uniqueidentifier NOT NULL,
            [EntityId] uniqueidentifier NOT NULL,
            [EntityType] nvarchar(50) NULL,
            [EntityDisplayName] nvarchar(500) NULL,
            [Severity] nvarchar(50) NOT NULL,
            [Status] nvarchar(50) NOT NULL,
            [ViolationScore] decimal(5,2) NOT NULL,
            [Message] nvarchar(max) NULL,
            [Description] nvarchar(max) NULL,
            [DisplayName] nvarchar(500) NULL,
            [DetectedAt] datetime2 NOT NULL,
            [AcknowledgedAt] datetime2 NULL,
            [AcknowledgedBy] uniqueidentifier NULL,
            [RemediatedAt] datetime2 NULL,
            [RemediatedBy] uniqueidentifier NULL,
            [RemediationNotes] nvarchar(max) NULL,
            [ClosedAt] datetime2 NULL,
            [ActionsExecuted] bit NOT NULL,
            [ActionCount] int NOT NULL,
            [FirstNotificationSentAt] datetime2 NULL,
            [LastNotificationSentAt] datetime2 NULL,
            [NotificationCount] int NOT NULL,
            [NextReminderAt] datetime2 NULL,
            CONSTRAINT [PK_CompliancePolicyViolations] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_CompliancePolicyViolations_CompliancePolicies_CompliancePolicyId] FOREIGN KEY ([CompliancePolicyId]) REFERENCES [CompliancePolicies] ([Id]) ON DELETE NO ACTION
            -- Note: EntityId is polymorphic (Identity OR Object) so no FK constraint
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Objects')
BEGIN
    CREATE TABLE [Objects] (
            [Id] uniqueidentifier NOT NULL,
            [IdentityId] uniqueidentifier NULL,
            [SourceConnectionId] uniqueidentifier NOT NULL,
            [SourceUniqueId] nvarchar(500) NOT NULL,
            [SourceType] nvarchar(100) NOT NULL,
            [ObjectClass] nvarchar(100) NULL,
            [DisplayName] nvarchar(500) NULL,
            [Email] nvarchar(500) NULL,
            [Username] nvarchar(500) NULL,
            [FirstName] nvarchar(500) NULL,
            [LastName] nvarchar(500) NULL,
            [Department] nvarchar(500) NULL,
            [JobTitle] nvarchar(500) NULL,
            [Phone] nvarchar(50) NULL,
            [DN] nvarchar(2000) NULL,
            [CN] nvarchar(500) NULL,
            [ManagerSourceId] nvarchar(500) NULL,
            [ManagerObjectId] uniqueidentifier NULL,
            [ManagerId] uniqueidentifier NULL,
            [OwnerObjectId] uniqueidentifier NULL,
            [IsActive] bit NOT NULL,
            [IsAuthoritative] bit NOT NULL,
            [MatchConfidence] int NOT NULL,
            [MatchMethod] nvarchar(100) NULL,
            [FirstSyncedAt] datetime2 NOT NULL,
            [LastSyncedAt] datetime2 NOT NULL,
            [LastSeenAt] datetime2 NULL,
            [DeletedAt] datetime2 NULL,
            [PasswordLastSet] datetime2 NULL,
            [IsBuiltIn] bit NOT NULL,
            [IsAdminSDHolder] bit NOT NULL,
            [PasswordNeverExpires] bit NOT NULL,
            [UserAccountControl] int NULL,
            CONSTRAINT [PK_Objects] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_Objects_DirectoryConnections_SourceConnectionId] FOREIGN KEY ([SourceConnectionId]) REFERENCES [DirectoryConnections] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_Objects_Identities_IdentityId] FOREIGN KEY ([IdentityId]) REFERENCES [Identities] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_Objects_Objects_ManagerObjectId] FOREIGN KEY ([ManagerObjectId]) REFERENCES [Objects] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'OrganizationalFolderMembers')
BEGIN
    CREATE TABLE [OrganizationalFolderMembers] (
            [Id] uniqueidentifier NOT NULL,
            [FolderId] uniqueidentifier NOT NULL,
            [IdentityId] uniqueidentifier NOT NULL,
            [AddedAt] datetime2 NOT NULL,
            [AddedBy] nvarchar(200) NULL,
            [Notes] nvarchar(500) NULL,
            [ExpiresAt] datetime2 NULL,
            [IsActive] bit NOT NULL,
            CONSTRAINT [PK_OrganizationalFolderMembers] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_OrganizationalFolderMembers_Identities_IdentityId] FOREIGN KEY ([IdentityId]) REFERENCES [Identities] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_OrganizationalFolderMembers_OrganizationalFolders_FolderId] FOREIGN KEY ([FolderId]) REFERENCES [OrganizationalFolders] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'OrganizationalFolderPolicies')
BEGIN
    CREATE TABLE [OrganizationalFolderPolicies] (
            [Id] uniqueidentifier NOT NULL,
            [FolderId] uniqueidentifier NOT NULL,
            [PolicyId] uniqueidentifier NOT NULL,
            [InheritToChildren] bit NOT NULL,
            [AppliedAt] datetime2 NOT NULL,
            [AppliedBy] nvarchar(200) NULL,
            [IsActive] bit NOT NULL,
            CONSTRAINT [PK_OrganizationalFolderPolicies] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_OrganizationalFolderPolicies_CompliancePolicies_PolicyId] FOREIGN KEY ([PolicyId]) REFERENCES [CompliancePolicies] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_OrganizationalFolderPolicies_OrganizationalFolders_FolderId] FOREIGN KEY ([FolderId]) REFERENCES [OrganizationalFolders] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ApiKeys')
BEGIN
    CREATE TABLE [ApiKeys] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [KeyHash] nvarchar(500) NOT NULL,
            [KeyPrefix] nvarchar(10) NOT NULL,
            [KeyType] nvarchar(20) NOT NULL,
            [AgentId] uniqueidentifier NULL,
            [UserId] nvarchar(max) NULL,
            [Scopes] nvarchar(1000) NOT NULL,
            [IsEnabled] bit NOT NULL,
            [ExpiresAt] datetime2 NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(200) NOT NULL,
            [LastUsedAt] datetime2 NULL,
            [LastUsedFromIp] nvarchar(50) NULL,
            [UsageCount] int NOT NULL,
            [RevokedAt] datetime2 NULL,
            [RevokedReason] nvarchar(500) NULL,
            CONSTRAINT [PK_ApiKeys] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ApiKeys_RemoteAgents_AgentId] FOREIGN KEY ([AgentId]) REFERENCES [RemoteAgents] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'JobQueue')
BEGIN
    CREATE TABLE [JobQueue] (
            [Id] uniqueidentifier NOT NULL,
            [JobType] nvarchar(50) NOT NULL,
            [JobName] nvarchar(200) NOT NULL,
            [RelatedEntityId] uniqueidentifier NULL,
            [RelatedEntityType] nvarchar(50) NULL,
            [Status] nvarchar(20) NOT NULL,
            [Priority] int NOT NULL,
            [Ready2Execute] bit NOT NULL,
            [ScheduledAt] datetime2 NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(200) NOT NULL,
            [ClaimedByAgentId] uniqueidentifier NULL,
            [ClaimedAt] datetime2 NULL,
            [StartedAt] datetime2 NULL,
            [CompletedAt] datetime2 NULL,
            [DurationMs] int NULL,
            [ItemsProcessed] int NOT NULL,
            [ItemsSucceeded] int NOT NULL,
            [ItemsFailed] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [ExceptionDetailsJson] nvarchar(max) NULL,
            [RetryAttempt] int NOT NULL,
            [MaxRetries] int NOT NULL,
            [PayloadJson] nvarchar(max) NULL,
            [ResultJson] nvarchar(max) NULL,
            [ProgressPercent] int NOT NULL,
            [ProgressMessage] nvarchar(500) NULL,
            [LastProgressUpdate] datetime2 NULL,
            [RowVersion] rowversion NULL,
            [Tags] nvarchar(500) NULL,
            CONSTRAINT [PK_JobQueue] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_JobQueue_RemoteAgents_ClaimedByAgentId] FOREIGN KEY ([ClaimedByAgentId]) REFERENCES [RemoteAgents] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MaintenanceSettings')
BEGIN
    CREATE TABLE [MaintenanceSettings] (
            [Id] int NOT NULL DEFAULT 1,
            [SyncLogRetentionDays] int NOT NULL DEFAULT 30,
            [ChangeLogRetentionDays] int NOT NULL DEFAULT 365,
            [SystemLogRetentionDays] int NOT NULL DEFAULT 90,
            [JobHistoryRetentionDays] int NOT NULL DEFAULT 30,
            [NotificationLogRetentionDays] int NOT NULL DEFAULT 60,
            [EnableIndexMaintenance] bit NOT NULL DEFAULT CAST(1 AS bit),
            [IndexReorganizeThreshold] int NOT NULL DEFAULT 10,
            [IndexRebuildThreshold] int NOT NULL DEFAULT 30,
            [EnableStatisticsUpdate] bit NOT NULL DEFAULT CAST(1 AS bit),
            [StatisticsUpdateThreshold] int NOT NULL DEFAULT 20,
            [EnableSessionCleanup] bit NOT NULL DEFAULT CAST(1 AS bit),
            [ExpiredSessionRetentionDays] int NOT NULL DEFAULT 7,
            [EnableOrphanedDataCleanup] bit NOT NULL DEFAULT CAST(1 AS bit),
            [OrphanedDataRetentionDays] int NOT NULL DEFAULT 14,
            [EnableTempFileCleanup] bit NOT NULL DEFAULT CAST(1 AS bit),
            [TempFileRetentionDays] int NOT NULL DEFAULT 7,
            [LogCleanupSchedule] nvarchar(100) NOT NULL DEFAULT N'0 0 2 * * ?',
            [IndexMaintenanceSchedule] nvarchar(100) NOT NULL DEFAULT N'0 0 3 ? * SUN',
            [StatisticsUpdateSchedule] nvarchar(100) NOT NULL DEFAULT N'0 30 3 * * ?',
            [SessionCleanupSchedule] nvarchar(100) NOT NULL DEFAULT N'0 0 */6 * * ?',
            [OrphanedDataCleanupSchedule] nvarchar(100) NOT NULL DEFAULT N'0 0 4 * * ?',
            [LogCleanupEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
            [IndexMaintenanceEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
            [StatisticsUpdateEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
            [SessionCleanupEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
            [OrphanedDataCleanupEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
            [LastLogCleanupRun] datetime2 NULL,
            [LastIndexMaintenanceRun] datetime2 NULL,
            [LastStatisticsUpdateRun] datetime2 NULL,
            [LastSessionCleanupRun] datetime2 NULL,
            [LastOrphanedDataCleanupRun] datetime2 NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(256) NOT NULL DEFAULT N'',
            CONSTRAINT [PK_MaintenanceSettings] PRIMARY KEY ([Id]),
            CONSTRAINT [CK_MaintenanceSettings_SingleRow] CHECK ([Id] = 1)
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ReportColumns')
BEGIN
    CREATE TABLE [ReportColumns] (
            [Id] uniqueidentifier NOT NULL,
            [ReportId] uniqueidentifier NOT NULL,
            [ColumnName] nvarchar(100) NOT NULL,
            [DisplayName] nvarchar(200) NOT NULL,
            [DataType] nvarchar(50) NOT NULL,
            [FormatString] nvarchar(100) NULL,
            [SortOrder] int NOT NULL,
            [IsVisible] bit NOT NULL,
            [AllowFilter] bit NOT NULL,
            [AllowSort] bit NOT NULL,
            [IsRequired] bit NOT NULL,
            [DefaultSortDirection] nvarchar(20) NULL,
            [Width] nvarchar(20) NULL,
            [AggregateFunction] nvarchar(20) NULL,
            CONSTRAINT [PK_ReportColumns] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ReportColumns_Reports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ReportParameters')
BEGIN
    CREATE TABLE [ReportParameters] (
            [Id] uniqueidentifier NOT NULL,
            [ReportId] uniqueidentifier NOT NULL,
            [ParameterName] nvarchar(100) NOT NULL,
            [DisplayName] nvarchar(200) NOT NULL,
            [DataType] nvarchar(50) NOT NULL,
            [ControlType] nvarchar(50) NOT NULL,
            [IsRequired] bit NOT NULL,
            [DefaultValue] nvarchar(max) NULL,
            [OptionsSource] nvarchar(max) NULL,
            [ValidationRules] nvarchar(max) NULL,
            [SortOrder] int NOT NULL,
            CONSTRAINT [PK_ReportParameters] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ReportParameters_Reports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ReportSchedules')
BEGIN
    CREATE TABLE [ReportSchedules] (
            [Id] uniqueidentifier NOT NULL,
            [ReportId] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Frequency] nvarchar(50) NOT NULL,
            [CronExpression] nvarchar(100) NULL,
            [ExecutionTime] nvarchar(10) NOT NULL,
            [DayOfWeek] int NULL,
            [DayOfMonth] int NULL,
            [IsActive] bit NOT NULL,
            [OutputFormat] nvarchar(20) NOT NULL,
            [EmailRecipients] nvarchar(max) NOT NULL,
            [EmailSubject] nvarchar(200) NULL,
            [EmailBody] nvarchar(max) NULL,
            [AttachReport] bit NOT NULL,
            [EmbedInEmail] bit NOT NULL,
            [ParameterValuesJson] nvarchar(max) NULL,
            [LastExecutedAt] datetime2 NULL,
            [NextExecutionAt] datetime2 NULL,
            [LastExecutionStatus] nvarchar(max) NULL,
            [LastExecutionError] nvarchar(max) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NOT NULL,
            CONSTRAINT [PK_ReportSchedules] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ReportSchedules_Reports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserReportFavorites')
BEGIN
    CREATE TABLE [UserReportFavorites] (
            [Id] uniqueidentifier NOT NULL,
            [UserId] uniqueidentifier NOT NULL,
            [ReportId] uniqueidentifier NOT NULL,
            [AddedAt] datetime2 NOT NULL,
            [SortOrder] int NOT NULL,
            [SavedParametersJson] nvarchar(max) NULL,
            CONSTRAINT [PK_UserReportFavorites] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_UserReportFavorites_Reports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncWorkflowTemplates')
BEGIN
    CREATE TABLE [SyncWorkflowTemplates] (
            [Id] uniqueidentifier NOT NULL,
            [ProjectTemplateId] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [ObjectClass] nvarchar(100) NOT NULL,
            [TemplateJson] nvarchar(max) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_SyncWorkflowTemplates] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncWorkflowTemplates_SyncProjectTemplates_ProjectTemplateId] FOREIGN KEY ([ProjectTemplateId]) REFERENCES [SyncProjectTemplates] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'IdentityTags')
BEGIN
    CREATE TABLE [IdentityTags] (
            [Id] uniqueidentifier NOT NULL,
            [IdentityId] uniqueidentifier NOT NULL,
            [TagId] uniqueidentifier NOT NULL,
            [IsInherited] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_IdentityTags] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_IdentityTags_Identities_IdentityId] FOREIGN KEY ([IdentityId]) REFERENCES [Identities] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_IdentityTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerActions')
BEGIN
    CREATE TABLE [TriggerActions] (
            [Id] uniqueidentifier NOT NULL,
            [TriggerId] uniqueidentifier NOT NULL,
            [ActionType] nvarchar(100) NOT NULL,
            [ActionName] nvarchar(200) NULL,
            [ActionConfig] nvarchar(max) NULL,
            [ExecutionOrder] int NOT NULL,
            [IsAsync] bit NOT NULL,
            [ContinueOnError] bit NOT NULL,
            [DelayMinutes] int NOT NULL,
            [TimeoutMinutes] int NOT NULL,
            [MaxRetries] int NOT NULL,
            [RetryDelaySeconds] int NOT NULL,
            [IsActive] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_TriggerActions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_TriggerActions_WorkflowTriggers_TriggerId] FOREIGN KEY ([TriggerId]) REFERENCES [WorkflowTriggers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerConditions')
BEGIN
    CREATE TABLE [TriggerConditions] (
            [Id] uniqueidentifier NOT NULL,
            [TriggerId] uniqueidentifier NOT NULL,
            [ConditionType] nvarchar(100) NOT NULL,
            [FieldName] nvarchar(200) NULL,
            [Operator] nvarchar(50) NOT NULL,
            [Value] nvarchar(2000) NULL,
            [ValueType] nvarchar(50) NOT NULL,
            [LogicalGroup] nvarchar(50) NOT NULL,
            [GroupOrder] int NOT NULL,
            [IsActive] bit NOT NULL,
            [SortOrder] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_TriggerConditions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_TriggerConditions_WorkflowTriggers_TriggerId] FOREIGN KEY ([TriggerId]) REFERENCES [WorkflowTriggers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerExecutions')
BEGIN
    CREATE TABLE [TriggerExecutions] (
            [Id] uniqueidentifier NOT NULL,
            [TriggerId] uniqueidentifier NOT NULL,
            [EventId] uniqueidentifier NULL,
            [WorkflowInstanceId] uniqueidentifier NULL,
            [TargetEntityType] nvarchar(100) NULL,
            [TargetEntityId] uniqueidentifier NULL,
            [Status] nvarchar(50) NOT NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [DurationMs] bigint NULL,
            [ActionsExecuted] int NOT NULL,
            [ActionsFailed] int NOT NULL,
            [ResultSummary] nvarchar(max) NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [EventDataSnapshot] nvarchar(max) NULL,
            [TriggerConfigSnapshot] nvarchar(max) NULL,
            [TriggeredBy] nvarchar(256) NULL,
            CONSTRAINT [PK_TriggerExecutions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_TriggerExecutions_TriggerEvents_EventId] FOREIGN KEY ([EventId]) REFERENCES [TriggerEvents] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_TriggerExecutions_WorkflowTriggers_TriggerId] FOREIGN KEY ([TriggerId]) REFERENCES [WorkflowTriggers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ApprovalWorkflowConnections')
BEGIN
    CREATE TABLE [ApprovalWorkflowConnections] (
            [Id] uniqueidentifier NOT NULL,
            [WorkflowId] uniqueidentifier NOT NULL,
            [SourceNodeId] uniqueidentifier NOT NULL,
            [TargetNodeId] uniqueidentifier NOT NULL,
            [Label] nvarchar(100) NULL,
            [SourcePort] nvarchar(50) NULL,
            [TargetPort] nvarchar(50) NULL,
            [ConditionData] nvarchar(max) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ApprovalWorkflowId] uniqueidentifier NULL,
            CONSTRAINT [PK_ApprovalWorkflowConnections] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ApprovalWorkflowConnections_ApprovalWorkflowNodes_SourceNodeId] FOREIGN KEY ([SourceNodeId]) REFERENCES [ApprovalWorkflowNodes] ([Id]),
            CONSTRAINT [FK_ApprovalWorkflowConnections_ApprovalWorkflowNodes_TargetNodeId] FOREIGN KEY ([TargetNodeId]) REFERENCES [ApprovalWorkflowNodes] ([Id]),
            CONSTRAINT [FK_ApprovalWorkflowConnections_ApprovalWorkflows_ApprovalWorkflowId] FOREIGN KEY ([ApprovalWorkflowId]) REFERENCES [ApprovalWorkflows] ([Id]),
            CONSTRAINT [FK_ApprovalWorkflowConnections_ApprovalWorkflows_WorkflowId] FOREIGN KEY ([WorkflowId]) REFERENCES [ApprovalWorkflows] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserAccess')
BEGIN
    CREATE TABLE [UserAccess] (
            [Id] uniqueidentifier NOT NULL,
            [UserId] nvarchar(450) NOT NULL,
            [ResourceType] nvarchar(100) NOT NULL,
            [ResourceId] nvarchar(256) NOT NULL,
            [ResourceName] nvarchar(200) NOT NULL,
            [GrantedAt] datetime2 NOT NULL,
            [GrantedBy] nvarchar(max) NULL,
            [ExpiresAt] datetime2 NULL,
            [IsActive] bit NOT NULL,
            [AccessRequestId] uniqueidentifier NULL,
            CONSTRAINT [PK_UserAccess] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_UserAccess_AccessRequests_AccessRequestId] FOREIGN KEY ([AccessRequestId]) REFERENCES [AccessRequests] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_UserAccess_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'GroupAttributes')
BEGIN
    CREATE TABLE [GroupAttributes] (
            [Id] uniqueidentifier NOT NULL,
            [GroupId] uniqueidentifier NOT NULL,
            [AttributeName] nvarchar(200) NOT NULL,
            [AttributeValue] nvarchar(max) NULL,
            [DataType] nvarchar(50) NULL,
            [LastSyncedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_GroupAttributes] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_GroupAttributes_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [Groups] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'IdentityGroupMemberships')
BEGIN
    CREATE TABLE [IdentityGroupMemberships] (
            [Id] uniqueidentifier NOT NULL,
            [IdentityId] uniqueidentifier NOT NULL,
            [GroupId] uniqueidentifier NOT NULL,
            [IsPrimary] bit NOT NULL,
            [AddedAt] datetime2 NOT NULL,
            [LastSyncedAt] datetime2 NOT NULL,
            [RemovedAt] datetime2 NULL,
            [IsActive] bit NOT NULL,
            [AddedBy] nvarchar(256) NULL,
            [Justification] nvarchar(max) NULL,
            [ExpirationDate] datetime2 NULL,
            [RemovedBy] nvarchar(256) NULL,
            [RemovalReason] nvarchar(max) NULL,
            CONSTRAINT [PK_IdentityGroupMemberships] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_IdentityGroupMemberships_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [Groups] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_IdentityGroupMemberships_Identities_IdentityId] FOREIGN KEY ([IdentityId]) REFERENCES [Identities] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'InternalSyncSteps')
BEGIN
    CREATE TABLE [InternalSyncSteps] (
            [Id] uniqueidentifier NOT NULL,
            [SyncProjectId] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(max) NULL,
            [ExecutionOrder] int NOT NULL,
            [Direction] nvarchar(30) NOT NULL,
            [StepType] nvarchar(50) NOT NULL,
            [ObjectClassFilter] nvarchar(100) NULL,
            [IsEnabled] bit NOT NULL,
            [ContinueOnError] bit NOT NULL,
            [Configuration] nvarchar(max) NULL,
            [SourceConnectionId] uniqueidentifier NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_InternalSyncSteps] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_InternalSyncSteps_SyncProjects_SyncProjectId] FOREIGN KEY ([SyncProjectId]) REFERENCES [SyncProjects] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncProjectChains')
BEGIN
    CREATE TABLE [SyncProjectChains] (
            [Id] uniqueidentifier NOT NULL,
            [SourceProjectId] uniqueidentifier NOT NULL,
            [TargetProjectId] uniqueidentifier NOT NULL,
            [ExecutionOrder] int NOT NULL,
            [TriggerCondition] nvarchar(20) NOT NULL,
            [IsEnabled] bit NOT NULL,
            [DelaySeconds] int NOT NULL,
            [Description] nvarchar(500) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_SyncProjectChains] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncProjectChains_SyncProjects_SourceProjectId] FOREIGN KEY ([SourceProjectId]) REFERENCES [SyncProjects] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_SyncProjectChains_SyncProjects_TargetProjectId] FOREIGN KEY ([TargetProjectId]) REFERENCES [SyncProjects] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncProjectRuns')
BEGIN
    CREATE TABLE [SyncProjectRuns] (
            [Id] uniqueidentifier NOT NULL,
            [SyncProjectId] uniqueidentifier NOT NULL,
            [TriggerType] nvarchar(50) NOT NULL,
            [TriggeredBy] nvarchar(256) NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [Status] nvarchar(50) NOT NULL,
            [ProgressPercentage] int NOT NULL,
            [CurrentStep] nvarchar(200) NULL,
            [TotalSteps] int NOT NULL,
            [CompletedSteps] int NOT NULL,
            [FailedSteps] int NOT NULL,
            [SkippedSteps] int NOT NULL,
            [TotalObjectsProcessed] int NOT NULL,
            [TotalObjectsCreated] int NOT NULL,
            [TotalObjectsUpdated] int NOT NULL,
            [TotalObjectsDeleted] int NOT NULL,
            [TotalErrors] int NOT NULL,
            [TotalPersonsCreated] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [ExecutionLog] nvarchar(max) NULL,
            [DurationSeconds] int NULL,
            CONSTRAINT [PK_SyncProjectRuns] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncProjectRuns_SyncProjects_SyncProjectId] FOREIGN KEY ([SyncProjectId]) REFERENCES [SyncProjects] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncWorkflows')
BEGIN
    CREATE TABLE [SyncWorkflows] (
            [Id] uniqueidentifier NOT NULL,
            [SyncProjectId] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(max) NULL,
            [ObjectClass] nvarchar(100) NOT NULL,
            [WorkflowType] nvarchar(50) NOT NULL,
            [ExecutionOrder] int NOT NULL,
            [IsEnabled] bit NOT NULL,
            [ContinueOnError] bit NOT NULL,
            [MaxExecutionTimeMinutes] int NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_SyncWorkflows] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncWorkflows_SyncProjects_SyncProjectId] FOREIGN KEY ([SyncProjectId]) REFERENCES [SyncProjects] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BusinessRoles')
BEGIN
    CREATE TABLE [BusinessRoles] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [DisplayName] nvarchar(200) NULL,
            [Description] nvarchar(1000) NULL,
            [Category] nvarchar(50) NULL,
            [ADGroupDN] nvarchar(500) NULL,
            [ADGroupObjectId] uniqueidentifier NULL,
            [LinkedGroupId] uniqueidentifier NULL,
            [Icon] nvarchar(50) NULL,
            [Color] nvarchar(20) NULL,
            [SortOrder] int NOT NULL,
            [IsSystem] bit NOT NULL,
            [IsActive] bit NOT NULL,
            [CanApprove] bit NOT NULL,
            [CanEscalate] bit NOT NULL,
            [FallbackEmail] nvarchar(200) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(max) NULL,
            [ModifiedAt] datetime2 NULL,
            [ModifiedBy] nvarchar(max) NULL,
            CONSTRAINT [PK_BusinessRoles] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_BusinessRoles_Objects_LinkedGroupId] FOREIGN KEY ([LinkedGroupId]) REFERENCES [Objects] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'IdentityMatchLogs')
BEGIN
    CREATE TABLE [IdentityMatchLogs] (
            [Id] uniqueidentifier NOT NULL,
            [IdentityId] uniqueidentifier NOT NULL,
            [ObjectId] uniqueidentifier NOT NULL,
            [MatchedAt] datetime2 NOT NULL,
            [MatchMethod] nvarchar(100) NOT NULL,
            [MatchConfidence] int NOT NULL,
            [MatchCriteria] nvarchar(max) NULL,
            [IsManualMatch] bit NOT NULL,
            [MatchedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_IdentityMatchLogs] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_IdentityMatchLogs_Identities_IdentityId] FOREIGN KEY ([IdentityId]) REFERENCES [Identities] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_IdentityMatchLogs_Objects_ObjectId] FOREIGN KEY ([ObjectId]) REFERENCES [Objects] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ObjectAttributes')
BEGIN
    CREATE TABLE [ObjectAttributes] (
            [Id] uniqueidentifier NOT NULL,
            [ObjectId] uniqueidentifier NOT NULL,
            [AttributeName] nvarchar(200) NOT NULL,
            [AttributeValue] nvarchar(max) NULL,
            [DataType] nvarchar(50) NULL,
            [LastSyncedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_ObjectAttributes] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ObjectAttributes_Objects_ObjectId] FOREIGN KEY ([ObjectId]) REFERENCES [Objects] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ObjectGroupMemberships')
BEGIN
    CREATE TABLE [ObjectGroupMemberships] (
            [Id] uniqueidentifier NOT NULL,
            [ObjectId] uniqueidentifier NOT NULL,
            [GroupId] uniqueidentifier NOT NULL,
            [IsDirect] bit NOT NULL,
            [IsPrimary] bit NOT NULL,
            [MembershipPath] nvarchar(2000) NULL,
            [AddedAt] datetime2 NOT NULL,
            [LastSyncedAt] datetime2 NOT NULL,
            [RemovedAt] datetime2 NULL,
            [IsActive] bit NOT NULL,
            [AddedBy] nvarchar(256) NULL,
            [Justification] nvarchar(max) NULL,
            [ExpirationDate] datetime2 NULL,
            [RemovedBy] nvarchar(256) NULL,
            [RemovalReason] nvarchar(max) NULL,
            CONSTRAINT [PK_ObjectGroupMemberships] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ObjectGroupMemberships_Objects_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [Objects] ([Id]),
            CONSTRAINT [FK_ObjectGroupMemberships_Objects_ObjectId] FOREIGN KEY ([ObjectId]) REFERENCES [Objects] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ObjectTags')
BEGIN
    CREATE TABLE [ObjectTags] (
            [Id] uniqueidentifier NOT NULL,
            [ObjectId] uniqueidentifier NOT NULL,
            [TagId] uniqueidentifier NOT NULL,
            [IsInherited] bit NOT NULL,
            [InheritedFromWorkflowId] uniqueidentifier NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_ObjectTags] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ObjectTags_Objects_ObjectId] FOREIGN KEY ([ObjectId]) REFERENCES [Objects] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_ObjectTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ReportExecutions')
BEGIN
    CREATE TABLE [ReportExecutions] (
            [Id] uniqueidentifier NOT NULL,
            [ReportId] uniqueidentifier NOT NULL,
            [ScheduleId] uniqueidentifier NULL,
            [ExecutedAt] datetime2 NOT NULL,
            [ExecutedBy] nvarchar(max) NOT NULL,
            [ExecutionContext] nvarchar(50) NOT NULL,
            [ExecutionTimeMs] int NOT NULL,
            [RowCount] int NOT NULL,
            [Status] nvarchar(20) NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [ParametersUsed] nvarchar(max) NULL,
            [OutputFormat] nvarchar(20) NULL,
            [OutputFilePath] nvarchar(max) NULL,
            CONSTRAINT [PK_ReportExecutions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ReportExecutions_ReportSchedules_ScheduleId] FOREIGN KEY ([ScheduleId]) REFERENCES [ReportSchedules] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_ReportExecutions_Reports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [Reports] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TriggerActionLogs')
BEGIN
    CREATE TABLE [TriggerActionLogs] (
            [Id] uniqueidentifier NOT NULL,
            [ExecutionId] uniqueidentifier NOT NULL,
            [ActionId] uniqueidentifier NOT NULL,
            [ActionType] nvarchar(100) NOT NULL,
            [ActionName] nvarchar(200) NULL,
            [Status] nvarchar(50) NOT NULL,
            [StartedAt] datetime2 NULL,
            [CompletedAt] datetime2 NULL,
            [DurationMs] bigint NULL,
            [InputData] nvarchar(max) NULL,
            [OutputData] nvarchar(max) NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [AttemptNumber] int NOT NULL,
            [WillRetry] bit NOT NULL,
            [NextRetryAt] datetime2 NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_TriggerActionLogs] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_TriggerActionLogs_TriggerActions_ActionId] FOREIGN KEY ([ActionId]) REFERENCES [TriggerActions] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_TriggerActionLogs_TriggerExecutions_ExecutionId] FOREIGN KEY ([ExecutionId]) REFERENCES [TriggerExecutions] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'InternalSyncStepMappings')
BEGIN
    CREATE TABLE [InternalSyncStepMappings] (
            [Id] uniqueidentifier NOT NULL,
            [InternalSyncStepId] uniqueidentifier NOT NULL,
            [SourceField] nvarchar(200) NOT NULL,
            [TargetField] nvarchar(200) NOT NULL,
            [OverwriteExisting] bit NOT NULL,
            [IsRequired] bit NOT NULL,
            [DefaultValue] nvarchar(500) NULL,
            [Transformation] nvarchar(max) NULL,
            [MappingOrder] int NOT NULL,
            [IsEnabled] bit NOT NULL,
            CONSTRAINT [PK_InternalSyncStepMappings] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_InternalSyncStepMappings_InternalSyncSteps_InternalSyncStepId] FOREIGN KEY ([InternalSyncStepId]) REFERENCES [InternalSyncSteps] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'InternalSyncStepRuns')
BEGIN
    CREATE TABLE [InternalSyncStepRuns] (
            [Id] uniqueidentifier NOT NULL,
            [InternalSyncRunId] uniqueidentifier NOT NULL,
            [InternalSyncStepId] uniqueidentifier NOT NULL,
            [StepName] nvarchar(200) NOT NULL,
            [StepType] nvarchar(50) NOT NULL,
            [ExecutionOrder] int NOT NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [Status] nvarchar(20) NOT NULL,
            [Processed] int NOT NULL,
            [Matched] int NOT NULL,
            [Created] int NOT NULL,
            [Updated] int NOT NULL,
            [Skipped] int NOT NULL,
            [Errors] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [DurationSeconds] float NULL,
            CONSTRAINT [PK_InternalSyncStepRuns] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_InternalSyncStepRuns_InternalSyncRuns_InternalSyncRunId] FOREIGN KEY ([InternalSyncRunId]) REFERENCES [InternalSyncRuns] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_InternalSyncStepRuns_InternalSyncSteps_InternalSyncStepId] FOREIGN KEY ([InternalSyncStepId]) REFERENCES [InternalSyncSteps] ([Id])
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PostSyncTasks')
BEGIN
    CREATE TABLE [PostSyncTasks] (
            [Id] uniqueidentifier NOT NULL,
            [SyncProjectRunId] uniqueidentifier NOT NULL,
            [TaskType] nvarchar(100) NOT NULL,
            [Status] nvarchar(50) NOT NULL,
            [Priority] int NOT NULL,
            [ObjectsProcessed] int NOT NULL,
            [ObjectsTotal] int NULL,
            [ObjectsSkipped] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [StartedAt] datetime2 NULL,
            [CompletedAt] datetime2 NULL,
            [DurationSeconds] int NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_PostSyncTasks] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_PostSyncTasks_SyncProjectRuns_SyncProjectRunId] FOREIGN KEY ([SyncProjectRunId]) REFERENCES [SyncProjectRuns] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncSteps')
BEGIN
    CREATE TABLE [SyncSteps] (
            [Id] uniqueidentifier NOT NULL,
            [SyncWorkflowId] uniqueidentifier NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Description] nvarchar(max) NULL,
            [ExecutionOrder] int NOT NULL,
            [ObjectClass] nvarchar(100) NOT NULL,
            [StepType] nvarchar(50) NULL,
            [MarkAsType] nvarchar(100) NULL,
            [LdapFilter] nvarchar(max) NULL,
            [SearchBase] nvarchar(2000) NULL,
            [SearchBases] nvarchar(4000) NULL,
            [ExcludedSearchBases] nvarchar(4000) NULL,
            [SearchScope] nvarchar(20) NOT NULL,
            [IsEnabled] bit NOT NULL,
            [ContinueOnError] bit NOT NULL,
            [MaxExecutionTimeMinutes] int NOT NULL,
            [DependsOnStepIds] nvarchar(1000) NULL,
            [ProcessDeletions] bit NOT NULL,
            [UpdateExisting] bit NOT NULL,
            [BatchSize] int NOT NULL,
            [LdapPageSize] int NOT NULL,
            [Configuration] nvarchar(max) NULL,
            [EnableIdentityMatching] bit NOT NULL,
            [IdentityMatchingAttribute] nvarchar(200) NULL,
            [InheritWorkflowTags] bit NOT NULL,
            [SkipPersonMatching] bit NOT NULL,
            [EnablePersonMatching] bit NOT NULL,
            [CreatePersonIfNotFound] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_SyncSteps] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncSteps_SyncWorkflows_SyncWorkflowId] FOREIGN KEY ([SyncWorkflowId]) REFERENCES [SyncWorkflows] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowTags')
BEGIN
    CREATE TABLE [WorkflowTags] (
            [Id] uniqueidentifier NOT NULL,
            [SyncWorkflowId] uniqueidentifier NOT NULL,
            [TagId] uniqueidentifier NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_WorkflowTags] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_WorkflowTags_SyncWorkflows_SyncWorkflowId] FOREIGN KEY ([SyncWorkflowId]) REFERENCES [SyncWorkflows] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_WorkflowTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BusinessRoleMembers')
BEGIN
    CREATE TABLE [BusinessRoleMembers] (
            [Id] uniqueidentifier NOT NULL,
            [BusinessRoleId] uniqueidentifier NOT NULL,
            [IdentityId] uniqueidentifier NOT NULL,
            [DisplayName] nvarchar(200) NULL,
            [Email] nvarchar(200) NULL,
            [LastVerifiedAt] datetime2 NOT NULL,
            [IsDirectAssignment] bit NOT NULL,
            CONSTRAINT [PK_BusinessRoleMembers] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_BusinessRoleMembers_BusinessRoles_BusinessRoleId] FOREIGN KEY ([BusinessRoleId]) REFERENCES [BusinessRoles] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_BusinessRoleMembers_Identities_IdentityId] FOREIGN KEY ([IdentityId]) REFERENCES [Identities] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MembershipTags')
BEGIN
    CREATE TABLE [MembershipTags] (
            [Id] uniqueidentifier NOT NULL,
            [MembershipId] uniqueidentifier NOT NULL,
            [TagId] uniqueidentifier NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(256) NULL,
            CONSTRAINT [PK_MembershipTags] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_MembershipTags_ObjectGroupMemberships_MembershipId] FOREIGN KEY ([MembershipId]) REFERENCES [ObjectGroupMemberships] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_MembershipTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AttributeMappings')
BEGIN
    CREATE TABLE [AttributeMappings] (
            [Id] uniqueidentifier NOT NULL,
            [SyncStepId] uniqueidentifier NOT NULL,
            [SourceAttribute] nvarchar(200) NOT NULL,
            [SourceDisplayName] nvarchar(200) NOT NULL,
            [DataType] nvarchar(50) NOT NULL,
            [TargetType] nvarchar(50) NOT NULL,
            [TargetAttribute] nvarchar(200) NOT NULL,
            [TransformationType] nvarchar(50) NOT NULL,
            [TransformationExpression] nvarchar(max) NULL,
            [DefaultValue] nvarchar(500) NULL,
            [IsRequired] bit NOT NULL,
            [UseForMatching] bit NOT NULL,
            [MatchWeight] int NOT NULL,
            [UseFuzzyMatch] bit NOT NULL,
            [FuzzyMatchThreshold] float NOT NULL,
            [FuzzyMatchAlgorithm] nvarchar(50) NOT NULL,
            [ExecutionOrder] int NOT NULL,
            [IsEnabled] bit NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ModifiedAt] datetime2 NULL,
            CONSTRAINT [PK_AttributeMappings] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_AttributeMappings_SyncSteps_SyncStepId] FOREIGN KEY ([SyncStepId]) REFERENCES [SyncSteps] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncStepRuns')
BEGIN
    CREATE TABLE [SyncStepRuns] (
            [Id] uniqueidentifier NOT NULL,
            [SyncProjectRunId] uniqueidentifier NOT NULL,
            [SyncStepId] uniqueidentifier NULL,
            [StepName] nvarchar(200) NOT NULL,
            [ObjectClass] nvarchar(100) NOT NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [Status] nvarchar(50) NOT NULL,
            [ObjectsQueried] int NOT NULL,
            [ObjectsProcessed] int NOT NULL,
            [ObjectsCreated] int NOT NULL,
            [ObjectsUpdated] int NOT NULL,
            [ObjectsDeleted] int NOT NULL,
            [ObjectsSkipped] int NOT NULL,
            [ErrorCount] int NOT NULL,
            [PersonsMatched] int NOT NULL,
            [PersonsCreated] int NOT NULL,
            [PersonMatchingSkipped] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [ExecutionLog] nvarchar(max) NULL,
            [DurationSeconds] int NULL,
            [AvgProcessingTimeMs] decimal(18,2) NULL,
            CONSTRAINT [PK_SyncStepRuns] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncStepRuns_SyncProjectRuns_SyncProjectRunId] FOREIGN KEY ([SyncProjectRunId]) REFERENCES [SyncProjectRuns] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_SyncStepRuns_SyncSteps_SyncStepId] FOREIGN KEY ([SyncStepId]) REFERENCES [SyncSteps] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncStepScripts')
BEGIN
    CREATE TABLE [SyncStepScripts] (
            [Id] uniqueidentifier NOT NULL,
            [SyncStepId] uniqueidentifier NOT NULL,
            [ScriptId] uniqueidentifier NOT NULL,
            [ExecutionPhase] nvarchar(50) NOT NULL,
            [ExecutionOrder] int NOT NULL,
            [IsEnabled] bit NOT NULL,
            [ParameterOverrides] nvarchar(max) NULL,
            CONSTRAINT [PK_SyncStepScripts] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncStepScripts_SyncProcessingScripts_ScriptId] FOREIGN KEY ([ScriptId]) REFERENCES [SyncProcessingScripts] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_SyncStepScripts_SyncSteps_SyncStepId] FOREIGN KEY ([SyncStepId]) REFERENCES [SyncSteps] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncStepTags')
BEGIN
    CREATE TABLE [SyncStepTags] (
            [Id] uniqueidentifier NOT NULL,
            [SyncStepId] uniqueidentifier NOT NULL,
            [TagId] uniqueidentifier NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_SyncStepTags] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncStepTags_SyncSteps_SyncStepId] FOREIGN KEY ([SyncStepId]) REFERENCES [SyncSteps] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_SyncStepTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE NO ACTION
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncAuditLogs')
BEGIN
    CREATE TABLE [SyncAuditLogs] (
            [Id] uniqueidentifier NOT NULL,
            [SyncStepRunId] uniqueidentifier NOT NULL,
            [ObjectId] uniqueidentifier NULL,
            [OperationType] nvarchar(50) NOT NULL,
            [ObjectDisplayName] nvarchar(500) NULL,
            [SourceUniqueId] nvarchar(500) NULL,
            [Email] nvarchar(500) NULL,
            [Username] nvarchar(500) NULL,
            [ChangeDetails] nvarchar(max) NULL,
            [ChangeCount] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [Timestamp] datetime2 NOT NULL,
            [ProcessingTimeMs] decimal(18,2) NULL,
            CONSTRAINT [PK_SyncAuditLogs] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncAuditLogs_Objects_ObjectId] FOREIGN KEY ([ObjectId]) REFERENCES [Objects] ([Id]) ON DELETE SET NULL,
            CONSTRAINT [FK_SyncAuditLogs_SyncStepRuns_SyncStepRunId] FOREIGN KEY ([SyncStepRunId]) REFERENCES [SyncStepRuns] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncScriptExecutions')
BEGIN
    CREATE TABLE [SyncScriptExecutions] (
            [Id] uniqueidentifier NOT NULL,
            [SyncStepRunId] uniqueidentifier NOT NULL,
            [ScriptId] uniqueidentifier NOT NULL,
            [ExecutionPhase] nvarchar(50) NOT NULL,
            [Status] nvarchar(50) NOT NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [DurationMs] int NULL,
            [ObjectsProcessed] int NOT NULL,
            [ObjectsModified] int NOT NULL,
            [IdentitiesCreated] int NOT NULL,
            [ManagersResolved] int NOT NULL,
            [ErrorMessage] nvarchar(max) NULL,
            [OutputLog] nvarchar(max) NULL,
            CONSTRAINT [PK_SyncScriptExecutions] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_SyncScriptExecutions_SyncProcessingScripts_ScriptId] FOREIGN KEY ([ScriptId]) REFERENCES [SyncProcessingScripts] ([Id]) ON DELETE NO ACTION,
            CONSTRAINT [FK_SyncScriptExecutions_SyncStepRuns_SyncStepRunId] FOREIGN KEY ([SyncStepRunId]) REFERENCES [SyncStepRuns] ([Id]) ON DELETE CASCADE
        );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccessRequests_ApproverId' AND object_id = OBJECT_ID('AccessRequests'))
BEGIN
    CREATE INDEX [IX_AccessRequests_ApproverId] ON [AccessRequests] ([ApproverId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccessRequests_RequesterId' AND object_id = OBJECT_ID('AccessRequests'))
BEGIN
    CREATE INDEX [IX_AccessRequests_RequesterId] ON [AccessRequests] ([RequesterId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApiKeys_AgentId' AND object_id = OBJECT_ID('ApiKeys'))
BEGIN
    CREATE INDEX [IX_ApiKeys_AgentId] ON [ApiKeys] ([AgentId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalWorkflowConnections_ApprovalWorkflowId' AND object_id = OBJECT_ID('ApprovalWorkflowConnections'))
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowConnections_ApprovalWorkflowId] ON [ApprovalWorkflowConnections] ([ApprovalWorkflowId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalWorkflowConnections_SourceNodeId' AND object_id = OBJECT_ID('ApprovalWorkflowConnections'))
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowConnections_SourceNodeId] ON [ApprovalWorkflowConnections] ([SourceNodeId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalWorkflowConnections_TargetNodeId' AND object_id = OBJECT_ID('ApprovalWorkflowConnections'))
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowConnections_TargetNodeId] ON [ApprovalWorkflowConnections] ([TargetNodeId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalWorkflowConnections_WorkflowId' AND object_id = OBJECT_ID('ApprovalWorkflowConnections'))
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowConnections_WorkflowId] ON [ApprovalWorkflowConnections] ([WorkflowId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalWorkflowNodes_WorkflowId' AND object_id = OBJECT_ID('ApprovalWorkflowNodes'))
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowNodes_WorkflowId] ON [ApprovalWorkflowNodes] ([WorkflowId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetRoleClaims_RoleId' AND object_id = OBJECT_ID('AspNetRoleClaims'))
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

-- From migration: 20260115012917_InitialCreate
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'RoleNameIndex' AND object_id = OBJECT_ID('AspNetRoles'))
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUserClaims_UserId' AND object_id = OBJECT_ID('AspNetUserClaims'))
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUserLogins_UserId' AND object_id = OBJECT_ID('AspNetUserLogins'))
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUserRoles_RoleId' AND object_id = OBJECT_ID('AspNetUserRoles'))
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'EmailIndex' AND object_id = OBJECT_ID('AspNetUsers'))
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

-- From migration: 20260115012917_InitialCreate
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UserNameIndex' AND object_id = OBJECT_ID('AspNetUsers'))
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttributeMappings_IsEnabled' AND object_id = OBJECT_ID('AttributeMappings'))
BEGIN
    CREATE INDEX [IX_AttributeMappings_IsEnabled] ON [AttributeMappings] ([IsEnabled]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttributeMappings_StepSource' AND object_id = OBJECT_ID('AttributeMappings'))
BEGIN
    CREATE INDEX [IX_AttributeMappings_StepSource] ON [AttributeMappings] ([SyncStepId], [SourceAttribute]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttributeMappings_SyncStepId' AND object_id = OBJECT_ID('AttributeMappings'))
BEGIN
    CREATE INDEX [IX_AttributeMappings_SyncStepId] ON [AttributeMappings] ([SyncStepId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttributeMappings_UseForMatching' AND object_id = OBJECT_ID('AttributeMappings'))
BEGIN
    CREATE INDEX [IX_AttributeMappings_UseForMatching] ON [AttributeMappings] ([UseForMatching]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_Entity' AND object_id = OBJECT_ID('AuditLogs'))
BEGIN
    CREATE INDEX [IX_AuditLogs_Entity] ON [AuditLogs] ([EntityType], [EntityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_Timestamp' AND object_id = OBJECT_ID('AuditLogs'))
BEGIN
    CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_UserId' AND object_id = OBJECT_ID('AuditLogs'))
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BusinessRoleMembers_BusinessRoleId' AND object_id = OBJECT_ID('BusinessRoleMembers'))
BEGIN
    CREATE INDEX [IX_BusinessRoleMembers_BusinessRoleId] ON [BusinessRoleMembers] ([BusinessRoleId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BusinessRoleMembers_IdentityId' AND object_id = OBJECT_ID('BusinessRoleMembers'))
BEGIN
    CREATE INDEX [IX_BusinessRoleMembers_IdentityId] ON [BusinessRoleMembers] ([IdentityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BusinessRoles_LinkedGroupId' AND object_id = OBJECT_ID('BusinessRoles'))
BEGIN
    CREATE INDEX [IX_BusinessRoles_LinkedGroupId] ON [BusinessRoles] ([LinkedGroupId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_CorrelationId' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX [IX_ChangeAuditLogs_CorrelationId] ON [ChangeAuditLogs] ([CorrelationId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_Entity' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX [IX_ChangeAuditLogs_Entity] ON [ChangeAuditLogs] ([EntityType], [EntityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_EntityId' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX [IX_ChangeAuditLogs_EntityId] ON [ChangeAuditLogs] ([EntityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_OperationType' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX [IX_ChangeAuditLogs_OperationType] ON [ChangeAuditLogs] ([OperationType]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_Source' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX [IX_ChangeAuditLogs_Source] ON [ChangeAuditLogs] ([Source]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_Timestamp' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX [IX_ChangeAuditLogs_Timestamp] ON [ChangeAuditLogs] ([Timestamp]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChangeAuditLogs_UserId' AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE INDEX [IX_ChangeAuditLogs_UserId] ON [ChangeAuditLogs] ([UserId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ComplianceFrameworkPolicyMappings_CompliancePolicyId' AND object_id = OBJECT_ID('ComplianceFrameworkPolicyMappings'))
BEGIN
    CREATE INDEX [IX_ComplianceFrameworkPolicyMappings_CompliancePolicyId] ON [ComplianceFrameworkPolicyMappings] ([CompliancePolicyId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ComplianceFrameworkPolicyMappings_FrameworkId' AND object_id = OBJECT_ID('ComplianceFrameworkPolicyMappings'))
BEGIN
    CREATE INDEX [IX_ComplianceFrameworkPolicyMappings_FrameworkId] ON [ComplianceFrameworkPolicyMappings] ([FrameworkId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CompliancePolicyAction_CompliancePolicyId' AND object_id = OBJECT_ID('CompliancePolicyAction'))
BEGIN
    CREATE INDEX [IX_CompliancePolicyAction_CompliancePolicyId] ON [CompliancePolicyAction] ([CompliancePolicyId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CompliancePolicyExecutions_CompliancePolicyId' AND object_id = OBJECT_ID('CompliancePolicyExecutions'))
BEGIN
    CREATE INDEX [IX_CompliancePolicyExecutions_CompliancePolicyId] ON [CompliancePolicyExecutions] ([CompliancePolicyId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CompliancePolicyRule_CompliancePolicyId' AND object_id = OBJECT_ID('CompliancePolicyRule'))
BEGIN
    CREATE INDEX [IX_CompliancePolicyRule_CompliancePolicyId] ON [CompliancePolicyRule] ([CompliancePolicyId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CompliancePolicyViolations_CompliancePolicyId' AND object_id = OBJECT_ID('CompliancePolicyViolations'))
BEGIN
    CREATE INDEX [IX_CompliancePolicyViolations_CompliancePolicyId] ON [CompliancePolicyViolations] ([CompliancePolicyId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CompliancePolicyViolations_EntityId' AND object_id = OBJECT_ID('CompliancePolicyViolations'))
BEGIN
    CREATE INDEX [IX_CompliancePolicyViolations_EntityId] ON [CompliancePolicyViolations] ([EntityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GroupAttributes_GroupAttribute' AND object_id = OBJECT_ID('GroupAttributes'))
BEGIN
    CREATE INDEX [IX_GroupAttributes_GroupAttribute] ON [GroupAttributes] ([GroupId], [AttributeName]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_Email' AND object_id = OBJECT_ID('Groups'))
BEGIN
    CREATE INDEX [IX_Groups_Email] ON [Groups] ([Email]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_IsActive' AND object_id = OBJECT_ID('Groups'))
BEGIN
    CREATE INDEX [IX_Groups_IsActive] ON [Groups] ([IsActive]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_Name' AND object_id = OBJECT_ID('Groups'))
BEGIN
    CREATE INDEX [IX_Groups_Name] ON [Groups] ([Name]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_SourceUnique' AND object_id = OBJECT_ID('Groups'))
BEGIN
    CREATE UNIQUE INDEX [IX_Groups_SourceUnique] ON [Groups] ([SourceConnectionId], [SourceUniqueId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Identities_IsActive' AND object_id = OBJECT_ID('Identities'))
BEGIN
    CREATE INDEX [IX_Identities_IsActive] ON [Identities] ([IsActive]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Identities_ManagerIdentityId' AND object_id = OBJECT_ID('Identities'))
BEGIN
    CREATE INDEX [IX_Identities_ManagerIdentityId] ON [Identities] ([ManagerIdentityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Identities_NameDepartment' AND object_id = OBJECT_ID('Identities'))
BEGIN
    CREATE INDEX [IX_Identities_NameDepartment] ON [Identities] ([FirstName], [LastName], [Department]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Identities_PrimaryEmail' AND object_id = OBJECT_ID('Identities'))
BEGIN
    CREATE INDEX [IX_Identities_PrimaryEmail] ON [Identities] ([PrimaryEmail]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityGroupMemberships_GroupId' AND object_id = OBJECT_ID('IdentityGroupMemberships'))
BEGIN
    CREATE INDEX [IX_IdentityGroupMemberships_GroupId] ON [IdentityGroupMemberships] ([GroupId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityGroupMemberships_IdentityGroup' AND object_id = OBJECT_ID('IdentityGroupMemberships'))
BEGIN
    CREATE UNIQUE INDEX [IX_IdentityGroupMemberships_IdentityGroup] ON [IdentityGroupMemberships] ([IdentityId], [GroupId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityMatchLogs_IdentityId' AND object_id = OBJECT_ID('IdentityMatchLogs'))
BEGIN
    CREATE INDEX [IX_IdentityMatchLogs_IdentityId] ON [IdentityMatchLogs] ([IdentityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityMatchLogs_MatchedAt' AND object_id = OBJECT_ID('IdentityMatchLogs'))
BEGIN
    CREATE INDEX [IX_IdentityMatchLogs_MatchedAt] ON [IdentityMatchLogs] ([MatchedAt]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityMatchLogs_ObjectId' AND object_id = OBJECT_ID('IdentityMatchLogs'))
BEGIN
    CREATE INDEX [IX_IdentityMatchLogs_ObjectId] ON [IdentityMatchLogs] ([ObjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityTags_IdentityId' AND object_id = OBJECT_ID('IdentityTags'))
BEGIN
    CREATE INDEX [IX_IdentityTags_IdentityId] ON [IdentityTags] ([IdentityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityTags_IdentityTag' AND object_id = OBJECT_ID('IdentityTags'))
BEGIN
    CREATE UNIQUE INDEX [IX_IdentityTags_IdentityTag] ON [IdentityTags] ([IdentityId], [TagId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IdentityTags_TagId' AND object_id = OBJECT_ID('IdentityTags'))
BEGIN
    CREATE INDEX [IX_IdentityTags_TagId] ON [IdentityTags] ([TagId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InternalSyncStepMappings_Step' AND object_id = OBJECT_ID('InternalSyncStepMappings'))
BEGIN
    CREATE INDEX [IX_InternalSyncStepMappings_Step] ON [InternalSyncStepMappings] ([InternalSyncStepId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InternalSyncStepRuns_InternalSyncStepId' AND object_id = OBJECT_ID('InternalSyncStepRuns'))
BEGIN
    CREATE INDEX [IX_InternalSyncStepRuns_InternalSyncStepId] ON [InternalSyncStepRuns] ([InternalSyncStepId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InternalSyncStepRuns_Run' AND object_id = OBJECT_ID('InternalSyncStepRuns'))
BEGIN
    CREATE INDEX [IX_InternalSyncStepRuns_Run] ON [InternalSyncStepRuns] ([InternalSyncRunId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InternalSyncSteps_Project_Order' AND object_id = OBJECT_ID('InternalSyncSteps'))
BEGIN
    CREATE INDEX [IX_InternalSyncSteps_Project_Order] ON [InternalSyncSteps] ([SyncProjectId], [ExecutionOrder]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobQueue_ClaimedByAgentId' AND object_id = OBJECT_ID('JobQueue'))
BEGIN
    CREATE INDEX [IX_JobQueue_ClaimedByAgentId] ON [JobQueue] ([ClaimedByAgentId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MembershipTags_MembershipId' AND object_id = OBJECT_ID('MembershipTags'))
BEGIN
    CREATE INDEX [IX_MembershipTags_MembershipId] ON [MembershipTags] ([MembershipId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MembershipTags_MembershipTag' AND object_id = OBJECT_ID('MembershipTags'))
BEGIN
    CREATE UNIQUE INDEX [IX_MembershipTags_MembershipTag] ON [MembershipTags] ([MembershipId], [TagId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MembershipTags_TagId' AND object_id = OBJECT_ID('MembershipTags'))
BEGIN
    CREATE INDEX [IX_MembershipTags_TagId] ON [MembershipTags] ([TagId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectAttributes_ObjectAttribute' AND object_id = OBJECT_ID('ObjectAttributes'))
BEGIN
    CREATE INDEX [IX_ObjectAttributes_ObjectAttribute] ON [ObjectAttributes] ([ObjectId], [AttributeName]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectGroupMemberships_GroupId' AND object_id = OBJECT_ID('ObjectGroupMemberships'))
BEGIN
    CREATE INDEX [IX_ObjectGroupMemberships_GroupId] ON [ObjectGroupMemberships] ([GroupId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectGroupMemberships_ObjectGroup' AND object_id = OBJECT_ID('ObjectGroupMemberships'))
BEGIN
    CREATE UNIQUE INDEX [IX_ObjectGroupMemberships_ObjectGroup] ON [ObjectGroupMemberships] ([ObjectId], [GroupId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_Email' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE INDEX [IX_Objects_Email] ON [Objects] ([Email]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_IdentityId' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE INDEX [IX_Objects_IdentityId] ON [Objects] ([IdentityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_IsActive' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE INDEX [IX_Objects_IsActive] ON [Objects] ([IsActive]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_ManagerObjectId' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE INDEX [IX_Objects_ManagerObjectId] ON [Objects] ([ManagerObjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_SourceUnique' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE UNIQUE INDEX [IX_Objects_SourceUnique] ON [Objects] ([SourceConnectionId], [SourceUniqueId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_Username' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE INDEX [IX_Objects_Username] ON [Objects] ([Username]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectTags_ObjectId' AND object_id = OBJECT_ID('ObjectTags'))
BEGIN
    CREATE INDEX [IX_ObjectTags_ObjectId] ON [ObjectTags] ([ObjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectTags_ObjectTag' AND object_id = OBJECT_ID('ObjectTags'))
BEGIN
    CREATE UNIQUE INDEX [IX_ObjectTags_ObjectTag] ON [ObjectTags] ([ObjectId], [TagId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectTags_TagId' AND object_id = OBJECT_ID('ObjectTags'))
BEGIN
    CREATE INDEX [IX_ObjectTags_TagId] ON [ObjectTags] ([TagId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrganizationalFolderMembers_FolderId' AND object_id = OBJECT_ID('OrganizationalFolderMembers'))
BEGIN
    CREATE INDEX [IX_OrganizationalFolderMembers_FolderId] ON [OrganizationalFolderMembers] ([FolderId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrganizationalFolderMembers_IdentityId' AND object_id = OBJECT_ID('OrganizationalFolderMembers'))
BEGIN
    CREATE INDEX [IX_OrganizationalFolderMembers_IdentityId] ON [OrganizationalFolderMembers] ([IdentityId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrganizationalFolderPolicies_FolderId' AND object_id = OBJECT_ID('OrganizationalFolderPolicies'))
BEGIN
    CREATE INDEX [IX_OrganizationalFolderPolicies_FolderId] ON [OrganizationalFolderPolicies] ([FolderId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrganizationalFolderPolicies_PolicyId' AND object_id = OBJECT_ID('OrganizationalFolderPolicies'))
BEGIN
    CREATE INDEX [IX_OrganizationalFolderPolicies_PolicyId] ON [OrganizationalFolderPolicies] ([PolicyId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrganizationalFolders_ParentId' AND object_id = OBJECT_ID('OrganizationalFolders'))
BEGIN
    CREATE INDEX [IX_OrganizationalFolders_ParentId] ON [OrganizationalFolders] ([ParentId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PolicyActions_PolicyId' AND object_id = OBJECT_ID('PolicyActions'))
BEGIN
    CREATE INDEX [IX_PolicyActions_PolicyId] ON [PolicyActions] ([PolicyId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PolicyConditions_PolicyId' AND object_id = OBJECT_ID('PolicyConditions'))
BEGIN
    CREATE INDEX [IX_PolicyConditions_PolicyId] ON [PolicyConditions] ([PolicyId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PostSyncTasks_SyncProjectRunId' AND object_id = OBJECT_ID('PostSyncTasks'))
BEGIN
    CREATE INDEX [IX_PostSyncTasks_SyncProjectRunId] ON [PostSyncTasks] ([SyncProjectRunId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReportColumns_ReportId' AND object_id = OBJECT_ID('ReportColumns'))
BEGIN
    CREATE INDEX [IX_ReportColumns_ReportId] ON [ReportColumns] ([ReportId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReportExecutions_ReportId' AND object_id = OBJECT_ID('ReportExecutions'))
BEGIN
    CREATE INDEX [IX_ReportExecutions_ReportId] ON [ReportExecutions] ([ReportId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReportExecutions_ScheduleId' AND object_id = OBJECT_ID('ReportExecutions'))
BEGIN
    CREATE INDEX [IX_ReportExecutions_ScheduleId] ON [ReportExecutions] ([ScheduleId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReportParameters_ReportId' AND object_id = OBJECT_ID('ReportParameters'))
BEGIN
    CREATE INDEX [IX_ReportParameters_ReportId] ON [ReportParameters] ([ReportId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReportSchedules_ReportId' AND object_id = OBJECT_ID('ReportSchedules'))
BEGIN
    CREATE INDEX [IX_ReportSchedules_ReportId] ON [ReportSchedules] ([ReportId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Settings_Category_Key' AND object_id = OBJECT_ID('Settings'))
BEGIN
    CREATE UNIQUE INDEX [IX_Settings_Category_Key] ON [Settings] ([Category], [Key]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncAuditLogs_ObjectId' AND object_id = OBJECT_ID('SyncAuditLogs'))
BEGIN
    CREATE INDEX [IX_SyncAuditLogs_ObjectId] ON [SyncAuditLogs] ([ObjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncAuditLogs_OperationType' AND object_id = OBJECT_ID('SyncAuditLogs'))
BEGIN
    CREATE INDEX [IX_SyncAuditLogs_OperationType] ON [SyncAuditLogs] ([OperationType]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncAuditLogs_SyncStepRunId' AND object_id = OBJECT_ID('SyncAuditLogs'))
BEGIN
    CREATE INDEX [IX_SyncAuditLogs_SyncStepRunId] ON [SyncAuditLogs] ([SyncStepRunId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncAuditLogs_Timestamp' AND object_id = OBJECT_ID('SyncAuditLogs'))
BEGIN
    CREATE INDEX [IX_SyncAuditLogs_Timestamp] ON [SyncAuditLogs] ([Timestamp]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncExecutions_DirectoryConnectionId' AND object_id = OBJECT_ID('SyncExecutions'))
BEGIN
    CREATE INDEX [IX_SyncExecutions_DirectoryConnectionId] ON [SyncExecutions] ([DirectoryConnectionId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncExecutions_StartedAt' AND object_id = OBJECT_ID('SyncExecutions'))
BEGIN
    CREATE INDEX [IX_SyncExecutions_StartedAt] ON [SyncExecutions] ([StartedAt]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncExecutions_Status' AND object_id = OBJECT_ID('SyncExecutions'))
BEGIN
    CREATE INDEX [IX_SyncExecutions_Status] ON [SyncExecutions] ([Status]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_Category' AND object_id = OBJECT_ID('SyncProcessingScripts'))
BEGIN
    CREATE INDEX [IX_SyncProcessingScripts_Category] ON [SyncProcessingScripts] ([Category]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_IsEnabled' AND object_id = OBJECT_ID('SyncProcessingScripts'))
BEGIN
    CREATE INDEX [IX_SyncProcessingScripts_IsEnabled] ON [SyncProcessingScripts] ([IsEnabled]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_IsSystem' AND object_id = OBJECT_ID('SyncProcessingScripts'))
BEGIN
    CREATE INDEX [IX_SyncProcessingScripts_IsSystem] ON [SyncProcessingScripts] ([IsSystem]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_Name' AND object_id = OBJECT_ID('SyncProcessingScripts'))
BEGIN
    CREATE INDEX [IX_SyncProcessingScripts_Name] ON [SyncProcessingScripts] ([Name]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_ScriptType' AND object_id = OBJECT_ID('SyncProcessingScripts'))
BEGIN
    CREATE INDEX [IX_SyncProcessingScripts_ScriptType] ON [SyncProcessingScripts] ([ScriptType]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectChains_SourceProjectId' AND object_id = OBJECT_ID('SyncProjectChains'))
BEGIN
    CREATE INDEX [IX_SyncProjectChains_SourceProjectId] ON [SyncProjectChains] ([SourceProjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectChains_SourceTarget' AND object_id = OBJECT_ID('SyncProjectChains'))
BEGIN
    CREATE UNIQUE INDEX [IX_SyncProjectChains_SourceTarget] ON [SyncProjectChains] ([SourceProjectId], [TargetProjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectChains_TargetProjectId' AND object_id = OBJECT_ID('SyncProjectChains'))
BEGIN
    CREATE INDEX [IX_SyncProjectChains_TargetProjectId] ON [SyncProjectChains] ([TargetProjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectRuns_StartedAt' AND object_id = OBJECT_ID('SyncProjectRuns'))
BEGIN
    CREATE INDEX [IX_SyncProjectRuns_StartedAt] ON [SyncProjectRuns] ([StartedAt]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectRuns_Status' AND object_id = OBJECT_ID('SyncProjectRuns'))
BEGIN
    CREATE INDEX [IX_SyncProjectRuns_Status] ON [SyncProjectRuns] ([Status]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectRuns_SyncProjectId' AND object_id = OBJECT_ID('SyncProjectRuns'))
BEGIN
    CREATE INDEX [IX_SyncProjectRuns_SyncProjectId] ON [SyncProjectRuns] ([SyncProjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectRuns_TriggerType' AND object_id = OBJECT_ID('SyncProjectRuns'))
BEGIN
    CREATE INDEX [IX_SyncProjectRuns_TriggerType] ON [SyncProjectRuns] ([TriggerType]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjects_IsEnabled' AND object_id = OBJECT_ID('SyncProjects'))
BEGIN
    CREATE INDEX [IX_SyncProjects_IsEnabled] ON [SyncProjects] ([IsEnabled]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjects_IsRunning' AND object_id = OBJECT_ID('SyncProjects'))
BEGIN
    CREATE INDEX [IX_SyncProjects_IsRunning] ON [SyncProjects] ([IsRunning]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjects_Name' AND object_id = OBJECT_ID('SyncProjects'))
BEGIN
    CREATE INDEX [IX_SyncProjects_Name] ON [SyncProjects] ([Name]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjects_NextScheduledRunAt' AND object_id = OBJECT_ID('SyncProjects'))
BEGIN
    CREATE INDEX [IX_SyncProjects_NextScheduledRunAt] ON [SyncProjects] ([NextScheduledRunAt]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjects_SourceConnectionId' AND object_id = OBJECT_ID('SyncProjects'))
BEGIN
    CREATE INDEX [IX_SyncProjects_SourceConnectionId] ON [SyncProjects] ([SourceConnectionId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjects_SourceSyncProjectId' AND object_id = OBJECT_ID('SyncProjects'))
BEGIN
    CREATE INDEX [IX_SyncProjects_SourceSyncProjectId] ON [SyncProjects] ([SourceSyncProjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjects_TargetConnectionId' AND object_id = OBJECT_ID('SyncProjects'))
BEGIN
    CREATE INDEX [IX_SyncProjects_TargetConnectionId] ON [SyncProjects] ([TargetConnectionId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectTemplates_Category' AND object_id = OBJECT_ID('SyncProjectTemplates'))
BEGIN
    CREATE INDEX [IX_SyncProjectTemplates_Category] ON [SyncProjectTemplates] ([Category]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectTemplates_IsSystem' AND object_id = OBJECT_ID('SyncProjectTemplates'))
BEGIN
    CREATE INDEX [IX_SyncProjectTemplates_IsSystem] ON [SyncProjectTemplates] ([IsSystem]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProjectTemplates_Name' AND object_id = OBJECT_ID('SyncProjectTemplates'))
BEGIN
    CREATE INDEX [IX_SyncProjectTemplates_Name] ON [SyncProjectTemplates] ([Name]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncScriptExecutions_ScriptId' AND object_id = OBJECT_ID('SyncScriptExecutions'))
BEGIN
    CREATE INDEX [IX_SyncScriptExecutions_ScriptId] ON [SyncScriptExecutions] ([ScriptId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncScriptExecutions_StartedAt' AND object_id = OBJECT_ID('SyncScriptExecutions'))
BEGIN
    CREATE INDEX [IX_SyncScriptExecutions_StartedAt] ON [SyncScriptExecutions] ([StartedAt]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncScriptExecutions_Status' AND object_id = OBJECT_ID('SyncScriptExecutions'))
BEGIN
    CREATE INDEX [IX_SyncScriptExecutions_Status] ON [SyncScriptExecutions] ([Status]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncScriptExecutions_SyncStepRunId' AND object_id = OBJECT_ID('SyncScriptExecutions'))
BEGIN
    CREATE INDEX [IX_SyncScriptExecutions_SyncStepRunId] ON [SyncScriptExecutions] ([SyncStepRunId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepRuns_ProjectStep' AND object_id = OBJECT_ID('SyncStepRuns'))
BEGIN
    CREATE INDEX [IX_SyncStepRuns_ProjectStep] ON [SyncStepRuns] ([SyncProjectRunId], [SyncStepId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepRuns_Status' AND object_id = OBJECT_ID('SyncStepRuns'))
BEGIN
    CREATE INDEX [IX_SyncStepRuns_Status] ON [SyncStepRuns] ([Status]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepRuns_SyncProjectRunId' AND object_id = OBJECT_ID('SyncStepRuns'))
BEGIN
    CREATE INDEX [IX_SyncStepRuns_SyncProjectRunId] ON [SyncStepRuns] ([SyncProjectRunId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepRuns_SyncStepId' AND object_id = OBJECT_ID('SyncStepRuns'))
BEGIN
    CREATE INDEX [IX_SyncStepRuns_SyncStepId] ON [SyncStepRuns] ([SyncStepId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncSteps_IsEnabled' AND object_id = OBJECT_ID('SyncSteps'))
BEGIN
    CREATE INDEX [IX_SyncSteps_IsEnabled] ON [SyncSteps] ([IsEnabled]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncSteps_ObjectClass' AND object_id = OBJECT_ID('SyncSteps'))
BEGIN
    CREATE INDEX [IX_SyncSteps_ObjectClass] ON [SyncSteps] ([ObjectClass]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncSteps_SyncWorkflowId' AND object_id = OBJECT_ID('SyncSteps'))
BEGIN
    CREATE INDEX [IX_SyncSteps_SyncWorkflowId] ON [SyncSteps] ([SyncWorkflowId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncSteps_WorkflowOrder' AND object_id = OBJECT_ID('SyncSteps'))
BEGIN
    CREATE INDEX [IX_SyncSteps_WorkflowOrder] ON [SyncSteps] ([SyncWorkflowId], [ExecutionOrder]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepScripts_ScriptId' AND object_id = OBJECT_ID('SyncStepScripts'))
BEGIN
    CREATE INDEX [IX_SyncStepScripts_ScriptId] ON [SyncStepScripts] ([ScriptId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepScripts_StepPhaseOrder' AND object_id = OBJECT_ID('SyncStepScripts'))
BEGIN
    CREATE INDEX [IX_SyncStepScripts_StepPhaseOrder] ON [SyncStepScripts] ([SyncStepId], [ExecutionPhase], [ExecutionOrder]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepScripts_SyncStepId' AND object_id = OBJECT_ID('SyncStepScripts'))
BEGIN
    CREATE INDEX [IX_SyncStepScripts_SyncStepId] ON [SyncStepScripts] ([SyncStepId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepTags_SyncStepId' AND object_id = OBJECT_ID('SyncStepTags'))
BEGIN
    CREATE INDEX [IX_SyncStepTags_SyncStepId] ON [SyncStepTags] ([SyncStepId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepTags_TagId' AND object_id = OBJECT_ID('SyncStepTags'))
BEGIN
    CREATE INDEX [IX_SyncStepTags_TagId] ON [SyncStepTags] ([TagId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncWorkflows_IsEnabled' AND object_id = OBJECT_ID('SyncWorkflows'))
BEGIN
    CREATE INDEX [IX_SyncWorkflows_IsEnabled] ON [SyncWorkflows] ([IsEnabled]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncWorkflows_ObjectClass' AND object_id = OBJECT_ID('SyncWorkflows'))
BEGIN
    CREATE INDEX [IX_SyncWorkflows_ObjectClass] ON [SyncWorkflows] ([ObjectClass]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncWorkflows_ProjectOrder' AND object_id = OBJECT_ID('SyncWorkflows'))
BEGIN
    CREATE INDEX [IX_SyncWorkflows_ProjectOrder] ON [SyncWorkflows] ([SyncProjectId], [ExecutionOrder]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncWorkflows_SyncProjectId' AND object_id = OBJECT_ID('SyncWorkflows'))
BEGIN
    CREATE INDEX [IX_SyncWorkflows_SyncProjectId] ON [SyncWorkflows] ([SyncProjectId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncWorkflows_WorkflowType' AND object_id = OBJECT_ID('SyncWorkflows'))
BEGIN
    CREATE INDEX [IX_SyncWorkflows_WorkflowType] ON [SyncWorkflows] ([WorkflowType]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncWorkflowTemplates_ObjectClass' AND object_id = OBJECT_ID('SyncWorkflowTemplates'))
BEGIN
    CREATE INDEX [IX_SyncWorkflowTemplates_ObjectClass] ON [SyncWorkflowTemplates] ([ObjectClass]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncWorkflowTemplates_ProjectTemplateId' AND object_id = OBJECT_ID('SyncWorkflowTemplates'))
BEGIN
    CREATE INDEX [IX_SyncWorkflowTemplates_ProjectTemplateId] ON [SyncWorkflowTemplates] ([ProjectTemplateId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Tags_Category' AND object_id = OBJECT_ID('Tags'))
BEGIN
    CREATE INDEX [IX_Tags_Category] ON [Tags] ([Category]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Tags_IsSystem' AND object_id = OBJECT_ID('Tags'))
BEGIN
    CREATE INDEX [IX_Tags_IsSystem] ON [Tags] ([IsSystem]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Tags_Name' AND object_id = OBJECT_ID('Tags'))
BEGIN
    CREATE UNIQUE INDEX [IX_Tags_Name] ON [Tags] ([Name]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TriggerActionLogs_ActionId' AND object_id = OBJECT_ID('TriggerActionLogs'))
BEGIN
    CREATE INDEX [IX_TriggerActionLogs_ActionId] ON [TriggerActionLogs] ([ActionId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TriggerActionLogs_ExecutionId' AND object_id = OBJECT_ID('TriggerActionLogs'))
BEGIN
    CREATE INDEX [IX_TriggerActionLogs_ExecutionId] ON [TriggerActionLogs] ([ExecutionId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TriggerActions_TriggerId' AND object_id = OBJECT_ID('TriggerActions'))
BEGIN
    CREATE INDEX [IX_TriggerActions_TriggerId] ON [TriggerActions] ([TriggerId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TriggerConditions_TriggerId' AND object_id = OBJECT_ID('TriggerConditions'))
BEGIN
    CREATE INDEX [IX_TriggerConditions_TriggerId] ON [TriggerConditions] ([TriggerId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TriggerExecutions_EventId' AND object_id = OBJECT_ID('TriggerExecutions'))
BEGIN
    CREATE INDEX [IX_TriggerExecutions_EventId] ON [TriggerExecutions] ([EventId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TriggerExecutions_TriggerId' AND object_id = OBJECT_ID('TriggerExecutions'))
BEGIN
    CREATE INDEX [IX_TriggerExecutions_TriggerId] ON [TriggerExecutions] ([TriggerId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserAccess_AccessRequestId' AND object_id = OBJECT_ID('UserAccess'))
BEGIN
    CREATE INDEX [IX_UserAccess_AccessRequestId] ON [UserAccess] ([AccessRequestId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserAccess_UserId' AND object_id = OBJECT_ID('UserAccess'))
BEGIN
    CREATE INDEX [IX_UserAccess_UserId] ON [UserAccess] ([UserId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserReportFavorites_ReportId' AND object_id = OBJECT_ID('UserReportFavorites'))
BEGIN
    CREATE INDEX [IX_UserReportFavorites_ReportId] ON [UserReportFavorites] ([ReportId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkflowStep_WorkflowId' AND object_id = OBJECT_ID('WorkflowStep'))
BEGIN
    CREATE INDEX [IX_WorkflowStep_WorkflowId] ON [WorkflowStep] ([WorkflowId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkflowTags_SyncWorkflowId' AND object_id = OBJECT_ID('WorkflowTags'))
BEGIN
    CREATE INDEX [IX_WorkflowTags_SyncWorkflowId] ON [WorkflowTags] ([SyncWorkflowId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkflowTags_TagId' AND object_id = OBJECT_ID('WorkflowTags'))
BEGIN
    CREATE INDEX [IX_WorkflowTags_TagId] ON [WorkflowTags] ([TagId]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkflowTags_WorkflowTag' AND object_id = OBJECT_ID('WorkflowTags'))
BEGIN
    CREATE UNIQUE INDEX [IX_WorkflowTags_WorkflowTag] ON [WorkflowTags] ([SyncWorkflowId], [TagId]);
END;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Suffix')
                        ALTER TABLE Identities ADD Suffix NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Salutation')
                        ALTER TABLE Identities ADD Salutation NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PreferredName')
                        ALTER TABLE Identities ADD PreferredName NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'DateOfBirth')
                        ALTER TABLE Identities ADD DateOfBirth DATETIME2 NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Gender')
                        ALTER TABLE Identities ADD Gender NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'NationalId')
                        ALTER TABLE Identities ADD NationalId NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PhotoUrl')
                        ALTER TABLE Identities ADD PhotoUrl NVARCHAR(2000) NULL;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'SecondaryEmail')
                        ALTER TABLE Identities ADD SecondaryEmail NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'MobilePhone')
                        ALTER TABLE Identities ADD MobilePhone NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'HomePhone')
                        ALTER TABLE Identities ADD HomePhone NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Fax')
                        ALTER TABLE Identities ADD Fax NVARCHAR(50) NULL;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'StreetAddress')
                        ALTER TABLE Identities ADD StreetAddress NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'City')
                        ALTER TABLE Identities ADD City NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'State')
                        ALTER TABLE Identities ADD State NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PostalCode')
                        ALTER TABLE Identities ADD PostalCode NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Country')
                        ALTER TABLE Identities ADD Country NVARCHAR(200) NULL;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EmployeeId')
                        ALTER TABLE Identities ADD EmployeeId NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Division')
                        ALTER TABLE Identities ADD Division NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Company')
                        ALTER TABLE Identities ADD Company NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Office')
                        ALTER TABLE Identities ADD Office NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Building')
                        ALTER TABLE Identities ADD Building NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Floor')
                        ALTER TABLE Identities ADD Floor NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Room')
                        ALTER TABLE Identities ADD Room NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CostCenter')
                        ALTER TABLE Identities ADD CostCenter NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'ProfitCenter')
                        ALTER TABLE Identities ADD ProfitCenter NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'EmployeeType')
                        ALTER TABLE Identities ADD EmployeeType NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'ContractType')
                        ALTER TABLE Identities ADD ContractType NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'HireDate')
                        ALTER TABLE Identities ADD HireDate DATETIME2 NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'TerminationDate')
                        ALTER TABLE Identities ADD TerminationDate DATETIME2 NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'LastWorkDay')
                        ALTER TABLE Identities ADD LastWorkDay DATETIME2 NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Description')
                        ALTER TABLE Identities ADD Description NVARCHAR(1000) NULL;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Username')
                        ALTER TABLE Identities ADD Username NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'UserPrincipalName')
                        ALTER TABLE Identities ADD UserPrincipalName NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Status')
                        ALTER TABLE Identities ADD Status NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'SecurityClearance')
                        ALTER TABLE Identities ADD SecurityClearance NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'RiskScore')
                        ALTER TABLE Identities ADD RiskScore INT NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'RiskLevel')
                        ALTER TABLE Identities ADD RiskLevel NVARCHAR(50) NULL;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PreferredLanguage')
                        ALTER TABLE Identities ADD PreferredLanguage NVARCHAR(20) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'TimeZone')
                        ALTER TABLE Identities ADD TimeZone NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'Locale')
                        ALTER TABLE Identities ADD Locale NVARCHAR(10) NULL;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'LastLoginAt')
                        ALTER TABLE Identities ADD LastLoginAt DATETIME2 NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'PasswordLastChangedAt')
                        ALTER TABLE Identities ADD PasswordLastChangedAt DATETIME2 NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'LastAccessReviewAt')
                        ALTER TABLE Identities ADD LastAccessReviewAt DATETIME2 NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CreatedBy')
                        ALTER TABLE Identities ADD CreatedBy NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'ModifiedBy')
                        ALTER TABLE Identities ADD ModifiedBy NVARCHAR(200) NULL;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CustomAttributes')
                        ALTER TABLE Identities ADD CustomAttributes NVARCHAR(MAX) NULL;
GO

-- From migration: 20260115200000_AddComprehensiveIdentityFields
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'MiddleName')
                        ALTER TABLE Objects ADD MiddleName NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'MobilePhone')
                        ALTER TABLE Objects ADD MobilePhone NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'HomePhone')
                        ALTER TABLE Objects ADD HomePhone NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'Fax')
                        ALTER TABLE Objects ADD Fax NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'StreetAddress')
                        ALTER TABLE Objects ADD StreetAddress NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'City')
                        ALTER TABLE Objects ADD City NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'State')
                        ALTER TABLE Objects ADD State NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'PostalCode')
                        ALTER TABLE Objects ADD PostalCode NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'Country')
                        ALTER TABLE Objects ADD Country NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'Company')
                        ALTER TABLE Objects ADD Company NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'Division')
                        ALTER TABLE Objects ADD Division NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'Office')
                        ALTER TABLE Objects ADD Office NVARCHAR(200) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'EmployeeId')
                        ALTER TABLE Objects ADD EmployeeId NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'EmployeeType')
                        ALTER TABLE Objects ADD EmployeeType NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'UserPrincipalName')
                        ALTER TABLE Objects ADD UserPrincipalName NVARCHAR(500) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'Description')
                        ALTER TABLE Objects ADD Description NVARCHAR(2000) NULL;
GO

-- From migration: 20260117061905_AddPolicyTypeColumn
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SyncProjects') AND name = 'EnablePreSyncIndexing')
                    BEGIN
                        ALTER TABLE [SyncProjects] ADD [EnablePreSyncIndexing] bit NOT NULL DEFAULT 0;
                    END
GO

-- From migration: 20260117061905_AddPolicyTypeColumn
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PostSyncTasks') AND name = 'TaskPhase')
                    BEGIN
                        ALTER TABLE [PostSyncTasks] ADD [TaskPhase] nvarchar(20) NOT NULL DEFAULT 'PostSync';
                    END
GO

-- From migration: 20260117061905_AddPolicyTypeColumn
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'SecondaryEmail')
                    BEGIN
                        ALTER TABLE [Identities] ADD [SecondaryEmail] nvarchar(500) NULL;
                    END
GO

-- From migration: 20260117061905_AddPolicyTypeColumn
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'PolicyType')
                    BEGIN
                        ALTER TABLE [CompliancePolicies] ADD [PolicyType] nvarchar(50) NOT NULL DEFAULT 'Detection';
                    END
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'LastProcessingResetDate')
                        AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'CurrentRunStartedAt')
                        EXEC sp_rename 'CompliancePolicies.LastProcessingResetDate', 'CurrentRunStartedAt', 'COLUMN';
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'DailyProcessingLimit')
                        AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'ProcessingLimitPerRun')
                        EXEC sp_rename 'CompliancePolicies.DailyProcessingLimit', 'ProcessingLimitPerRun', 'COLUMN';
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyExecutions') AND name = 'NewViolations')
                        ALTER TABLE CompliancePolicyExecutions ADD NewViolations INT NOT NULL DEFAULT 0;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyExecutions') AND name = 'ResolvedViolations')
                        ALTER TABLE CompliancePolicyExecutions ADD ResolvedViolations INT NOT NULL DEFAULT 0;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyExecutions') AND name = 'SkippedViolations')
                        ALTER TABLE CompliancePolicyExecutions ADD SkippedViolations INT NOT NULL DEFAULT 0;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'ProcessedThisRun')
                        ALTER TABLE CompliancePolicies ADD ProcessedThisRun INT NOT NULL DEFAULT 0;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'RemoveOutOfScopeViolations')
                        ALTER TABLE CompliancePolicies ADD RemoveOutOfScopeViolations BIT NOT NULL DEFAULT 0;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ComplianceFrameworkPolicyMappings') AND name = 'SortOrder')
                        ALTER TABLE ComplianceFrameworkPolicyMappings ADD SortOrder INT NOT NULL DEFAULT 0;
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'FrameworkAssignments') AND type = 'U')
                    BEGIN
                        CREATE TABLE [FrameworkAssignments] (
                            [Id] uniqueidentifier NOT NULL,
                            [FrameworkId] uniqueidentifier NOT NULL,
                            [ConnectionId] uniqueidentifier NULL,
                            [DepartmentId] uniqueidentifier NULL,
                            [ApplicationId] uniqueidentifier NULL,
                            [ScopeExpression] nvarchar(max) NULL,
                            [ScopeInheritance] nvarchar(20) NOT NULL,
                            [IsActive] bit NOT NULL,
                            [ActivatedAt] datetime2 NULL,
                            [DeactivatedAt] datetime2 NULL,
                            [DeactivationReason] nvarchar(1000) NULL,
                            [ComplianceScore] decimal(5,2) NOT NULL,
                            [LastEvaluatedAt] datetime2 NULL,
                            [TotalPolicies] int NOT NULL,
                            [PassingPolicies] int NOT NULL,
                            [FailingPolicies] int NOT NULL,
                            [TotalViolations] int NOT NULL,
                            [CriticalViolations] int NOT NULL,
                            [CreatedAt] datetime2 NOT NULL,
                            [CreatedBy] nvarchar(256) NOT NULL,
                            [ModifiedAt] datetime2 NULL,
                            [ModifiedBy] nvarchar(256) NULL,
                            CONSTRAINT [PK_FrameworkAssignments] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_FrameworkAssignments_ComplianceFrameworks_FrameworkId] FOREIGN KEY ([FrameworkId]) REFERENCES [ComplianceFrameworks] ([Id]) ON DELETE NO ACTION,
                            CONSTRAINT [FK_FrameworkAssignments_DirectoryConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [DirectoryConnections] ([Id]) ON DELETE NO ACTION
                        );
                    END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'MaintenanceSettings') AND type = 'U')
                    BEGIN
                        CREATE TABLE [MaintenanceSettings] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [SyncLogRetentionDays] int NOT NULL,
                            [ChangeLogRetentionDays] int NOT NULL,
                            [SystemLogRetentionDays] int NOT NULL,
                            [JobHistoryRetentionDays] int NOT NULL,
                            [NotificationLogRetentionDays] int NOT NULL,
                            [EnableIndexMaintenance] bit NOT NULL,
                            [IndexReorganizeThreshold] int NOT NULL,
                            [IndexRebuildThreshold] int NOT NULL,
                            [EnableStatisticsUpdate] bit NOT NULL,
                            [StatisticsUpdateThreshold] int NOT NULL,
                            [EnableSessionCleanup] bit NOT NULL,
                            [ExpiredSessionRetentionDays] int NOT NULL,
                            [EnableOrphanedDataCleanup] bit NOT NULL,
                            [OrphanedDataRetentionDays] int NOT NULL,
                            [EnableTempFileCleanup] bit NOT NULL,
                            [TempFileRetentionDays] int NOT NULL,
                            [LogCleanupSchedule] nvarchar(max) NOT NULL,
                            [IndexMaintenanceSchedule] nvarchar(max) NOT NULL,
                            [StatisticsUpdateSchedule] nvarchar(max) NOT NULL,
                            [SessionCleanupSchedule] nvarchar(max) NOT NULL,
                            [OrphanedDataCleanupSchedule] nvarchar(max) NOT NULL,
                            [LogCleanupEnabled] bit NOT NULL,
                            [IndexMaintenanceEnabled] bit NOT NULL,
                            [StatisticsUpdateEnabled] bit NOT NULL,
                            [SessionCleanupEnabled] bit NOT NULL,
                            [OrphanedDataCleanupEnabled] bit NOT NULL,
                            [LastLogCleanupRun] datetime2 NULL,
                            [LastIndexMaintenanceRun] datetime2 NULL,
                            [LastStatisticsUpdateRun] datetime2 NULL,
                            [LastSessionCleanupRun] datetime2 NULL,
                            [LastOrphanedDataCleanupRun] datetime2 NULL,
                            [CreatedAt] datetime2 NOT NULL,
                            [ModifiedAt] datetime2 NULL,
                            [ModifiedBy] nvarchar(max) NOT NULL,
                            CONSTRAINT [PK_MaintenanceSettings] PRIMARY KEY ([Id])
                        );
                    END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'RemediationActions') AND type = 'U')
                    BEGIN
                        CREATE TABLE [RemediationActions] (
                            [Id] uniqueidentifier NOT NULL,
                            [AssignmentId] uniqueidentifier NOT NULL,
                            [CampaignId] uniqueidentifier NOT NULL,
                            [DecisionHistoryId] uniqueidentifier NULL,
                            [ActionType] nvarchar(50) NOT NULL,
                            [ActionDescription] nvarchar(200) NOT NULL,
                            [ActionParameters] nvarchar(max) NULL,
                            [TargetEntityId] uniqueidentifier NOT NULL,
                            [TargetEntityType] nvarchar(50) NOT NULL,
                            [Status] nvarchar(50) NOT NULL,
                            [ScheduledFor] datetime2 NULL,
                            [ExecutedAt] datetime2 NULL,
                            [CompletedAt] datetime2 NULL,
                            [ExecutionResult] nvarchar(max) NULL,
                            [ErrorMessage] nvarchar(max) NULL,
                            [RetryCount] int NOT NULL,
                            [LastRetryAt] datetime2 NULL,
                            [RequiresApproval] bit NOT NULL,
                            [ApprovedBy] uniqueidentifier NULL,
                            [ApprovedAt] datetime2 NULL,
                            [CreatedAt] datetime2 NOT NULL,
                            [CreatedBy] nvarchar(200) NOT NULL,
                            CONSTRAINT [PK_RemediationActions] PRIMARY KEY ([Id])
                        );
                    END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'FrameworkAssignmentPolicyOverrides') AND type = 'U')
                    BEGIN
                        CREATE TABLE [FrameworkAssignmentPolicyOverrides] (
                            [Id] uniqueidentifier NOT NULL,
                            [AssignmentId] uniqueidentifier NOT NULL,
                            [PolicyId] uniqueidentifier NOT NULL,
                            [IsEnabled] bit NULL,
                            [EnforcementMode] nvarchar(20) NULL,
                            [CustomParameters] nvarchar(max) NULL,
                            [Justification] nvarchar(2000) NULL,
                            [ExpiresAt] datetime2 NULL,
                            [CreatedAt] datetime2 NOT NULL,
                            [CreatedBy] nvarchar(256) NOT NULL,
                            [ModifiedAt] datetime2 NULL,
                            [ModifiedBy] nvarchar(256) NULL,
                            CONSTRAINT [PK_FrameworkAssignmentPolicyOverrides] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_FrameworkAssignmentPolicyOverrides_CompliancePolicies_PolicyId] FOREIGN KEY ([PolicyId]) REFERENCES [CompliancePolicies] ([Id]) ON DELETE NO ACTION,
                            CONSTRAINT [FK_FrameworkAssignmentPolicyOverrides_FrameworkAssignments_AssignmentId] FOREIGN KEY ([AssignmentId]) REFERENCES [FrameworkAssignments] ([Id]) ON DELETE CASCADE
                        );
                    END
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ReviewDecisionHistory_AssignmentId' AND object_id = OBJECT_ID(N'ReviewDecisionHistory'))
                        CREATE NONCLUSTERED INDEX [IX_ReviewDecisionHistory_AssignmentId] ON [ReviewDecisionHistory] ([AssignmentId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ReviewDecisionHistory_CampaignId' AND object_id = OBJECT_ID(N'ReviewDecisionHistory'))
                        CREATE NONCLUSTERED INDEX [IX_ReviewDecisionHistory_CampaignId] ON [ReviewDecisionHistory] ([CampaignId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ReviewDecisionHistory_Decision' AND object_id = OBJECT_ID(N'ReviewDecisionHistory'))
                        CREATE NONCLUSTERED INDEX [IX_ReviewDecisionHistory_Decision] ON [ReviewDecisionHistory] ([Decision]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ReviewDecisionHistory_DecisionDate' AND object_id = OBJECT_ID(N'ReviewDecisionHistory'))
                        CREATE NONCLUSTERED INDEX [IX_ReviewDecisionHistory_DecisionDate] ON [ReviewDecisionHistory] ([DecisionDate]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CampaignTemplates_ComplianceFramework' AND object_id = OBJECT_ID(N'CampaignTemplates'))
                        CREATE NONCLUSTERED INDEX [IX_CampaignTemplates_ComplianceFramework] ON [CampaignTemplates] ([ComplianceFramework]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CampaignTemplates_IsActive' AND object_id = OBJECT_ID(N'CampaignTemplates'))
                        CREATE NONCLUSTERED INDEX [IX_CampaignTemplates_IsActive] ON [CampaignTemplates] ([IsActive]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CampaignTemplates_TemplateType' AND object_id = OBJECT_ID(N'CampaignTemplates'))
                        CREATE NONCLUSTERED INDEX [IX_CampaignTemplates_TemplateType] ON [CampaignTemplates] ([TemplateType]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Campaigns_CampaignType' AND object_id = OBJECT_ID(N'Campaigns'))
                        CREATE NONCLUSTERED INDEX [IX_Campaigns_CampaignType] ON [Campaigns] ([CampaignType]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Campaigns_ComplianceFramework' AND object_id = OBJECT_ID(N'Campaigns'))
                        CREATE NONCLUSTERED INDEX [IX_Campaigns_ComplianceFramework] ON [Campaigns] ([ComplianceFramework]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Campaigns_DueDate' AND object_id = OBJECT_ID(N'Campaigns'))
                        CREATE NONCLUSTERED INDEX [IX_Campaigns_DueDate] ON [Campaigns] ([DueDate]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Campaigns_StartDate' AND object_id = OBJECT_ID(N'Campaigns'))
                        CREATE NONCLUSTERED INDEX [IX_Campaigns_StartDate] ON [Campaigns] ([StartDate]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Campaigns_Status' AND object_id = OBJECT_ID(N'Campaigns'))
                        CREATE NONCLUSTERED INDEX [IX_Campaigns_Status] ON [Campaigns] ([Status]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessReviewAssignments_Campaign_Reviewer' AND object_id = OBJECT_ID(N'AccessReviewAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_AccessReviewAssignments_Campaign_Reviewer] ON [AccessReviewAssignments] ([CampaignId], [ReviewerId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessReviewAssignments_CampaignId' AND object_id = OBJECT_ID(N'AccessReviewAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_AccessReviewAssignments_CampaignId] ON [AccessReviewAssignments] ([CampaignId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessReviewAssignments_ReviewerId' AND object_id = OBJECT_ID(N'AccessReviewAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_AccessReviewAssignments_ReviewerId] ON [AccessReviewAssignments] ([ReviewerId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessReviewAssignments_ReviewTargetId' AND object_id = OBJECT_ID(N'AccessReviewAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_AccessReviewAssignments_ReviewTargetId] ON [AccessReviewAssignments] ([ReviewTargetId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessReviewAssignments_Status' AND object_id = OBJECT_ID(N'AccessReviewAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_AccessReviewAssignments_Status] ON [AccessReviewAssignments] ([Status]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignmentPolicyOverrides_AssignmentId' AND object_id = OBJECT_ID(N'FrameworkAssignmentPolicyOverrides'))
                        CREATE NONCLUSTERED INDEX [IX_FrameworkAssignmentPolicyOverrides_AssignmentId] ON [FrameworkAssignmentPolicyOverrides] ([AssignmentId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignmentPolicyOverrides_AssignmentPolicy' AND object_id = OBJECT_ID(N'FrameworkAssignmentPolicyOverrides'))
                        CREATE UNIQUE NONCLUSTERED INDEX [IX_FrameworkAssignmentPolicyOverrides_AssignmentPolicy] ON [FrameworkAssignmentPolicyOverrides] ([AssignmentId], [PolicyId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignmentPolicyOverrides_PolicyId' AND object_id = OBJECT_ID(N'FrameworkAssignmentPolicyOverrides'))
                        CREATE NONCLUSTERED INDEX [IX_FrameworkAssignmentPolicyOverrides_PolicyId] ON [FrameworkAssignmentPolicyOverrides] ([PolicyId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignments_ApplicationId' AND object_id = OBJECT_ID(N'FrameworkAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_FrameworkAssignments_ApplicationId] ON [FrameworkAssignments] ([ApplicationId]) WHERE [ApplicationId] IS NOT NULL;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignments_ConnectionId' AND object_id = OBJECT_ID(N'FrameworkAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_FrameworkAssignments_ConnectionId] ON [FrameworkAssignments] ([ConnectionId]) WHERE [ConnectionId] IS NOT NULL;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignments_DepartmentId' AND object_id = OBJECT_ID(N'FrameworkAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_FrameworkAssignments_DepartmentId] ON [FrameworkAssignments] ([DepartmentId]) WHERE [DepartmentId] IS NOT NULL;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignments_FrameworkConnection' AND object_id = OBJECT_ID(N'FrameworkAssignments'))
                        CREATE UNIQUE NONCLUSTERED INDEX [IX_FrameworkAssignments_FrameworkConnection] ON [FrameworkAssignments] ([FrameworkId], [ConnectionId]) WHERE [ConnectionId] IS NOT NULL AND [IsActive] = 1;
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignments_FrameworkId' AND object_id = OBJECT_ID(N'FrameworkAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_FrameworkAssignments_FrameworkId] ON [FrameworkAssignments] ([FrameworkId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignments_IsActive' AND object_id = OBJECT_ID(N'FrameworkAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_FrameworkAssignments_IsActive] ON [FrameworkAssignments] ([IsActive]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FrameworkAssignments_LastEvaluatedAt' AND object_id = OBJECT_ID(N'FrameworkAssignments'))
                        CREATE NONCLUSTERED INDEX [IX_FrameworkAssignments_LastEvaluatedAt] ON [FrameworkAssignments] ([LastEvaluatedAt]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RemediationActions_AssignmentId' AND object_id = OBJECT_ID(N'RemediationActions'))
                        CREATE NONCLUSTERED INDEX [IX_RemediationActions_AssignmentId] ON [RemediationActions] ([AssignmentId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RemediationActions_CampaignId' AND object_id = OBJECT_ID(N'RemediationActions'))
                        CREATE NONCLUSTERED INDEX [IX_RemediationActions_CampaignId] ON [RemediationActions] ([CampaignId]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RemediationActions_Status' AND object_id = OBJECT_ID(N'RemediationActions'))
                        CREATE NONCLUSTERED INDEX [IX_RemediationActions_Status] ON [RemediationActions] ([Status]);
GO

-- From migration: 20260123002747_AddFrameworkAssignmentTables
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_RemediationActions_Status_ScheduledFor' AND object_id = OBJECT_ID(N'RemediationActions'))
                        CREATE NONCLUSTERED INDEX [IX_RemediationActions_Status_ScheduledFor] ON [RemediationActions] ([Status], [ScheduledFor]);
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MaintenanceSettings' AND COLUMN_NAME = 'ChangeLogMaxRecordCount')
BEGIN
    ALTER TABLE [MaintenanceSettings] ADD [ChangeLogMaxRecordCount] int NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MaintenanceSettings' AND COLUMN_NAME = 'ChangeLogMaxSizeMB')
BEGIN
    ALTER TABLE [MaintenanceSettings] ADD [ChangeLogMaxSizeMB] int NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MaintenanceSettings' AND COLUMN_NAME = 'ChangeLogRetentionMode')
BEGIN
    ALTER TABLE [MaintenanceSettings] ADD [ChangeLogRetentionMode] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Objects' AND COLUMN_NAME = 'OwnerIdentityId')
BEGIN
    ALTER TABLE [Objects] ADD [OwnerIdentityId] uniqueidentifier NULL;
END;
GO

-- From migration: 20260128073534_AddBulkIssueSnapshotsAndOwnerColumn
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_OwnerIdentityId' AND object_id = OBJECT_ID('Objects'))
BEGIN
    EXEC(N'CREATE INDEX [IX_Objects_OwnerIdentityId] ON [Objects] ([OwnerIdentityId]) WHERE [OwnerIdentityId] IS NOT NULL');
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = 'FK_Objects_Identities_OwnerIdentityId')
BEGIN
    ALTER TABLE [Objects] ADD CONSTRAINT [FK_Objects_Identities_OwnerIdentityId] FOREIGN KEY ([OwnerIdentityId]) REFERENCES [Identities] ([Id]) ON DELETE NO ACTION;
END;
GO

-- From migration: 20260202085150_AddInternalSyncStepTagFilter
DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MaintenanceSettings]') AND [c].[name] = N'Id');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [MaintenanceSettings] DROP CONSTRAINT [' + @var0 + '];');
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'InternalSyncSteps' AND COLUMN_NAME = 'TagFilter')
BEGIN
    ALTER TABLE [InternalSyncSteps] ADD [TagFilter] nvarchar(500) NULL;
END;
GO


PRINT 'Schema version 4 applied - complete initial schema created';
