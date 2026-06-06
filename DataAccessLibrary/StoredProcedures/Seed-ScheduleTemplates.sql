-- Seed Schedule Templates
-- Run this script to create the ScheduleTemplates table and populate built-in schedules

-- Create table if not exists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ScheduleTemplates')
BEGIN
    CREATE TABLE ScheduleTemplates (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        Name NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500) NULL,
        Category NVARCHAR(50) NOT NULL,
        CronExpression NVARCHAR(100) NOT NULL,
        SortOrder INT NOT NULL DEFAULT 0,
        IsSystem BIT NOT NULL DEFAULT 1,
        IsActive BIT NOT NULL DEFAULT 1,
        IconClass NVARCHAR(50) NULL,
        Color NVARCHAR(20) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'Created ScheduleTemplates table';
END
GO

-- Clear existing system templates and re-seed
DELETE FROM ScheduleTemplates WHERE IsSystem = 1;
PRINT 'Cleared existing system templates';

-- HOURLY SCHEDULES
INSERT INTO ScheduleTemplates (Id, Name, Description, Category, CronExpression, SortOrder, IsSystem, IsActive, IconClass, Color, CreatedAt)
VALUES
    ('10000000-0000-0000-0000-000000000001', 'Every Hour', 'Runs at the top of every hour', 'Hourly', '0 0 * * * ?', 1, 1, 1, 'fas fa-clock', '#3b82f6', GETUTCDATE()),
    ('10000000-0000-0000-0000-000000000002', 'Every 2 Hours', 'Runs every 2 hours starting at midnight', 'Hourly', '0 0 0/2 * * ?', 2, 1, 1, 'fas fa-clock', '#3b82f6', GETUTCDATE()),
    ('10000000-0000-0000-0000-000000000003', 'Every 4 Hours', 'Runs every 4 hours (6 times per day)', 'Hourly', '0 0 0/4 * * ?', 3, 1, 1, 'fas fa-clock', '#3b82f6', GETUTCDATE()),
    ('10000000-0000-0000-0000-000000000004', 'Every 6 Hours', 'Runs every 6 hours (4 times per day)', 'Hourly', '0 0 0/6 * * ?', 4, 1, 1, 'fas fa-clock', '#3b82f6', GETUTCDATE()),
    ('10000000-0000-0000-0000-000000000005', 'Every 8 Hours', 'Runs every 8 hours (3 times per day)', 'Hourly', '0 0 0/8 * * ?', 5, 1, 1, 'fas fa-clock', '#3b82f6', GETUTCDATE()),
    ('10000000-0000-0000-0000-000000000006', 'Every 12 Hours (Twice Daily)', 'Runs at midnight and noon', 'Hourly', '0 0 0,12 * * ?', 6, 1, 1, 'fas fa-clock', '#3b82f6', GETUTCDATE());
PRINT 'Inserted Hourly schedules';

-- DAILY SCHEDULES
INSERT INTO ScheduleTemplates (Id, Name, Description, Category, CronExpression, SortOrder, IsSystem, IsActive, IconClass, Color, CreatedAt)
VALUES
    ('20000000-0000-0000-0000-000000000001', 'Daily at Midnight', 'Runs every day at 12:00 AM', 'Daily', '0 0 0 * * ?', 1, 1, 1, 'fas fa-sun', '#10b981', GETUTCDATE()),
    ('20000000-0000-0000-0000-000000000002', 'Daily at 2 AM', 'Runs every day at 2:00 AM (recommended for low-traffic)', 'Daily', '0 0 2 * * ?', 2, 1, 1, 'fas fa-sun', '#10b981', GETUTCDATE()),
    ('20000000-0000-0000-0000-000000000003', 'Daily at 6 AM', 'Runs every day at 6:00 AM (before business hours)', 'Daily', '0 0 6 * * ?', 3, 1, 1, 'fas fa-sun', '#10b981', GETUTCDATE()),
    ('20000000-0000-0000-0000-000000000004', 'Daily at Noon', 'Runs every day at 12:00 PM', 'Daily', '0 0 12 * * ?', 4, 1, 1, 'fas fa-sun', '#10b981', GETUTCDATE()),
    ('20000000-0000-0000-0000-000000000005', 'Daily at 6 PM', 'Runs every day at 6:00 PM (after business hours)', 'Daily', '0 0 18 * * ?', 5, 1, 1, 'fas fa-sun', '#10b981', GETUTCDATE());
PRINT 'Inserted Daily schedules';

-- WEEKLY SCHEDULES
INSERT INTO ScheduleTemplates (Id, Name, Description, Category, CronExpression, SortOrder, IsSystem, IsActive, IconClass, Color, CreatedAt)
VALUES
    ('30000000-0000-0000-0000-000000000001', 'Weekly on Sunday at 2 AM', 'Runs every Sunday at 2:00 AM', 'Weekly', '0 0 2 ? * SUN', 1, 1, 1, 'fas fa-calendar-week', '#8b5cf6', GETUTCDATE()),
    ('30000000-0000-0000-0000-000000000002', 'Weekly on Monday at 6 AM', 'Runs every Monday at 6:00 AM (start of work week)', 'Weekly', '0 0 6 ? * MON', 2, 1, 1, 'fas fa-calendar-week', '#8b5cf6', GETUTCDATE()),
    ('30000000-0000-0000-0000-000000000003', 'Weekly on Friday at 6 PM', 'Runs every Friday at 6:00 PM (end of work week)', 'Weekly', '0 0 18 ? * FRI', 3, 1, 1, 'fas fa-calendar-week', '#8b5cf6', GETUTCDATE()),
    ('30000000-0000-0000-0000-000000000004', 'Weekly on Saturday at 2 AM', 'Runs every Saturday at 2:00 AM', 'Weekly', '0 0 2 ? * SAT', 4, 1, 1, 'fas fa-calendar-week', '#8b5cf6', GETUTCDATE()),
    ('30000000-0000-0000-0000-000000000005', 'Weekdays at 6 AM', 'Runs Monday through Friday at 6:00 AM', 'Weekly', '0 0 6 ? * MON-FRI', 5, 1, 1, 'fas fa-calendar-week', '#8b5cf6', GETUTCDATE()),
    ('30000000-0000-0000-0000-000000000006', 'Weekends at 2 AM', 'Runs Saturday and Sunday at 2:00 AM', 'Weekly', '0 0 2 ? * SAT,SUN', 6, 1, 1, 'fas fa-calendar-week', '#8b5cf6', GETUTCDATE());
PRINT 'Inserted Weekly schedules';

-- MONTHLY SCHEDULES
INSERT INTO ScheduleTemplates (Id, Name, Description, Category, CronExpression, SortOrder, IsSystem, IsActive, IconClass, Color, CreatedAt)
VALUES
    ('40000000-0000-0000-0000-000000000001', 'Monthly on the 1st at 2 AM', 'Runs on the 1st of each month at 2:00 AM', 'Monthly', '0 0 2 1 * ?', 1, 1, 1, 'fas fa-calendar-alt', '#f59e0b', GETUTCDATE()),
    ('40000000-0000-0000-0000-000000000002', 'Monthly on the 15th at 2 AM', 'Runs on the 15th of each month at 2:00 AM', 'Monthly', '0 0 2 15 * ?', 2, 1, 1, 'fas fa-calendar-alt', '#f59e0b', GETUTCDATE()),
    ('40000000-0000-0000-0000-000000000003', 'Monthly Last Day at 6 PM', 'Runs on the last day of each month at 6:00 PM', 'Monthly', '0 0 18 L * ?', 3, 1, 1, 'fas fa-calendar-alt', '#f59e0b', GETUTCDATE()),
    ('40000000-0000-0000-0000-000000000004', 'Twice Monthly (1st and 15th)', 'Runs on the 1st and 15th at 2:00 AM', 'Monthly', '0 0 2 1,15 * ?', 4, 1, 1, 'fas fa-calendar-alt', '#f59e0b', GETUTCDATE()),
    ('40000000-0000-0000-0000-000000000005', 'Monthly First Monday at 6 AM', 'Runs on the first Monday of each month at 6:00 AM', 'Monthly', '0 0 6 ? * 2#1', 5, 1, 1, 'fas fa-calendar-alt', '#f59e0b', GETUTCDATE());
PRINT 'Inserted Monthly schedules';

-- QUARTERLY SCHEDULES
INSERT INTO ScheduleTemplates (Id, Name, Description, Category, CronExpression, SortOrder, IsSystem, IsActive, IconClass, Color, CreatedAt)
VALUES
    ('50000000-0000-0000-0000-000000000001', 'Quarterly Start (Jan, Apr, Jul, Oct 1st)', 'Runs on the first day of each quarter at 2:00 AM', 'Quarterly', '0 0 2 1 1,4,7,10 ?', 1, 1, 1, 'fas fa-calendar', '#ef4444', GETUTCDATE()),
    ('50000000-0000-0000-0000-000000000002', 'Quarterly End (Mar, Jun, Sep, Dec last day)', 'Runs on the last day of each quarter at 6:00 PM', 'Quarterly', '0 0 18 L 3,6,9,12 ?', 2, 1, 1, 'fas fa-calendar', '#ef4444', GETUTCDATE());
PRINT 'Inserted Quarterly schedules';

-- YEARLY SCHEDULES
INSERT INTO ScheduleTemplates (Id, Name, Description, Category, CronExpression, SortOrder, IsSystem, IsActive, IconClass, Color, CreatedAt)
VALUES
    ('60000000-0000-0000-0000-000000000001', 'Yearly on January 1st at 2 AM', 'Runs once per year on January 1st at 2:00 AM', 'Yearly', '0 0 2 1 1 ?', 1, 1, 1, 'fas fa-calendar-check', '#6366f1', GETUTCDATE()),
    ('60000000-0000-0000-0000-000000000002', 'Yearly on July 1st at 2 AM', 'Runs once per year on July 1st at 2:00 AM (mid-year)', 'Yearly', '0 0 2 1 7 ?', 2, 1, 1, 'fas fa-calendar-check', '#6366f1', GETUTCDATE()),
    ('60000000-0000-0000-0000-000000000003', 'Yearly on December 31st at 6 PM', 'Runs once per year on December 31st at 6:00 PM (year-end)', 'Yearly', '0 0 18 31 12 ?', 3, 1, 1, 'fas fa-calendar-check', '#6366f1', GETUTCDATE());
PRINT 'Inserted Yearly schedules';

-- Verify
SELECT Category, COUNT(*) as Count FROM ScheduleTemplates GROUP BY Category ORDER BY
    CASE Category
        WHEN 'Hourly' THEN 1
        WHEN 'Daily' THEN 2
        WHEN 'Weekly' THEN 3
        WHEN 'Monthly' THEN 4
        WHEN 'Quarterly' THEN 5
        WHEN 'Yearly' THEN 6
    END;

SELECT COUNT(*) as TotalTemplates FROM ScheduleTemplates;
PRINT 'Schedule templates seeded successfully!';
