-- Cleanup: Delete Identities that have no linked Objects (orphans from failed sync)
-- Run this before re-running the O to P sync

-- First, show what will be deleted
PRINT 'Orphan Identities (no linked Objects) to be deleted:';
SELECT i.Id, i.DisplayName, i.PrimaryEmail, i.CreatedAt
FROM Identities i
WHERE NOT EXISTS (SELECT 1 FROM Objects o WHERE o.IdentityId = i.Id)
ORDER BY i.CreatedAt DESC;

-- Count
DECLARE @OrphanCount INT;
SELECT @OrphanCount = COUNT(*)
FROM Identities i
WHERE NOT EXISTS (SELECT 1 FROM Objects o WHERE o.IdentityId = i.Id);
PRINT 'Total orphan Identities: ' + CAST(@OrphanCount AS VARCHAR(10));

-- Delete orphans (uncomment to execute)
-- DELETE FROM Identities
-- WHERE NOT EXISTS (SELECT 1 FROM Objects o WHERE o.IdentityId = Identities.Id);

-- Safer approach: Delete recent orphans (created today)
PRINT '';
PRINT 'Deleting orphan Identities created today...';
DELETE FROM Identities
WHERE NOT EXISTS (SELECT 1 FROM Objects o WHERE o.IdentityId = Identities.Id)
  AND CreatedAt > DATEADD(DAY, -1, GETUTCDATE());

PRINT 'Deleted orphan Identities. Ready to re-run O to P sync.';
