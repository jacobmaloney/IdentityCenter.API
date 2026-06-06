-- Migration: Fix All Report Queries
-- Date: 2026-01-04
-- Description: Updates all report queries to use correct table and column names
-- Tables: Objects (not IdentityObjects, not Groups), Identities (not Persons)

-- =====================================
-- GROUP REPORTS - Use Objects WHERE ObjectClass = 'group'
-- =====================================

UPDATE Reports
SET QueryDefinition = 'SELECT Id,
    COALESCE(DisplayName, CN, SUBSTRING(DN, 4, CHARINDEX('','', DN) - 4)) as GroupName,
    DN, Email, OwnerObjectId, IsActive, FirstSyncedAt, LastSyncedAt
    FROM Objects WHERE ObjectClass = ''group'' ORDER BY GroupName'
WHERE Name = 'all_groups';

UPDATE Reports
SET QueryDefinition = 'SELECT g.Id,
    COALESCE(g.DisplayName, g.CN, SUBSTRING(g.DN, 4, CHARINDEX('','', g.DN) - 4)) as GroupName,
    g.DN, g.IsActive, g.FirstSyncedAt
    FROM Objects g
    LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
    WHERE g.ObjectClass = ''group'' AND ogm.Id IS NULL
    ORDER BY GroupName'
WHERE Name = 'empty_groups';

UPDATE Reports
SET QueryDefinition = 'SELECT g.Id,
    COALESCE(g.DisplayName, g.CN) as GroupName,
    g.DN, COUNT(ogm.Id) as MemberCount
    FROM Objects g
    INNER JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
    WHERE g.ObjectClass = ''group''
    GROUP BY g.Id, g.DisplayName, g.CN, g.DN
    HAVING COUNT(ogm.Id) > 100
    ORDER BY MemberCount DESC'
WHERE Name = 'large_groups';

UPDATE Reports
SET QueryDefinition = 'SELECT Id,
    COALESCE(DisplayName, CN) as GroupName,
    DN, IsActive, FirstSyncedAt
    FROM Objects
    WHERE ObjectClass = ''group'' AND OwnerObjectId IS NULL
    ORDER BY GroupName'
WHERE Name = 'groups_without_owner';

UPDATE Reports
SET QueryDefinition = 'SELECT Id,
    COALESCE(DisplayName, CN) as GroupName,
    Email, DN, IsActive
    FROM Objects
    WHERE ObjectClass = ''group'' AND Email IS NOT NULL AND Email <> ''''
    ORDER BY GroupName'
WHERE Name = 'groups_with_email';

UPDATE Reports
SET QueryDefinition = 'SELECT Id,
    COALESCE(DisplayName, CN) as GroupName,
    DN, IsActive, FirstSyncedAt
    FROM Objects
    WHERE ObjectClass = ''group'' AND (Email IS NULL OR Email = '''')
    ORDER BY GroupName'
WHERE Name = 'groups_without_email';

UPDATE Reports
SET QueryDefinition = 'SELECT child.Id as ChildGroupId,
    COALESCE(child.DisplayName, child.CN) as ChildGroupName,
    parent.Id as ParentGroupId,
    COALESCE(parent.DisplayName, parent.CN) as ParentGroupName
    FROM Objects child
    INNER JOIN ObjectGroupMemberships ogm ON child.Id = ogm.ObjectId
    INNER JOIN Objects parent ON ogm.GroupId = parent.Id
    WHERE child.ObjectClass = ''group'' AND parent.ObjectClass = ''group''
    ORDER BY ParentGroupName, ChildGroupName'
WHERE Name = 'nested_group_membership';

UPDATE Reports
SET QueryDefinition = 'SELECT g.Id,
    COALESCE(g.DisplayName, g.CN) as GroupName,
    COUNT(ogm.Id) as MemberCount
    FROM Objects g
    LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
    WHERE g.ObjectClass = ''group''
    GROUP BY g.Id, g.DisplayName, g.CN
    ORDER BY MemberCount DESC'
WHERE Name = 'group_member_counts';

-- =====================================
-- SECURITY REPORTS - Use Objects table
-- =====================================

UPDATE Reports
SET QueryDefinition = 'SELECT DISTINCT o.Id, o.DisplayName, o.Username, o.Email, o.ObjectClass, o.IsActive,
    g.DisplayName as GroupName, o.FirstSyncedAt, o.LastSyncedAt
    FROM Objects o
    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
    INNER JOIN Objects g ON ogm.GroupId = g.Id
    WHERE g.ObjectClass = ''group''
    AND (g.DisplayName LIKE ''%Admin%'' OR g.CN LIKE ''%Admin%''
         OR g.DisplayName LIKE ''%Privileged%'' OR g.CN LIKE ''%Privileged%'')
    ORDER BY o.DisplayName'
WHERE Name = 'privileged_accounts';

UPDATE Reports
SET QueryDefinition = 'SELECT Id, DisplayName, Username, Email, ObjectClass, IsActive, FirstSyncedAt, LastSyncedAt
    FROM Objects
    WHERE ObjectClass = ''user'' AND (
        Username LIKE ''svc%'' OR
        Username LIKE ''service%''
    )
    ORDER BY Username'
WHERE Name = 'service_accounts';

UPDATE Reports
SET QueryDefinition = 'SELECT Id, DisplayName, Username, Email, ObjectClass, IsActive, FirstSyncedAt, LastSyncedAt
    FROM Objects
    WHERE ObjectClass = ''user'' AND LastSyncedAt < DATEADD(DAY, -90, GETDATE())
    ORDER BY LastSyncedAt ASC'
WHERE Name = 'stale_accounts';

UPDATE Reports
SET QueryDefinition = 'SELECT Id, DisplayName, Username, Email, PasswordLastSet,
    DATEDIFF(DAY, PasswordLastSet, GETDATE()) as PasswordAgeDays
    FROM Objects
    WHERE ObjectClass = ''user'' AND PasswordLastSet IS NOT NULL
    ORDER BY PasswordLastSet ASC',
    DisplayName = 'Password Age Report',
    Description = 'Accounts by password age - identify expired or aging passwords'
WHERE Name = 'accounts_password_never_expires';

UPDATE Reports
SET QueryDefinition = 'SELECT Id, DisplayName, CN, DN, IsActive, FirstSyncedAt, LastSyncedAt
    FROM Objects WHERE ObjectClass = ''computer''
    ORDER BY DisplayName'
WHERE Name = 'computer_accounts';

UPDATE Reports
SET QueryDefinition = 'SELECT o.Id, o.DisplayName, o.Username, o.Email, o.IsActive,
    COUNT(ogm.Id) as GroupCount
    FROM Objects o
    INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
    WHERE o.ObjectClass = ''user'' AND o.IsActive = 0
    GROUP BY o.Id, o.DisplayName, o.Username, o.Email, o.IsActive
    HAVING COUNT(ogm.Id) > 0
    ORDER BY GroupCount DESC',
    DisplayName = 'Disabled Accounts with Group Membership',
    Description = 'Disabled accounts that still have group memberships - security risk'
WHERE Name = 'high_risk_accounts';

-- =====================================
-- IDENTITY REPORTS - Use correct column names
-- =====================================

UPDATE Reports
SET QueryDefinition = 'SELECT COALESCE(Department, ''(No Department)'') as Department, COUNT(*) as Count FROM Identities GROUP BY Department ORDER BY Count DESC'
WHERE Name = 'identities_by_department';

UPDATE Reports
SET QueryDefinition = 'SELECT COALESCE(JobTitle, ''(No Title)'') as JobTitle, COUNT(*) as Count FROM Identities GROUP BY JobTitle ORDER BY Count DESC'
WHERE Name = 'identities_by_job_title';

UPDATE Reports
SET QueryDefinition = 'SELECT i.Id, i.DisplayName, i.FirstName, i.LastName, i.PrimaryEmail,
    i.Department, i.JobTitle, i.IsActive, COUNT(o.Id) as ObjectCount
    FROM Identities i
    INNER JOIN Objects o ON i.Id = o.IdentityId
    GROUP BY i.Id, i.DisplayName, i.FirstName, i.LastName, i.PrimaryEmail,
        i.Department, i.JobTitle, i.IsActive
    HAVING COUNT(o.Id) > 1
    ORDER BY ObjectCount DESC'
WHERE Name = 'identities_multiple_objects';

-- Replace identities_by_location with all_user_objects (location columns don't exist)
UPDATE Reports
SET Name = 'all_user_objects',
    DisplayName = 'All User Objects',
    Description = 'All user accounts from directory sources',
    QueryDefinition = 'SELECT Id, DisplayName, Username, Email, Department, JobTitle, DN, IsActive, FirstSyncedAt, LastSyncedAt
    FROM Objects WHERE ObjectClass = ''user'' ORDER BY DisplayName',
    Tags = 'identity,users,objects,inventory'
WHERE Name = 'identities_by_location';

-- Replace contractor_identities with unlinked_identities (EmployeeType doesn't exist)
UPDATE Reports
SET Name = 'unlinked_identities',
    DisplayName = 'Unlinked Identities',
    Description = 'Identities without any linked directory objects',
    QueryDefinition = 'SELECT i.* FROM Identities i
    LEFT JOIN Objects o ON i.Id = o.IdentityId
    WHERE o.Id IS NULL',
    Tags = 'identity,unlinked,cleanup'
WHERE Name = 'contractor_identities';

PRINT 'Successfully updated all report queries to use correct table and column names';
