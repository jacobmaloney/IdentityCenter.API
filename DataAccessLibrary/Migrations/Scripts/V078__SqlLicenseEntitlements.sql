-- V078: SQL Server License Entitlements — what the org owns

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SqlLicenseEntitlements')
BEGIN
    CREATE TABLE SqlLicenseEntitlements (
        Id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        LicenseType             NVARCHAR(50) NOT NULL,      -- 'CoreBased' | 'ServerCAL' | 'Enterprise' | 'Standard' | 'Developer' | 'Express'
        Edition                 NVARCHAR(50) NOT NULL,      -- 'Enterprise' | 'Standard' | 'Developer' | 'Express'
        Quantity                INT NOT NULL DEFAULT 1,     -- number of licenses (cores or server seats)
        QuantityUnit            NVARCHAR(20) NOT NULL DEFAULT 'Cores',  -- 'Cores' | 'Seats' | 'Servers'
        CostPerUnit             DECIMAL(18,2) NULL,
        TotalCost               AS (Quantity * ISNULL(CostPerUnit, 0)) PERSISTED,
        VendorAgreementNumber   NVARCHAR(200) NULL,
        PurchaseDate            DATE NULL,
        ExpiryDate              DATE NULL,
        SoftwareAssurance       BIT NOT NULL DEFAULT 0,
        IsActive                BIT NOT NULL DEFAULT 1,
        Notes                   NVARCHAR(MAX) NULL,
        CreatedAt               DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy               NVARCHAR(256) NULL,
        UpdatedAt               DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedBy               NVARCHAR(256) NULL
    );
    CREATE INDEX IX_SqlLicenseEntitlements_Edition ON SqlLicenseEntitlements(Edition) WHERE IsActive = 1;
END

-- Track license-to-server assignments (which entitlement covers which server)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SqlLicenseAssignments')
BEGIN
    CREATE TABLE SqlLicenseAssignments (
        Id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        EntitlementId           UNIQUEIDENTIFIER NOT NULL,
        ObjectId                NVARCHAR(450) NOT NULL,     -- FK to Objects.Id (the server)
        AssignedCores           INT NULL,                   -- for core-based: how many cores this assignment covers
        AssignedBy              NVARCHAR(256) NULL,
        AssignedAt              DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        IsActive                BIT NOT NULL DEFAULT 1,
        Notes                   NVARCHAR(MAX) NULL,
        CONSTRAINT FK_SqlLicenseAssignments_Entitlement FOREIGN KEY (EntitlementId) REFERENCES SqlLicenseEntitlements(Id)
    );
    CREATE UNIQUE INDEX IX_SqlLicenseAssignments_Object ON SqlLicenseAssignments(ObjectId, EntitlementId) WHERE IsActive = 1;
END
