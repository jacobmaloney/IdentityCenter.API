-- V015: Compensating migration - add missing CentralId column and create approval stored procedures
-- V010 was recorded as applied but the CentralId ALTER TABLE never executed due to a
-- compile-time column reference issue in the original script. This migration compensates.
-- Also deploys usp_GetApprovalStats and usp_GetUrgentApprovals which were never migrated.

-- Step 1: Add CentralId column if still missing (compensates for V010 failure)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Identities') AND name = 'CentralId')
BEGIN
    ALTER TABLE Identities ADD CentralId NVARCHAR(50) NULL;
    PRINT 'Added CentralId column to Identities (compensating for V010).';
END
ELSE
BEGIN
    PRINT 'CentralId already exists - skipping column add.';
END
GO

-- Step 2: Create unique filtered index (separate batch so column exists at compile time)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Identities') AND name = 'IX_Identities_CentralId')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_Identities_CentralId
        ON Identities (CentralId)
        WHERE CentralId IS NOT NULL;
    PRINT 'Created IX_Identities_CentralId index.';
END
GO

-- Step 3: Backfill existing identities that have no CentralId yet (separate batch)
WITH NumberedIdentities AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt, Id) AS RowNum
    FROM Identities
    WHERE CentralId IS NULL
)
UPDATE i
SET i.CentralId = 'IC-' + RIGHT('00000' + CAST(n.RowNum AS NVARCHAR(10)), 5)
FROM Identities i
INNER JOIN NumberedIdentities n ON i.Id = n.Id;

PRINT 'Backfill of CentralId complete.';
GO

-- Step 4: Create approval stored procedures

-- =============================================
-- usp_GetApprovalStats
-- Get real-time approval statistics for dashboard
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_GetApprovalStats]
    @ApproverId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    DECLARE @NowUtc DATETIME2 = GETUTCDATE();
    DECLARE @ThisWeekStart DATETIME2 = DATEADD(DAY, -DATEPART(WEEKDAY, @NowUtc) + 1, CAST(CAST(@NowUtc AS DATE) AS DATETIME2));
    DECLARE @ThisMonthStart DATETIME2 = DATEADD(DAY, -DAY(@NowUtc) + 1, CAST(CAST(@NowUtc AS DATE) AS DATETIME2));

    DECLARE @IsAdminView BIT = CASE WHEN @ApproverId = '00000000-0000-0000-0000-000000000000' THEN 1 ELSE 0 END;

    SELECT
        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending') +
        (SELECT COUNT(*)
         FROM AccessRequests
         WHERE (@IsAdminView = 1 OR ApproverId = CAST(@ApproverId AS NVARCHAR(450)))
           AND Status = 'Pending') AS TotalPending,

        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending'
           AND DueDate < @NowUtc) AS TotalOverdue,

        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending'
           AND DueDate BETWEEN @NowUtc AND DATEADD(DAY, 7, @NowUtc)) AS DueThisWeek,

        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Completed'
           AND CompletedAt >= @ThisMonthStart) +
        (SELECT COUNT(*)
         FROM AccessRequests
         WHERE (@IsAdminView = 1 OR ApproverId = CAST(@ApproverId AS NVARCHAR(450)))
           AND Status IN ('Approved', 'Denied')
           AND ApprovedAt >= @ThisMonthStart) AS CompletedThisMonth,

        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending') AS PendingReviewAssignments,

        (SELECT COUNT(*)
         FROM AccessRequests
         WHERE (@IsAdminView = 1 OR ApproverId = CAST(@ApproverId AS NVARCHAR(450)))
           AND Status = 'Pending') AS PendingAccessRequests,

        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending'
           AND RiskLevel = 'Critical') AS CriticalRisk,

        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending'
           AND RiskLevel = 'High') AS HighRisk,

        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending'
           AND RiskLevel = 'Medium') AS MediumRisk,

        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending'
           AND RiskLevel = 'Low') AS LowRisk,

        ISNULL((SELECT AVG(CAST(DATEDIFF(HOUR, AssignedAt, CompletedAt) AS DECIMAL(10,2)))
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Completed'
           AND CompletedAt >= DATEADD(MONTH, -1, @NowUtc)), 0) AS AverageDecisionTimeHours,

        CASE
            WHEN (SELECT COUNT(*)
                  FROM AccessReviewAssignments
                  WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
                    AND Status = 'Completed'
                    AND CompletedAt >= DATEADD(MONTH, -1, @NowUtc)) > 0
            THEN CAST((SELECT COUNT(*) * 100.0
                       FROM AccessReviewAssignments
                       WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
                         AND Status = 'Completed'
                         AND Decision = 'Approved'
                         AND CompletedAt >= DATEADD(MONTH, -1, @NowUtc)) AS DECIMAL(5,2)) /
                 (SELECT COUNT(*)
                  FROM AccessReviewAssignments
                  WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
                    AND Status = 'Completed'
                    AND CompletedAt >= DATEADD(MONTH, -1, @NowUtc))
            ELSE 0
        END AS ApprovalRate;
END
GO

-- =============================================
-- usp_GetUrgentApprovals
-- Get approvals that are overdue or due within 24 hours
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_GetUrgentApprovals]
    @ApproverId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    DECLARE @NowUtc DATETIME2 = GETUTCDATE();
    DECLARE @Next24Hours DATETIME2 = DATEADD(HOUR, 24, @NowUtc);

    SELECT
        ara.Id AS ApprovalId,
        'ReviewAssignment' AS ApprovalType,
        COALESCE(ara.ReviewTargetName, p.DisplayName, 'Unknown') AS TargetName,
        ara.ReviewTargetType AS TargetType,
        ara.ReviewTargetId AS TargetId,
        c.Name AS CampaignName,
        NULL AS ResourceName,
        NULL AS RequestReason,
        ara.RiskScore,
        ara.RiskLevel,
        CASE WHEN ara.DueDate < @NowUtc THEN 1 ELSE 0 END AS IsOverdue,
        DATEDIFF(DAY, @NowUtc, ara.DueDate) AS DaysUntilDue,
        ara.AssignedAt AS AssignedDate,
        ara.DueDate,
        ara.LastAccessDate,
        ara.ReasonForAccess AS ContextSummary,
        p.Department,
        ara.AccessFrequency,
        CASE
            WHEN ara.DueDate < @NowUtc THEN 1
            WHEN ara.RiskLevel = 'Critical' THEN 2
            WHEN ara.DueDate < DATEADD(HOUR, 4, @NowUtc) THEN 3
            WHEN ara.RiskLevel = 'High' THEN 4
            WHEN ara.DueDate < DATEADD(HOUR, 12, @NowUtc) THEN 5
            ELSE 6
        END AS UrgencyScore
    FROM AccessReviewAssignments ara
    INNER JOIN Campaigns c ON ara.CampaignId = c.Id
    LEFT JOIN Identities p ON ara.ReviewTargetId = p.Id AND ara.ReviewTargetType = 'User'
    WHERE ara.ReviewerId = @ApproverId
      AND ara.Status = 'Pending'
      AND (
          ara.DueDate < @Next24Hours
          OR ara.RiskLevel IN ('Critical', 'High')
      )
    ORDER BY UrgencyScore ASC, ara.DueDate ASC, ara.RiskScore DESC;
END
GO
