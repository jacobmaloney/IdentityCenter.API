-- Update all stored procedures with corrected table/column names
-- Run this to fix the 500 sync errors

PRINT 'Updating stored procedures...'

-- =============================================
-- 1. usp_BulkInsertAuditLogs
-- =============================================
PRINT 'Updating usp_BulkInsertAuditLogs...'
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_BulkInsertAuditLogs]
    @AuditLogsJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();

    -- Bulk insert audit logs from JSON
    INSERT INTO SyncAuditLogs (
        Id,
        SyncStepRunId,
        ObjectId,
        OperationType,
        ObjectDisplayName,
        SourceUniqueId,
        Email,
        Username,
        UserPrincipalName,
        ChangeDetails,
        ChangeCount,
        ProcessingTimeMs,
        Timestamp
    )
    SELECT
        NEWID(),
        SyncStepRunId,
        ObjectId,
        OperationType,
        ObjectDisplayName,
        SourceUniqueId,
        Email,
        Username,
        UserPrincipalName,
        ChangeDetails,
        ChangeCount,
        ProcessingTimeMs,
        @Now
    FROM OPENJSON(@AuditLogsJson)
    WITH (
        SyncStepRunId UNIQUEIDENTIFIER '$.SyncStepRunId',
        ObjectId UNIQUEIDENTIFIER '$.ObjectId',
        OperationType NVARCHAR(50) '$.OperationType',
        ObjectDisplayName NVARCHAR(200) '$.ObjectDisplayName',
        SourceUniqueId NVARCHAR(450) '$.SourceUniqueId',
        Email NVARCHAR(256) '$.Email',
        Username NVARCHAR(256) '$.Username',
        UserPrincipalName NVARCHAR(500) '$.UserPrincipalName',
        ChangeDetails NVARCHAR(MAX) '$.ChangeDetails',
        ChangeCount INT '$.ChangeCount',
        ProcessingTimeMs BIGINT '$.ProcessingTimeMs'
    );

    PRINT 'Inserted ' + CAST(@@ROWCOUNT AS VARCHAR) + ' audit log records';
END
GO

PRINT 'usp_BulkInsertAuditLogs updated successfully'
GO

-- =============================================
-- 2. usp_FindIdentityBySourceUniqueId
-- =============================================
PRINT 'Updating usp_FindIdentityBySourceUniqueId...'
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_FindIdentityBySourceUniqueId]
    @SourceConnectionId UNIQUEIDENTIFIER,
    @SourceUniqueId NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;

    -- Return the identity object (account)
    SELECT
        i.Id,
        i.SourceConnectionId,
        i.SourceUniqueId,
        i.SourceType,
        i.DisplayName,
        i.FirstName,
        i.LastName,
        i.Email,
        i.Username,
        i.JobTitle,
        i.Department,
        i.Phone,
        i.Manager,
        i.DistinguishedName,
        i.ObjectClass,
        i.IsActive,
        i.IsAuthoritative,
        i.IsBuiltIn,
        i.IdentityId,
        i.FirstSyncedAt,
        i.LastSyncedAt,
        i.LastSeenAt,
        i.DeletedAt,
        i.CreatedAt,
        i.UpdatedAt
    FROM Objects i WITH (NOLOCK)
    WHERE i.SourceConnectionId = @SourceConnectionId
      AND i.SourceUniqueId = @SourceUniqueId;

    -- Return extended attributes
    SELECT
        ia.Id,
        ia.ObjectId,
        ia.AttributeName,
        ia.AttributeValue,
        ia.ValueType,
        ia.IsSensitive,
        ia.CreatedAt,
        ia.UpdatedAt
    FROM ObjectAttributes ia WITH (NOLOCK)
    INNER JOIN Objects i WITH (NOLOCK) ON ia.ObjectId = i.Id
    WHERE i.SourceConnectionId = @SourceConnectionId
      AND i.SourceUniqueId = @SourceUniqueId;
END
GO

PRINT 'usp_FindIdentityBySourceUniqueId updated successfully'
GO

-- =============================================
-- 3. usp_UpsertIdentityWithAttributes
-- =============================================
PRINT 'Updating usp_UpsertIdentityWithAttributes...'
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_UpsertIdentityWithAttributes]
    @Id UNIQUEIDENTIFIER,
    @SourceConnectionId UNIQUEIDENTIFIER,
    @SourceUniqueId NVARCHAR(450),
    @SourceType NVARCHAR(50),
    @DisplayName NVARCHAR(200),
    @FirstName NVARCHAR(100) = NULL,
    @LastName NVARCHAR(100) = NULL,
    @Email NVARCHAR(256) = NULL,
    @Username NVARCHAR(256) = NULL,
    @JobTitle NVARCHAR(200) = NULL,
    @Department NVARCHAR(200) = NULL,
    @Phone NVARCHAR(50) = NULL,
    @Manager NVARCHAR(500) = NULL,
    @DistinguishedName NVARCHAR(500) = NULL,
    @ObjectClass NVARCHAR(100) = NULL,
    @IsActive BIT = 1,
    @IsAuthoritative BIT = 0,
    @IsBuiltIn BIT = 0,
    @IdentityId UNIQUEIDENTIFIER = NULL,
    @ExtendedAttributesJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();
    DECLARE @ExistingId UNIQUEIDENTIFIER;
    DECLARE @IsNew BIT = 0;

    -- Check if identity object already exists by unique index
    SELECT @ExistingId = Id
    FROM Objects
    WHERE SourceConnectionId = @SourceConnectionId
      AND SourceUniqueId = @SourceUniqueId;

    IF @ExistingId IS NOT NULL
    BEGIN
        -- UPDATE existing identity object
        UPDATE Objects
        SET Id = @Id,
            SourceType = @SourceType,
            DisplayName = @DisplayName,
            FirstName = @FirstName,
            LastName = @LastName,
            Email = @Email,
            Username = @Username,
            JobTitle = @JobTitle,
            Department = @Department,
            Phone = @Phone,
            Manager = @Manager,
            DistinguishedName = @DistinguishedName,
            ObjectClass = @ObjectClass,
            IsActive = @IsActive,
            IsAuthoritative = @IsAuthoritative,
            IsBuiltIn = @IsBuiltIn,
            IdentityId = @IdentityId,
            LastSyncedAt = @Now,
            LastSeenAt = @Now,
            UpdatedAt = @Now
        WHERE Id = @ExistingId;

        -- Delete existing attributes (will be re-inserted)
        DELETE FROM ObjectAttributes WHERE ObjectId = @ExistingId;

        SET @IsNew = 0;
    END
    ELSE
    BEGIN
        -- INSERT new identity object
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
            Manager,
            DistinguishedName,
            ObjectClass,
            IsActive,
            IsAuthoritative,
            IsBuiltIn,
            IdentityId,
            FirstSyncedAt,
            LastSyncedAt,
            LastSeenAt,
            CreatedAt,
            UpdatedAt
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
            @Manager,
            @DistinguishedName,
            @ObjectClass,
            @IsActive,
            @IsAuthoritative,
            @IsBuiltIn,
            @IdentityId,
            @Now,
            @Now,
            @Now,
            @Now,
            @Now
        );

        SET @ExistingId = @Id;
        SET @IsNew = 1;
    END

    -- Handle extended attributes if provided
    IF @ExtendedAttributesJson IS NOT NULL AND LEN(@ExtendedAttributesJson) > 0
    BEGIN
        INSERT INTO ObjectAttributes (
            Id,
            ObjectId,
            AttributeName,
            AttributeValue,
            ValueType,
            IsSensitive,
            CreatedAt,
            UpdatedAt
        )
        SELECT
            NEWID(),
            @ExistingId,
            AttributeName,
            AttributeValue,
            ValueType,
            IsSensitive,
            @Now,
            @Now
        FROM OPENJSON(@ExtendedAttributesJson)
        WITH (
            AttributeName NVARCHAR(200) '$.AttributeName',
            AttributeValue NVARCHAR(MAX) '$.AttributeValue',
            ValueType NVARCHAR(50) '$.ValueType',
            IsSensitive BIT '$.IsSensitive'
        );
    END

    -- Return result
    SELECT
        @ExistingId AS Id,
        @IsNew AS IsNew,
        @@ROWCOUNT AS AttributesInserted;
END
GO

PRINT 'usp_UpsertIdentityWithAttributes updated successfully'
GO

PRINT ''
PRINT '========================================='
PRINT 'ALL STORED PROCEDURES UPDATED SUCCESSFULLY'
PRINT '========================================='
PRINT ''
PRINT 'Changes applied:'
PRINT '  - Identities table -> Objects table'
PRINT '  - IdentityAttributes table -> ObjectAttributes table'
PRINT '  - PersonId column -> IdentityId column'
PRINT '  - IdentityId column -> ObjectId column (in attributes)'
PRINT ''
PRINT 'You can now run a new sync and it should work without errors!'
