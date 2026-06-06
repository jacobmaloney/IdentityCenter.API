-- Manual application of UPSERT stored procedure fix
-- Run this script directly against the IdentityCenter database on 192.168.1.20

-- Add IsBuiltIn column if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Identities]') AND name = 'IsBuiltIn')
BEGIN
    ALTER TABLE [dbo].[Identities] ADD [IsBuiltIn] BIT NOT NULL DEFAULT 0;
    PRINT 'Added IsBuiltIn column';
END
ELSE
BEGIN
    PRINT 'IsBuiltIn column already exists';
END
GO

-- Add IsAdminSDHolder column if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Identities]') AND name = 'IsAdminSDHolder')
BEGIN
    ALTER TABLE [dbo].[Identities] ADD [IsAdminSDHolder] BIT NOT NULL DEFAULT 0;
    PRINT 'Added IsAdminSDHolder column';
END
ELSE
BEGIN
    PRINT 'IsAdminSDHolder column already exists';
END
GO

-- Update usp_UpsertIdentityWithAttributes stored procedure with UPSERT fix
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
    @PersonId UNIQUEIDENTIFIER = NULL,
    @IsActive BIT,
    @IsAuthoritative BIT = 0,
    @MatchConfidence INT = 0,
    @MatchMethod NVARCHAR(200) = NULL,
    @LastSyncedAt DATETIME2,
    @LastSeenAt DATETIME2 = NULL,
    @IsBuiltIn BIT = 0,
    @IsAdminSDHolder BIT = 0,
    @AttributesJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();
    DECLARE @IsNew BIT = 0;
    DECLARE @AttributesInserted INT = 0;
    DECLARE @ExistingId UNIQUEIDENTIFIER = NULL;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Check by unique index (SourceConnectionId + SourceUniqueId) instead of just Id
        -- This prevents duplicate key violations on IX_Identities_SourceUnique
        SELECT @ExistingId = Id
        FROM Identities
        WHERE SourceConnectionId = @SourceConnectionId
          AND SourceUniqueId = @SourceUniqueId;

        IF @ExistingId IS NOT NULL
        BEGIN
            -- Update existing record
            UPDATE Identities
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
                PersonId = @PersonId,
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
            INSERT INTO Identities (
                Id, SourceConnectionId, SourceUniqueId, SourceType, DisplayName,
                Email, Username, FirstName, LastName, Department,
                JobTitle, Phone, ManagerSourceId, PersonId, IsActive,
                IsAuthoritative, MatchConfidence, MatchMethod,
                LastSyncedAt, LastSeenAt, FirstSyncedAt,
                IsBuiltIn, IsAdminSDHolder, CreatedAt, ModifiedAt
            )
            VALUES (
                @Id, @SourceConnectionId, @SourceUniqueId, @SourceType, @DisplayName,
                @Email, @Username, @FirstName, @LastName, @Department,
                @JobTitle, @Phone, @ManagerSourceId, @PersonId, @IsActive,
                @IsAuthoritative, @MatchConfidence, @MatchMethod,
                @LastSyncedAt, @LastSeenAt, @Now,
                @IsBuiltIn, @IsAdminSDHolder, @Now, @Now
            );

            SET @IsNew = 1;
            SET @ExistingId = @Id;
        END

        -- Handle extended attributes (delete old ones based on the EXISTING Id, insert new ones with NEW Id)
        DELETE FROM IdentityAttributes WHERE IdentityId = @ExistingId;

        -- If we updated the Id field, also delete attributes linked to the new Id (in case of orphans)
        IF @ExistingId != @Id
            DELETE FROM IdentityAttributes WHERE IdentityId = @Id;

        IF @AttributesJson IS NOT NULL AND LEN(@AttributesJson) > 0
        BEGIN
            INSERT INTO IdentityAttributes (
                Id, IdentityId, AttributeName, AttributeValue,
                DataType, LastSyncedAt, CreatedAt, ModifiedAt
            )
            SELECT
                NEWID(),
                @Id,
                AttributeName,
                AttributeValue,
                DataType,
                @LastSyncedAt,
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

        SELECT
            @Id AS Id,
            @IsNew AS IsNew,
            @AttributesInserted AS AttributesInserted;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();
        DECLARE @ErrorNumber INT = ERROR_NUMBER();
        DECLARE @ErrorLine INT = ERROR_LINE();

        RAISERROR (
            'usp_UpsertIdentityWithAttributes failed: Error %d at line %d: %s',
            @ErrorSeverity,
            @ErrorState,
            @ErrorNumber,
            @ErrorLine,
            @ErrorMessage
        );

        SELECT
            @Id AS Id,
            0 AS IsNew,
            0 AS AttributesInserted;
    END CATCH
END
GO

PRINT 'UPSERT stored procedure fix applied successfully';
PRINT 'The stored procedure now checks by (SourceConnectionId, SourceUniqueId) instead of just Id';
PRINT 'This should eliminate the duplicate key violations';
