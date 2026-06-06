-- V060: Patch existing Entra ID user sync workflows with new step types
-- Adds LicenseSync, SignInLogSync, UsageReportSync, and AppRoleSync steps
-- to any Entra ID sync project workflow for ObjectClass = 'user' that
-- does not already have them. Fully idempotent (safe to re-run).

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. LicenseSync step
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO [SyncSteps] (
    [Id], [SyncWorkflowId], [Name], [Description], [ExecutionOrder],
    [ObjectClass], [StepType], [MarkAsType],
    [LdapFilter], [SearchBase], [SearchBases], [ExcludedSearchBases], [SearchScope],
    [IsEnabled], [ContinueOnError], [MaxExecutionTimeMinutes],
    [DependsOnStepIds], [ProcessDeletions], [UpdateExisting],
    [BatchSize], [LdapPageSize], [Configuration],
    [EnableIdentityMatching], [IdentityMatchingAttribute],
    [InheritWorkflowTags], [SkipPersonMatching], [EnablePersonMatching], [CreatePersonIfNotFound],
    [CreatedAt], [ModifiedAt]
)
SELECT
    NEWID(),
    w.[Id],
    N'Sync M365 Licenses',
    N'Synchronizes Microsoft 365 license assignments from Entra ID',
    (SELECT ISNULL(MAX(s.[ExecutionOrder]), 0) + 1 FROM [SyncSteps] s WHERE s.[SyncWorkflowId] = w.[Id]),
    N'user',
    N'LicenseSync',
    NULL,
    NULL, NULL, NULL, NULL, N'Subtree',
    1, 1, 60,
    NULL, 0, 1,
    100, 0, NULL,
    0, NULL,
    0, 1, 0, 0,
    GETUTCDATE(), GETUTCDATE()
FROM [SyncWorkflows] w
INNER JOIN [SyncProjects] p ON p.[Id] = w.[SyncProjectId]
INNER JOIN [DirectoryConnections] dc ON dc.[Id] = p.[SourceConnectionId]
WHERE dc.[ConnectionType] IN ('EntraID', 'AzureAD')
  AND w.[ObjectClass] = 'user'
  AND NOT EXISTS (
      SELECT 1 FROM [SyncSteps] ex
      WHERE ex.[SyncWorkflowId] = w.[Id]
        AND ex.[StepType] = 'LicenseSync'
  );

PRINT 'V060: LicenseSync step patched into Entra ID user workflows';
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. SignInLogSync step
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO [SyncSteps] (
    [Id], [SyncWorkflowId], [Name], [Description], [ExecutionOrder],
    [ObjectClass], [StepType], [MarkAsType],
    [LdapFilter], [SearchBase], [SearchBases], [ExcludedSearchBases], [SearchScope],
    [IsEnabled], [ContinueOnError], [MaxExecutionTimeMinutes],
    [DependsOnStepIds], [ProcessDeletions], [UpdateExisting],
    [BatchSize], [LdapPageSize], [Configuration],
    [EnableIdentityMatching], [IdentityMatchingAttribute],
    [InheritWorkflowTags], [SkipPersonMatching], [EnablePersonMatching], [CreatePersonIfNotFound],
    [CreatedAt], [ModifiedAt]
)
SELECT
    NEWID(),
    w.[Id],
    N'Sync Sign-In Logs',
    N'Synchronizes Entra ID sign-in log data for risk and activity analysis',
    (SELECT ISNULL(MAX(s.[ExecutionOrder]), 0) + 1 FROM [SyncSteps] s WHERE s.[SyncWorkflowId] = w.[Id]),
    N'user',
    N'SignInLogSync',
    NULL,
    NULL, NULL, NULL, NULL, N'Subtree',
    1, 1, 60,
    NULL, 0, 1,
    100, 0, NULL,
    0, NULL,
    0, 1, 0, 0,
    GETUTCDATE(), GETUTCDATE()
FROM [SyncWorkflows] w
INNER JOIN [SyncProjects] p ON p.[Id] = w.[SyncProjectId]
INNER JOIN [DirectoryConnections] dc ON dc.[Id] = p.[SourceConnectionId]
WHERE dc.[ConnectionType] IN ('EntraID', 'AzureAD')
  AND w.[ObjectClass] = 'user'
  AND NOT EXISTS (
      SELECT 1 FROM [SyncSteps] ex
      WHERE ex.[SyncWorkflowId] = w.[Id]
        AND ex.[StepType] = 'SignInLogSync'
  );

PRINT 'V060: SignInLogSync step patched into Entra ID user workflows';
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. UsageReportSync step
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO [SyncSteps] (
    [Id], [SyncWorkflowId], [Name], [Description], [ExecutionOrder],
    [ObjectClass], [StepType], [MarkAsType],
    [LdapFilter], [SearchBase], [SearchBases], [ExcludedSearchBases], [SearchScope],
    [IsEnabled], [ContinueOnError], [MaxExecutionTimeMinutes],
    [DependsOnStepIds], [ProcessDeletions], [UpdateExisting],
    [BatchSize], [LdapPageSize], [Configuration],
    [EnableIdentityMatching], [IdentityMatchingAttribute],
    [InheritWorkflowTags], [SkipPersonMatching], [EnablePersonMatching], [CreatePersonIfNotFound],
    [CreatedAt], [ModifiedAt]
)
SELECT
    NEWID(),
    w.[Id],
    N'Sync Usage Reports',
    N'Synchronizes Microsoft 365 usage report data for license optimization',
    (SELECT ISNULL(MAX(s.[ExecutionOrder]), 0) + 1 FROM [SyncSteps] s WHERE s.[SyncWorkflowId] = w.[Id]),
    N'user',
    N'UsageReportSync',
    NULL,
    NULL, NULL, NULL, NULL, N'Subtree',
    1, 1, 60,
    NULL, 0, 1,
    100, 0, NULL,
    0, NULL,
    0, 1, 0, 0,
    GETUTCDATE(), GETUTCDATE()
FROM [SyncWorkflows] w
INNER JOIN [SyncProjects] p ON p.[Id] = w.[SyncProjectId]
INNER JOIN [DirectoryConnections] dc ON dc.[Id] = p.[SourceConnectionId]
WHERE dc.[ConnectionType] IN ('EntraID', 'AzureAD')
  AND w.[ObjectClass] = 'user'
  AND NOT EXISTS (
      SELECT 1 FROM [SyncSteps] ex
      WHERE ex.[SyncWorkflowId] = w.[Id]
        AND ex.[StepType] = 'UsageReportSync'
  );

PRINT 'V060: UsageReportSync step patched into Entra ID user workflows';
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. AppRoleSync step
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO [SyncSteps] (
    [Id], [SyncWorkflowId], [Name], [Description], [ExecutionOrder],
    [ObjectClass], [StepType], [MarkAsType],
    [LdapFilter], [SearchBase], [SearchBases], [ExcludedSearchBases], [SearchScope],
    [IsEnabled], [ContinueOnError], [MaxExecutionTimeMinutes],
    [DependsOnStepIds], [ProcessDeletions], [UpdateExisting],
    [BatchSize], [LdapPageSize], [Configuration],
    [EnableIdentityMatching], [IdentityMatchingAttribute],
    [InheritWorkflowTags], [SkipPersonMatching], [EnablePersonMatching], [CreatePersonIfNotFound],
    [CreatedAt], [ModifiedAt]
)
SELECT
    NEWID(),
    w.[Id],
    N'Sync App Role Assignments',
    N'Synchronizes Entra ID application role assignments for entitlement visibility',
    (SELECT ISNULL(MAX(s.[ExecutionOrder]), 0) + 1 FROM [SyncSteps] s WHERE s.[SyncWorkflowId] = w.[Id]),
    N'user',
    N'AppRoleSync',
    NULL,
    NULL, NULL, NULL, NULL, N'Subtree',
    1, 1, 60,
    NULL, 0, 1,
    100, 0, NULL,
    0, NULL,
    0, 1, 0, 0,
    GETUTCDATE(), GETUTCDATE()
FROM [SyncWorkflows] w
INNER JOIN [SyncProjects] p ON p.[Id] = w.[SyncProjectId]
INNER JOIN [DirectoryConnections] dc ON dc.[Id] = p.[SourceConnectionId]
WHERE dc.[ConnectionType] IN ('EntraID', 'AzureAD')
  AND w.[ObjectClass] = 'user'
  AND NOT EXISTS (
      SELECT 1 FROM [SyncSteps] ex
      WHERE ex.[SyncWorkflowId] = w.[Id]
        AND ex.[StepType] = 'AppRoleSync'
  );

PRINT 'V060: AppRoleSync step patched into Entra ID user workflows';
GO

PRINT 'V060: PatchExistingEntraSyncWithLicenseStep migration complete';
GO
