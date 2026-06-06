-- Migration: Add FallbackReviewer, UserManager, and AuditViewer roles
-- Date: 2026-02-03

-- Add FallbackReviewer role if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'FallbackReviewer')
BEGIN
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, Description, ConcurrencyStamp, Permissions, AdGroupMappings, EntraIdGroupMappings)
    VALUES (
        NEWID(),
        'FallbackReviewer',
        'FALLBACKREVIEWER',
        'Designated fallback reviewers for access reviews when primary reviewers are unavailable',
        NEWID(),
        'Review access certifications as backup, approve/revoke access when primary reviewer unavailable',
        'Fallback Reviewers;Backup Approvers;Emergency Access Team',
        'Fallback Reviewers;Backup Approvers'
    );
    PRINT 'Added FallbackReviewer role';
END
GO

-- Add UserManager role if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'UserManager')
BEGIN
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, Description, ConcurrencyStamp, Permissions, AdGroupMappings, EntraIdGroupMappings)
    VALUES (
        NEWID(),
        'UserManager',
        'USERMANAGER',
        'Can manage users and groups',
        NEWID(),
        'Create users, modify user attributes, manage group memberships',
        'User Managers;Account Managers;Identity Admins',
        'User Account Administrators;User Managers'
    );
    PRINT 'Added UserManager role';
END
GO

-- Add AuditViewer role if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'AuditViewer')
BEGIN
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, Description, ConcurrencyStamp, Permissions, AdGroupMappings, EntraIdGroupMappings)
    VALUES (
        NEWID(),
        'AuditViewer',
        'AUDITVIEWER',
        'Can view audit logs and reports',
        NEWID(),
        'View audit logs, run reports, export audit data',
        'Audit Viewers;Report Viewers;Log Analysts',
        'Audit Viewers;Reports Reader'
    );
    PRINT 'Added AuditViewer role';
END
GO

PRINT 'Migration complete: Added FallbackReviewer, UserManager, and AuditViewer roles';
GO
