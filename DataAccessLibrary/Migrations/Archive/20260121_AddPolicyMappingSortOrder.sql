-- Add SortOrder column to ComplianceFrameworkPolicyMappings
-- Allows ordering policies within a framework

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ComplianceFrameworkPolicyMappings') AND name = 'SortOrder')
BEGIN
    ALTER TABLE ComplianceFrameworkPolicyMappings ADD SortOrder INT NOT NULL DEFAULT 0;
    PRINT 'Added SortOrder column to ComplianceFrameworkPolicyMappings';
END
ELSE
BEGIN
    PRINT 'SortOrder column already exists on ComplianceFrameworkPolicyMappings';
END
GO

-- Initialize sort order based on creation date for existing mappings
UPDATE m
SET m.SortOrder = sub.RowNum
FROM ComplianceFrameworkPolicyMappings m
INNER JOIN (
    SELECT Id, FrameworkId, ROW_NUMBER() OVER (PARTITION BY FrameworkId ORDER BY CreatedAt) AS RowNum
    FROM ComplianceFrameworkPolicyMappings
) sub ON m.Id = sub.Id
WHERE m.SortOrder = 0;
GO
