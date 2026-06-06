-- V097: Auto-discover organizational field values into FieldLookupValues
-- Populates Cost Center, Division, Company, Office, Building, Department
-- from existing data in Objects and Identities tables

-- ═══════════════════════════════════════════════════════════════
-- COST CENTER — from Objects (seeded by V096) + direct fallback
-- ═══════════════════════════════════════════════════════════════

-- First try auto-discover from Objects
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'CostCenter')
BEGIN
    INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt)
    SELECT NEWID(), 'CostCenter', src.CostCenter, ROW_NUMBER() OVER (ORDER BY src.CostCenter), 1, GETUTCDATE()
    FROM (SELECT DISTINCT CostCenter FROM Objects WHERE CostCenter IS NOT NULL AND CostCenter != '' AND DeletedAt IS NULL) src
    WHERE NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = 'CostCenter' AND Value = src.CostCenter);
END

-- Direct seed fallback if auto-discover found nothing
IF NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = 'CostCenter')
BEGIN
    INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt) VALUES
    (NEWID(), 'CostCenter', 'CC-1100-DC-EAST', 1, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-1200-DC-WEST', 2, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-1300-DC-CENT', 3, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-2100-WEB-PROD', 4, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-2200-APP-PROD', 5, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-3100-DB-PROD', 6, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-3200-DB-DEV', 7, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-4100-FILEPRINT', 8, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-4200-EXCHANGE', 9, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5100-FIN-INFRA', 10, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5200-ENG-INFRA', 11, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5300-SALES-OPS', 12, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5400-OPS-INFRA', 13, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5500-HR-ADMIN', 14, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5600-MKT-TECH', 15, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5700-LEGAL-OPS', 16, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5800-DATA-PLAT', 17, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-5900-SUPPORT', 18, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-9900-UNALLOC', 19, 1, GETUTCDATE()),
    (NEWID(), 'CostCenter', 'CC-0000-GEN', 20, 1, GETUTCDATE());
END

-- ═══════════════════════════════════════════════════════════════
-- DIVISION — from Objects + Identities
-- ═══════════════════════════════════════════════════════════════
INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt)
SELECT NEWID(), 'Division', src.Division, ROW_NUMBER() OVER (ORDER BY src.Division), 1, GETUTCDATE()
FROM (
    SELECT DISTINCT Division FROM Objects WHERE Division IS NOT NULL AND Division != '' AND DeletedAt IS NULL
    UNION
    SELECT DISTINCT Division FROM Identities WHERE Division IS NOT NULL AND Division != ''
) src
WHERE NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = 'Division' AND Value = src.Division);

-- ═══════════════════════════════════════════════════════════════
-- COMPANY — from Objects + Identities
-- ═══════════════════════════════════════════════════════════════
INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt)
SELECT NEWID(), 'Company', src.Company, ROW_NUMBER() OVER (ORDER BY src.Company), 1, GETUTCDATE()
FROM (
    SELECT DISTINCT Company FROM Objects WHERE Company IS NOT NULL AND Company != '' AND DeletedAt IS NULL
    UNION
    SELECT DISTINCT Company FROM Identities WHERE Company IS NOT NULL AND Company != ''
) src
WHERE NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = 'Company' AND Value = src.Company);

-- ═══════════════════════════════════════════════════════════════
-- OFFICE — from Objects + Identities
-- ═══════════════════════════════════════════════════════════════
INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt)
SELECT NEWID(), 'Office', src.Office, ROW_NUMBER() OVER (ORDER BY src.Office), 1, GETUTCDATE()
FROM (
    SELECT DISTINCT Office FROM Objects WHERE Office IS NOT NULL AND Office != '' AND DeletedAt IS NULL
    UNION
    SELECT DISTINCT Office FROM Identities WHERE Office IS NOT NULL AND Office != ''
) src
WHERE NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = 'Office' AND Value = src.Office);

-- ═══════════════════════════════════════════════════════════════
-- DEPARTMENT — from Objects + Identities (may already have some)
-- ═══════════════════════════════════════════════════════════════
INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt)
SELECT NEWID(), 'Department', src.Department, ROW_NUMBER() OVER (ORDER BY src.Department), 1, GETUTCDATE()
FROM (
    SELECT DISTINCT Department FROM Objects WHERE Department IS NOT NULL AND Department != '' AND DeletedAt IS NULL
    UNION
    SELECT DISTINCT Department FROM Identities WHERE Department IS NOT NULL AND Department != ''
) src
WHERE NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = 'Department' AND Value = src.Department);

-- ═══════════════════════════════════════════════════════════════
-- BUILDING — from Identities only (not on Objects table)
-- ═══════════════════════════════════════════════════════════════
INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt)
SELECT NEWID(), 'Building', src.Building, ROW_NUMBER() OVER (ORDER BY src.Building), 1, GETUTCDATE()
FROM (SELECT DISTINCT Building FROM Identities WHERE Building IS NOT NULL AND Building != '') src
WHERE NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = 'Building' AND Value = src.Building);
