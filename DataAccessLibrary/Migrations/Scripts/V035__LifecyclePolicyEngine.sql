-- V035: Lifecycle Policy Engine - Tag-based policy binding, auto-tag rules, provisioning retry queue
-- All changes are additive - no breaking changes to existing data.
--
-- Every DDL statement is guarded so a partial-apply crash leaves the migration safely re-runnable.

-- Add new columns to LifecycleTemplates for tag-based policy binding and provisioning config
IF COL_LENGTH(N'LifecycleTemplates', N'TagIds') IS NULL
BEGIN
    ALTER TABLE LifecycleTemplates ADD TagIds NVARCHAR(MAX) NULL;
END
GO

IF COL_LENGTH(N'LifecycleTemplates', N'TargetConnectionId') IS NULL
BEGIN
    ALTER TABLE LifecycleTemplates ADD TargetConnectionId UNIQUEIDENTIFIER NULL;
END
GO

IF COL_LENGTH(N'LifecycleTemplates', N'TargetOU') IS NULL
BEGIN
    ALTER TABLE LifecycleTemplates ADD TargetOU NVARCHAR(1000) NULL;
END
GO

IF COL_LENGTH(N'LifecycleTemplates', N'ProvisioningConfig') IS NULL
BEGIN
    ALTER TABLE LifecycleTemplates ADD ProvisioningConfig NVARCHAR(MAX) NULL;
END
GO

IF COL_LENGTH(N'LifecycleTemplates', N'Priority') IS NULL
BEGIN
    ALTER TABLE LifecycleTemplates ADD Priority INT NOT NULL DEFAULT 100;
END
GO

-- Join table for indexed tag-template lookups
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'LifecycleTemplateTags')
BEGIN
    CREATE TABLE LifecycleTemplateTags (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        TemplateId UNIQUEIDENTIFIER NOT NULL REFERENCES LifecycleTemplates(Id) ON DELETE CASCADE,
        TagId UNIQUEIDENTIFIER NOT NULL REFERENCES Tags(Id) ON DELETE CASCADE,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LifecycleTemplateTags_Unique' AND object_id = OBJECT_ID(N'LifecycleTemplateTags'))
BEGIN
    CREATE UNIQUE INDEX IX_LifecycleTemplateTags_Unique ON LifecycleTemplateTags (TemplateId, TagId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LifecycleTemplateTags_TagId' AND object_id = OBJECT_ID(N'LifecycleTemplateTags'))
BEGIN
    CREATE INDEX IX_LifecycleTemplateTags_TagId ON LifecycleTemplateTags (TagId);
END
GO

-- Auto-tag rules: assign tags to identities based on field values
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'IdentityTagRules')
BEGIN
    CREATE TABLE IdentityTagRules (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        TagId UNIQUEIDENTIFIER NOT NULL REFERENCES Tags(Id) ON DELETE CASCADE,
        FieldName NVARCHAR(200) NOT NULL,
        Operator NVARCHAR(50) NOT NULL DEFAULT 'Equals',
        FieldValue NVARCHAR(500) NULL,
        IsEnabled BIT NOT NULL DEFAULT 1,
        Priority INT NOT NULL DEFAULT 100,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(256) NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IdentityTagRules_TagId' AND object_id = OBJECT_ID(N'IdentityTagRules'))
BEGIN
    CREATE INDEX IX_IdentityTagRules_TagId ON IdentityTagRules (TagId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IdentityTagRules_Enabled' AND object_id = OBJECT_ID(N'IdentityTagRules'))
BEGIN
    CREATE INDEX IX_IdentityTagRules_Enabled ON IdentityTagRules (IsEnabled) WHERE IsEnabled = 1;
END
GO

-- Durable provisioning retry queue
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'ProvisioningTasks')
BEGIN
    CREATE TABLE ProvisioningTasks (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        LifecycleEventId UNIQUEIDENTIFIER NOT NULL,
        TemplateId UNIQUEIDENTIFIER NULL,
        IdentityId UNIQUEIDENTIFIER NOT NULL,
        ObjectId UNIQUEIDENTIFIER NULL,
        ActionType NVARCHAR(100) NOT NULL,
        ActionConfig NVARCHAR(MAX) NULL,
        TargetConnectionId UNIQUEIDENTIFIER NULL,
        TargetOU NVARCHAR(1000) NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
        AttemptCount INT NOT NULL DEFAULT 0,
        MaxAttempts INT NOT NULL DEFAULT 5,
        LastAttemptAt DATETIME2 NULL,
        NextRetryAt DATETIME2 NULL,
        LastError NVARCHAR(MAX) NULL,
        ResultData NVARCHAR(MAX) NULL,
        CompletedAt DATETIME2 NULL,
        CreatedBy NVARCHAR(256) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProvisioningTasks_Status_NextRetry' AND object_id = OBJECT_ID(N'ProvisioningTasks'))
BEGIN
    CREATE INDEX IX_ProvisioningTasks_Status_NextRetry ON ProvisioningTasks (Status, NextRetryAt) WHERE Status IN ('Pending','Failed','ManualRetry');
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProvisioningTasks_IdentityId' AND object_id = OBJECT_ID(N'ProvisioningTasks'))
BEGIN
    CREATE INDEX IX_ProvisioningTasks_IdentityId ON ProvisioningTasks (IdentityId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProvisioningTasks_LifecycleEventId' AND object_id = OBJECT_ID(N'ProvisioningTasks'))
BEGIN
    CREATE INDEX IX_ProvisioningTasks_LifecycleEventId ON ProvisioningTasks (LifecycleEventId);
END
GO
