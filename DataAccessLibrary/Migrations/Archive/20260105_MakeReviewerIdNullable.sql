-- Make ReviewerId nullable for role-based fallback support
-- When BusinessRoleId is set, ReviewerId should be NULL (all role holders get assignment)

-- Check if constraint exists and drop it
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_CampaignReviewerFallbacks_ReviewerId')
BEGIN
    ALTER TABLE CampaignReviewerFallbacks DROP CONSTRAINT DF_CampaignReviewerFallbacks_ReviewerId;
END
GO

-- Make ReviewerId nullable
ALTER TABLE CampaignReviewerFallbacks ALTER COLUMN ReviewerId UNIQUEIDENTIFIER NULL;
GO

PRINT 'ReviewerId column is now nullable for role-based fallback support';
GO
