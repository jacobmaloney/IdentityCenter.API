-- Migration: Add Fallback Reviewer Support
-- Creates CampaignReviewerFallbacks table and usp_GetFallbackReviewer stored procedure

-- ============================================
-- Create CampaignReviewerFallbacks table
-- ============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CampaignReviewerFallbacks')
BEGIN
    CREATE TABLE CampaignReviewerFallbacks (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        CampaignType NVARCHAR(100) NULL,
        Department NVARCHAR(200) NULL,
        ReviewerId UNIQUEIDENTIFIER NOT NULL,
        ReviewerName NVARCHAR(500) NOT NULL,
        ReviewerEmail NVARCHAR(500) NULL,
        ReviewerRelationship NVARCHAR(100) NOT NULL DEFAULT 'Fallback Reviewer',
        Priority INT NOT NULL DEFAULT 100,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ModifiedAt DATETIME2 NULL
    );

    PRINT 'Created CampaignReviewerFallbacks table';
END
GO

-- Create index for efficient lookup
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CampaignReviewerFallbacks_Lookup')
BEGIN
    CREATE INDEX IX_CampaignReviewerFallbacks_Lookup
    ON CampaignReviewerFallbacks(CampaignType, Department, IsActive);
    PRINT 'Created IX_CampaignReviewerFallbacks_Lookup index';
END
GO

-- ============================================
-- Create/Update usp_GetFallbackReviewer stored procedure
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

PRINT 'Created/updated usp_GetFallbackReviewer stored procedure';
GO

-- ============================================
-- Insert default fallback reviewer if none exists
-- ============================================
IF NOT EXISTS (SELECT 1 FROM CampaignReviewerFallbacks)
BEGIN
    PRINT 'No fallback reviewers configured. Adding default...';

    DECLARE @DefaultReviewerId UNIQUEIDENTIFIER;
    DECLARE @DefaultReviewerName NVARCHAR(500);
    DECLARE @DefaultReviewerEmail NVARCHAR(500);

    -- Find first active identity to use as default
    SELECT TOP 1
        @DefaultReviewerId = Id,
        @DefaultReviewerName = DisplayName,
        @DefaultReviewerEmail = PrimaryEmail
    FROM Identities
    WHERE IsActive = 1
    ORDER BY CreatedAt;

    IF @DefaultReviewerId IS NOT NULL
    BEGIN
        INSERT INTO CampaignReviewerFallbacks
        (Id, CampaignType, Department, ReviewerId, ReviewerName, ReviewerEmail, ReviewerRelationship, Priority)
        VALUES
        (NEWID(), NULL, NULL, @DefaultReviewerId, @DefaultReviewerName, @DefaultReviewerEmail, 'Default Administrator', 1);

        PRINT 'Default global fallback reviewer added: ' + @DefaultReviewerName;
    END
    ELSE
    BEGIN
        PRINT 'WARNING: No active identities found. Please add a fallback reviewer manually.';
    END
END
ELSE
BEGIN
    PRINT 'Fallback reviewers already configured.';
END
GO

-- Record migration
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20260104000000_AddFallbackReviewerSupport')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260104000000_AddFallbackReviewerSupport', '8.0.0');
    PRINT 'Migration recorded in history';
END
GO

PRINT 'Migration complete: Fallback reviewer support added successfully!';
GO
