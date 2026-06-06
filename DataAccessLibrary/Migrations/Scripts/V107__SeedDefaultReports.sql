-- V107: Seed Default Report Definitions
-- Adds the 5 baseline reports that ship out-of-the-box so the Report Library
-- is never empty on a fresh install. All inserts are idempotent.
--
-- Each report is keyed by Name (unique-ish handle, lowercase snake_case).
-- ConfigurationJson holds a VisualReportDefinition that the IReportExecutionEngine
-- can run directly. License Waste uses QueryDefinition (SQL-backed) so it can
-- compute InactiveDays from LicenseAssignments.LastActiveDate.

SET NOCOUNT ON;

-- 1. Inactive Users (90+ days)
IF NOT EXISTS (SELECT 1 FROM [Reports] WHERE [Name] = N'inactive_users_90d')
BEGIN
    INSERT INTO [Reports] (
        [Id], [Name], [DisplayName], [Description], [Category], [SubCategory], [Icon],
        [QueryDefinition], [ConfigurationJson], [DefaultFilters], [ParametersJson],
        [IsBuiltIn], [IsActive], [IsPublic], [RequiredRole], [Tags], [SortOrder],
        [CreatedAt], [CreatedBy]
    )
    VALUES (
        '11111111-aaaa-1107-0001-000000000001',
        N'inactive_users_90d',
        N'Inactive Users (90+ days)',
        N'Users who have not signed in within the last 90 days. Candidates for disablement or removal.',
        N'Identity', N'Stale Accounts', N'fa-user-clock',
        N'SELECT TOP 10000
    o.DisplayName AS [Display Name],
    o.Username AS [Username],
    o.Email AS [Email],
    o.Department AS [Department],
    o.LastSeenAt AS [Last Seen],
    DATEDIFF(day, o.LastSeenAt, SYSUTCDATETIME()) AS [Days Inactive]
FROM Objects o
WHERE o.DeletedAt IS NULL
  AND o.ObjectClass = ''user''
  AND (o.LastSeenAt IS NULL OR o.LastSeenAt < DATEADD(day, -90, SYSUTCDATETIME()))
ORDER BY o.LastSeenAt ASC',
        N'{}',
        N'', N'[]',
        1, 1, 1, NULL, N'identity,inactive,stale,users', 10,
        SYSUTCDATETIME(), N'System'
    );
END;

-- 2. Users Without Managers
IF NOT EXISTS (SELECT 1 FROM [Reports] WHERE [Name] = N'users_without_managers')
BEGIN
    INSERT INTO [Reports] (
        [Id], [Name], [DisplayName], [Description], [Category], [SubCategory], [Icon],
        [QueryDefinition], [ConfigurationJson], [DefaultFilters], [ParametersJson],
        [IsBuiltIn], [IsActive], [IsPublic], [RequiredRole], [Tags], [SortOrder],
        [CreatedAt], [CreatedBy]
    )
    VALUES (
        '11111111-aaaa-1107-0001-000000000002',
        N'users_without_managers',
        N'Users Without Managers',
        N'Active users who have no manager assigned. These accounts often fail access reviews and lack a clear approver.',
        N'Compliance', N'Org Hygiene', N'fa-user-slash',
        N'SELECT TOP 10000
    o.DisplayName AS [Display Name],
    o.Username AS [Username],
    o.Email AS [Email],
    o.Department AS [Department],
    o.JobTitle AS [Job Title]
FROM Objects o
WHERE o.DeletedAt IS NULL
  AND o.IsActive = 1
  AND o.ObjectClass = ''user''
  AND o.ManagerObjectId IS NULL
ORDER BY o.Department, o.DisplayName',
        N'{}',
        N'', N'[]',
        1, 1, 1, NULL, N'compliance,manager,hygiene', 20,
        SYSUTCDATETIME(), N'System'
    );
END;

-- 3. License Waste Report (Otis demo opener -- must work end-to-end)
IF NOT EXISTS (SELECT 1 FROM [Reports] WHERE [Name] = N'license_waste_report')
BEGIN
    INSERT INTO [Reports] (
        [Id], [Name], [DisplayName], [Description], [Category], [SubCategory], [Icon],
        [QueryDefinition], [ConfigurationJson], [DefaultFilters], [ParametersJson],
        [IsBuiltIn], [IsActive], [IsPublic], [RequiredRole], [Tags], [SortOrder],
        [CreatedAt], [CreatedBy]
    )
    VALUES (
        '11111111-aaaa-1107-0001-000000000003',
        N'license_waste_report',
        N'License Waste Report',
        N'Assigned licenses where the user has not been active in 90+ days. Estimated monthly waste = sum of CostPerUnitMonthly per assignment.',
        N'Compliance', N'License Optimization', N'fa-coins',
        N'SELECT TOP 10000
    o.DisplayName AS [User],
    o.Email AS [Email],
    o.Department AS [Department],
    lp.SkuName AS [License SKU],
    lp.SkuPartNumber AS [Part Number],
    la.AssignedDate AS [Assigned Date],
    la.LastActiveDate AS [Last Active],
    DATEDIFF(day, COALESCE(la.LastActiveDate, la.AssignedDate), SYSUTCDATETIME()) AS [Inactive Days],
    lp.CostPerUnitMonthly AS [Monthly Cost]
FROM LicenseAssignments la
INNER JOIN LicensePools lp ON lp.Id = la.LicensePoolId
LEFT JOIN Objects o ON o.Id = la.ObjectId
WHERE la.IsActive = 1
  AND DATEDIFF(day, COALESCE(la.LastActiveDate, la.AssignedDate), SYSUTCDATETIME()) > 90
ORDER BY [Inactive Days] DESC, [Monthly Cost] DESC',
        N'{}',
        N'', N'[]',
        1, 1, 1, NULL, N'license,waste,cost,optimization,m365,entra', 30,
        SYSUTCDATETIME(), N'System'
    );
END;

-- 4. Non-Expiring Passwords
IF NOT EXISTS (SELECT 1 FROM [Reports] WHERE [Name] = N'non_expiring_passwords')
BEGIN
    INSERT INTO [Reports] (
        [Id], [Name], [DisplayName], [Description], [Category], [SubCategory], [Icon],
        [QueryDefinition], [ConfigurationJson], [DefaultFilters], [ParametersJson],
        [IsBuiltIn], [IsActive], [IsPublic], [RequiredRole], [Tags], [SortOrder],
        [CreatedAt], [CreatedBy]
    )
    VALUES (
        '11111111-aaaa-1107-0001-000000000004',
        N'non_expiring_passwords',
        N'Non-Expiring Passwords',
        N'Active user accounts whose passwords are flagged as never-expiring. Common policy violation and audit finding.',
        N'Security', N'Password Policy', N'fa-key',
        N'-- Visual report: see ConfigurationJson',
        N'{"DataSource":"Objects","ObjectClassFilter":"user","Columns":[{"Field":"DisplayName","Label":"Display Name","Order":0,"IsAttribute":false,"FieldType":"string"},{"Field":"Username","Label":"Username","Order":1,"IsAttribute":false,"FieldType":"string"},{"Field":"Email","Label":"Email","Order":2,"IsAttribute":false,"FieldType":"string"},{"Field":"Department","Label":"Department","Order":3,"IsAttribute":false,"FieldType":"string"},{"Field":"PasswordLastSet","Label":"Password Last Set","Order":4,"IsAttribute":false,"FieldType":"date"},{"Field":"PasswordNeverExpires","Label":"Never Expires","Order":5,"IsAttribute":false,"FieldType":"boolean"}],"Filters":[{"Field":"PasswordNeverExpires","Operator":"equals","Value":"1","IsAttribute":false}],"SortBy":[{"Field":"DisplayName","Direction":"asc","IsAttribute":false}],"MaxRows":null,"IncludeInactive":false}',
        N'', N'[]',
        1, 1, 1, NULL, N'security,password,policy,audit', 40,
        SYSUTCDATETIME(), N'System'
    );
END;

-- 5. Group Membership Summary
IF NOT EXISTS (SELECT 1 FROM [Reports] WHERE [Name] = N'group_membership_summary')
BEGIN
    INSERT INTO [Reports] (
        [Id], [Name], [DisplayName], [Description], [Category], [SubCategory], [Icon],
        [QueryDefinition], [ConfigurationJson], [DefaultFilters], [ParametersJson],
        [IsBuiltIn], [IsActive], [IsPublic], [RequiredRole], [Tags], [SortOrder],
        [CreatedAt], [CreatedBy]
    )
    VALUES (
        '11111111-aaaa-1107-0001-000000000005',
        N'group_membership_summary',
        N'Group Membership Summary',
        N'All synced groups with member counts and source. Useful as a starting point for group-cleanup or access-review prep.',
        N'Access', N'Groups', N'fa-users',
        N'SELECT TOP 10000
    o.DisplayName AS [Group Name],
    o.CN AS [CN],
    o.Description AS [Description],
    o.SourceType AS [Source],
    (SELECT COUNT(*) FROM ObjectGroupMemberships gm WHERE gm.GroupId = o.Id) AS [Member Count]
FROM Objects o
WHERE o.DeletedAt IS NULL
  AND o.IsActive = 1
  AND o.ObjectClass = ''group''
ORDER BY [Member Count] DESC, o.DisplayName',
        N'{}',
        N'', N'[]',
        1, 1, 1, NULL, N'access,groups,membership,review', 50,
        SYSUTCDATETIME(), N'System'
    );
END;

GO
