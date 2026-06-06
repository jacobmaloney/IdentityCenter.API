-- =============================================
-- V033: Add TenantId to TeamsBotConfigurations
-- Single-tenant bot apps require the actual
-- Azure AD tenant ID for authentication
-- =============================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[TeamsBotConfigurations]')
    AND name = 'TenantId'
)
BEGIN
    ALTER TABLE [dbo].[TeamsBotConfigurations]
    ADD [TenantId] NVARCHAR(100) NULL;
END

-- =============================================
-- Recreate insert stored procedure to include TenantId
-- =============================================

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_InsertTeamsBotConfiguration]') AND type in (N'P', N'PC'))
BEGIN
    DROP PROCEDURE [dbo].[usp_InsertTeamsBotConfiguration];
END

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
