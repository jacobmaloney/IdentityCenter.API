-- V063: Add filtered index on ProcessInstances for Pending status
-- The ProcessInstanceWorkerJob queries WHERE Status = 'Pending' every 30 seconds.
-- The existing IX_ProcessInstances_Status only covers Running/Waiting* statuses,
-- causing a full table scan on every poll cycle.

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ProcessInstances_Status_Pending'
      AND object_id = OBJECT_ID('ProcessInstances')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_ProcessInstances_Status_Pending
        ON ProcessInstances (CreatedAt ASC)
        INCLUDE (Id)
        WHERE Status = 'Pending';
END
