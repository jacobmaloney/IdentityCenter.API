-- V079: Enriched SQL Server inventory collected by remote agent or network scan

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SqlServerInventory')
BEGIN
    CREATE TABLE SqlServerInventory (
        Id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        ObjectId                NVARCHAR(450) NULL,         -- FK to Objects.Id (null if not yet matched)
        DiscoveryMethod         NVARCHAR(50) NOT NULL,      -- 'ActiveDirectory' | 'NetworkScan' | 'RemoteAgent'
        ServerName              NVARCHAR(255) NOT NULL,
        Fqdn                    NVARCHAR(500) NULL,
        IpAddress               NVARCHAR(50) NULL,
        Port                    INT NULL DEFAULT 1433,
        InstanceName            NVARCHAR(255) NULL,         -- NULL = default instance
        SqlEdition              NVARCHAR(100) NULL,
        SqlVersion              NVARCHAR(100) NULL,
        SqlVersionMajor         INT NULL,
        CpuCores                INT NULL,
        MemoryGb                INT NULL,
        OsName                  NVARCHAR(255) NULL,
        OsVersion               NVARCHAR(100) NULL,
        IsOnline                BIT NOT NULL DEFAULT 1,
        IsProduction            BIT NULL,                   -- null = unknown
        OwnerId                 NVARCHAR(450) NULL,         -- FK to Objects.Id (the owner user/group)
        OwnerAssignedAt         DATETIME2 NULL,
        OwnerAssignedBy         NVARCHAR(256) NULL,
        ComplianceStatus        NVARCHAR(50) NULL,          -- 'Licensed' | 'Unlicensed' | 'OverLicensed' | 'Unknown' | 'Violation'
        ComplianceCheckedAt     DATETIME2 NULL,
        LastDiscoveredAt        DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastAgentContactAt      DATETIME2 NULL,
        CreatedAt               DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt               DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE INDEX IX_SqlServerInventory_ObjectId ON SqlServerInventory(ObjectId) WHERE ObjectId IS NOT NULL;
    CREATE INDEX IX_SqlServerInventory_ComplianceStatus ON SqlServerInventory(ComplianceStatus);
    CREATE UNIQUE INDEX IX_SqlServerInventory_ServerPort ON SqlServerInventory(ServerName, Port, InstanceName);
END

-- Database-level inventory (populated by remote agent)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SqlDatabaseInventory')
BEGIN
    CREATE TABLE SqlDatabaseInventory (
        Id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        SqlServerInventoryId    UNIQUEIDENTIFIER NOT NULL,
        DatabaseName            NVARCHAR(255) NOT NULL,
        SizeGb                  DECIMAL(10,3) NULL,
        LogSizeGb               DECIMAL(10,3) NULL,
        RecoveryModel           NVARCHAR(50) NULL,          -- 'Simple' | 'Full' | 'BulkLogged'
        CompatibilityLevel      INT NULL,
        IsSystemDb              BIT NOT NULL DEFAULT 0,
        LastBackupAt            DATETIME2 NULL,
        LastBackupType          NVARCHAR(50) NULL,          -- 'Full' | 'Differential' | 'Log'
        State                   NVARCHAR(50) NULL,          -- 'Online' | 'Offline' | 'Suspect'
        CreatedAt               DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt               DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_SqlDatabaseInventory_Server FOREIGN KEY (SqlServerInventoryId) REFERENCES SqlServerInventory(Id)
    );
    CREATE INDEX IX_SqlDatabaseInventory_Server ON SqlDatabaseInventory(SqlServerInventoryId);
END

-- Network scan ranges that admins have configured
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'NetworkScanRanges')
BEGIN
    CREATE TABLE NetworkScanRanges (
        Id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        Name                    NVARCHAR(200) NOT NULL,
        CidrRange               NVARCHAR(50) NOT NULL,      -- e.g. '10.0.0.0/24'
        Description             NVARCHAR(500) NULL,
        IsEnabled               BIT NOT NULL DEFAULT 1,
        LastScannedAt           DATETIME2 NULL,
        LastScanDurationSeconds INT NULL,
        CreatedAt               DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy               NVARCHAR(256) NULL
    );
END
