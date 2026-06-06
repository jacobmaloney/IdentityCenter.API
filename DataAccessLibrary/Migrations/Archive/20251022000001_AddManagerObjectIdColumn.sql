-- Add ManagerObjectId column to Objects table
-- This enables object-to-object manager relationships (e.g., AD manager attribute)

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Objects]') AND name = 'ManagerObjectId')
BEGIN
    ALTER TABLE [dbo].[Objects]
    ADD [ManagerObjectId] uniqueidentifier NULL;

    PRINT 'Added ManagerObjectId column to Objects table';
END
ELSE
BEGIN
    PRINT 'ManagerObjectId column already exists in Objects table';
END
GO

-- Add foreign key constraint
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Objects_Objects_ManagerObjectId')
BEGIN
    ALTER TABLE [dbo].[Objects]
    ADD CONSTRAINT [FK_Objects_Objects_ManagerObjectId]
    FOREIGN KEY ([ManagerObjectId])
    REFERENCES [dbo].[Objects] ([Id]);

    PRINT 'Added FK_Objects_Objects_ManagerObjectId foreign key';
END
ELSE
BEGIN
    PRINT 'FK_Objects_Objects_ManagerObjectId foreign key already exists';
END
GO

-- Create index for performance
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Objects_ManagerObjectId' AND object_id = OBJECT_ID(N'[dbo].[Objects]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Objects_ManagerObjectId]
    ON [dbo].[Objects] ([ManagerObjectId])
    WHERE [ManagerObjectId] IS NOT NULL;

    PRINT 'Created IX_Objects_ManagerObjectId index';
END
ELSE
BEGIN
    PRINT 'IX_Objects_ManagerObjectId index already exists';
END
GO

PRINT 'ManagerObjectId migration completed successfully!';
