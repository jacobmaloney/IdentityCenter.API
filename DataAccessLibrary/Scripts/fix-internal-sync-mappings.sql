-- ============================================================================
-- FIX: Internal Sync Field Mappings - Correct Column Names
-- ============================================================================
-- Problem: Mappings used Object column names as Identity column targets
--          - Email should map to PrimaryEmail (not Email)
--          - Phone should map to PrimaryPhone (not Phone)
--          - Username has no equivalent in Identity table (delete)
-- ============================================================================

-- Show current (broken) mappings
PRINT 'Current mappings BEFORE fix:';
SELECT
    s.Name AS StepName,
    m.SourceField,
    m.TargetField,
    m.IsEnabled,
    m.OverwriteExisting
FROM InternalSyncStepMappings m
JOIN InternalSyncSteps s ON m.InternalSyncStepId = s.Id
ORDER BY s.ExecutionOrder, m.MappingOrder;

-- ============================================================================
-- FIX 1: Update Email -> PrimaryEmail (for Object→Identity direction)
-- ============================================================================
UPDATE m
SET m.TargetField = 'PrimaryEmail'
FROM InternalSyncStepMappings m
JOIN InternalSyncSteps s ON m.InternalSyncStepId = s.Id
WHERE m.TargetField = 'Email'
  AND s.Direction = 'ObjectToPerson';

PRINT 'Fixed: Email -> PrimaryEmail for ObjectToPerson steps';

-- ============================================================================
-- FIX 2: Update Phone -> PrimaryPhone (for Object→Identity direction)
-- ============================================================================
UPDATE m
SET m.TargetField = 'PrimaryPhone'
FROM InternalSyncStepMappings m
JOIN InternalSyncSteps s ON m.InternalSyncStepId = s.Id
WHERE m.TargetField = 'Phone'
  AND s.Direction = 'ObjectToPerson';

PRINT 'Fixed: Phone -> PrimaryPhone for ObjectToPerson steps';

-- ============================================================================
-- FIX 3: Delete Username mappings (no Username column in Identity table)
-- ============================================================================
DELETE m
FROM InternalSyncStepMappings m
JOIN InternalSyncSteps s ON m.InternalSyncStepId = s.Id
WHERE m.TargetField = 'Username'
  AND s.Direction = 'ObjectToPerson';

PRINT 'Deleted: Username mappings (column does not exist in Identity table)';

-- ============================================================================
-- FIX 4: For PersonToObject direction, fix source fields
-- ============================================================================
UPDATE m
SET m.SourceField = 'PrimaryEmail'
FROM InternalSyncStepMappings m
JOIN InternalSyncSteps s ON m.InternalSyncStepId = s.Id
WHERE m.SourceField = 'Email'
  AND s.Direction = 'PersonToObject';

UPDATE m
SET m.SourceField = 'PrimaryPhone'
FROM InternalSyncStepMappings m
JOIN InternalSyncSteps s ON m.InternalSyncStepId = s.Id
WHERE m.SourceField = 'Phone'
  AND s.Direction = 'PersonToObject';

PRINT 'Fixed: PersonToObject source field mappings';

-- ============================================================================
-- VERIFY: Show corrected mappings
-- ============================================================================
PRINT '';
PRINT 'Corrected mappings AFTER fix:';
SELECT
    s.Name AS StepName,
    s.Direction,
    m.SourceField,
    m.TargetField,
    m.IsEnabled,
    m.OverwriteExisting
FROM InternalSyncStepMappings m
JOIN InternalSyncSteps s ON m.InternalSyncStepId = s.Id
ORDER BY s.ExecutionOrder, m.MappingOrder;

-- ============================================================================
-- REFERENCE: Valid field mappings
-- ============================================================================
/*
OBJECT TABLE (IdentityObject/Objects):
  - DisplayName, FirstName, LastName
  - Email, Username, Phone
  - Department, JobTitle
  - DN, CN, ObjectClass
  - SourceUniqueId, SourceType
  - ManagerSourceId, ManagerObjectId
  - IsActive, IsBuiltIn, IsAdminSDHolder

IDENTITY TABLE (Identities):
  - DisplayName, FirstName, LastName, MiddleName
  - PrimaryEmail, PrimaryPhone (NOT Email/Phone!)
  - Department, JobTitle
  - ManagerIdentityId
  - IsActive (NO Username column!)

Object → Identity valid mappings:
  Email -> PrimaryEmail
  Phone -> PrimaryPhone
  DisplayName -> DisplayName
  FirstName -> FirstName
  LastName -> LastName
  Department -> Department
  JobTitle -> JobTitle

Identity → Object valid mappings:
  PrimaryEmail -> Email
  PrimaryPhone -> Phone
  DisplayName -> DisplayName
  FirstName -> FirstName
  LastName -> LastName
  Department -> Department
  JobTitle -> JobTitle
*/
