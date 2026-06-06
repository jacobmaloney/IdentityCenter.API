-- V100: Insight Policy Configuration — configurable required fields and thresholds

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('InsightPolicies') AND type = 'U')
BEGIN
    CREATE TABLE InsightPolicies (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        PolicyName NVARCHAR(200) NOT NULL,
        IsDefault BIT NOT NULL DEFAULT 0,

        -- Required fields (show warning chip if missing)
        RequireDepartment BIT NOT NULL DEFAULT 1,
        RequireManager BIT NOT NULL DEFAULT 1,
        RequireCostCenter BIT NOT NULL DEFAULT 0,
        RequireEmployeeId BIT NOT NULL DEFAULT 0,
        RequireEmail BIT NOT NULL DEFAULT 1,
        RequirePhone BIT NOT NULL DEFAULT 0,
        RequireOffice BIT NOT NULL DEFAULT 0,

        -- Thresholds
        InactiveDaysWarning INT NOT NULL DEFAULT 30,
        InactiveDaysCritical INT NOT NULL DEFAULT 90,
        PasswordStaleDays INT NOT NULL DEFAULT 90,

        -- Security flags
        FlagKerberoastable BIT NOT NULL DEFAULT 1,
        FlagUnconstrainedDelegation BIT NOT NULL DEFAULT 1,
        FlagPasswordNeverExpires BIT NOT NULL DEFAULT 1,
        FlagAdminSDHolder BIT NOT NULL DEFAULT 1,
        FlagPrivilegedGroups BIT NOT NULL DEFAULT 1,

        -- License thresholds
        LicenseWarningPercent INT NOT NULL DEFAULT 90,
        LicenseCriticalPercent INT NOT NULL DEFAULT 100,

        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ModifiedAt DATETIME2 NULL
    );

    -- Seed default policy
    INSERT INTO InsightPolicies (Id, PolicyName, IsDefault)
    VALUES (NEWID(), 'Default Insight Policy', 1);
END
