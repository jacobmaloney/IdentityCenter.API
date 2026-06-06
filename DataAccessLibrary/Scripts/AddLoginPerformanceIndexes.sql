-- Login Performance Indexes
-- Critical indexes for authentication and user lookup
SET QUOTED_IDENTIFIER ON;
GO

PRINT 'Adding login performance indexes...';

-- Objects lookup by email (used for login matching)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_Email_Active')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_Email_Active
    ON Objects(Email, IsActive)
    INCLUDE (Id, DisplayName, Username, SourceConnectionId, IdentityId);
    PRINT '   Created IX_Objects_Email_Active';
END
ELSE
    PRINT '   IX_Objects_Email_Active already exists';

-- Objects lookup by username (used for login matching)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_Username_Active')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_Username_Active
    ON Objects(Username, IsActive)
    INCLUDE (Id, DisplayName, Email, SourceConnectionId, IdentityId);
    PRINT '   Created IX_Objects_Username_Active';
END
ELSE
    PRINT '   IX_Objects_Username_Active already exists';

-- Identities lookup by email (used for login)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Identities_Email_Active')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Identities_Email_Active
    ON Identities(PrimaryEmail, IsActive)
    INCLUDE (Id, DisplayName, FirstName, LastName);
    PRINT '   Created IX_Identities_Email_Active';
END
ELSE
    PRINT '   IX_Identities_Email_Active already exists';

-- Objects source lookup (used during sync and DN resolution)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_SourceConnection_Active')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_SourceConnection_Active
    ON Objects(SourceConnectionId, IsActive)
    INCLUDE (SourceUniqueId, IdentityId, DisplayName, Email, ObjectClass, DN);
    PRINT '   Created IX_Objects_SourceConnection_Active';
END
ELSE
    PRINT '   IX_Objects_SourceConnection_Active already exists';

-- Objects DN lookup (used for manager/owner resolution)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Objects_DN')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Objects_DN
    ON Objects(DN)
    INCLUDE (Id, DisplayName, SourceConnectionId);
    PRINT '   Created IX_Objects_DN';
END
ELSE
    PRINT '   IX_Objects_DN already exists';

-- Groups DN lookup (used for membership resolution)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Groups_DistinguishedName')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Groups_DistinguishedName
    ON Groups(DistinguishedName)
    INCLUDE (Id, Name, SourceConnectionId);
    PRINT '   Created IX_Groups_DistinguishedName';
END
ELSE
    PRINT '   IX_Groups_DistinguishedName already exists';

-- Update statistics
PRINT 'Updating statistics...';
UPDATE STATISTICS Objects;
UPDATE STATISTICS Identities;
UPDATE STATISTICS Groups;

PRINT 'Done - login performance indexes created!';
