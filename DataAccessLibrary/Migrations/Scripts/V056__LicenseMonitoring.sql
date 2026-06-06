-- V056: License Monitoring Foundation
-- Tracks M365 / Entra ID license pools, per-user assignments, service plan detail,
-- daily usage snapshots, and AI-driven optimization recommendations.
-- All tables guarded with IF NOT EXISTS so the migration is safe to replay.

-- ─────────────────────────────────────────────────────────────────────────────
-- LicensePools: Organization-level license inventory per directory connection
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicensePools')
BEGIN
    CREATE TABLE [LicensePools] (
        [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [SourceConnectionId]  UNIQUEIDENTIFIER NOT NULL,
        [SkuId]               NVARCHAR(200)    NOT NULL,
        [SkuName]             NVARCHAR(500)    NOT NULL,
        [SkuPartNumber]       NVARCHAR(200)    NULL,
        [TotalUnits]          INT              NOT NULL DEFAULT 0,
        [ConsumedUnits]       INT              NOT NULL DEFAULT 0,
        [WarningUnits]        INT              NOT NULL DEFAULT 0,
        [SuspendedUnits]      INT              NOT NULL DEFAULT 0,
        [AvailableUnits]      AS (TotalUnits - ConsumedUnits - WarningUnits - SuspendedUnits),
        [CostPerUnitMonthly]  DECIMAL(10,2)   NULL,
        [Currency]            NVARCHAR(10)     NULL DEFAULT 'USD',
        [LastSyncedAt]        DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [IsActive]            BIT              NOT NULL DEFAULT 1,
        CONSTRAINT [PK_LicensePools] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LicensePools_Connection] FOREIGN KEY ([SourceConnectionId])
            REFERENCES [DirectoryConnections] ([Id]) ON DELETE CASCADE
    );

    -- Primary lookup: pools for a given connection
    CREATE NONCLUSTERED INDEX [IX_LicensePools_Connection]
        ON [LicensePools] ([SourceConnectionId], [IsActive])
        INCLUDE ([SkuId], [SkuName], [TotalUnits], [ConsumedUnits], [LastSyncedAt]);

    -- Upsert support: look up pool by connection + SKU
    CREATE NONCLUSTERED INDEX [IX_LicensePools_ConnectionSku]
        ON [LicensePools] ([SourceConnectionId], [SkuId]);

    PRINT 'Created LicensePools table';
END;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- LicenseAssignments: Per-user license assignments
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseAssignments')
BEGIN
    CREATE TABLE [LicenseAssignments] (
        [Id]               UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [LicensePoolId]    UNIQUEIDENTIFIER NOT NULL,
        [ObjectId]         UNIQUEIDENTIFIER NOT NULL,
        [AssignedAt]       DATETIME2        NULL,
        [AssignmentSource] NVARCHAR(50)     NOT NULL DEFAULT 'Direct',
        [SourceGroupId]    UNIQUEIDENTIFIER NULL,
        [LastUsedAt]       DATETIME2        NULL,
        [IsActive]         BIT              NOT NULL DEFAULT 1,
        [LastSyncedAt]     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_LicenseAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LicenseAssignments_Pool] FOREIGN KEY ([LicensePoolId])
            REFERENCES [LicensePools] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_LicenseAssignments_Object] FOREIGN KEY ([ObjectId])
            REFERENCES [Objects] ([Id]) ON DELETE CASCADE
    );

    -- All active assignments for a pool (waste calculation)
    CREATE NONCLUSTERED INDEX [IX_LicenseAssignments_Pool]
        ON [LicenseAssignments] ([LicensePoolId], [IsActive])
        INCLUDE ([ObjectId], [AssignmentSource], [LastUsedAt]);

    -- All licenses assigned to a specific user
    CREATE NONCLUSTERED INDEX [IX_LicenseAssignments_Object]
        ON [LicenseAssignments] ([ObjectId], [IsActive])
        INCLUDE ([LicensePoolId], [AssignedAt], [LastUsedAt]);

    -- Upsert support: look up assignment by pool + object
    CREATE NONCLUSTERED INDEX [IX_LicenseAssignments_PoolObject]
        ON [LicenseAssignments] ([LicensePoolId], [ObjectId]);

    PRINT 'Created LicenseAssignments table';
END;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- LicenseServicePlans: Feature-level detail per license pool
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseServicePlans')
BEGIN
    CREATE TABLE [LicenseServicePlans] (
        [Id]                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [LicensePoolId]       UNIQUEIDENTIFIER NOT NULL,
        [ServicePlanId]       NVARCHAR(200)    NOT NULL,
        [ServicePlanName]     NVARCHAR(500)    NOT NULL,
        [ProvisioningStatus]  NVARCHAR(50)     NULL,
        [AppliesTo]           NVARCHAR(50)     NULL,
        CONSTRAINT [PK_LicenseServicePlans] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LicenseServicePlans_Pool] FOREIGN KEY ([LicensePoolId])
            REFERENCES [LicensePools] ([Id]) ON DELETE CASCADE
    );

    -- Plans by pool (cascade loads with pool record)
    CREATE NONCLUSTERED INDEX [IX_LicenseServicePlans_Pool]
        ON [LicenseServicePlans] ([LicensePoolId])
        INCLUDE ([ServicePlanId], [ServicePlanName], [ProvisioningStatus]);

    -- Upsert support: look up plan by pool + service plan GUID
    CREATE NONCLUSTERED INDEX [IX_LicenseServicePlans_PoolServicePlan]
        ON [LicenseServicePlans] ([LicensePoolId], [ServicePlanId]);

    PRINT 'Created LicenseServicePlans table';
END;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- LicenseUsageSnapshots: Daily time-series for trend analysis
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseUsageSnapshots')
BEGIN
    CREATE TABLE [LicenseUsageSnapshots] (
        [Id]                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [LicensePoolId]           UNIQUEIDENTIFIER NOT NULL,
        [SnapshotDate]            DATE             NOT NULL,
        [TotalUnits]              INT              NOT NULL,
        [ConsumedUnits]           INT              NOT NULL,
        [WastedUnits]             INT              NOT NULL DEFAULT 0,
        [EstimatedWasteMonthly]   DECIMAL(10,2)   NULL,
        CONSTRAINT [PK_LicenseUsageSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LicenseUsageSnapshots_Pool] FOREIGN KEY ([LicensePoolId])
            REFERENCES [LicensePools] ([Id]) ON DELETE CASCADE
    );

    -- Trend queries: pool history ordered by date
    CREATE NONCLUSTERED INDEX [IX_LicenseUsageSnapshots_PoolDate]
        ON [LicenseUsageSnapshots] ([LicensePoolId], [SnapshotDate] DESC)
        INCLUDE ([TotalUnits], [ConsumedUnits], [WastedUnits], [EstimatedWasteMonthly]);

    -- Prevent duplicate snapshots for the same pool on the same day
    CREATE UNIQUE NONCLUSTERED INDEX [UX_LicenseUsageSnapshots_PoolDay]
        ON [LicenseUsageSnapshots] ([LicensePoolId], [SnapshotDate]);

    PRINT 'Created LicenseUsageSnapshots table';
END;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- LicenseOptimizationRecommendations: AI-driven suggestions
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseOptimizationRecommendations')
BEGIN
    CREATE TABLE [LicenseOptimizationRecommendations] (
        [Id]                       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [ObjectId]                 UNIQUEIDENTIFIER NOT NULL,
        [LicensePoolId]            UNIQUEIDENTIFIER NULL,
        [RecommendationType]       NVARCHAR(50)     NOT NULL,
        [CurrentSkuName]           NVARCHAR(500)    NULL,
        [RecommendedSkuName]       NVARCHAR(500)    NULL,
        [Reason]                   NVARCHAR(1000)   NOT NULL,
        [EstimatedMonthlySavings]  DECIMAL(10,2)   NULL,
        [Status]                   NVARCHAR(50)     NOT NULL DEFAULT 'Pending',
        [CreatedAt]                DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [ReviewedBy]               NVARCHAR(256)    NULL,
        [ReviewedAt]               DATETIME2        NULL,
        [AppliedAt]                DATETIME2        NULL,
        CONSTRAINT [PK_LicenseOptimizationRecs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LicenseOptRecs_Object] FOREIGN KEY ([ObjectId])
            REFERENCES [Objects] ([Id]) ON DELETE CASCADE
    );

    -- Pending recommendations queue (primary UI view)
    CREATE NONCLUSTERED INDEX [IX_LicenseOptRecs_Status]
        ON [LicenseOptimizationRecommendations] ([Status], [CreatedAt] DESC)
        INCLUDE ([ObjectId], [LicensePoolId], [RecommendationType], [EstimatedMonthlySavings]);

    -- All recommendations for a specific user
    CREATE NONCLUSTERED INDEX [IX_LicenseOptRecs_Object]
        ON [LicenseOptimizationRecommendations] ([ObjectId], [Status])
        INCLUDE ([RecommendationType], [EstimatedMonthlySavings], [CreatedAt]);

    -- All recommendations linked to a specific pool
    CREATE NONCLUSTERED INDEX [IX_LicenseOptRecs_Pool]
        ON [LicenseOptimizationRecommendations] ([LicensePoolId], [Status])
        INCLUDE ([ObjectId], [RecommendationType], [EstimatedMonthlySavings]);

    PRINT 'Created LicenseOptimizationRecommendations table';
END;
GO

PRINT 'V056: License Monitoring migration complete';
GO
