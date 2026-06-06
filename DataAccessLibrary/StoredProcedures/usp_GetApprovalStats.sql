-- =============================================
-- Author:      Chief Engineer Geordi La Forge
-- Create date: 2025-11-17
-- Description: Get real-time approval statistics for dashboard
--              Performance target: <100ms (with caching at app layer)
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_GetApprovalStats]
    @ApproverId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; -- Performance optimization

    DECLARE @NowUtc DATETIME2 = GETUTCDATE();
    DECLARE @ThisWeekStart DATETIME2 = DATEADD(DAY, -DATEPART(WEEKDAY, @NowUtc) + 1, CAST(CAST(@NowUtc AS DATE) AS DATETIME2));
    DECLARE @ThisMonthStart DATETIME2 = DATEADD(DAY, -DAY(@NowUtc) + 1, CAST(CAST(@NowUtc AS DATE) AS DATETIME2));

    -- Single comprehensive query for all statistics
    -- When @ApproverId is empty GUID (00000000-0000-0000-0000-000000000000), return stats for ALL approvers (admin view)
    DECLARE @IsAdminView BIT = CASE WHEN @ApproverId = '00000000-0000-0000-0000-000000000000' THEN 1 ELSE 0 END;

    SELECT
        -- Overall Counts
        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending') +
        (SELECT COUNT(*)
         FROM AccessRequests
         WHERE (@IsAdminView = 1 OR ApproverId = CAST(@ApproverId AS NVARCHAR(450)))
           AND Status = 'Pending') AS TotalPending,

        -- Overdue Count
        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending'
           AND DueDate < @NowUtc) AS TotalOverdue,

        -- Due This Week
        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending'
           AND DueDate BETWEEN @NowUtc AND DATEADD(DAY, 7, @NowUtc)) AS DueThisWeek,

        -- Completed This Month
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

        -- By Type
        (SELECT COUNT(*)
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Pending') AS PendingReviewAssignments,

        (SELECT COUNT(*)
         FROM AccessRequests
         WHERE (@IsAdminView = 1 OR ApproverId = CAST(@ApproverId AS NVARCHAR(450)))
           AND Status = 'Pending') AS PendingAccessRequests,

        -- By Risk Level
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

        -- Performance Metrics
        ISNULL((SELECT AVG(CAST(DATEDIFF(HOUR, AssignedAt, CompletedAt) AS DECIMAL(10,2)))
         FROM AccessReviewAssignments
         WHERE (@IsAdminView = 1 OR ReviewerId = @ApproverId)
           AND Status = 'Completed'
           AND CompletedAt >= DATEADD(MONTH, -1, @NowUtc)), 0) AS AverageDecisionTimeHours,

        -- Approval Rate (percentage)
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
