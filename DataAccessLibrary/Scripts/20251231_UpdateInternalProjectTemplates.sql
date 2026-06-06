-- Update PersonMatch and PersonCreate templates to have correct ProjectType
-- This fixes templates that were created before ProjectType was implemented

PRINT 'Updating Internal Project Templates...'

-- Update PersonMatch template
UPDATE SyncProjects
SET ProjectType = 'PersonMatch'
WHERE Id = '11111111-1111-1111-1111-111111111001'
   OR Name LIKE '%Person Match%Link to Existing%'

-- Update PersonCreate template
UPDATE SyncProjects
SET ProjectType = 'PersonCreate'
WHERE Id = '11111111-1111-1111-1111-111111111002'
   OR Name LIKE '%Person Create%Match or Create%'

-- Also update any project that has [Template] in name and relates to Person operations
UPDATE SyncProjects
SET ProjectType = 'PersonMatch'
WHERE Name LIKE '%[Template]%Person%Match%'
  AND (ProjectType IS NULL OR ProjectType = '' OR ProjectType = 'ObjectSync')
  AND Name NOT LIKE '%Create%'

UPDATE SyncProjects
SET ProjectType = 'PersonCreate'
WHERE Name LIKE '%[Template]%Person%Create%'
  AND (ProjectType IS NULL OR ProjectType = '' OR ProjectType = 'ObjectSync')

PRINT 'Done updating Internal Project Templates'

-- Show results
SELECT Id, Name, ProjectType, IsBuiltIn FROM SyncProjects WHERE ProjectType IN ('PersonMatch', 'PersonCreate')
