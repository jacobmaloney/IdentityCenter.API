-- Migration: Add Role-Based Fallback Reviewer Support
-- Allows fallback reviewers to be assigned to BusinessRoles instead of individuals
-- When role-based, all role holders receive the assignment (first responder pattern)

-- ============================================
-- Add BusinessRoleId to CampaignReviewerFallbacks
-- ============================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('CampaignReviewerFallbacks') AND name = 'BusinessRoleId')
BEGIN
    ALTER TABLE CampaignReviewerFallbacks ADD BusinessRoleId UNIQUEIDENTIFIER NULL;
    PRINT 'Added BusinessRoleId column to CampaignReviewerFallbacks';
END
GO

-- Add foreign key constraint
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_CampaignReviewerFallbacks_BusinessRole')
BEGIN
    ALTER TABLE CampaignReviewerFallbacks
    ADD CONSTRAINT FK_CampaignReviewerFallbacks_BusinessRole
    FOREIGN KEY (BusinessRoleId) REFERENCES BusinessRoles(Id);
    PRINT 'Added FK constraint to BusinessRoles';
END
GO

-- Add index for BusinessRoleId lookup
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CampaignReviewerFallbacks_BusinessRoleId')
BEGIN
    CREATE INDEX IX_CampaignReviewerFallbacks_BusinessRoleId
    ON CampaignReviewerFallbacks(BusinessRoleId)
    WHERE BusinessRoleId IS NOT NULL;
    PRINT 'Created IX_CampaignReviewerFallbacks_BusinessRoleId index';
END
GO

-- ============================================
-- Add PeerGroupId to AccessReviewAssignments
-- ============================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('AccessReviewAssignments') AND name = 'PeerGroupId')
BEGIN
    ALTER TABLE AccessReviewAssignments ADD PeerGroupId UNIQUEIDENTIFIER NULL;
    PRINT 'Added PeerGroupId column to AccessReviewAssignments';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('AccessReviewAssignments') AND name = 'CompletedByPeerId')
BEGIN
    ALTER TABLE AccessReviewAssignments ADD CompletedByPeerId UNIQUEIDENTIFIER NULL;
    PRINT 'Added CompletedByPeerId column to AccessReviewAssignments';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('AccessReviewAssignments') AND name = 'CompletedByPeerName')
BEGIN
    ALTER TABLE AccessReviewAssignments ADD CompletedByPeerName NVARCHAR(500) NULL;
    PRINT 'Added CompletedByPeerName column to AccessReviewAssignments';
END
GO

-- Add index for PeerGroupId lookup (for finding peer assignments quickly)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessReviewAssignments_PeerGroupId')
BEGIN
    CREATE INDEX IX_AccessReviewAssignments_PeerGroupId
    ON AccessReviewAssignments(PeerGroupId)
    WHERE PeerGroupId IS NOT NULL;
    PRINT 'Created IX_AccessReviewAssignments_PeerGroupId index';
END
GO

-- ============================================
-- Update usp_GetFallbackReviewer to return BusinessRoleId
-- ============================================
CREATE OR ALTER PROCEDURE usp_GetFallbackReviewer
    @CampaignType NVARCHAR(100),
    @Department NVARCHAR(200) = NULL,
    @Reason NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Try to find a fallback reviewer with this priority:
    -- 1. Exact match: CampaignType AND Department
    -- 2. Campaign type only
    -- 3. Department only
    -- 4. Global fallback (NULL for both)

    SELECT TOP 1
        ReviewerId,
        ReviewerName,
        ReviewerEmail,
        ReviewerRelationship,
        BusinessRoleId,  -- NEW: Return BusinessRoleId if role-based
        CAST(0 AS BIT) as NeedsManualAssignment,
        ISNULL(@Reason, 'Using fallback reviewer') as ManualAssignmentReason
    FROM CampaignReviewerFallbacks
    WHERE IsActive = 1
      AND (
        -- Exact match has highest priority
        (CampaignType = @CampaignType AND Department = @Department)
        OR
        -- Campaign type match, any department
        (CampaignType = @CampaignType AND Department IS NULL)
        OR
        -- Department match, any campaign type
        (CampaignType IS NULL AND Department = @Department)
        OR
        -- Global fallback
        (CampaignType IS NULL AND Department IS NULL)
      )
    ORDER BY
        -- Prioritize exact matches
        CASE
            WHEN CampaignType = @CampaignType AND Department = @Department THEN 1
            WHEN CampaignType = @CampaignType AND Department IS NULL THEN 2
            WHEN CampaignType IS NULL AND Department = @Department THEN 3
            WHEN CampaignType IS NULL AND Department IS NULL THEN 4
            ELSE 5
        END,
        Priority ASC;

    -- If no rows returned, the calling code handles NeedsManualAssignment = true
END
GO

PRINT 'Updated usp_GetFallbackReviewer to support BusinessRoleId';
GO

-- ============================================
-- Add stored procedure to complete peer assignments
-- ============================================
CREATE OR ALTER PROCEDURE usp_CompletePeerAssignments
    @PeerGroupId UNIQUEIDENTIFIER,
    @CompletedByAssignmentId UNIQUEIDENTIFIER,
    @CompletedByReviewerId UNIQUEIDENTIFIER,
    @CompletedByReviewerName NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE AccessReviewAssignments
    SET
        Status = 'CompletedByPeer',
        CompletedByPeerId = @CompletedByReviewerId,
        CompletedByPeerName = @CompletedByReviewerName,
        CompletedAt = GETUTCDATE(),
        ModifiedAt = GETUTCDATE()
    WHERE PeerGroupId = @PeerGroupId
      AND Id != @CompletedByAssignmentId
      AND Status IN ('Pending', 'InProgress');

    SELECT @@ROWCOUNT as AffectedCount;
END
GO

PRINT 'Created usp_CompletePeerAssignments stored procedure';
GO

-- Record migration
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20260105_AddRoleBasedFallbackSupport')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260105_AddRoleBasedFallbackSupport', '8.0.0');
    PRINT 'Migration recorded in history';
END
GO

PRINT 'Migration complete: Role-based fallback reviewer support added!';
GO
