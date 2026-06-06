-- =============================================
-- Author:      Chief Engineer Geordi La Forge
-- Create date: 2025-11-17
-- Description: Get detailed information for a specific approval
--              Returns multiple result sets based on approval type
--              Performance target: <100ms
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_GetApprovalDetails]
    @ApprovalId UNIQUEIDENTIFIER,
    @ApprovalType NVARCHAR(50) -- 'ReviewAssignment' or 'AccessRequest'
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    IF @ApprovalType = 'ReviewAssignment'
    BEGIN
        -- Result Set 1: Review Assignment
        SELECT *
        FROM AccessReviewAssignments
        WHERE Id = @ApprovalId;

        -- Result Set 2: Campaign
        SELECT c.*
        FROM Campaigns c
        INNER JOIN AccessReviewAssignments ara ON c.Id = ara.CampaignId
        WHERE ara.Id = @ApprovalId;

        -- Result Set 3: Target Person Details
        SELECT
            p.Id,
            p.DisplayName,
            p.Email,
            p.Department,
            NULL AS JobTitle, -- JobTitle not in Identities table
            mgr.DisplayName AS ManagerName,
            (SELECT COUNT(*) FROM ObjectGroupMemberships ogm
             INNER JOIN Objects o ON ogm.ObjectId = o.Id
             WHERE o.IdentityId = p.Id AND ogm.RemovedAt IS NULL) AS TotalGroupMemberships,
            (SELECT COUNT(*) FROM ObjectGroupMemberships ogm
             INNER JOIN Objects o ON ogm.ObjectId = o.Id
             INNER JOIN Objects g ON ogm.GroupId = g.Id
             WHERE o.IdentityId = p.Id
               AND ogm.RemovedAt IS NULL
               AND g.IsHighRisk = 1) AS HighRiskGroupMemberships,
            p.LastLoginDate
        FROM Identities p
        LEFT JOIN Identities mgr ON p.ManagerIdentityId = mgr.Id
        INNER JOIN AccessReviewAssignments ara ON p.Id = ara.ReviewTargetId
        WHERE ara.Id = @ApprovalId
          AND ara.ReviewTargetType = 'User';

        -- Result Set 4: Previous Decisions for this Assignment
        SELECT TOP 5 *
        FROM ReviewDecisionHistory
        WHERE AssignmentId = @ApprovalId
        ORDER BY DecisionDate DESC;

        -- Result Set 5: Risk Analysis (JSON)
        SELECT (
            SELECT
                ara.RiskScore,
                ara.RiskLevel,
                JSON_QUERY((
                    SELECT
                        CASE WHEN ara.RiskScore > 80 THEN 'Critical risk score'
                             WHEN ara.IsEscalated = 1 THEN 'Previously escalated'
                             WHEN ara.AccessFrequency = 'Never' THEN 'No recent access'
                             ELSE NULL
                        END AS Factor
                    FOR JSON PATH
                )) AS RiskFactors,
                NULL AS MlRecommendation,
                NULL AS ConfidenceScore
            FROM AccessReviewAssignments ara
            WHERE ara.Id = @ApprovalId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) AS RiskAnalysisJson;
    END

    ELSE IF @ApprovalType = 'AccessRequest'
    BEGIN
        -- Result Set 1: Access Request
        SELECT *
        FROM AccessRequests
        WHERE Id = @ApprovalId;

        -- Result Set 2: Target Resource Info (JSON)
        SELECT (
            SELECT
                ar.ResourceType,
                ar.ResourceName,
                ar.ResourceId,
                ar.DurationDays,
                'Requested access duration' AS AccessType
            FROM AccessRequests ar
            WHERE ar.Id = @ApprovalId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) AS ResourceInfoJson;

        -- Result Set 3: Requester Info
        SELECT
            u.Id,
            u.DisplayName,
            u.Email,
            u.Department,
            NULL AS JobTitle, -- JobTitle not in AspNetUsers table
            NULL AS ManagerName,
            0 AS TotalGroupMemberships,
            0 AS HighRiskGroupMemberships,
            NULL AS LastLoginDate
        FROM AspNetUsers u
        INNER JOIN AccessRequests ar ON u.Id = ar.RequesterId
        WHERE ar.Id = @ApprovalId;
    END
END
GO
