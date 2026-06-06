-- V116: Access Catalog admin curation (hidden synced groups) + custom catalog items.

-- Track which synced Objects (groups) admins have hidden from the catalog.
-- ObjectId is the PK so a group can only be in one state. Reappearing rows
-- mean the group is hidden; absence = visible.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CatalogVisibility')
BEGIN
    CREATE TABLE [CatalogVisibility] (
        ObjectId    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        IsHidden    BIT              NOT NULL DEFAULT 1,
        HiddenAt    DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        HiddenBy    NVARCHAR(200)    NULL,
        Reason      NVARCHAR(MAX)    NULL
    );
END
GO

-- Custom (non-sync) catalog entries authored by admins. Listed alongside
-- synced groups in the catalog. ResourceType is freeform but suggested
-- values: 'Application', 'FileShare', 'License', 'PhysicalAccess', 'Other'.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CustomCatalogItems')
BEGIN
    CREATE TABLE [CustomCatalogItems] (
        Id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        Name            NVARCHAR(200)    NOT NULL,
        Description     NVARCHAR(MAX)    NULL,
        ResourceType    NVARCHAR(50)     NOT NULL DEFAULT 'Application',
        ExternalUrl     NVARCHAR(500)    NULL,
        RiskLevel       NVARCHAR(20)     NOT NULL DEFAULT 'Low',
        OwnerObjectId   UNIQUEIDENTIFIER NULL,
        CreatedAt       DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy       NVARCHAR(200)    NULL,
        ModifiedAt      DATETIME2        NULL,
        ModifiedBy      NVARCHAR(200)    NULL,
        IsActive        BIT              NOT NULL DEFAULT 1
    );

    CREATE INDEX IX_CustomCatalogItems_IsActive ON CustomCatalogItems (IsActive) WHERE IsActive = 1;
END
GO
