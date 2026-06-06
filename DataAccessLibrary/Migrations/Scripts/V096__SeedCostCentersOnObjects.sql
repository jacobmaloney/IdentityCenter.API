-- V096: Seed 20 cost centers across user and computer Objects
-- Gives the License Center Cost Center tab real chargeback data

-- ═══════════════════════════════════════════════════════════════
-- COMPUTERS — assign by server naming prefix + department combos
-- ═══════════════════════════════════════════════════════════════

-- Data center infrastructure
UPDATE Objects SET CostCenter = 'CC-1100-DC-EAST' WHERE ObjectClass = 'computer' AND (CN LIKE 'DC-%' OR CN LIKE 'SRV-%') AND Department = 'IT Infrastructure' AND City IN ('New York', 'Boston', 'Atlanta') AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-1200-DC-WEST' WHERE ObjectClass = 'computer' AND (CN LIKE 'DC-%' OR CN LIKE 'SRV-%') AND Department = 'IT Infrastructure' AND City IN ('Seattle', 'San Francisco', 'Denver', 'Phoenix') AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-1300-DC-CENT' WHERE ObjectClass = 'computer' AND (CN LIKE 'DC-%' OR CN LIKE 'SRV-%') AND Department = 'IT Infrastructure' AND CostCenter IS NULL;

-- Web & app servers by region
UPDATE Objects SET CostCenter = 'CC-2100-WEB-PROD' WHERE ObjectClass = 'computer' AND CN LIKE 'WEB-%' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-2200-APP-PROD' WHERE ObjectClass = 'computer' AND CN LIKE 'APP-%' AND CostCenter IS NULL;

-- Database servers
UPDATE Objects SET CostCenter = 'CC-3100-DB-PROD' WHERE ObjectClass = 'computer' AND CN LIKE 'DB-%' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-3200-DB-DEV' WHERE ObjectClass = 'computer' AND CN LIKE 'SQL-%' AND CostCenter IS NULL;

-- File & print
UPDATE Objects SET CostCenter = 'CC-4100-FILEPRINT' WHERE ObjectClass = 'computer' AND (CN LIKE 'FILE-%' OR CN LIKE 'PRINT-%') AND CostCenter IS NULL;

-- Exchange
UPDATE Objects SET CostCenter = 'CC-4200-EXCHANGE' WHERE ObjectClass = 'computer' AND CN LIKE 'EXCH-%' AND CostCenter IS NULL;

-- Department-based for remaining computers
UPDATE Objects SET CostCenter = 'CC-5100-FIN-INFRA' WHERE ObjectClass = 'computer' AND Department = 'Finance' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5200-ENG-INFRA' WHERE ObjectClass = 'computer' AND Department = 'Engineering' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5300-SALES-OPS' WHERE ObjectClass = 'computer' AND Department = 'Sales' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5400-OPS-INFRA' WHERE ObjectClass = 'computer' AND Department = 'Operations' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5500-HR-ADMIN' WHERE ObjectClass = 'computer' AND Department = 'HR' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5600-MKT-TECH' WHERE ObjectClass = 'computer' AND Department = 'Marketing' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5700-LEGAL-OPS' WHERE ObjectClass = 'computer' AND Department = 'Legal' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5800-DATA-PLAT' WHERE ObjectClass = 'computer' AND Department = 'Data Analytics' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5900-SUPPORT' WHERE ObjectClass = 'computer' AND Department = 'Customer Support' AND CostCenter IS NULL;

-- Catch-all for any remaining computers
UPDATE Objects SET CostCenter = 'CC-9900-UNALLOC' WHERE ObjectClass = 'computer' AND CostCenter IS NULL AND DeletedAt IS NULL AND IsActive = 1;

-- ═══════════════════════════════════════════════════════════════
-- USERS — assign by department (matching the 20 cost center scheme)
-- ═══════════════════════════════════════════════════════════════

UPDATE Objects SET CostCenter = 'CC-5100-FIN-INFRA' WHERE ObjectClass = 'user' AND Department = 'Finance' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5200-ENG-INFRA' WHERE ObjectClass = 'user' AND Department = 'Engineering' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5300-SALES-OPS' WHERE ObjectClass = 'user' AND Department = 'Sales' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5400-OPS-INFRA' WHERE ObjectClass = 'user' AND Department = 'Operations' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5500-HR-ADMIN' WHERE ObjectClass = 'user' AND Department = 'HR' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-1300-DC-CENT' WHERE ObjectClass = 'user' AND (Department = 'IT Infrastructure' OR Department = 'IT') AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5600-MKT-TECH' WHERE ObjectClass = 'user' AND Department = 'Marketing' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5700-LEGAL-OPS' WHERE ObjectClass = 'user' AND Department = 'Legal' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5800-DATA-PLAT' WHERE ObjectClass = 'user' AND Department = 'Data Analytics' AND CostCenter IS NULL;
UPDATE Objects SET CostCenter = 'CC-5900-SUPPORT' WHERE ObjectClass = 'user' AND Department = 'Customer Support' AND CostCenter IS NULL;

-- Catch-all for remaining users
UPDATE Objects SET CostCenter = 'CC-9900-UNALLOC' WHERE ObjectClass = 'user' AND Department IS NOT NULL AND CostCenter IS NULL AND DeletedAt IS NULL AND IsActive = 1;

-- ═══════════════════════════════════════════════════════════════
-- COPY TO IDENTITIES — Organization Center reads from Identities table
-- Also copy Division and Company from Objects where missing
-- ═══════════════════════════════════════════════════════════════

UPDATE i SET i.CostCenter = o.CostCenter
FROM Identities i
INNER JOIN Objects o ON o.IdentityId = i.Id
WHERE o.CostCenter IS NOT NULL AND o.CostCenter != ''
  AND (i.CostCenter IS NULL OR i.CostCenter = '');

UPDATE i SET i.Division = o.Division
FROM Identities i
INNER JOIN Objects o ON o.IdentityId = i.Id
WHERE o.Division IS NOT NULL AND o.Division != ''
  AND (i.Division IS NULL OR i.Division = '');

UPDATE i SET i.Company = o.Company
FROM Identities i
INNER JOIN Objects o ON o.IdentityId = i.Id
WHERE o.Company IS NOT NULL AND o.Company != ''
  AND (i.Company IS NULL OR i.Company = '');

UPDATE i SET i.Office = o.Office
FROM Identities i
INNER JOIN Objects o ON o.IdentityId = i.Id
WHERE o.Office IS NOT NULL AND o.Office != ''
  AND (i.Office IS NULL OR i.Office = '');

UPDATE i SET i.Department = o.Department
FROM Identities i
INNER JOIN Objects o ON o.IdentityId = i.Id
WHERE o.Department IS NOT NULL AND o.Department != ''
  AND (i.Department IS NULL OR i.Department = '');
