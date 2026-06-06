-- V050: Deploy Approval Center stored procedures
-- Deploys usp_GetPendingApprovals and usp_GetApprovalDetails for the unified approval inbox.
-- These support both AccessReviewAssignments and AccessRequests in a single query.

-- Pre-requisite: Ensure IsHighRisk column exists on Objects table
-- (used by usp_GetApprovalDetails to count high-risk group memberships)
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Objects' AND COLUMN_NAME = 'IsHighRisk'
)
BEGIN
    ALTER TABLE [Objects] ADD [IsHighRisk] BIT NOT NULL DEFAULT 0;
    PRINT 'Added Objects.IsHighRisk column';
END;
GO

-- =============================================
-- usp_GetPendingApprovals
-- Unified query for both AccessReviewAssignments and AccessRequests
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_GetPendingApprovals]
    @ApproverId UNIQUEIDENTIFIER,
    @ApprovalType NVARCHAR(50) = NULL,
    @RiskLevel NVARCHAR(50) = NULL,
    @CampaignId UNIQUEIDENTIFIER = NULL,
    @ResourceType NVARCHAR(100) = NULL,
    @OnlyOverdue BIT = 0,
    @SearchTerm NVARCHAR(200) = NULL,
    @SortBy NVARCHAR(50) = 'DueDate',
    @SortDirection NVARCHAR(4) = 'Asc',
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;
    DECLARE @IsAdminView BIT = CASE WHEN @ApproverId = '00000000-0000-0000-0000-000000000000' THEN 1 ELSE 0 END;

    WITH UnifiedApprovals AS (
        -- Review Assignments
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
            CASE WHEN ara.DueDate < GETUTCDATE() THEN 1 ELSE 0 END AS IsOverdue,
            CASE WHEN ara.DueDate IS NULL THEN 999 ELSE DATEDIFF(DAY, GETUTCDATE(), ara.DueDate) END AS DaysUntilDue,
            ara.AssignedAt AS AssignedDate,
            ara.DueDate,
            ara.LastAccessDate,
            ara.ReasonForAccess AS ContextSummary,
            p.Department,
            ara.AccessFrequency
        FROM AccessReviewAssignments ara
        INNER JOIN Campaigns c ON ara.CampaignId = c.Id
        LEFT JOIN Identities p ON ara.ReviewTargetId = p.Id AND ara.ReviewTargetType = 'User'
        WHERE (@IsAdminView = 1 OR ara.ReviewerId = @ApproverId)
          AND ara.Status = 'Pending'
          AND (@ApprovalType IS NULL OR @ApprovalType = 'ReviewAssignment')
          AND (@RiskLevel IS NULL OR ara.RiskLevel = @RiskLevel)
          AND (@CampaignId IS NULL OR ara.CampaignId = @CampaignId)
          AND (@OnlyOverdue = 0 OR ara.DueDate < GETUTCDATE())
          AND (@SearchTerm IS NULL OR
               ara.ReviewTargetName LIKE '%' + @SearchTerm + '%' OR
               p.DisplayName LIKE '%' + @SearchTerm + '%' OR
               p.PrimaryEmail LIKE '%' + @SearchTerm + '%')

        UNION ALL

        -- Access Requests
        SELECT
            ar.Id AS ApprovalId,
            'AccessRequest' AS ApprovalType,
            ar.ResourceName AS TargetName,
            ar.ResourceType AS TargetType,
            CASE WHEN TRY_CAST(ar.ResourceId AS UNIQUEIDENTIFIER) IS NOT NULL
                 THEN CAST(ar.ResourceId AS UNIQUEIDENTIFIER)
                 ELSE '00000000-0000-0000-0000-000000000000'
            END AS TargetId,
            NULL AS CampaignName,
            ar.ResourceName,
            ar.Justification AS RequestReason,
            0 AS RiskScore,
            'Medium' AS RiskLevel,
            0 AS IsOverdue,
            999 AS DaysUntilDue,
            ar.RequestedAt AS AssignedDate,
            NULL AS DueDate,
            NULL AS LastAccessDate,
            ar.Justification AS ContextSummary,
            u.Department,
            NULL AS AccessFrequency
        FROM AccessRequests ar
        LEFT JOIN AspNetUsers u ON ar.RequesterId = u.Id
        WHERE (@IsAdminView = 1 OR ar.ApproverId = CAST(@ApproverId AS NVARCHAR(450)))
          AND ar.Status = 'Pending'
          AND (@ApprovalType IS NULL OR @ApprovalType = 'AccessRequest')
          AND (@ResourceType IS NULL OR ar.ResourceType = @ResourceType)
          AND (@SearchTerm IS NULL OR
               ar.ResourceName LIKE '%' + @SearchTerm + '%' OR
               ar.Justification LIKE '%' + @SearchTerm + '%')
    ),
    SortedApprovals AS (
        SELECT *,
            ROW_NUMBER() OVER (
                ORDER BY
                    CASE WHEN @SortBy = 'DueDate' AND @SortDirection = 'Asc' THEN DaysUntilDue END ASC,
                    CASE WHEN @SortBy = 'DueDate' AND @SortDirection = 'Desc' THEN DaysUntilDue END DESC,
                    CASE WHEN @SortBy = 'RiskScore' AND @SortDirection = 'Asc' THEN RiskScore END ASC,
                    CASE WHEN @SortBy = 'RiskScore' AND @SortDirection = 'Desc' THEN RiskScore END DESC,
                    CASE WHEN @SortBy = 'TargetName' AND @SortDirection = 'Asc' THEN TargetName END ASC,
                    CASE WHEN @SortBy = 'TargetName' AND @SortDirection = 'Desc' THEN TargetName END DESC,
                    CASE WHEN @SortBy = 'AssignedDate' AND @SortDirection = 'Asc' THEN AssignedDate END ASC,
                    CASE WHEN @SortBy = 'AssignedDate' AND @SortDirection = 'Desc' THEN AssignedDate END DESC,
                    IsOverdue DESC,
                    DaysUntilDue ASC
            ) AS RowNum
        FROM UnifiedApprovals
    )

    -- Result set 1: total count
    SELECT COUNT(*) AS TotalCount FROM SortedApprovals;

    -- Result set 2: paginated items
    SELECT
        ApprovalId, ApprovalType, TargetName, TargetType, TargetId,
        CampaignName, ResourceName, RequestReason, RiskScore, RiskLevel,
        IsOverdue, DaysUntilDue, AssignedDate, DueDate, LastAccessDate,
        ContextSummary, Department, AccessFrequency
    FROM SortedApprovals
    WHERE RowNum BETWEEN @Offset + 1 AND @Offset + @PageSize
    ORDER BY RowNum;
END
GO

-- =============================================
-- usp_GetApprovalDetails
-- Detailed information for a specific approval
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_GetApprovalDetails]
    @ApprovalId UNIQUEIDENTIFIER,
    @ApprovalType NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    IF @ApprovalType = 'ReviewAssignment'
    BEGIN
        SELECT * FROM AccessReviewAssignments WHERE Id = @ApprovalId;

        SELECT c.* FROM Campaigns c
        INNER JOIN AccessReviewAssignments ara ON c.Id = ara.CampaignId
        WHERE ara.Id = @ApprovalId;

        SELECT
            p.Id, p.DisplayName, p.PrimaryEmail AS Email, p.Department,
            NULL AS JobTitle, mgr.DisplayName AS ManagerName,
            (SELECT COUNT(*) FROM ObjectGroupMemberships ogm
             INNER JOIN Objects o ON ogm.ObjectId = o.Id
             WHERE o.IdentityId = p.Id AND ogm.RemovedAt IS NULL) AS TotalGroupMemberships,
            (SELECT COUNT(*) FROM ObjectGroupMemberships ogm
             INNER JOIN Objects o ON ogm.ObjectId = o.Id
             INNER JOIN Objects g ON ogm.GroupId = g.Id
             WHERE o.IdentityId = p.Id AND ogm.RemovedAt IS NULL AND g.IsHighRisk = 1) AS HighRiskGroupMemberships,
            p.LastLoginAt AS LastLoginDate
        FROM Identities p
        LEFT JOIN Identities mgr ON p.ManagerIdentityId = mgr.Id
        INNER JOIN AccessReviewAssignments ara ON p.Id = ara.ReviewTargetId
        WHERE ara.Id = @ApprovalId AND ara.ReviewTargetType = 'User';

        SELECT TOP 5 * FROM ReviewDecisionHistory
        WHERE AssignmentId = @ApprovalId ORDER BY DecisionDate DESC;

        SELECT (
            SELECT ara.RiskScore, ara.RiskLevel,
                JSON_QUERY((
                    SELECT CASE
                        WHEN ara.RiskScore > 80 THEN 'Critical risk score'
                        WHEN ara.IsEscalated = 1 THEN 'Previously escalated'
                        WHEN ara.AccessFrequency = 'Never' THEN 'No recent access'
                        ELSE NULL
                    END AS Factor FOR JSON PATH
                )) AS RiskFactors,
                NULL AS MlRecommendation, NULL AS ConfidenceScore
            FROM AccessReviewAssignments ara WHERE ara.Id = @ApprovalId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) AS RiskAnalysisJson;
    END
    ELSE IF @ApprovalType = 'AccessRequest'
    BEGIN
        SELECT * FROM AccessRequests WHERE Id = @ApprovalId;

        SELECT (
            SELECT ar.ResourceType, ar.ResourceName, ar.ResourceId, ar.DurationDays,
                'Requested access duration' AS AccessType
            FROM AccessRequests ar WHERE ar.Id = @ApprovalId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) AS ResourceInfoJson;

        SELECT u.Id, u.DisplayName, u.Email, u.Department,
            NULL AS JobTitle, NULL AS ManagerName,
            0 AS TotalGroupMemberships, 0 AS HighRiskGroupMemberships, NULL AS LastLoginDate
        FROM AspNetUsers u
        INNER JOIN AccessRequests ar ON u.Id = ar.RequesterId
        WHERE ar.Id = @ApprovalId;
    END
END
GO

-- Performance indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessReviewAssignments_Approver_Status_DueDate')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AccessReviewAssignments_Approver_Status_DueDate]
    ON [dbo].[AccessReviewAssignments] ([ReviewerId], [Status], [DueDate] DESC)
    INCLUDE ([ReviewTargetId], [ReviewTargetType], [ReviewTargetName], [RiskScore], [RiskLevel], [AssignedAt], [CampaignId]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AccessRequests_Approver_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AccessRequests_Approver_Status]
    ON [dbo].[AccessRequests] ([ApproverId], [Status])
    INCLUDE ([ResourceType], [ResourceName], [RequesterId], [Justification], [RequestedAt]);
END
GO
