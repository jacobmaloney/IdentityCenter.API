-- =============================================
-- Author:      Chief Engineer Geordi La Forge
-- Create date: 2025-11-17
-- Description: Get approvals that are overdue or due within 24 hours
--              Sorted by urgency for priority notification
--              Performance target: <200ms
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_GetUrgentApprovals]
    @ApproverId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    DECLARE @NowUtc DATETIME2 = GETUTCDATE();
    DECLARE @Next24Hours DATETIME2 = DATEADD(HOUR, 24, @NowUtc);

    -- Get urgent review assignments
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
        -- Urgency score for sorting (lower = more urgent)
        CASE
            WHEN ara.DueDate < @NowUtc THEN 1 -- Overdue
            WHEN ara.RiskLevel = 'Critical' THEN 2 -- Critical risk
            WHEN ara.DueDate < DATEADD(HOUR, 4, @NowUtc) THEN 3 -- Due in <4 hours
            WHEN ara.RiskLevel = 'High' THEN 4 -- High risk
            WHEN ara.DueDate < DATEADD(HOUR, 12, @NowUtc) THEN 5 -- Due in <12 hours
            ELSE 6 -- Due in <24 hours
        END AS UrgencyScore
    FROM AccessReviewAssignments ara
    INNER JOIN Campaigns c ON ara.CampaignId = c.Id
    LEFT JOIN Identities p ON ara.ReviewTargetId = p.Id AND ara.ReviewTargetType = 'User'
    WHERE ara.ReviewerId = @ApproverId
      AND ara.Status = 'Pending'
      AND (
          ara.DueDate < @Next24Hours -- Due within 24 hours
          OR ara.RiskLevel IN ('Critical', 'High') -- Or high/critical risk
      )
    ORDER BY UrgencyScore ASC, ara.DueDate ASC, ara.RiskScore DESC;

END
GO
