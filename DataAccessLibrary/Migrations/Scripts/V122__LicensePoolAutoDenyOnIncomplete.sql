-- V122: License Pool — per-pool opt-in for AutoDeny on incomplete review.
--
-- Adds AutoDenyOnIncomplete to LicensePools. When set, the auto-triggered
-- access review campaign uses OnIncompleteAction='AutoDeny' instead of the
-- safer default 'Escalate'. Off by default; admins enable per pool.
--
-- See Phase 4 of the License Center auto-trigger loop.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('LicensePools') AND name = 'AutoDenyOnIncomplete')
BEGIN
    ALTER TABLE [LicensePools]
        ADD [AutoDenyOnIncomplete] BIT NOT NULL CONSTRAINT [DF_LicensePools_AutoDenyOnIncomplete] DEFAULT 0;
    PRINT 'V122: Added LicensePools.AutoDenyOnIncomplete column';
END
ELSE
BEGIN
    PRINT 'V122: LicensePools.AutoDenyOnIncomplete already present - skipping';
END
GO

PRINT 'Schema version 122 applied - LicensePools.AutoDenyOnIncomplete';
