-- V026: Add configurable case assignment columns to Campaigns
-- Allows policies to control WHO reviews violation cases instead of hardcoding to entity's manager

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'CaseAssigneeId')
BEGIN
    ALTER TABLE Campaigns ADD CaseAssigneeId UNIQUEIDENTIFIER NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Campaigns') AND name = 'CaseAssigneeName')
BEGIN
    ALTER TABLE Campaigns ADD CaseAssigneeName NVARCHAR(256) NULL;
END

-- Backfill: default existing campaigns to 'Manager' strategy
UPDATE Campaigns SET AssignmentStrategy = 'Manager' WHERE AssignmentStrategy IS NULL;
