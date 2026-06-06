-- ============================================================================
-- Dev Center Scripts Migration
-- Creates tables for processing scripts, step-script associations, and execution logs
-- Run this on IdentityCenter database
-- ============================================================================

USE IdentityCenter13;
GO

PRINT 'Starting Dev Center Scripts migration...';
PRINT '';

-- ============================================================================
-- 1. SyncProcessingScripts - Stores script definitions
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncProcessingScripts')
BEGIN
    PRINT 'Creating SyncProcessingScripts table...';

    CREATE TABLE SyncProcessingScripts (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Name NVARCHAR(200) NOT NULL,
        Description NVARCHAR(2000) NULL,
        ScriptType NVARCHAR(50) NOT NULL DEFAULT 'PostProcessing', -- 'PreProcessing' | 'PostProcessing'
        ScriptCode NVARCHAR(MAX) NOT NULL,
        IsSystem BIT NOT NULL DEFAULT 0,
        IsEnabled BIT NOT NULL DEFAULT 1,
        Version INT NOT NULL DEFAULT 1,
        Category NVARCHAR(100) NOT NULL DEFAULT 'Custom', -- 'Attributes' | 'Identity' | 'Manager' | 'Groups' | 'Custom'
        CompilationStatus NVARCHAR(50) NOT NULL DEFAULT 'NotCompiled', -- 'NotCompiled' | 'Success' | 'Error'
        CompilationError NVARCHAR(MAX) NULL,
        LastCompiledAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(256) NOT NULL DEFAULT 'System',
        ModifiedAt DATETIME2 NULL,
        ModifiedBy NVARCHAR(256) NULL,
        CopiedFromScriptId UNIQUEIDENTIFIER NULL
    );

    PRINT 'Created SyncProcessingScripts table.';
END
ELSE
BEGIN
    PRINT 'SyncProcessingScripts table already exists.';
END
GO

-- ============================================================================
-- 2. SyncStepScripts - Links scripts to sync steps (many-to-many)
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncStepScripts')
BEGIN
    PRINT 'Creating SyncStepScripts table...';

    CREATE TABLE SyncStepScripts (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        SyncStepId UNIQUEIDENTIFIER NOT NULL,
        ScriptId UNIQUEIDENTIFIER NOT NULL,
        ExecutionPhase NVARCHAR(50) NOT NULL DEFAULT 'PostProcessing', -- 'PreProcessing' | 'PostProcessing'
        ExecutionOrder INT NOT NULL DEFAULT 0,
        IsEnabled BIT NOT NULL DEFAULT 1,
        ParameterOverrides NVARCHAR(MAX) NULL, -- JSON for step-specific parameter overrides

        CONSTRAINT FK_SyncStepScripts_SyncStep FOREIGN KEY (SyncStepId)
            REFERENCES SyncSteps(Id) ON DELETE CASCADE,
        CONSTRAINT FK_SyncStepScripts_Script FOREIGN KEY (ScriptId)
            REFERENCES SyncProcessingScripts(Id) ON DELETE CASCADE
    );

    PRINT 'Created SyncStepScripts table.';
END
ELSE
BEGIN
    PRINT 'SyncStepScripts table already exists.';
END
GO

-- ============================================================================
-- 3. SyncScriptExecutions - Audit trail for script executions
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SyncScriptExecutions')
BEGIN
    PRINT 'Creating SyncScriptExecutions table...';

    CREATE TABLE SyncScriptExecutions (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        SyncStepRunId UNIQUEIDENTIFIER NOT NULL,
        ScriptId UNIQUEIDENTIFIER NOT NULL,
        ExecutionPhase NVARCHAR(50) NOT NULL DEFAULT 'PostProcessing',
        Status NVARCHAR(50) NOT NULL DEFAULT 'Success', -- 'Success' | 'Error' | 'Skipped' | 'Timeout' | 'Running'
        StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt DATETIME2 NULL,
        DurationMs INT NULL,
        ObjectsProcessed INT NOT NULL DEFAULT 0,
        ObjectsModified INT NOT NULL DEFAULT 0,
        IdentitiesCreated INT NOT NULL DEFAULT 0,
        ManagersResolved INT NOT NULL DEFAULT 0,
        ErrorMessage NVARCHAR(MAX) NULL,
        OutputLog NVARCHAR(MAX) NULL, -- JSON array of log entries

        CONSTRAINT FK_SyncScriptExecutions_StepRun FOREIGN KEY (SyncStepRunId)
            REFERENCES SyncStepRuns(Id) ON DELETE CASCADE,
        CONSTRAINT FK_SyncScriptExecutions_Script FOREIGN KEY (ScriptId)
            REFERENCES SyncProcessingScripts(Id) ON DELETE NO ACTION
    );

    PRINT 'Created SyncScriptExecutions table.';
END
ELSE
BEGIN
    PRINT 'SyncScriptExecutions table already exists.';
END
GO

-- ============================================================================
-- 4. Create indexes for performance
-- ============================================================================
PRINT 'Creating indexes...';

-- SyncProcessingScripts indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_Name')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncProcessingScripts_Name
    ON SyncProcessingScripts(Name);
    PRINT 'Created IX_SyncProcessingScripts_Name';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_ScriptType')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncProcessingScripts_ScriptType
    ON SyncProcessingScripts(ScriptType);
    PRINT 'Created IX_SyncProcessingScripts_ScriptType';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_Category')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncProcessingScripts_Category
    ON SyncProcessingScripts(Category);
    PRINT 'Created IX_SyncProcessingScripts_Category';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_IsSystem')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncProcessingScripts_IsSystem
    ON SyncProcessingScripts(IsSystem);
    PRINT 'Created IX_SyncProcessingScripts_IsSystem';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncProcessingScripts_IsEnabled')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncProcessingScripts_IsEnabled
    ON SyncProcessingScripts(IsEnabled);
    PRINT 'Created IX_SyncProcessingScripts_IsEnabled';
END

-- SyncStepScripts indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepScripts_SyncStepId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncStepScripts_SyncStepId
    ON SyncStepScripts(SyncStepId);
    PRINT 'Created IX_SyncStepScripts_SyncStepId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepScripts_ScriptId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncStepScripts_ScriptId
    ON SyncStepScripts(ScriptId);
    PRINT 'Created IX_SyncStepScripts_ScriptId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncStepScripts_StepPhaseOrder')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncStepScripts_StepPhaseOrder
    ON SyncStepScripts(SyncStepId, ExecutionPhase, ExecutionOrder);
    PRINT 'Created IX_SyncStepScripts_StepPhaseOrder';
END

-- SyncScriptExecutions indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncScriptExecutions_SyncStepRunId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncScriptExecutions_SyncStepRunId
    ON SyncScriptExecutions(SyncStepRunId);
    PRINT 'Created IX_SyncScriptExecutions_SyncStepRunId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncScriptExecutions_ScriptId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncScriptExecutions_ScriptId
    ON SyncScriptExecutions(ScriptId);
    PRINT 'Created IX_SyncScriptExecutions_ScriptId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncScriptExecutions_StartedAt')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncScriptExecutions_StartedAt
    ON SyncScriptExecutions(StartedAt DESC);
    PRINT 'Created IX_SyncScriptExecutions_StartedAt';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SyncScriptExecutions_Status')
BEGIN
    CREATE NONCLUSTERED INDEX IX_SyncScriptExecutions_Status
    ON SyncScriptExecutions(Status);
    PRINT 'Created IX_SyncScriptExecutions_Status';
END
GO

-- ============================================================================
-- 5. Seed system default scripts
-- ============================================================================
PRINT 'Seeding system default scripts...';

-- ConvertBinaryValues (PreProcessing)
IF NOT EXISTS (SELECT 1 FROM SyncProcessingScripts WHERE Name = 'ConvertBinaryValues' AND IsSystem = 1)
BEGIN
    INSERT INTO SyncProcessingScripts (
        Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled,
        Version, Category, CompilationStatus, CreatedAt, CreatedBy
    )
    VALUES (
        '11111111-1111-1111-1111-111111111111',
        'ConvertBinaryValues',
        'Converts objectGUID and objectSid from byte[] to readable strings. Uses case-insensitive attribute matching to handle LDAP attribute name variations.',
        'PreProcessing',
        '// ConvertBinaryValues - Pre-Processing Script
// Converts binary LDAP attributes to readable string formats

foreach (var obj in SourceObjects)
{
    // Case-insensitive objectGUID conversion
    var guidKey = obj.Keys.FirstOrDefault(k => k.Equals("objectGuid", StringComparison.OrdinalIgnoreCase));
    if (guidKey != null && obj[guidKey] is byte[] guidBytes && guidBytes.Length == 16)
    {
        obj[guidKey] = new Guid(guidBytes).ToString();
        Log.Debug($"Converted objectGUID for object");
    }

    // Case-insensitive objectSid conversion
    var sidKey = obj.Keys.FirstOrDefault(k => k.Equals("objectSid", StringComparison.OrdinalIgnoreCase));
    if (sidKey != null && obj[sidKey] is byte[] sidBytes)
    {
        try
        {
            obj[sidKey] = new System.Security.Principal.SecurityIdentifier(sidBytes, 0).ToString();
            Log.Debug($"Converted objectSid for object");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to convert objectSid: {ex.Message}");
        }
    }
}

Log.Info($"Converted binary values for {SourceObjects.Count} objects");',
        1, -- IsSystem
        1, -- IsEnabled
        1, -- Version
        'Attributes',
        'NotCompiled',
        GETUTCDATE(),
        'System'
    );
    PRINT 'Created ConvertBinaryValues system script.';
END

-- CreateOrUpdateIdentity (PostProcessing)
IF NOT EXISTS (SELECT 1 FROM SyncProcessingScripts WHERE Name = 'CreateOrUpdateIdentity' AND IsSystem = 1)
BEGIN
    INSERT INTO SyncProcessingScripts (
        Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled,
        Version, Category, CompilationStatus, CreatedAt, CreatedBy
    )
    VALUES (
        '22222222-2222-2222-2222-222222222222',
        'CreateOrUpdateIdentity',
        'Creates or matches Identity (Person) records for user/contact objects. Replaces the PostSyncTask person matching logic with a visible, editable script.',
        'PostProcessing',
        '// CreateOrUpdateIdentity - Post-Processing Script
// Creates or matches Identity records for synced user/contact objects

var userObjects = SyncedObjects.Where(o =>
    (o.ObjectClass == "user" || o.ObjectClass == "contact") &&
    !o.IdentityId.HasValue &&
    !o.IsBuiltIn
).ToList();

Log.Info($"Processing {userObjects.Count} objects for identity matching");

foreach (var obj in userObjects)
{
    Identity identity = null;

    // Try email match first (most reliable)
    if (!string.IsNullOrEmpty(obj.Email))
    {
        identity = await Repository.FindIdentityByEmailAsync(obj.Email, CancellationToken);
        if (identity != null)
        {
            Log.Debug($"Matched by email: {obj.Email}");
        }
    }

    // Try name match if no email match
    if (identity == null && !string.IsNullOrEmpty(obj.FirstName) && !string.IsNullOrEmpty(obj.LastName))
    {
        var matches = await Repository.FindIdentitiesByNameAsync(obj.FirstName, obj.LastName, CancellationToken);
        if (matches.Count == 1)
        {
            identity = matches[0];
            Log.Debug($"Matched by name: {obj.FirstName} {obj.LastName}");
        }
        else if (matches.Count > 1)
        {
            Log.Warning($"Multiple identity matches for {obj.FirstName} {obj.LastName} - skipping");
            continue;
        }
    }

    // Create new identity if not found
    if (identity == null)
    {
        identity = new Identity
        {
            Id = Guid.NewGuid(),
            DisplayName = obj.DisplayName ?? $"{obj.FirstName} {obj.LastName}".Trim(),
            FirstName = obj.FirstName,
            LastName = obj.LastName,
            PrimaryEmail = obj.Email,
            Department = obj.Department,
            JobTitle = obj.JobTitle,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await Repository.CreateIdentityAsync(identity, CancellationToken);
        Metrics.IdentitiesCreated++;
        Log.Info($"Created identity: {identity.DisplayName}");
    }

    // Link object to identity
    await Repository.UpdateObjectIdentityLinkAsync(obj.Id, identity.Id, CancellationToken);
    Metrics.ObjectsModified++;
}

Log.Info($"Identity matching complete: {Metrics.IdentitiesCreated} created, {Metrics.ObjectsModified} linked");',
        1, -- IsSystem
        1, -- IsEnabled
        1, -- Version
        'Identity',
        'NotCompiled',
        GETUTCDATE(),
        'System'
    );
    PRINT 'Created CreateOrUpdateIdentity system script.';
END

-- ResolveManagerRelationships (PostProcessing)
IF NOT EXISTS (SELECT 1 FROM SyncProcessingScripts WHERE Name = 'ResolveManagerRelationships' AND IsSystem = 1)
BEGIN
    INSERT INTO SyncProcessingScripts (
        Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled,
        Version, Category, CompilationStatus, CreatedAt, CreatedBy
    )
    VALUES (
        '33333333-3333-3333-3333-333333333333',
        'ResolveManagerRelationships',
        'Resolves manager relationships by matching ManagerSourceId (DN) to existing objects. Creates ManagerObjectId foreign key relationships.',
        'PostProcessing',
        '// ResolveManagerRelationships - Post-Processing Script
// Resolves manager DN references to actual object relationships

var objectsWithManager = SyncedObjects.Where(o =>
    !string.IsNullOrEmpty(o.ManagerSourceId) &&
    !o.ManagerObjectId.HasValue
).ToList();

Log.Info($"Resolving managers for {objectsWithManager.Count} objects");

foreach (var obj in objectsWithManager)
{
    var manager = await Repository.FindObjectByDNAsync(
        obj.SourceConnectionId,
        obj.ManagerSourceId,
        CancellationToken
    );

    if (manager != null)
    {
        await Repository.UpdateObjectManagerIdAsync(obj.Id, manager.Object.Id, CancellationToken);
        Metrics.ManagersResolved++;
        Log.Debug($"Resolved manager for {obj.DisplayName}: {manager.Object.DisplayName}");
    }
    else
    {
        Log.Warning($"Manager not found for {obj.DisplayName}: {obj.ManagerSourceId}");
    }
}

Log.Info($"Manager resolution complete: {Metrics.ManagersResolved} resolved");',
        1, -- IsSystem
        1, -- IsEnabled
        1, -- Version
        'Manager',
        'NotCompiled',
        GETUTCDATE(),
        'System'
    );
    PRINT 'Created ResolveManagerRelationships system script.';
END

PRINT '';
PRINT '============================================================================';
PRINT 'Dev Center Scripts migration complete!';
PRINT '============================================================================';
PRINT '';
PRINT 'Tables created:';
PRINT '  - SyncProcessingScripts (script definitions)';
PRINT '  - SyncStepScripts (step-script associations)';
PRINT '  - SyncScriptExecutions (execution audit trail)';
PRINT '';
PRINT 'System scripts seeded:';
PRINT '  - ConvertBinaryValues (PreProcessing)';
PRINT '  - CreateOrUpdateIdentity (PostProcessing)';
PRINT '  - ResolveManagerRelationships (PostProcessing)';
PRINT '';
GO
