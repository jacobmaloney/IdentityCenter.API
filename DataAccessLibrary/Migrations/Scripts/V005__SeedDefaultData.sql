-- V005: Seed Default Data
-- Seeds the default roles, admin user, settings, system configuration, and schedule templates
-- All inserts use IF NOT EXISTS checks so existing databases are not affected

-- =============================================
-- 1. Default Roles (3 roles)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Id] = '9c960570-0226-4d4a-a3bb-6e3507d6b509')
BEGIN
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [Description], [Permissions], [AdGroupMappings], [EntraIdGroupMappings], [IsSystem], [CreatedAt], [ConcurrencyStamp])
    VALUES ('9c960570-0226-4d4a-a3bb-6e3507d6b509', N'Admin', N'ADMIN', N'Full system administration access', N'', N'', N'', 0, '2025-10-12T17:12:04.3701989Z', NULL);
END

IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Id] = '3e055850-ecfa-4e16-abf2-a764a0fba89f')
BEGIN
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [Description], [Permissions], [AdGroupMappings], [EntraIdGroupMappings], [IsSystem], [CreatedAt], [ConcurrencyStamp])
    VALUES ('3e055850-ecfa-4e16-abf2-a764a0fba89f', N'UserManager', N'USERMANAGER', N'Can manage users and groups', N'', N'', N'', 0, '2025-10-12T17:12:04.3702007Z', NULL);
END

IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [Id] = '5af6d2aa-47dd-4732-aa1c-1f7b8473d03d')
BEGIN
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [Description], [Permissions], [AdGroupMappings], [EntraIdGroupMappings], [IsSystem], [CreatedAt], [ConcurrencyStamp])
    VALUES ('5af6d2aa-47dd-4732-aa1c-1f7b8473d03d', N'AuditViewer', N'AUDITVIEWER', N'Can view audit logs and reports', N'', N'', N'', 0, '2025-10-12T17:12:04.3702009Z', NULL);
END

GO

-- Sections 2 & 3 removed: the legacy seeded admin user and its role assignment.
-- The first-run wizard now creates the sole admin account with a randomly-
-- generated password via BootstrapPasswordGenerator. V121 cleans up the
-- legacy admin row on existing installs where the original hash is intact.

-- =============================================
-- 4. Default Settings (3 settings)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Id] = 1)
BEGIN
    SET IDENTITY_INSERT [Settings] ON;
    INSERT INTO [Settings] ([Id], [Category], [Key], [Value], [IsEncrypted], [DataType], [ModifiedAt], [ModifiedBy])
    VALUES (1, N'Security', N'SessionTimeout', N'30', 0, N'int', '2025-10-12T17:12:04.3702135Z', NULL);
    SET IDENTITY_INSERT [Settings] OFF;
END

IF NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Id] = 2)
BEGIN
    SET IDENTITY_INSERT [Settings] ON;
    INSERT INTO [Settings] ([Id], [Category], [Key], [Value], [IsEncrypted], [DataType], [ModifiedAt], [ModifiedBy])
    VALUES (2, N'Security', N'MaxFailedAttempts', N'5', 0, N'int', '2025-10-12T17:12:04.3702137Z', NULL);
    SET IDENTITY_INSERT [Settings] OFF;
END

IF NOT EXISTS (SELECT 1 FROM [Settings] WHERE [Id] = 3)
BEGIN
    SET IDENTITY_INSERT [Settings] ON;
    INSERT INTO [Settings] ([Id], [Category], [Key], [Value], [IsEncrypted], [DataType], [ModifiedAt], [ModifiedBy])
    VALUES (3, N'Security', N'LockoutDuration', N'30', 0, N'int', '2025-10-12T17:12:04.3702138Z', NULL);
    SET IDENTITY_INSERT [Settings] OFF;
END

GO

-- =============================================
-- 5. Default System Configuration
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [SystemConfigurations] WHERE [Id] = 1)
BEGIN
    SET IDENTITY_INSERT [SystemConfigurations] ON;
    INSERT INTO [SystemConfigurations] ([Id], [AllowSelfRegistration], [RequireEmailConfirmation], [AllowExternalLogins],
        [MinimumPasswordLength], [RequireDigit], [RequireLowercase], [RequireUppercase], [RequireNonAlphanumeric],
        [MaxFailedAccessAttempts], [LockoutDurationMinutes], [SessionTimeoutMinutes], [SlidingExpiration],
        [EnableAuditLogging], [AuditRetentionDays], [CreatedAt],
        [ChatLlmEnabled], [ChatLlmProvider], [ChatLlmModel], [ChatLlmEndpoint], [ChatLlmMaxTokens],
        [ChatLlmTemperature], [ChatLlmTimeoutSeconds],
        [EnableSyncNotifications], [EnablePolicyNotifications], [EnableEscalationNotifications],
        [PortalDisplayName], [PortalUrl], [ModifiedBy])
    VALUES (
        1,
        0,   -- AllowSelfRegistration
        0,   -- RequireEmailConfirmation
        1,   -- AllowExternalLogins
        8,   -- MinimumPasswordLength
        1,   -- RequireDigit
        1,   -- RequireLowercase
        1,   -- RequireUppercase
        1,   -- RequireNonAlphanumeric
        5,   -- MaxFailedAccessAttempts
        30,  -- LockoutDurationMinutes
        30,  -- SessionTimeoutMinutes
        1,   -- SlidingExpiration
        1,   -- EnableAuditLogging
        90,  -- AuditRetentionDays
        '2025-10-12T17:12:04.3702170Z',
        0,   -- ChatLlmEnabled
        N'OpenAI',
        N'gpt-3.5-turbo',
        N'https://api.openai.com/v1',
        500, -- ChatLlmMaxTokens
        0.3, -- ChatLlmTemperature
        30,  -- ChatLlmTimeoutSeconds
        1,   -- EnableSyncNotifications
        1,   -- EnablePolicyNotifications
        1,   -- EnableEscalationNotifications
        N'Identity Center',
        N'https://localhost:7001',
        N'system'  -- ModifiedBy: NOT NULL column; seed value (matches audit-column convention)
    );
    SET IDENTITY_INSERT [SystemConfigurations] OFF;
END

GO

-- =============================================
-- 6. Schedule Templates (27 templates)
-- =============================================

-- HOURLY SCHEDULES
IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '10000000-0000-0000-0000-000000000001')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('10000000-0000-0000-0000-000000000001', N'Every Hour', N'Runs at the top of every hour', N'Hourly', N'0 0 * * * ?', 1, 1, 1, N'fas fa-clock', N'#3b82f6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '10000000-0000-0000-0000-000000000002')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('10000000-0000-0000-0000-000000000002', N'Every 2 Hours', N'Runs every 2 hours starting at midnight', N'Hourly', N'0 0 0/2 * * ?', 2, 1, 1, N'fas fa-clock', N'#3b82f6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '10000000-0000-0000-0000-000000000003')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('10000000-0000-0000-0000-000000000003', N'Every 4 Hours', N'Runs every 4 hours (6 times per day)', N'Hourly', N'0 0 0/4 * * ?', 3, 1, 1, N'fas fa-clock', N'#3b82f6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '10000000-0000-0000-0000-000000000004')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('10000000-0000-0000-0000-000000000004', N'Every 6 Hours', N'Runs every 6 hours (4 times per day)', N'Hourly', N'0 0 0/6 * * ?', 4, 1, 1, N'fas fa-clock', N'#3b82f6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '10000000-0000-0000-0000-000000000005')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('10000000-0000-0000-0000-000000000005', N'Every 8 Hours', N'Runs every 8 hours (3 times per day)', N'Hourly', N'0 0 0/8 * * ?', 5, 1, 1, N'fas fa-clock', N'#3b82f6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '10000000-0000-0000-0000-000000000006')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('10000000-0000-0000-0000-000000000006', N'Every 12 Hours (Twice Daily)', N'Runs at midnight and noon', N'Hourly', N'0 0 0,12 * * ?', 6, 1, 1, N'fas fa-clock', N'#3b82f6', '2025-11-30T18:00:00.0000000Z');

-- DAILY SCHEDULES
IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '20000000-0000-0000-0000-000000000001')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('20000000-0000-0000-0000-000000000001', N'Daily at Midnight', N'Runs every day at 12:00 AM', N'Daily', N'0 0 0 * * ?', 1, 1, 1, N'fas fa-sun', N'#10b981', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '20000000-0000-0000-0000-000000000002')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('20000000-0000-0000-0000-000000000002', N'Daily at 2 AM', N'Runs every day at 2:00 AM (recommended for low-traffic)', N'Daily', N'0 0 2 * * ?', 2, 1, 1, N'fas fa-sun', N'#10b981', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '20000000-0000-0000-0000-000000000003')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('20000000-0000-0000-0000-000000000003', N'Daily at 6 AM', N'Runs every day at 6:00 AM (before business hours)', N'Daily', N'0 0 6 * * ?', 3, 1, 1, N'fas fa-sun', N'#10b981', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '20000000-0000-0000-0000-000000000004')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('20000000-0000-0000-0000-000000000004', N'Daily at Noon', N'Runs every day at 12:00 PM', N'Daily', N'0 0 12 * * ?', 4, 1, 1, N'fas fa-sun', N'#10b981', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '20000000-0000-0000-0000-000000000005')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('20000000-0000-0000-0000-000000000005', N'Daily at 6 PM', N'Runs every day at 6:00 PM (after business hours)', N'Daily', N'0 0 18 * * ?', 5, 1, 1, N'fas fa-sun', N'#10b981', '2025-11-30T18:00:00.0000000Z');

-- WEEKLY SCHEDULES
IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '30000000-0000-0000-0000-000000000001')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('30000000-0000-0000-0000-000000000001', N'Weekly on Sunday at 2 AM', N'Runs every Sunday at 2:00 AM', N'Weekly', N'0 0 2 ? * SUN', 1, 1, 1, N'fas fa-calendar-week', N'#8b5cf6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '30000000-0000-0000-0000-000000000002')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('30000000-0000-0000-0000-000000000002', N'Weekly on Monday at 6 AM', N'Runs every Monday at 6:00 AM (start of work week)', N'Weekly', N'0 0 6 ? * MON', 2, 1, 1, N'fas fa-calendar-week', N'#8b5cf6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '30000000-0000-0000-0000-000000000003')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('30000000-0000-0000-0000-000000000003', N'Weekly on Friday at 6 PM', N'Runs every Friday at 6:00 PM (end of work week)', N'Weekly', N'0 0 18 ? * FRI', 3, 1, 1, N'fas fa-calendar-week', N'#8b5cf6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '30000000-0000-0000-0000-000000000004')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('30000000-0000-0000-0000-000000000004', N'Weekly on Saturday at 2 AM', N'Runs every Saturday at 2:00 AM', N'Weekly', N'0 0 2 ? * SAT', 4, 1, 1, N'fas fa-calendar-week', N'#8b5cf6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '30000000-0000-0000-0000-000000000005')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('30000000-0000-0000-0000-000000000005', N'Weekdays at 6 AM', N'Runs Monday through Friday at 6:00 AM', N'Weekly', N'0 0 6 ? * MON-FRI', 5, 1, 1, N'fas fa-calendar-week', N'#8b5cf6', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '30000000-0000-0000-0000-000000000006')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('30000000-0000-0000-0000-000000000006', N'Weekends at 3 AM', N'Runs Saturday and Sunday at 3:00 AM', N'Weekly', N'0 0 3 ? * SAT,SUN', 6, 1, 1, N'fas fa-calendar-week', N'#8b5cf6', '2025-11-30T18:00:00.0000000Z');

-- MONTHLY SCHEDULES
IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '40000000-0000-0000-0000-000000000001')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('40000000-0000-0000-0000-000000000001', N'Monthly on the 1st at 2 AM', N'Runs on the 1st day of every month at 2:00 AM', N'Monthly', N'0 0 2 1 * ?', 1, 1, 1, N'fas fa-calendar-alt', N'#f59e0b', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '40000000-0000-0000-0000-000000000002')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('40000000-0000-0000-0000-000000000002', N'Monthly on the 15th at 2 AM', N'Runs on the 15th day of every month at 2:00 AM', N'Monthly', N'0 0 2 15 * ?', 2, 1, 1, N'fas fa-calendar-alt', N'#f59e0b', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '40000000-0000-0000-0000-000000000003')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('40000000-0000-0000-0000-000000000003', N'Monthly on Last Day at 11 PM', N'Runs on the last day of every month at 11:00 PM', N'Monthly', N'0 0 23 L * ?', 3, 1, 1, N'fas fa-calendar-alt', N'#f59e0b', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '40000000-0000-0000-0000-000000000004')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('40000000-0000-0000-0000-000000000004', N'Twice Monthly (1st & 15th)', N'Runs on the 1st and 15th of every month at 2:00 AM', N'Monthly', N'0 0 2 1,15 * ?', 4, 1, 1, N'fas fa-calendar-alt', N'#f59e0b', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '40000000-0000-0000-0000-000000000005')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('40000000-0000-0000-0000-000000000005', N'First Monday of Month at 6 AM', N'Runs on the first Monday of every month at 6:00 AM', N'Monthly', N'0 0 6 ? * MON#1', 5, 1, 1, N'fas fa-calendar-alt', N'#f59e0b', '2025-11-30T18:00:00.0000000Z');

-- QUARTERLY SCHEDULES
IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '50000000-0000-0000-0000-000000000001')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('50000000-0000-0000-0000-000000000001', N'Quarterly (Jan, Apr, Jul, Oct) 1st at 2 AM', N'Runs on the 1st day of each quarter at 2:00 AM', N'Quarterly', N'0 0 2 1 1,4,7,10 ?', 1, 1, 1, N'fas fa-calendar-check', N'#ec4899', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '50000000-0000-0000-0000-000000000002')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('50000000-0000-0000-0000-000000000002', N'End of Quarter (Mar, Jun, Sep, Dec) Last Day', N'Runs on the last day of each quarter at 11:00 PM', N'Quarterly', N'0 0 23 L 3,6,9,12 ?', 2, 1, 1, N'fas fa-calendar-check', N'#ec4899', '2025-11-30T18:00:00.0000000Z');

-- YEARLY SCHEDULES
IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '60000000-0000-0000-0000-000000000001')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('60000000-0000-0000-0000-000000000001', N'Yearly on January 1st at 2 AM', N'Runs once a year on January 1st at 2:00 AM', N'Yearly', N'0 0 2 1 1 ?', 1, 1, 1, N'fas fa-calendar-star', N'#ef4444', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '60000000-0000-0000-0000-000000000002')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('60000000-0000-0000-0000-000000000002', N'Yearly on July 1st at 2 AM', N'Runs once a year on July 1st at 2:00 AM (mid-year)', N'Yearly', N'0 0 2 1 7 ?', 2, 1, 1, N'fas fa-calendar-star', N'#ef4444', '2025-11-30T18:00:00.0000000Z');

IF NOT EXISTS (SELECT 1 FROM [ScheduleTemplates] WHERE [Id] = '60000000-0000-0000-0000-000000000003')
    INSERT INTO [ScheduleTemplates] ([Id], [Name], [Description], [Category], [CronExpression], [SortOrder], [IsSystem], [IsActive], [IconClass], [Color], [CreatedAt])
    VALUES ('60000000-0000-0000-0000-000000000003', N'Yearly on December 31st at 11 PM', N'Runs once a year on December 31st at 11:00 PM (year-end)', N'Yearly', N'0 0 23 31 12 ?', 3, 1, 1, N'fas fa-calendar-star', N'#ef4444', '2025-11-30T18:00:00.0000000Z');

GO

PRINT 'Schema version 5 applied - default data seeded';
