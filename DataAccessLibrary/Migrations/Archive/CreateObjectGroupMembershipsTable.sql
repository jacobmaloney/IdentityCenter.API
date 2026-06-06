-- Create ObjectGroupMemberships table for ObjectGroupMembership class
-- This table links Objects (accounts from source systems) to Groups
-- Includes Phase 1.5 fields for access review and justification tracking

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ObjectGroupMemberships')
BEGIN
    CREATE TABLE [ObjectGroupMemberships] (
        [Id] uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [ObjectId] uniqueidentifier NOT NULL,
        [GroupId] uniqueidentifier NOT NULL,
        [IsDirect] bit NOT NULL DEFAULT 1,
        [MembershipPath] nvarchar(2000) NULL,
        [AddedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [LastSyncedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [RemovedAt] datetime2 NULL,

        -- PHASE 1.5: ACCESS REVIEW & JUSTIFICATION TRACKING
        [IsActive] bit NOT NULL DEFAULT 1,
        [AddedBy] nvarchar(256) NULL,
        [Justification] nvarchar(max) NULL,
        [ExpirationDate] datetime2 NULL,
        [RemovedBy] nvarchar(256) NULL,
        [RemovalReason] nvarchar(max) NULL,

        -- Foreign keys
        CONSTRAINT [FK_ObjectGroupMemberships_Objects_ObjectId] FOREIGN KEY ([ObjectId])
            REFERENCES [Objects]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ObjectGroupMemberships_Objects_GroupId] FOREIGN KEY ([GroupId])
            REFERENCES [Objects]([Id]) ON DELETE NO ACTION,

        -- Unique constraint
        CONSTRAINT [UQ_ObjectGroupMemberships_ObjectGroup] UNIQUE ([ObjectId], [GroupId])
    );

    PRINT 'Created ObjectGroupMemberships table';
END
ELSE
BEGIN
    PRINT 'ObjectGroupMemberships table already exists';
END
GO

-- Create indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectGroupMemberships_GroupId'
    AND object_id = OBJECT_ID('ObjectGroupMemberships'))
BEGIN
    CREATE INDEX IX_ObjectGroupMemberships_GroupId
    ON [ObjectGroupMemberships] ([GroupId]);
    PRINT 'Created IX_ObjectGroupMemberships_GroupId index';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectGroupMemberships_IsActive'
    AND object_id = OBJECT_ID('ObjectGroupMemberships'))
BEGIN
    CREATE INDEX IX_ObjectGroupMemberships_IsActive
    ON [ObjectGroupMemberships] ([IsActive]);
    PRINT 'Created IX_ObjectGroupMemberships_IsActive index';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ObjectGroupMemberships_ExpirationDate'
    AND object_id = OBJECT_ID('ObjectGroupMemberships'))
BEGIN
    CREATE INDEX IX_ObjectGroupMemberships_ExpirationDate
    ON [ObjectGroupMemberships] ([ExpirationDate])
    WHERE [ExpirationDate] IS NOT NULL;
    PRINT 'Created IX_ObjectGroupMemberships_ExpirationDate filtered index';
END
GO

PRINT 'ObjectGroupMemberships table setup complete';
