-- Migration: Add Notification Scheduling Fields
-- Date: 2026-01-02
-- Description: Adds fields for tracking notification history and configurable reminder schedules

-- ============================================
-- CompliancePolicyViolation - Notification Tracking
-- ============================================

-- First notification sent timestamp
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyViolations') AND name = 'FirstNotificationSentAt')
BEGIN
    ALTER TABLE CompliancePolicyViolations ADD FirstNotificationSentAt DATETIME2 NULL;
END

-- Last notification sent timestamp
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyViolations') AND name = 'LastNotificationSentAt')
BEGIN
    ALTER TABLE CompliancePolicyViolations ADD LastNotificationSentAt DATETIME2 NULL;
END

-- Count of notifications sent for this violation
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyViolations') AND name = 'NotificationCount')
BEGIN
    ALTER TABLE CompliancePolicyViolations ADD NotificationCount INT NOT NULL DEFAULT 0;
END

-- Next scheduled reminder time
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicyViolations') AND name = 'NextReminderAt')
BEGIN
    ALTER TABLE CompliancePolicyViolations ADD NextReminderAt DATETIME2 NULL;
END

-- ============================================
-- CompliancePolicies - Reminder Configuration
-- ============================================

-- Days to wait before first notification (0 = immediate)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'FirstReminderDelayDays')
BEGIN
    ALTER TABLE CompliancePolicies ADD FirstReminderDelayDays INT NOT NULL DEFAULT 0;
END

-- Days between subsequent reminders
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'ReminderIntervalDays')
BEGIN
    ALTER TABLE CompliancePolicies ADD ReminderIntervalDays INT NOT NULL DEFAULT 5;
END

-- Maximum number of reminders (NULL = unlimited)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'MaxReminderCount')
BEGIN
    ALTER TABLE CompliancePolicies ADD MaxReminderCount INT NULL DEFAULT 3;
END

-- Whether to use reminder schedule vs sending every time
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CompliancePolicies') AND name = 'EnableReminderSchedule')
BEGIN
    ALTER TABLE CompliancePolicies ADD EnableReminderSchedule BIT NOT NULL DEFAULT 1;
END

-- ============================================
-- Set default values for existing policies
-- ============================================
UPDATE CompliancePolicies
SET FirstReminderDelayDays = 0,
    ReminderIntervalDays = 5,
    MaxReminderCount = 3,
    EnableReminderSchedule = 1
WHERE FirstReminderDelayDays IS NULL OR ReminderIntervalDays IS NULL;

PRINT 'Notification scheduling fields added successfully';
