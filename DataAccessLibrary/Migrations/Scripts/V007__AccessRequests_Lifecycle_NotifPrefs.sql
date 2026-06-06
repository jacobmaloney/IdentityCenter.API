-- V007: Add AccessRequests, LifecycleTemplates, LifecycleEvents, and NotificationPreferences tables
-- Supports: Self-Service Access Catalog (Phase 2), Lifecycle Management (Phase 3), Notification Preferences (Phase 6)

-- ============================================================
-- Access Requests (model exists in AccessManagementModels.cs)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AccessRequests')
BEGIN
    CREATE TABLE AccessRequests (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        RequesterId NVARCHAR(450) NOT NULL,
        RequesterName NVARCHAR(256),
        ResourceType NVARCHAR(100) NOT NULL,
        ResourceId NVARCHAR(256) NOT NULL,
        ResourceName NVARCHAR(256),
        Justification NVARCHAR(MAX),
        DurationDays INT NOT NULL DEFAULT 0,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
        ApproverId NVARCHAR(450),
        ApprovedAt DATETIME2,
        ApprovalComments NVARCHAR(500),
        ExpiresAt DATETIME2,
        WorkflowInstanceId UNIQUEIDENTIFIER,
        RequestedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE NONCLUSTERED INDEX IX_AccessRequests_RequesterId ON AccessRequests (RequesterId);
    CREATE NONCLUSTERED INDEX IX_AccessRequests_Status ON AccessRequests (Status) INCLUDE (RequesterId, ResourceType, ResourceName);
    CREATE NONCLUSTERED INDEX IX_AccessRequests_ResourceId ON AccessRequests (ResourceId, ResourceType);
    CREATE NONCLUSTERED INDEX IX_AccessRequests_CreatedAt ON AccessRequests (CreatedAt DESC);
END;

-- ============================================================
-- Lifecycle Templates (Joiner/Mover/Leaver)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LifecycleTemplates')
BEGIN
    CREATE TABLE LifecycleTemplates (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        Name NVARCHAR(256) NOT NULL,
        Type NVARCHAR(50) NOT NULL, -- Joiner, Mover, Leaver
        TriggerType NVARCHAR(50) NOT NULL DEFAULT 'Manual', -- Manual, HRFeed, Schedule
        Description NVARCHAR(MAX),
        Actions NVARCHAR(MAX), -- JSON array of actions
        WorkflowId UNIQUEIDENTIFIER,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedBy NVARCHAR(256),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE NONCLUSTERED INDEX IX_LifecycleTemplates_Type ON LifecycleTemplates (Type) WHERE IsActive = 1;
    CREATE NONCLUSTERED INDEX IX_LifecycleTemplates_IsActive ON LifecycleTemplates (IsActive);
END;

-- ============================================================
-- Lifecycle Events (execution log)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LifecycleEvents')
BEGIN
    CREATE TABLE LifecycleEvents (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        TemplateId UNIQUEIDENTIFIER,
        PersonId UNIQUEIDENTIFIER,
        ObjectId UNIQUEIDENTIFIER,
        PersonName NVARCHAR(256),
        EventType NVARCHAR(50) NOT NULL, -- Joiner, Mover, Leaver
        Status NVARCHAR(50) NOT NULL DEFAULT 'Pending', -- Pending, InProgress, Completed, Failed
        TriggeredBy NVARCHAR(256),
        StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt DATETIME2,
        Actions NVARCHAR(MAX), -- JSON of executed actions with results
        ErrorMessage NVARCHAR(MAX),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE NONCLUSTERED INDEX IX_LifecycleEvents_EventType ON LifecycleEvents (EventType, Status);
    CREATE NONCLUSTERED INDEX IX_LifecycleEvents_PersonId ON LifecycleEvents (PersonId);
    CREATE NONCLUSTERED INDEX IX_LifecycleEvents_Status ON LifecycleEvents (Status);
    CREATE NONCLUSTERED INDEX IX_LifecycleEvents_StartedAt ON LifecycleEvents (StartedAt DESC);
    CREATE NONCLUSTERED INDEX IX_LifecycleEvents_TemplateId ON LifecycleEvents (TemplateId);
END;

-- ============================================================
-- Notification Preferences (per-user settings)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'NotificationPreferences')
BEGIN
    CREATE TABLE NotificationPreferences (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        UserId NVARCHAR(450) NOT NULL,
        Category NVARCHAR(100) NOT NULL, -- Sync, Compliance, AccessReview, System, Security
        IsEnabled BIT NOT NULL DEFAULT 1,
        DigestMode NVARCHAR(50) NOT NULL DEFAULT 'Immediate', -- Immediate, Daily, Weekly
        EmailEnabled BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_NotifPref_User_Category UNIQUE (UserId, Category)
    );

    CREATE NONCLUSTERED INDEX IX_NotificationPreferences_UserId ON NotificationPreferences (UserId);
END;
