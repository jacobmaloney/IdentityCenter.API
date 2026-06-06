-- ============================================
-- Create Dev Center Script Tables
-- Run this on IdentityCenter13 database
-- ============================================

USE IdentityCenter13;
GO

-- Check if tables already exist
IF OBJECT_ID('SyncProcessingScripts', 'U') IS NOT NULL
BEGIN
    PRINT 'SyncProcessingScripts table already exists. Skipping creation.';
END
ELSE
BEGIN
    PRINT 'Creating SyncProcessingScripts table...';

    CREATE TABLE SyncProcessingScripts (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name NVARCHAR(200) NOT NULL,
        Description NVARCHAR(2000) NULL,
        ScriptType NVARCHAR(50) NOT NULL DEFAULT 'PostProcessing',
        ScriptCode NVARCHAR(MAX) NOT NULL,
        IsSystem BIT NOT NULL DEFAULT 0,
        IsEnabled BIT NOT NULL DEFAULT 1,
        Version INT NOT NULL DEFAULT 1,
        Category NVARCHAR(100) NOT NULL DEFAULT 'Custom',
        CompilationStatus NVARCHAR(50) NOT NULL DEFAULT 'NotCompiled',
        CompilationError NVARCHAR(MAX) NULL,
        LastCompiledAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(256) NOT NULL DEFAULT 'System',
        ModifiedAt DATETIME2 NULL,
        ModifiedBy NVARCHAR(256) NULL,
        CopiedFromScriptId UNIQUEIDENTIFIER NULL
    );

    -- Indexes
    CREATE INDEX IX_SyncProcessingScripts_Name ON SyncProcessingScripts(Name);
    CREATE INDEX IX_SyncProcessingScripts_ScriptType ON SyncProcessingScripts(ScriptType);
    CREATE INDEX IX_SyncProcessingScripts_Category ON SyncProcessingScripts(Category);
    CREATE INDEX IX_SyncProcessingScripts_IsSystem ON SyncProcessingScripts(IsSystem);
    CREATE INDEX IX_SyncProcessingScripts_IsEnabled ON SyncProcessingScripts(IsEnabled);

    PRINT 'SyncProcessingScripts table created successfully.';
END
GO

-- Create SyncStepScripts join table
IF OBJECT_ID('SyncStepScripts', 'U') IS NOT NULL
BEGIN
    PRINT 'SyncStepScripts table already exists. Skipping creation.';
END
ELSE
BEGIN
    PRINT 'Creating SyncStepScripts table...';

    CREATE TABLE SyncStepScripts (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        SyncStepId UNIQUEIDENTIFIER NOT NULL,
        ScriptId UNIQUEIDENTIFIER NOT NULL,
        ExecutionPhase NVARCHAR(50) NOT NULL DEFAULT 'PostProcessing',
        ExecutionOrder INT NOT NULL DEFAULT 0,
        IsEnabled BIT NOT NULL DEFAULT 1,
        ParameterOverrides NVARCHAR(MAX) NULL,

        CONSTRAINT FK_SyncStepScripts_SyncStep FOREIGN KEY (SyncStepId) REFERENCES SyncSteps(Id) ON DELETE CASCADE,
        CONSTRAINT FK_SyncStepScripts_Script FOREIGN KEY (ScriptId) REFERENCES SyncProcessingScripts(Id) ON DELETE CASCADE
    );

    -- Indexes
    CREATE INDEX IX_SyncStepScripts_SyncStepId ON SyncStepScripts(SyncStepId);
    CREATE INDEX IX_SyncStepScripts_ScriptId ON SyncStepScripts(ScriptId);
    CREATE INDEX IX_SyncStepScripts_StepPhaseOrder ON SyncStepScripts(SyncStepId, ExecutionPhase, ExecutionOrder);

    PRINT 'SyncStepScripts table created successfully.';
END
GO

-- Create SyncScriptExecutions audit table
IF OBJECT_ID('SyncScriptExecutions', 'U') IS NOT NULL
BEGIN
    PRINT 'SyncScriptExecutions table already exists. Skipping creation.';
END
ELSE
BEGIN
    PRINT 'Creating SyncScriptExecutions table...';

    CREATE TABLE SyncScriptExecutions (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        SyncStepRunId UNIQUEIDENTIFIER NOT NULL,
        ScriptId UNIQUEIDENTIFIER NOT NULL,
        ExecutionPhase NVARCHAR(50) NOT NULL DEFAULT 'PostProcessing',
        Status NVARCHAR(50) NOT NULL DEFAULT 'Success',
        StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt DATETIME2 NULL,
        DurationMs INT NULL,
        ObjectsProcessed INT NOT NULL DEFAULT 0,
        ObjectsModified INT NOT NULL DEFAULT 0,
        IdentitiesCreated INT NOT NULL DEFAULT 0,
        ManagersResolved INT NOT NULL DEFAULT 0,
        ErrorMessage NVARCHAR(MAX) NULL,
        OutputLog NVARCHAR(MAX) NULL,

        CONSTRAINT FK_SyncScriptExecutions_StepRun FOREIGN KEY (SyncStepRunId) REFERENCES SyncStepRuns(Id) ON DELETE CASCADE,
        CONSTRAINT FK_SyncScriptExecutions_Script FOREIGN KEY (ScriptId) REFERENCES SyncProcessingScripts(Id) ON DELETE CASCADE
    );

    -- Indexes
    CREATE INDEX IX_SyncScriptExecutions_SyncStepRunId ON SyncScriptExecutions(SyncStepRunId);
    CREATE INDEX IX_SyncScriptExecutions_ScriptId ON SyncScriptExecutions(ScriptId);
    CREATE INDEX IX_SyncScriptExecutions_StartedAt ON SyncScriptExecutions(StartedAt);
    CREATE INDEX IX_SyncScriptExecutions_Status ON SyncScriptExecutions(Status);

    PRINT 'SyncScriptExecutions table created successfully.';
END
GO

-- Add migration record to EF migrations history
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251221000000_AddDevCenterScripts')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20251221000000_AddDevCenterScripts', '8.0.0');
    PRINT 'Migration record added to __EFMigrationsHistory.';
END
GO

-- Verify results
SELECT 'Tables Created:' AS Info;
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%Script%' ORDER BY TABLE_NAME;

PRINT 'Dev Center script tables created successfully!';
GO
