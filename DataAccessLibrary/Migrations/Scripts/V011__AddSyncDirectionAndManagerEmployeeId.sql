-- V010: Add SyncDirection to SyncProjects and ManagerEmployeeId to Identities
-- SyncDirection: Referenced by SyncConfigRepository but was never added to schema
-- ManagerEmployeeId: Staging field for HR Import manager resolution

-- =============================================
-- SyncProjects.SyncDirection
-- =============================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SyncProjects' AND COLUMN_NAME = 'SyncDirection')
BEGIN
    ALTER TABLE [SyncProjects] ADD [SyncDirection] NVARCHAR(50) NULL DEFAULT 'Inbound';
    PRINT 'Added SyncDirection column to SyncProjects';
END
ELSE
BEGIN
    PRINT 'SyncDirection column already exists on SyncProjects - skipping';
END
GO

-- =============================================
-- Identities.ManagerEmployeeId
-- =============================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Identities' AND COLUMN_NAME = 'ManagerEmployeeId')
BEGIN
    ALTER TABLE [Identities] ADD [ManagerEmployeeId] NVARCHAR(100) NULL;
    PRINT 'Added ManagerEmployeeId column to Identities';
END
ELSE
BEGIN
    PRINT 'ManagerEmployeeId column already exists on Identities - skipping';
END
GO

-- Index for manager lookup resolution (EmployeeId -> ManagerIdentityId)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Identities_ManagerEmployeeId')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Identities_ManagerEmployeeId]
        ON [Identities] ([ManagerEmployeeId])
        WHERE [ManagerEmployeeId] IS NOT NULL;
    PRINT 'Created IX_Identities_ManagerEmployeeId index';
END
GO

PRINT 'V010: SyncDirection + ManagerEmployeeId migration complete';
