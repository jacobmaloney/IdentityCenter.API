-- Add BusinessRoleId column to CampaignReviewerFallbacks table
-- This enables role-based fallback assignment where all role holders receive the review

-- Add BusinessRoleId column if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignReviewerFallbacks') AND name = 'BusinessRoleId')
BEGIN
    ALTER TABLE CampaignReviewerFallbacks ADD BusinessRoleId UNIQUEIDENTIFIER NULL;

    -- Add foreign key constraint
    ALTER TABLE CampaignReviewerFallbacks
    ADD CONSTRAINT FK_CampaignReviewerFallbacks_BusinessRole
    FOREIGN KEY (BusinessRoleId) REFERENCES BusinessRoles(Id);

    PRINT 'Added BusinessRoleId column to CampaignReviewerFallbacks';
END
ELSE
BEGIN
    PRINT 'BusinessRoleId column already exists';
END
GO
