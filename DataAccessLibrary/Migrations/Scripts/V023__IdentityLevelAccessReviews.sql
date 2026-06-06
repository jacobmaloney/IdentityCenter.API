-- V023: Add ScopeLevel to Campaigns for Identity-Level Access Reviews
-- ScopeLevel determines whether the campaign reviews Objects (AD accounts) or Identities (people)
-- Default 'Object' preserves backward compatibility with all existing campaigns

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'ScopeLevel'
)
BEGIN
    ALTER TABLE Campaigns
    ADD ScopeLevel NVARCHAR(50) NOT NULL DEFAULT 'Object';

    PRINT 'Added ScopeLevel column to Campaigns table';
END
ELSE
BEGIN
    PRINT 'ScopeLevel column already exists on Campaigns table - skipping';
END
GO

-- Index for filtering campaigns by scope level and status
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('Campaigns') AND name = 'IX_Campaigns_ScopeLevel_Status'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Campaigns_ScopeLevel_Status
    ON Campaigns (ScopeLevel, Status);

    PRINT 'Created index IX_Campaigns_ScopeLevel_Status';
END
GO
