-- Stored Procedure: Upsert Identity with Extended Attributes
-- High-performance bulk upsert for sync operations with transaction support
CREATE OR ALTER PROCEDURE [dbo].[usp_UpsertIdentityWithAttributes]
    @Id UNIQUEIDENTIFIER,
    @SourceConnectionId UNIQUEIDENTIFIER,
    @SourceUniqueId NVARCHAR(500),
    @SourceType NVARCHAR(200),
    @DisplayName NVARCHAR(200),
    @Email NVARCHAR(256) = NULL,
    @Username NVARCHAR(256) = NULL,
    @FirstName NVARCHAR(100) = NULL,
    @LastName NVARCHAR(100) = NULL,
    @Department NVARCHAR(200) = NULL,
    @JobTitle NVARCHAR(200) = NULL,
    @Phone NVARCHAR(50) = NULL,
    @ManagerSourceId NVARCHAR(1000) = NULL,
    @IdentityId UNIQUEIDENTIFIER = NULL,
    @IsActive BIT,
    @IsAuthoritative BIT = 0,
    @MatchConfidence INT = 0,
    @MatchMethod NVARCHAR(200) = NULL,
    @LastSyncedAt DATETIME2,
    @LastSeenAt DATETIME2 = NULL,
    @IsBuiltIn BIT = 0,
    @IsAdminSDHolder BIT = 0,
    @AttributesJson NVARCHAR(MAX) = NULL -- JSON array of attributes
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON; -- Automatically rollback on errors

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();
    DECLARE @IsNew BIT = 0;
    DECLARE @AttributesInserted INT = 0;
    DECLARE @ExistingId UNIQUEIDENTIFIER = NULL;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Upsert the identity record
        -- Check by unique index (SourceConnectionId + SourceUniqueId) instead of just Id
        -- This prevents duplicate key violations on IX_Objects_SourceUnique
        SELECT @ExistingId = Id
        FROM Objects
        WHERE SourceConnectionId = @SourceConnectionId
          AND SourceUniqueId = @SourceUniqueId;

        IF @ExistingId IS NOT NULL
        BEGIN
            -- Update existing record
            UPDATE Objects
            SET Id = @Id,  -- Update the Id to the new value
                SourceType = @SourceType,
                DisplayName = @DisplayName,
                Email = @Email,
                Username = @Username,
                FirstName = @FirstName,
                LastName = @LastName,
                Department = @Department,
                JobTitle = @JobTitle,
                Phone = @Phone,
                ManagerSourceId = @ManagerSourceId,
                IdentityId = @IdentityId,
                IsActive = @IsActive,
                IsAuthoritative = @IsAuthoritative,
                MatchConfidence = @MatchConfidence,
                MatchMethod = @MatchMethod,
                LastSyncedAt = @LastSyncedAt,
                LastSeenAt = @LastSeenAt,
                IsBuiltIn = @IsBuiltIn,
                IsAdminSDHolder = @IsAdminSDHolder,
                ModifiedAt = @Now
            WHERE SourceConnectionId = @SourceConnectionId
              AND SourceUniqueId = @SourceUniqueId;

            SET @IsNew = 0;
        END
        ELSE
        BEGIN
            -- Insert new record
            INSERT INTO Objects (
                Id, SourceConnectionId, SourceUniqueId, SourceType, DisplayName,
                Email, Username, FirstName, LastName, Department,
                JobTitle, Phone, ManagerSourceId, IdentityId, IsActive,
                IsAuthoritative, MatchConfidence, MatchMethod,
                LastSyncedAt, LastSeenAt, FirstSyncedAt,
                IsBuiltIn, IsAdminSDHolder, CreatedAt, ModifiedAt
            )
            VALUES (
                @Id, @SourceConnectionId, @SourceUniqueId, @SourceType, @DisplayName,
                @Email, @Username, @FirstName, @LastName, @Department,
                @JobTitle, @Phone, @ManagerSourceId, @IdentityId, @IsActive,
                @IsAuthoritative, @MatchConfidence, @MatchMethod,
                @LastSyncedAt, @LastSeenAt, @Now,
                @IsBuiltIn, @IsAdminSDHolder, @Now, @Now
            );

            SET @IsNew = 1;
            SET @ExistingId = @Id;
        END

        -- Handle extended attributes (delete old ones based on the EXISTING Id, insert new ones with NEW Id)
        -- If updating, @ExistingId has the old Id; if inserting, @ExistingId = @Id
        DELETE FROM ObjectAttributes WHERE ObjectId = @ExistingId;

        -- If we updated the Id field, also delete attributes linked to the new Id (in case of orphans)
        IF @ExistingId != @Id
            DELETE FROM ObjectAttributes WHERE ObjectId = @Id;

        IF @AttributesJson IS NOT NULL AND LEN(@AttributesJson) > 0
        BEGIN
            INSERT INTO ObjectAttributes (
                Id, ObjectId, AttributeName, AttributeValue,
                DataType, LastSyncedAt, CreatedAt, ModifiedAt
            )
            SELECT
                NEWID(),
                @Id,
                AttributeName,
                AttributeValue,
                DataType,
                @LastSyncedAt,  -- Use the same sync timestamp as the parent object
                @Now,
                @Now
            FROM OPENJSON(@AttributesJson)
            WITH (
                AttributeName NVARCHAR(200) '$.AttributeName',
                AttributeValue NVARCHAR(MAX) '$.AttributeValue',
                DataType NVARCHAR(50) '$.DataType'
            );

            SET @AttributesInserted = @@ROWCOUNT;
        END

        COMMIT TRANSACTION;

        -- Return the operation result
        SELECT
            @Id AS Id,
            @IsNew AS IsNew,
            @AttributesInserted AS AttributesInserted;

    END TRY
    BEGIN CATCH
        -- Rollback transaction on error
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        -- Re-throw the error with detailed information
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();
        DECLARE @ErrorNumber INT = ERROR_NUMBER();
        DECLARE @ErrorLine INT = ERROR_LINE();

        -- Raise error with context
        RAISERROR (
            'usp_UpsertIdentityWithAttributes failed: Error %d at line %d: %s',
            @ErrorSeverity,
            @ErrorState,
            @ErrorNumber,
            @ErrorLine,
            @ErrorMessage
        );

        -- Return error result
        SELECT
            @Id AS Id,
            0 AS IsNew,
            0 AS AttributesInserted;
    END CATCH
END
GO
