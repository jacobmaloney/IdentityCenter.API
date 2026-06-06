-- =============================================
-- V032: Create TeamsBotConfigurations Table
-- Stores Microsoft Teams bot configuration
-- with encrypted credentials
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TeamsBotConfigurations]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[TeamsBotConfigurations] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

        -- Azure AD / Bot Service Configuration
        [TenantId] NVARCHAR(100) NULL,
        [AppId] NVARCHAR(100) NOT NULL,
        [AppPassword] NVARCHAR(500) NOT NULL, -- Encrypted
        [MessagingEndpoint] NVARCHAR(500) NOT NULL,

        -- Bot Branding
        [BotName] NVARCHAR(30) NOT NULL,
        [ShortDescription] NVARCHAR(80) NOT NULL,
        [FullDescription] NVARCHAR(4000) NULL,
        [AccentColor] NVARCHAR(7) DEFAULT '#667eea',

        -- Organization Information
        [DeveloperName] NVARCHAR(100) NOT NULL,
        [WebsiteUrl] NVARCHAR(500) NULL,
        [PrivacyUrl] NVARCHAR(500) NULL,
        [TermsOfUseUrl] NVARCHAR(500) NULL,

        -- Status
        [IsActive] BIT DEFAULT 0,

        -- Testing & Validation
        [LastTestedAt] DATETIME2 NULL,
        [LastTestResult] NVARCHAR(MAX) NULL,
        [LastTestSuccess] BIT DEFAULT 0,

        -- Audit Fields
        [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
        [ModifiedAt] DATETIME2 NULL,
        [CreatedBy] NVARCHAR(100) NULL,
        [ModifiedBy] NVARCHAR(100) NULL,

        -- Constraints
        CONSTRAINT [CK_TeamsBotConfigurations_BotName_MaxLength] CHECK (LEN([BotName]) <= 30),
        CONSTRAINT [CK_TeamsBotConfigurations_ShortDescription_MaxLength] CHECK (LEN([ShortDescription]) <= 80),
        CONSTRAINT [CK_TeamsBotConfigurations_AccentColor_Format] CHECK ([AccentColor] LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]')
    );

    CREATE INDEX IX_TeamsBotConfigurations_IsActive ON [dbo].[TeamsBotConfigurations]([IsActive]);
    CREATE INDEX IX_TeamsBotConfigurations_CreatedAt ON [dbo].[TeamsBotConfigurations]([CreatedAt] DESC);
    CREATE INDEX IX_TeamsBotConfigurations_AppId ON [dbo].[TeamsBotConfigurations]([AppId]);
END

-- =============================================
-- Create stored procedure for encrypted insert
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_InsertTeamsBotConfiguration]') AND type in (N'P', N'PC'))
BEGIN
    EXEC('
    CREATE PROCEDURE [dbo].[usp_InsertTeamsBotConfiguration]
        @Id UNIQUEIDENTIFIER OUTPUT,
        @TenantId NVARCHAR(100) = NULL,
        @AppId NVARCHAR(100),
        @AppPassword NVARCHAR(500),
        @MessagingEndpoint NVARCHAR(500),
        @BotName NVARCHAR(30),
        @ShortDescription NVARCHAR(80),
        @FullDescription NVARCHAR(4000) = NULL,
        @AccentColor NVARCHAR(7) = ''#667eea'',
        @DeveloperName NVARCHAR(100),
        @WebsiteUrl NVARCHAR(500) = NULL,
        @PrivacyUrl NVARCHAR(500) = NULL,
        @TermsOfUseUrl NVARCHAR(500) = NULL,
        @IsActive BIT = 0,
        @CreatedBy NVARCHAR(100) = NULL
    AS
    BEGIN
        SET NOCOUNT ON;

        IF @IsActive = 1
        BEGIN
            UPDATE [dbo].[TeamsBotConfigurations]
            SET [IsActive] = 0, [ModifiedAt] = GETUTCDATE()
            WHERE [IsActive] = 1;
        END

        IF @Id IS NULL OR @Id = ''00000000-0000-0000-0000-000000000000''
            SET @Id = NEWID();

        INSERT INTO [dbo].[TeamsBotConfigurations] (
            [Id], [TenantId], [AppId], [AppPassword], [MessagingEndpoint],
            [BotName], [ShortDescription], [FullDescription], [AccentColor],
            [DeveloperName], [WebsiteUrl], [PrivacyUrl], [TermsOfUseUrl],
            [IsActive], [CreatedBy], [CreatedAt]
        )
        VALUES (
            @Id, @TenantId, @AppId, @AppPassword, @MessagingEndpoint,
            @BotName, @ShortDescription, @FullDescription, @AccentColor,
            @DeveloperName, @WebsiteUrl, @PrivacyUrl, @TermsOfUseUrl,
            @IsActive, @CreatedBy, GETUTCDATE()
        );

        SELECT @Id AS Id;
    END
    ')
END

-- =============================================
-- Create stored procedure for getting active config
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_GetActiveTeamsBotConfiguration]') AND type in (N'P', N'PC'))
BEGIN
    EXEC('
    CREATE PROCEDURE [dbo].[usp_GetActiveTeamsBotConfiguration]
    AS
    BEGIN
        SET NOCOUNT ON;

        SELECT TOP 1 *
        FROM [dbo].[TeamsBotConfigurations]
        WHERE [IsActive] = 1
        ORDER BY [ModifiedAt] DESC, [CreatedAt] DESC;
    END
    ')
END

-- =============================================
-- Create stored procedure for updating test results
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_UpdateTeamsBotTestResult]') AND type in (N'P', N'PC'))
BEGIN
    EXEC('
    CREATE PROCEDURE [dbo].[usp_UpdateTeamsBotTestResult]
        @Id UNIQUEIDENTIFIER,
        @TestSuccess BIT,
        @TestResult NVARCHAR(MAX)
    AS
    BEGIN
        SET NOCOUNT ON;

        UPDATE [dbo].[TeamsBotConfigurations]
        SET [LastTestedAt] = GETUTCDATE(),
            [LastTestSuccess] = @TestSuccess,
            [LastTestResult] = @TestResult,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @Id;
    END
    ')
END
