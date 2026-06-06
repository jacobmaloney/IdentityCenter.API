-- Stored Procedure: Find Identity by SourceUniqueId with all related data
-- Ultra-fast lookup for sync operations
CREATE OR ALTER PROCEDURE [dbo].[usp_FindIdentityBySourceUniqueId]
    @SourceConnectionId UNIQUEIDENTIFIER,
    @SourceUniqueId NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        i.Id,
        i.SourceConnectionId,
        i.SourceUniqueId,
        i.SourceType,
        i.DisplayName,
        i.Email,
        i.Username,
        i.FirstName,
        i.LastName,
        i.Department,
        i.JobTitle,
        i.Phone,
        i.ManagerSourceId,
        i.IdentityId,
        i.IsActive,
        i.IsAuthoritative,
        i.MatchConfidence,
        i.MatchMethod,
        i.FirstSyncedAt,
        i.LastSyncedAt,
        i.LastSeenAt,
        i.IsBuiltIn,
        i.IsAdminSDHolder
    FROM Objects i WITH (NOLOCK)
    WHERE i.SourceConnectionId = @SourceConnectionId
      AND i.SourceUniqueId = @SourceUniqueId;

    -- Return extended attributes separately for efficient mapping
    SELECT
        ia.Id,
        ia.ObjectId,
        ia.AttributeName,
        ia.AttributeValue,
        ia.DataType,
        ia.LastSyncedAt
    FROM ObjectAttributes ia WITH (NOLOCK)
    INNER JOIN Objects i WITH (NOLOCK) ON ia.ObjectId = i.Id
    WHERE i.SourceConnectionId = @SourceConnectionId
      AND i.SourceUniqueId = @SourceUniqueId;
END
GO
