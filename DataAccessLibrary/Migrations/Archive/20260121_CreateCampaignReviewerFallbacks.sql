-- Migration: Create CampaignReviewerFallbacks table
-- Fixes: Invalid object name 'CampaignReviewerFallbacks' error

-- ============================================
-- Create CampaignReviewerFallbacks table
-- ============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CampaignReviewerFallbacks')
BEGIN
    CREATE TABLE CampaignReviewerFallbacks (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        CampaignType NVARCHAR(100) NULL,
        Department NVARCHAR(200) NULL,
        ReviewerId UNIQUEIDENTIFIER NULL,
        ReviewerName NVARCHAR(500) NULL,
        ReviewerEmail NVARCHAR(500) NULL,
        ReviewerRelationship NVARCHAR(100) NOT NULL DEFAULT 'Fallback Reviewer',
        Priority INT NOT NULL DEFAULT 100,
        IsActive BIT NOT NULL DEFAULT 1,
        BusinessRoleId UNIQUEIDENTIFIER NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ModifiedAt DATETIME2 NULL
    );

    PRINT 'Created CampaignReviewerFallbacks table';
END
ELSE
BEGIN
    PRINT 'CampaignReviewerFallbacks table already exists';
END
GO

-- Add BusinessRoleId column if table existed but column doesn't
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'CampaignReviewerFallbacks')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignReviewerFallbacks') AND name = 'BusinessRoleId')
BEGIN
    ALTER TABLE CampaignReviewerFallbacks ADD BusinessRoleId UNIQUEIDENTIFIER NULL;
    PRINT 'Added BusinessRoleId column';
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
        BusinessRoleId,
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
END
GO

PRINT 'Created/updated usp_GetFallbackReviewer stored procedure';
GO

PRINT 'Migration complete: CampaignReviewerFallbacks table ready!';
GO
