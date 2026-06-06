-- Stored Procedure: Upsert Object with Extended Attributes
-- High-performance upsert for identity objects and their attributes

-- CRITICAL: SET QUOTED_IDENTIFIER ON is required for tables with:
-- - Indexed views
-- - Indexes on computed columns
-- - Filtered indexes (like IX_Objects_SourceConnectionId_SourceUniqueId)
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_UpsertObjectWithAttributes]
    @Id UNIQUEIDENTIFIER,
    @SourceConnectionId UNIQUEIDENTIFIER,
    @SourceUniqueId NVARCHAR(450),
    @SourceType NVARCHAR(50),
    @DisplayName NVARCHAR(200),
    @Email NVARCHAR(256) = NULL,
    @Username NVARCHAR(256) = NULL,
    @FirstName NVARCHAR(100) = NULL,
    @LastName NVARCHAR(100) = NULL,
    @Department NVARCHAR(200) = NULL,
    @JobTitle NVARCHAR(200) = NULL,
    @Phone NVARCHAR(50) = NULL,
    @ManagerSourceId NVARCHAR(500) = NULL,
    @IdentityId UNIQUEIDENTIFIER = NULL,
    @IsActive BIT = 1,
    @IsAuthoritative BIT = 0,
    @MatchConfidence INT = NULL,
    @MatchMethod NVARCHAR(50) = NULL,
    @LastSyncedAt DATETIME2 = NULL,
    @LastSeenAt DATETIME2 = NULL,
    @IsBuiltIn BIT = 0,
    @IsAdminSDHolder BIT = 0,
    @AttributesJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();
    DECLARE @ExistingId UNIQUEIDENTIFIER;
    DECLARE @IsNew BIT = 0;

    -- Check if object already exists by unique index
    SELECT @ExistingId = Id
    FROM Objects
    WHERE SourceConnectionId = @SourceConnectionId
      AND SourceUniqueId = @SourceUniqueId;

    IF @ExistingId IS NOT NULL
    BEGIN
        -- UPDATE existing object (DO NOT change Id - it's the primary key!)
        UPDATE Objects
        SET SourceType = @SourceType,
            DisplayName = @DisplayName,
            FirstName = @FirstName,
            LastName = @LastName,
            Email = @Email,
            Username = @Username,
            JobTitle = @JobTitle,
            Department = @Department,
            Phone = @Phone,
            ManagerSourceId = @ManagerSourceId,
            IsActive = @IsActive,
            IsAuthoritative = @IsAuthoritative,
            IsBuiltIn = @IsBuiltIn,
            IsAdminSDHolder = @IsAdminSDHolder,
            IdentityId = @IdentityId,
            MatchConfidence = @MatchConfidence,
            MatchMethod = @MatchMethod,
            LastSyncedAt = COALESCE(@LastSyncedAt, @Now),
            LastSeenAt = COALESCE(@LastSeenAt, @Now)
        WHERE Id = @ExistingId;

        -- ONLY delete existing attributes if we have new ones to insert
        -- This prevents accidental deletion when attributes aren't being synced
        IF @AttributesJson IS NOT NULL AND LEN(@AttributesJson) > 0
        BEGIN
            DELETE FROM ObjectAttributes WHERE ObjectId = @ExistingId;
        END

        SET @IsNew = 0;
    END
    ELSE
    BEGIN
        -- INSERT new object
        INSERT INTO Objects (
            Id,
            SourceConnectionId,
            SourceUniqueId,
            SourceType,
            DisplayName,
            FirstName,
            LastName,
            Email,
            Username,
            JobTitle,
            Department,
            Phone,
            ManagerSourceId,
            IsActive,
            IsAuthoritative,
            IsBuiltIn,
            IsAdminSDHolder,
            IdentityId,
            MatchConfidence,
            MatchMethod,
            FirstSyncedAt,
            LastSyncedAt,
            LastSeenAt
        )
        VALUES (
            @Id,
            @SourceConnectionId,
            @SourceUniqueId,
            @SourceType,
            @DisplayName,
            @FirstName,
            @LastName,
            @Email,
            @Username,
            @JobTitle,
            @Department,
            @Phone,
            @ManagerSourceId,
            @IsActive,
            @IsAuthoritative,
            @IsBuiltIn,
            @IsAdminSDHolder,
            @IdentityId,
            @MatchConfidence,
            @MatchMethod,
            @Now,
            COALESCE(@LastSyncedAt, @Now),
            COALESCE(@LastSeenAt, @Now)
        );

        SET @ExistingId = @Id;
        SET @IsNew = 1;
    END

    -- Handle extended attributes if provided
    IF @AttributesJson IS NOT NULL AND LEN(@AttributesJson) > 0
    BEGIN
        INSERT INTO ObjectAttributes (
            Id,
            ObjectId,
            AttributeName,
            AttributeValue,
            DataType,
            LastSyncedAt
        )
        SELECT
            NEWID(),
            @ExistingId,
            AttributeName,
            AttributeValue,
            DataType,
            @Now
        FROM OPENJSON(@AttributesJson)
        WITH (
            AttributeName NVARCHAR(200) '$.AttributeName',
            AttributeValue NVARCHAR(MAX) '$.AttributeValue',
            DataType NVARCHAR(50) '$.DataType'
        );
    END

    -- Return result
    SELECT
        @ExistingId AS Id,
        @IsNew AS IsNew,
        @@ROWCOUNT AS AttributesInserted;
END
GO
