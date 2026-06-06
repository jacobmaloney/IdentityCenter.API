-- V113: Separation of Duties (SoD) policy rules + detected violations.
--
-- Backs the /admin/compliance/sod page (Prompt 9). SoD scans run set-based:
-- ISoDRepository.RunScanAsync issues two SQL statements (INSERT new violations
-- where users are in BOTH conflicting groups, then UPDATE existing 'Open'
-- violations to 'Resolved' where the user is no longer in one of the groups).
-- Group membership lives in ObjectGroupMemberships (ObjectId, GroupId, IsActive).
--
-- Schema notes:
--   * Idempotent — re-running this migration is a no-op.
--   * ConditionA / ConditionB use shell-style wildcards (*) translated to SQL
--     LIKE patterns (% / _) at scan time. No raw SQL is allowed in conditions.
--   * Status values: Open / Mitigated / Resolved / Dismissed (string column,
--     enforced in repo code, not by CHECK constraint, to keep migrations cheap).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SoDRules')
BEGIN
    CREATE TABLE SoDRules (
        Id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_SoDRules_Id DEFAULT NEWSEQUENTIALID(),
        Name               NVARCHAR(200)    NOT NULL,
        Description        NVARCHAR(MAX)    NULL,
        ConditionA         NVARCHAR(500)    NOT NULL,
        ConditionB         NVARCHAR(500)    NOT NULL,
        Severity           NVARCHAR(20)     NOT NULL CONSTRAINT DF_SoDRules_Severity DEFAULT 'High',
        MitigationControl  NVARCHAR(MAX)    NULL,
        IsActive           BIT              NOT NULL CONSTRAINT DF_SoDRules_IsActive DEFAULT 1,
        CreatedAt          DATETIME2        NOT NULL CONSTRAINT DF_SoDRules_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy          NVARCHAR(200)    NULL,
        CONSTRAINT PK_SoDRules PRIMARY KEY CLUSTERED (Id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SoDViolations')
BEGIN
    CREATE TABLE SoDViolations (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_SoDViolations_Id DEFAULT NEWSEQUENTIALID(),
        RuleId          UNIQUEIDENTIFIER NOT NULL,
        ObjectId        UNIQUEIDENTIFIER NOT NULL,
        DetectedAt      DATETIME2        NOT NULL CONSTRAINT DF_SoDViolations_DetectedAt DEFAULT SYSUTCDATETIME(),
        ResolvedAt      DATETIME2        NULL,
        Status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_SoDViolations_Status DEFAULT 'Open',
        MitigationNote  NVARCHAR(MAX)    NULL,
        ReviewedBy      NVARCHAR(200)    NULL,
        ReviewedAt      DATETIME2        NULL,
        CONSTRAINT PK_SoDViolations PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_SoDViolations_SoDRules_RuleId FOREIGN KEY (RuleId) REFERENCES SoDRules (Id)
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_SoDViolations_Status'
      AND object_id = OBJECT_ID('SoDViolations'))
BEGIN
    CREATE INDEX IX_SoDViolations_Status ON SoDViolations (Status);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_SoDViolations_ObjectId'
      AND object_id = OBJECT_ID('SoDViolations'))
BEGIN
    CREATE INDEX IX_SoDViolations_ObjectId ON SoDViolations (ObjectId);
END
GO

-- Seed three example rules so the page is not empty on a fresh install.
-- Idempotent: each seed row is wrapped in WHERE NOT EXISTS by Name.

IF NOT EXISTS (SELECT 1 FROM SoDRules WHERE Name = 'PO Create + PO Approve')
BEGIN
    INSERT INTO SoDRules (Name, Description, ConditionA, ConditionB, Severity, MitigationControl)
    VALUES (
        'PO Create + PO Approve',
        'User cannot both create and approve purchase orders.',
        'Finance-AP-*',
        'Finance-AP-Approve*',
        'Critical',
        'Requires CFO approval as compensating control.'
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM SoDRules WHERE Name = 'Admin + Audit')
BEGIN
    INSERT INTO SoDRules (Name, Description, ConditionA, ConditionB, Severity, MitigationControl)
    VALUES (
        'Admin + Audit',
        'IT Administrators should not also be auditors.',
        'IT-Administrator*',
        '*Auditor*',
        'High',
        'Requires CISO sign-off if exception granted.'
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM SoDRules WHERE Name = 'HR Data Entry + HR Approval')
BEGIN
    INSERT INTO SoDRules (Name, Description, ConditionA, ConditionB, Severity, MitigationControl)
    VALUES (
        'HR Data Entry + HR Approval',
        'HR staff should not both enter and approve personnel changes.',
        'HR-DataEntry*',
        'HR-Manager*',
        'High',
        NULL
    );
END
GO
