-- Drop the FK constraint that's causing slowness (EntityId is polymorphic - can be Identity OR Object)
-- The ALTER TABLE NOCHECK/CHECK was taking 60+ seconds each time

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CompliancePolicyViolations_Identities_EntityId')
BEGIN
    ALTER TABLE CompliancePolicyViolations DROP CONSTRAINT FK_CompliancePolicyViolations_Identities_EntityId;
    PRINT 'Dropped FK constraint FK_CompliancePolicyViolations_Identities_EntityId';
END
ELSE
BEGIN
    PRINT 'FK constraint already dropped';
END
GO

-- Add index on Objects.ManagerObjectId for faster "Manager Required" policy queries
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_ManagerObjectId' AND object_id = OBJECT_ID('Objects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_ManagerObjectId
    ON Objects (ManagerObjectId)
    INCLUDE (DisplayName, CN, DN, ObjectClass, IsActive, SourceConnectionId);
    PRINT 'Created index IX_Objects_ManagerObjectId';
END
ELSE
BEGIN
    PRINT 'Index IX_Objects_ManagerObjectId already exists';
END
GO

-- Add index on CompliancePolicyViolations for faster lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CompliancePolicyViolations_PolicyId_EntityId' AND object_id = OBJECT_ID('CompliancePolicyViolations'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CompliancePolicyViolations_PolicyId_EntityId
    ON CompliancePolicyViolations (CompliancePolicyId, EntityId)
    INCLUDE (Status, Severity, DetectedAt);
    PRINT 'Created index IX_CompliancePolicyViolations_PolicyId_EntityId';
END
GO

PRINT 'Migration complete - policy execution should be much faster now';
