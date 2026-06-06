-- V011: Drop FK constraint on CompliancePolicyViolations.EntityId → Identities.Id
-- EntityId is polymorphic (can reference Identities OR Objects), so the FK is incorrect.

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_CompliancePolicyViolations_Identities_EntityId'
      AND parent_object_id = OBJECT_ID('CompliancePolicyViolations')
)
BEGIN
    ALTER TABLE [CompliancePolicyViolations]
        DROP CONSTRAINT [FK_CompliancePolicyViolations_Identities_EntityId];
    PRINT 'Dropped FK_CompliancePolicyViolations_Identities_EntityId';
END
ELSE
BEGIN
    PRINT 'FK_CompliancePolicyViolations_Identities_EntityId does not exist - skipping';
END
