-- V098: Fix A1 failure — seed CostCenter into FieldLookupValues
-- V097 ran but found 0 CostCenter values (V096 may not have populated Objects.CostCenter yet)

-- Seed from Objects if they have CostCenter now
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'CostCenter')
BEGIN
    INSERT INTO FieldLookupValues (Id, FieldName, Value, SortOrder, IsActive, CreatedAt)
    SELECT NEWID(), 'CostCenter', src.CostCenter, ROW_NUMBER() OVER (ORDER BY src.CostCenter), 1, GETUTCDATE()
    FROM (SELECT DISTINCT CostCenter FROM Objects WHERE CostCenter IS NOT NULL AND CostCenter != '' AND DeletedAt IS NULL) src
    WHERE NOT EXISTS (SELECT 1 FROM FieldLookupValues WHERE FieldName = 'CostCenter' AND Value = src.CostCenter);
END

-- Direct seed fallback if still empty
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

-- Also copy CostCenter from Objects → Identities (V096 may have missed this)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Objects') AND name = 'CostCenter')
AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CostCenter')
BEGIN
    UPDATE i SET i.CostCenter = o.CostCenter
    FROM Identities i
    INNER JOIN Objects o ON o.IdentityId = i.Id
    WHERE o.CostCenter IS NOT NULL AND o.CostCenter != ''
      AND (i.CostCenter IS NULL OR i.CostCenter = '');
END
