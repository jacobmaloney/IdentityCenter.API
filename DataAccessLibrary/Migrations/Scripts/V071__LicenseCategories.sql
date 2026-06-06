-- V071: License Categories — user-defined buckets for grouping license pools
-- Categories enable filtering, reporting, and cost attribution across pool types.
-- Examples: "M365 Subscriptions", "Windows CALs", "SQL Server", "Azure AD", "Dev/Test"

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseCategories')
BEGIN
    CREATE TABLE [LicenseCategories] (
        [Id]          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        [Name]        NVARCHAR(200)    NOT NULL,
        [Description] NVARCHAR(1000)   NULL,
        [Color]       NVARCHAR(20)     NULL DEFAULT '#6366f1',
        [Icon]        NVARCHAR(50)     NULL DEFAULT 'fa-layer-group',
        [SortOrder]   INT              NOT NULL DEFAULT 100,
        [IsBuiltIn]   BIT              NOT NULL DEFAULT 0,
        [IsActive]    BIT              NOT NULL DEFAULT 1,
        [CreatedAt]   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [ModifiedAt]  DATETIME2        NULL,
        CONSTRAINT [PK_LicenseCategories] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_LicenseCategories_Name] UNIQUE ([Name])
    );

    CREATE NONCLUSTERED INDEX [IX_LicenseCategories_Active]
        ON [LicenseCategories] ([IsActive], [SortOrder]);

    PRINT 'V071: Created LicenseCategories table';
END
GO

-- Seed 6 built-in categories
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseCategories')
  AND NOT EXISTS (SELECT 1 FROM LicenseCategories WHERE IsBuiltIn = 1)
BEGIN
    INSERT INTO LicenseCategories (Id, Name, Description, Color, Icon, SortOrder, IsBuiltIn) VALUES
        ('C0710000-0000-0000-0000-000000000001', N'M365 Subscriptions',
            N'Microsoft 365 / Office 365 per-user subscriptions (E3, E5, Business, etc.)',
            '#0078d4', 'fa-microsoft', 10, 1),
        ('C0710000-0000-0000-0000-000000000002', N'Windows CALs',
            N'Windows Server User CALs and Device CALs',
            '#00a4ef', 'fa-server', 20, 1),
        ('C0710000-0000-0000-0000-000000000003', N'SQL Server',
            N'SQL Server core licenses and instance-based licensing',
            '#a91d22', 'fa-database', 30, 1),
        ('C0710000-0000-0000-0000-000000000004', N'Azure / Entra',
            N'Entra ID P1/P2, Azure subscription-based services',
            '#0078d4', 'fa-cloud', 40, 1),
        ('C0710000-0000-0000-0000-000000000005', N'Dev / Test',
            N'Developer, MSDN, test environment licenses (non-production)',
            '#68217a', 'fa-code', 50, 1),
        ('C0710000-0000-0000-0000-000000000006', N'Uncategorized',
            N'Licenses not yet assigned to a category',
            '#6b7280', 'fa-question-circle', 999, 1);

    PRINT 'V071: Seeded 6 built-in license categories';
END
GO
