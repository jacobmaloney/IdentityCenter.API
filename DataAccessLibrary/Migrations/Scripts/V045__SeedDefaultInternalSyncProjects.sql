-- V045: Seed Default Internal Sync Projects
-- Creates the built-in internal sync projects with steps and field mappings
-- so they exist on fresh installations without requiring wizard setup.
-- All inserts use IF NOT EXISTS checks for idempotency.

-- =============================================
-- Well-known IDs
-- =============================================
-- Internal Database Connection:  00000000-0000-0000-0000-000000000001
-- Create Objects Project:        50000000-0000-0000-0000-000000000001
-- Step 1 - Create Objects:       50000000-0000-0000-0001-000000000001  (PersonToObjectCreate)
-- Step 2 - Push Identity Data:   50000000-0000-0000-0001-000000000002  (PersonToObjectFieldSync)

-- =============================================
-- 1. Internal Database Connection
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [DirectoryConnections] WHERE [Id] = '00000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [DirectoryConnections] ([Id], [Name], [ConnectionType], [ConnectionString], [Credentials], [Configuration], [IsActive], [IsAuthoritative], [CreatedAt])
    VALUES (
        '00000000-0000-0000-0000-000000000001',
        N'Certification Center Database',
        N'Internal',
        N'internal://identitycenter',
        N'{}',
        N'{ "type": "Internal", "description": "Built-in connection for internal sync projects" }',
        1, -- IsActive
        0, -- IsAuthoritative
        GETUTCDATE()
    );
END

GO

-- =============================================
-- 2. Create Objects Project (Identity → Object Provisioning)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [SyncProjects] WHERE [Id] = '50000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [SyncProjects] (
        [Id], [Name], [Description], [SourceConnectionId], [ProjectType],
        [IsTemplateMode], [IsEnabled], [IsRunning],
        [ConflictResolutionStrategy], [AutoCreateIdentities], [EnableManagerAssignment],
        [IsBuiltIn], [IsReadOnly], [MinMatchConfidenceThreshold],
        [PauseOnError], [MaxErrorsBeforePause], [Priority], [LogLevel],
        [TotalExecutions], [SuccessfulExecutions], [FailedExecutions],
        [CreatedAt], [ModifiedAt]
    )
    VALUES (
        '50000000-0000-0000-0000-000000000001',
        N'Create Objects',
        N'Create and update directory objects from identity records. Provisions new AD accounts for identities that do not yet have a linked object, then syncs all identity fields to the provisioned accounts.',
        '00000000-0000-0000-0000-000000000001', -- Internal connection
        N'Provisioning',
        0,           -- IsTemplateMode
        1,           -- IsEnabled
        0,           -- IsRunning
        N'SourceWins', -- ConflictResolutionStrategy
        0,           -- AutoCreateIdentities
        0,           -- EnableManagerAssignment
        1,           -- IsBuiltIn
        1,           -- IsReadOnly
        0,           -- MinMatchConfidenceThreshold
        0,           -- PauseOnError
        10,          -- MaxErrorsBeforePause
        100,         -- Priority
        N'Information', -- LogLevel
        0,           -- TotalExecutions
        0,           -- SuccessfulExecutions
        0,           -- FailedExecutions
        GETUTCDATE(),
        GETUTCDATE()
    );
END

GO

-- =============================================
-- 3. Step 1: Create Objects from Identities (PersonToObjectCreate)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [InternalSyncSteps] WHERE [Id] = '50000000-0000-0000-0001-000000000001')
BEGIN
    INSERT INTO [InternalSyncSteps] (
        [Id], [SyncProjectId], [Name], [Description], [ExecutionOrder], [Direction],
        [StepType], [ObjectClassFilter], [IsEnabled], [ContinueOnError],
        [Configuration], [SourceConnectionId], [CreatedAt], [ModifiedAt]
    )
    VALUES (
        '50000000-0000-0000-0001-000000000001',
        '50000000-0000-0000-0000-000000000001',
        N'Create Objects from Identities',
        N'Create new Object records for active identities that do not yet have a linked object in the target connection.',
        1,              -- ExecutionOrder
        N'PersonToObject',
        N'PersonToObjectCreate',
        N'user',        -- ObjectClassFilter
        1,              -- IsEnabled
        1,              -- ContinueOnError
        NULL,           -- Configuration
        NULL,           -- SourceConnectionId (uses project default)
        GETUTCDATE(),
        GETUTCDATE()
    );
END

GO

-- =============================================
-- 4. Step 1 Mappings: Full Identity → Object field mappings
--    These mappings define which identity fields are written
--    to newly created objects during provisioning.
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [InternalSyncStepMappings] WHERE [InternalSyncStepId] = '50000000-0000-0000-0001-000000000001')
BEGIN
    -- Core Biographic
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000001', '50000000-0000-0000-0001-000000000001', N'DisplayName',       N'DisplayName',       1, 0, NULL, NULL, 1,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000002', '50000000-0000-0000-0001-000000000001', N'FirstName',         N'FirstName',         1, 0, NULL, NULL, 2,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000003', '50000000-0000-0000-0001-000000000001', N'LastName',          N'LastName',          1, 0, NULL, NULL, 3,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000004', '50000000-0000-0000-0001-000000000001', N'MiddleName',        N'MiddleName',        1, 0, NULL, NULL, 4,  1);

    -- Contact Information
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000005', '50000000-0000-0000-0001-000000000001', N'PrimaryEmail',      N'Email',             1, 0, NULL, NULL, 5,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000006', '50000000-0000-0000-0001-000000000001', N'PrimaryPhone',      N'Phone',             1, 0, NULL, NULL, 6,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000007', '50000000-0000-0000-0001-000000000001', N'MobilePhone',       N'MobilePhone',       1, 0, NULL, NULL, 7,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000008', '50000000-0000-0000-0001-000000000001', N'HomePhone',         N'HomePhone',         1, 0, NULL, NULL, 8,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000009', '50000000-0000-0000-0001-000000000001', N'Fax',               N'Fax',               1, 0, NULL, NULL, 9,  1);

    -- Address
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000010', '50000000-0000-0000-0001-000000000001', N'StreetAddress',     N'StreetAddress',     1, 0, NULL, NULL, 10, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000011', '50000000-0000-0000-0001-000000000001', N'City',              N'City',              1, 0, NULL, NULL, 11, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000012', '50000000-0000-0000-0001-000000000001', N'State',             N'State',             1, 0, NULL, NULL, 12, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000013', '50000000-0000-0000-0001-000000000001', N'PostalCode',        N'PostalCode',        1, 0, NULL, NULL, 13, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000014', '50000000-0000-0000-0001-000000000001', N'Country',           N'Country',           1, 0, NULL, NULL, 14, 1);

    -- Organizational
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000015', '50000000-0000-0000-0001-000000000001', N'EmployeeId',        N'EmployeeId',        1, 0, NULL, NULL, 15, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000016', '50000000-0000-0000-0001-000000000001', N'JobTitle',          N'JobTitle',          1, 0, NULL, NULL, 16, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000017', '50000000-0000-0000-0001-000000000001', N'Department',        N'Department',        1, 0, NULL, NULL, 17, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000018', '50000000-0000-0000-0001-000000000001', N'Division',          N'Division',          1, 0, NULL, NULL, 18, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000019', '50000000-0000-0000-0001-000000000001', N'Company',           N'Company',           1, 0, NULL, NULL, 19, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000020', '50000000-0000-0000-0001-000000000001', N'Office',            N'Office',            1, 0, NULL, NULL, 20, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000021', '50000000-0000-0000-0001-000000000001', N'IdentityType',      N'EmployeeType',      1, 0, NULL, NULL, 21, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000022', '50000000-0000-0000-0001-000000000001', N'Description',       N'Description',       1, 0, NULL, NULL, 22, 1);

    -- Technical / Account
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000023', '50000000-0000-0000-0001-000000000001', N'Username',          N'Username',          1, 0, NULL, NULL, 23, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000024', '50000000-0000-0000-0001-000000000001', N'UserPrincipalName', N'UserPrincipalName', 1, 0, NULL, NULL, 24, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0002-000000000025', '50000000-0000-0000-0001-000000000001', N'IsActive',          N'IsActive',          1, 0, NULL, NULL, 25, 1);
END

GO

-- =============================================
-- 5. Step 2: Push Identity Data (PersonToObjectFieldSync)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [InternalSyncSteps] WHERE [Id] = '50000000-0000-0000-0001-000000000002')
BEGIN
    INSERT INTO [InternalSyncSteps] (
        [Id], [SyncProjectId], [Name], [Description], [ExecutionOrder], [Direction],
        [StepType], [ObjectClassFilter], [IsEnabled], [ContinueOnError],
        [Configuration], [SourceConnectionId], [CreatedAt], [ModifiedAt]
    )
    VALUES (
        '50000000-0000-0000-0001-000000000002',
        '50000000-0000-0000-0000-000000000001',
        N'Push Identity Data',
        N'Sync all identity fields to linked objects using the configured field mappings.',
        2,              -- ExecutionOrder
        N'PersonToObject',
        N'PersonToObjectFieldSync',
        N'user',        -- ObjectClassFilter
        1,              -- IsEnabled
        1,              -- ContinueOnError
        NULL,           -- Configuration
        NULL,           -- SourceConnectionId (uses project default)
        GETUTCDATE(),
        GETUTCDATE()
    );
END

GO

-- =============================================
-- 6. Step 2 Mappings: Full Identity → Object field mappings
--    All identity fields synced to linked objects using
--    the same mapping set as the AD Provisioning template.
-- =============================================
IF NOT EXISTS (SELECT 1 FROM [InternalSyncStepMappings] WHERE [InternalSyncStepId] = '50000000-0000-0000-0001-000000000002')
BEGIN
    -- Core Biographic
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000001', '50000000-0000-0000-0001-000000000002', N'DisplayName',       N'DisplayName',       1, 0, NULL, NULL, 1,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000002', '50000000-0000-0000-0001-000000000002', N'FirstName',         N'FirstName',         1, 0, NULL, NULL, 2,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000003', '50000000-0000-0000-0001-000000000002', N'LastName',          N'LastName',          1, 0, NULL, NULL, 3,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000004', '50000000-0000-0000-0001-000000000002', N'MiddleName',        N'MiddleName',        1, 0, NULL, NULL, 4,  1);

    -- Contact Information
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000005', '50000000-0000-0000-0001-000000000002', N'PrimaryEmail',      N'Email',             1, 0, NULL, NULL, 5,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000006', '50000000-0000-0000-0001-000000000002', N'PrimaryPhone',      N'Phone',             1, 0, NULL, NULL, 6,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000007', '50000000-0000-0000-0001-000000000002', N'MobilePhone',       N'MobilePhone',       1, 0, NULL, NULL, 7,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000008', '50000000-0000-0000-0001-000000000002', N'HomePhone',         N'HomePhone',         1, 0, NULL, NULL, 8,  1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000009', '50000000-0000-0000-0001-000000000002', N'Fax',               N'Fax',               1, 0, NULL, NULL, 9,  1);

    -- Address
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000010', '50000000-0000-0000-0001-000000000002', N'StreetAddress',     N'StreetAddress',     1, 0, NULL, NULL, 10, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000011', '50000000-0000-0000-0001-000000000002', N'City',              N'City',              1, 0, NULL, NULL, 11, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000012', '50000000-0000-0000-0001-000000000002', N'State',             N'State',             1, 0, NULL, NULL, 12, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000013', '50000000-0000-0000-0001-000000000002', N'PostalCode',        N'PostalCode',        1, 0, NULL, NULL, 13, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000014', '50000000-0000-0000-0001-000000000002', N'Country',           N'Country',           1, 0, NULL, NULL, 14, 1);

    -- Organizational
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000015', '50000000-0000-0000-0001-000000000002', N'EmployeeId',        N'EmployeeId',        1, 0, NULL, NULL, 15, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000016', '50000000-0000-0000-0001-000000000002', N'JobTitle',          N'JobTitle',          1, 0, NULL, NULL, 16, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000017', '50000000-0000-0000-0001-000000000002', N'Department',        N'Department',        1, 0, NULL, NULL, 17, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000018', '50000000-0000-0000-0001-000000000002', N'Division',          N'Division',          1, 0, NULL, NULL, 18, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000019', '50000000-0000-0000-0001-000000000002', N'Company',           N'Company',           1, 0, NULL, NULL, 19, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000020', '50000000-0000-0000-0001-000000000002', N'Office',            N'Office',            1, 0, NULL, NULL, 20, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000021', '50000000-0000-0000-0001-000000000002', N'IdentityType',      N'EmployeeType',      1, 0, NULL, NULL, 21, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000022', '50000000-0000-0000-0001-000000000002', N'Description',       N'Description',       1, 0, NULL, NULL, 22, 1);

    -- Technical / Account
    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000023', '50000000-0000-0000-0001-000000000002', N'Username',          N'Username',          1, 0, NULL, NULL, 23, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000024', '50000000-0000-0000-0001-000000000002', N'UserPrincipalName', N'UserPrincipalName', 1, 0, NULL, NULL, 24, 1);

    INSERT INTO [InternalSyncStepMappings] ([Id], [InternalSyncStepId], [SourceField], [TargetField], [OverwriteExisting], [IsRequired], [DefaultValue], [Transformation], [MappingOrder], [IsEnabled])
    VALUES ('50000000-0000-0000-0003-000000000025', '50000000-0000-0000-0001-000000000002', N'IsActive',          N'IsActive',          1, 0, NULL, NULL, 25, 1);
END

GO
