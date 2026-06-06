-- Fix ObjectGroupMemberships.GroupId foreign key to reference Objects table
-- Groups are stored as objects with ObjectClass='group' in the Objects table (unified model)

-- Step 1: Drop the FK that references the legacy Groups table
IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_ObjectGroupMemberships_Groups_GroupId')
BEGIN
    ALTER TABLE [dbo].[ObjectGroupMemberships] DROP CONSTRAINT [FK_ObjectGroupMemberships_Groups_GroupId];
    PRINT 'Dropped FK_ObjectGroupMemberships_Groups_GroupId';
END

-- Step 2: Add FK that references Objects table (where groups are stored as ObjectClass='group')
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_ObjectGroupMemberships_Objects_GroupId')
BEGIN
    ALTER TABLE [dbo].[ObjectGroupMemberships]
    ADD CONSTRAINT [FK_ObjectGroupMemberships_Objects_GroupId]
    FOREIGN KEY ([GroupId]) REFERENCES [dbo].[Objects] ([Id]);
    PRINT 'Added FK_ObjectGroupMemberships_Objects_GroupId';
END

-- Verify
SELECT name, OBJECT_NAME(parent_object_id) as TableName, OBJECT_NAME(referenced_object_id) as ReferencedTable
FROM sys.foreign_keys
WHERE OBJECT_NAME(parent_object_id) = 'ObjectGroupMemberships';
