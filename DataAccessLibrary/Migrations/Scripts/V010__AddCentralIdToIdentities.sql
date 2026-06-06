-- V010: Add CentralId column to Identities table
-- CentralId is an auto-generated unique identifier (e.g., "IC-00001")
-- used as the template key when objects are created from this identity.

-- Step 1: Add the column (separate batch to avoid compile-time column resolution error)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CentralId')
BEGIN
    ALTER TABLE Identities ADD CentralId NVARCHAR(50) NULL;
    PRINT 'Added CentralId column to Identities.';
END
ELSE
BEGIN
    PRINT 'CentralId column already exists on Identities table. Skipping column add.';
END
GO

-- Step 2: Create unique filtered index (separate batch so column exists at compile time)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Identities') AND name = 'IX_Identities_CentralId')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_Identities_CentralId
        ON Identities (CentralId)
        WHERE CentralId IS NOT NULL;
    PRINT 'Created IX_Identities_CentralId index.';
END
GO

-- Step 3: Backfill existing identities with auto-generated CentralId (separate batch)
WITH NumberedIdentities AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt, Id) AS RowNum
    FROM Identities
    WHERE CentralId IS NULL
)
UPDATE i
SET i.CentralId = 'IC-' + RIGHT('00000' + CAST(n.RowNum AS NVARCHAR(10)), 5)
FROM Identities i
INNER JOIN NumberedIdentities n ON i.Id = n.Id;

PRINT 'Backfilled existing identities with CentralId values.';
GO
