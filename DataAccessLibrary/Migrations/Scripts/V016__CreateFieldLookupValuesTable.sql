-- V016: Create FieldLookupValues table for managed dropdown values
-- Stores admin-managed lookup values for identity fields (Department, Division, IdentityType, etc.)

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FieldLookupValues')
BEGIN
    CREATE TABLE [FieldLookupValues] (
        [Id]         UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [FieldName]  NVARCHAR(100)    NOT NULL,
        [Value]      NVARCHAR(500)    NOT NULL,
        [SortOrder]  INT              NOT NULL DEFAULT 0,
        [IsActive]   BIT              NOT NULL DEFAULT 1,
        [CreatedAt]  DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
        [ModifiedAt] DATETIME2(7)     NULL,
        CONSTRAINT [PK_FieldLookupValues] PRIMARY KEY CLUSTERED ([Id])
    );

    -- Fast lookups by field name
    CREATE NONCLUSTERED INDEX [IX_FieldLookupValues_FieldName]
        ON [FieldLookupValues] ([FieldName], [IsActive])
        INCLUDE ([Value], [SortOrder]);

    -- Prevent duplicate values per field
    CREATE UNIQUE NONCLUSTERED INDEX [IX_FieldLookupValues_FieldName_Value]
        ON [FieldLookupValues] ([FieldName], [Value]);

    PRINT 'Created FieldLookupValues table with indexes';
END
ELSE
BEGIN
    PRINT 'FieldLookupValues table already exists - skipping';
END
GO

-- Seed default values for common fields
IF NOT EXISTS (SELECT 1 FROM FieldLookupValues)
BEGIN
    -- Identity Types
    INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder) VALUES
        (NEWID(), 'IdentityType', 'Employee', 1),
        (NEWID(), 'IdentityType', 'Contractor', 2),
        (NEWID(), 'IdentityType', 'Vendor', 3),
        (NEWID(), 'IdentityType', 'Intern', 4),
        (NEWID(), 'IdentityType', 'Service Account', 5),
        (NEWID(), 'IdentityType', 'Bot', 6);

    -- Contract Types
    INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder) VALUES
        (NEWID(), 'ContractType', 'Permanent', 1),
        (NEWID(), 'ContractType', 'Temporary', 2),
        (NEWID(), 'ContractType', 'Fixed-term', 3),
        (NEWID(), 'ContractType', 'Part-time', 4),
        (NEWID(), 'ContractType', 'Freelance', 5);

    PRINT 'Seeded default FieldLookupValues';
END
GO
