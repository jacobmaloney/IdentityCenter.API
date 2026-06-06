-- V108: Fix License Waste Report seeded in V107 to use real LicenseAssignments column names
-- V107 seeded the report with la.AssignedDate / la.LastActiveDate. The actual columns
-- defined in V056 are AssignedAt / LastUsedAt. Without this fix the Otis demo opener
-- (License Waste Report) throws a SqlException when the user clicks Run.
--
-- V107 cannot be modified in place once shipped, so this updates the row's
-- QueryDefinition with the corrected SQL. Idempotent — only updates rows whose
-- query still contains the broken column refs.

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('ReportDefinitions') AND type = 'U')
BEGIN
    UPDATE ReportDefinitions
    SET QueryDefinition = N'SELECT TOP 10000
    o.DisplayName AS [User],
    o.Email AS [Email],
    o.Department AS [Department],
    lp.SkuName AS [License SKU],
    lp.SkuPartNumber AS [Part Number],
    la.AssignedAt AS [Assigned Date],
    la.LastUsedAt AS [Last Active],
    DATEDIFF(day, COALESCE(la.LastUsedAt, la.AssignedAt), SYSUTCDATETIME()) AS [Inactive Days],
    lp.CostPerUnitMonthly AS [Monthly Cost]
FROM LicenseAssignments la
INNER JOIN LicensePools lp ON lp.Id = la.LicensePoolId
LEFT JOIN Objects o ON o.Id = la.ObjectId
WHERE la.IsActive = 1
  AND DATEDIFF(day, COALESCE(la.LastUsedAt, la.AssignedAt), SYSUTCDATETIME()) > 90
ORDER BY [Inactive Days] DESC, [Monthly Cost] DESC'
    WHERE Id = '11111111-aaaa-1107-0001-000000000003'
      AND QueryDefinition LIKE '%la.AssignedDate%';
END
