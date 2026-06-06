-- Make PersonId nullable in Identities table to support built-in accounts
-- Built-in accounts (Administrator, Guest, KRBTGT, etc.) should not have Person records

-- First, drop the foreign key constraint
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Identities_Persons_PersonId')
BEGIN
    ALTER TABLE [Identities] DROP CONSTRAINT [FK_Identities_Persons_PersonId];
    PRINT 'Dropped FK_Identities_Persons_PersonId constraint';
END

-- Make the PersonId column nullable
ALTER TABLE [Identities] ALTER COLUMN [PersonId] UNIQUEIDENTIFIER NULL;
PRINT 'Made PersonId column nullable';

-- Re-create the foreign key constraint (now allowing NULL)
ALTER TABLE [Identities]
    ADD CONSTRAINT [FK_Identities_Persons_PersonId]
    FOREIGN KEY ([PersonId]) REFERENCES [Persons]([Id]);
PRINT 'Re-created FK_Identities_Persons_PersonId constraint (allowing NULL)';

-- Also update the stored procedure to handle NULL PersonId correctly
-- The stored procedure already has @PersonId UNIQUEIDENTIFIER = NULL so it should work
PRINT 'PersonId is now nullable - built-in accounts can have NULL PersonId';
