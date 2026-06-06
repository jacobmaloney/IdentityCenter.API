-- V092: Server Scan — local users, groups, and installed products
-- Supports WinRM-based discovery of OS-level data on Windows servers.

-- 1. ServerLocalUsers: local user/group memberships discovered via WinRM
IF OBJECT_ID('ServerLocalUsers', 'U') IS NULL
CREATE TABLE ServerLocalUsers (
    Id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    SqlServerInventoryId    UNIQUEIDENTIFIER NOT NULL
        REFERENCES SqlServerInventory(Id) ON DELETE CASCADE,
    AccountName             NVARCHAR(256) NOT NULL,
    AccountType             NVARCHAR(50)  NOT NULL,       -- LocalUser, DomainUser, DomainGroup
    GroupName               NVARCHAR(256) NULL,
    IsLocalAdmin            BIT NOT NULL DEFAULT 0,
    IsDisabled              BIT NOT NULL DEFAULT 0,
    SID                     NVARCHAR(256) NULL,
    ObjectId                UNIQUEIDENTIFIER NULL,         -- FK to Objects (matched via SID/name)
    MatchMethod             NVARCHAR(50) NULL,             -- SID, SAMAccountName, UPN, Manual
    DiscoveredAt            DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastSeenAt              DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    IsActive                BIT NOT NULL DEFAULT 1
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServerLocalUsers_Server')
    CREATE NONCLUSTERED INDEX IX_ServerLocalUsers_Server
    ON ServerLocalUsers (SqlServerInventoryId) WHERE IsActive = 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServerLocalUsers_ObjectId')
    CREATE NONCLUSTERED INDEX IX_ServerLocalUsers_ObjectId
    ON ServerLocalUsers (ObjectId) WHERE ObjectId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServerLocalUsers_Admin')
    CREATE NONCLUSTERED INDEX IX_ServerLocalUsers_Admin
    ON ServerLocalUsers (SqlServerInventoryId, IsLocalAdmin) WHERE IsLocalAdmin = 1 AND IsActive = 1;
GO

-- 2. ServerInstalledProducts: Microsoft products discovered via WinRM registry scan
IF OBJECT_ID('ServerInstalledProducts', 'U') IS NULL
CREATE TABLE ServerInstalledProducts (
    Id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    SqlServerInventoryId    UNIQUEIDENTIFIER NOT NULL
        REFERENCES SqlServerInventory(Id) ON DELETE CASCADE,
    ProductName             NVARCHAR(500) NOT NULL,
    ProductVersion          NVARCHAR(100) NULL,
    ProductEdition          NVARCHAR(200) NULL,
    ProductCategory         NVARCHAR(100) NOT NULL,        -- WindowsServer, SQLServer, Office, Other
    LicenseKey              NVARCHAR(100) NULL,             -- last 5 chars only
    InstallDate             DATETIME2 NULL,
    InstallPath             NVARCHAR(500) NULL,
    Publisher               NVARCHAR(256) NULL,
    IsLicensable            BIT NOT NULL DEFAULT 1,
    DiscoveredAt            DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastSeenAt              DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    IsActive                BIT NOT NULL DEFAULT 1
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServerInstalledProducts_Server')
    CREATE NONCLUSTERED INDEX IX_ServerInstalledProducts_Server
    ON ServerInstalledProducts (SqlServerInventoryId) WHERE IsActive = 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ServerInstalledProducts_Category')
    CREATE NONCLUSTERED INDEX IX_ServerInstalledProducts_Category
    ON ServerInstalledProducts (ProductCategory) WHERE IsActive = 1;
GO

-- 3. Add WinRM scan status columns to SqlServerInventory
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'LastWinRmScanStatus')
    ALTER TABLE SqlServerInventory ADD LastWinRmScanStatus NVARCHAR(50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'LastWinRmScanMessage')
    ALTER TABLE SqlServerInventory ADD LastWinRmScanMessage NVARCHAR(MAX) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'LastWinRmScanAt')
    ALTER TABLE SqlServerInventory ADD LastWinRmScanAt DATETIME2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SqlServerInventory') AND name = 'LastWinRmScanDurationMs')
    ALTER TABLE SqlServerInventory ADD LastWinRmScanDurationMs INT NULL;
GO
