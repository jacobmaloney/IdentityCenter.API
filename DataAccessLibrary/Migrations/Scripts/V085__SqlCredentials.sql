-- V085: SQL Server credentials table for direct scanning
-- Stores encrypted SQL auth credentials so IdentityCenter can connect directly to SQL servers
-- without requiring an agent on each host.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SqlServerCredentials')
BEGIN
    CREATE TABLE SqlServerCredentials (
        Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Name NVARCHAR(200) NOT NULL, -- Friendly name: "Default SQL Auth", "Production Domain Account"
        Description NVARCHAR(500) NULL,
        AuthType NVARCHAR(20) NOT NULL, -- SqlAuth, WindowsAuth
        Username NVARCHAR(200) NULL, -- sa, DOMAIN\svc_ic, etc.
        EncryptedPassword NVARCHAR(MAX) NULL, -- Encrypted via IEncryptionService
        IsDefault BIT NOT NULL DEFAULT 0, -- If true, used when no specific cred assigned
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(256) NULL,
        ModifiedAt DATETIME2 NULL,
        ModifiedBy NVARCHAR(256) NULL,
        LastUsedAt DATETIME2 NULL,
        CONSTRAINT PK_SqlServerCredentials PRIMARY KEY (Id)
    );

    CREATE UNIQUE INDEX UX_SqlServerCredentials_Name ON SqlServerCredentials (Name) WHERE IsActive = 1;

    PRINT 'V085: Created SqlServerCredentials table';
END
ELSE
BEGIN
    PRINT 'V085: SqlServerCredentials already exists - skipping';
END
