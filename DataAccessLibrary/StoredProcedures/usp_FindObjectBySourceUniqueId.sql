-- Stored Procedure: Find Object by Source Unique ID
-- Fast lookup of identity object and attributes by source system identifier
CREATE OR ALTER PROCEDURE [dbo].[usp_FindObjectBySourceUniqueId]
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
        i.ManagerSourceId,
        i.ObjectClass,
        i.IsActive,
        i.IsAuthoritative,
        i.IsBuiltIn,
        i.IdentityId,
        i.MatchConfidence,
        i.MatchMethod,
        i.FirstSyncedAt,
        i.LastSyncedAt,
        i.LastSeenAt,
        i.DeletedAt
    FROM Objects i WITH (NOLOCK)
    WHERE i.SourceConnectionId = @SourceConnectionId
      AND i.SourceUniqueId = @SourceUniqueId;

    -- Return extended attributes
    SELECT
        ia.Id,
        ia.ObjectId,
        ia.AttributeName,
        ia.AttributeValue,
        ia.LastSyncedAt
    FROM ObjectAttributes ia WITH (NOLOCK)
    INNER JOIN Objects i WITH (NOLOCK) ON ia.ObjectId = i.Id
    WHERE i.SourceConnectionId = @SourceConnectionId
      AND i.SourceUniqueId = @SourceUniqueId;
END
GO
