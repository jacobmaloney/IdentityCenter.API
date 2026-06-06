-- Add Create Persons step to O to P internal sync project
-- Run this script to add the missing step

-- First, get the O to P project ID
DECLARE @ProjectId UNIQUEIDENTIFIER;
SELECT @ProjectId = Id FROM SyncProjects WHERE Name = 'O to P';

-- Show current steps
PRINT 'Current steps in O to P project:';
SELECT Id, Name, StepType, Direction, ObjectClassFilter, ExecutionOrder, IsEnabled
FROM InternalSyncSteps
WHERE SyncProjectId = @ProjectId
ORDER BY ExecutionOrder;

-- Get the max execution order
DECLARE @MaxOrder INT;
SELECT @MaxOrder = ISNULL(MAX(ExecutionOrder), 0) FROM InternalSyncSteps WHERE SyncProjectId = @ProjectId;

-- Check if Create step already exists
IF NOT EXISTS (SELECT 1 FROM InternalSyncSteps WHERE SyncProjectId = @ProjectId AND StepType = 'ObjectToPersonCreate')
BEGIN
    PRINT 'Adding Create Persons step...';

    INSERT INTO InternalSyncSteps (
        Id,
        SyncProjectId,
        Name,
        Description,
        ExecutionOrder,
        Direction,
        StepType,
        ObjectClassFilter,
        IsEnabled,
        ContinueOnError,
        Configuration,
        CreatedAt,
        ModifiedAt
    )
    VALUES (
        NEWID(),
        @ProjectId,
        'Create Persons',
        'Create identity records for unmatched objects from directory',
        @MaxOrder + 1,
        'ObjectToPerson',
        'ObjectToPersonCreate',
        'user',
        1,  -- IsEnabled
        0,  -- ContinueOnError
        '{"defaultStatus": "Active", "setAuthoritative": true}',
        GETUTCDATE(),
        GETUTCDATE()
    );

    PRINT 'Create Persons step added successfully!';
END
ELSE
BEGIN
    PRINT 'Create Persons step already exists!';
END

-- Show updated steps
PRINT 'Updated steps in O to P project:';
SELECT Id, Name, StepType, Direction, ObjectClassFilter, ExecutionOrder, IsEnabled
FROM InternalSyncSteps
WHERE SyncProjectId = @ProjectId
ORDER BY ExecutionOrder;
