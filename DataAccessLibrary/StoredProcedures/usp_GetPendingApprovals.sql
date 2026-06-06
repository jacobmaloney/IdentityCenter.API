-- =============================================
-- Author:      Chief Engineer Geordi La Forge
-- Create date: 2025-11-17
-- Description: Get pending approvals for Approval Center inbox
--              Unified query for both AccessReviewAssignments and AccessRequests
--              Performance target: <200ms for 50 items
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_GetPendingApprovals]
    @ApproverId UNIQUEIDENTIFIER,
    @ApprovalType NVARCHAR(50) = NULL,         -- NULL (all), 'ReviewAssignment', 'AccessRequest'
    @RiskLevel NVARCHAR(50) = NULL,            -- NULL (all), 'Critical', 'High', 'Medium', 'Low'
    @CampaignId UNIQUEIDENTIFIER = NULL,       -- Filter by campaign (review assignments only)
    @ResourceType NVARCHAR(100) = NULL,        -- Filter by resource type (access requests only)
    @OnlyOverdue BIT = 0,                      -- Only show overdue items
    @SearchTerm NVARCHAR(200) = NULL,          -- Search in target names
    @SortBy NVARCHAR(50) = 'DueDate',          -- DueDate, RiskScore, TargetName, AssignedDate
    @SortDirection NVARCHAR(4) = 'Asc',        -- Asc, Desc
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; -- Performance optimization for read-heavy queries

    -- Calculate pagination
    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;

    -- Build unified approval list
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
            CASE
                WHEN ara.DueDate < GETUTCDATE() THEN 1
                ELSE 0
            END AS IsOverdue,
            CASE
                WHEN ara.DueDate IS NULL THEN 999
                ELSE DATEDIFF(DAY, GETUTCDATE(), ara.DueDate)
            END AS DaysUntilDue,
            ara.AssignedAt AS AssignedDate,
            ara.DueDate,
            ara.LastAccessDate,
            ara.ReasonForAccess AS ContextSummary,
            p.Department,
            ara.AccessFrequency
        FROM AccessReviewAssignments ara
        INNER JOIN Campaigns c ON ara.CampaignId = c.Id
        LEFT JOIN Identities p ON ara.ReviewTargetId = p.Id AND ara.ReviewTargetType = 'User'
        WHERE ara.ReviewerId = @ApproverId
          AND ara.Status = 'Pending'
          AND (@ApprovalType IS NULL OR @ApprovalType = 'ReviewAssignment')
          AND (@RiskLevel IS NULL OR ara.RiskLevel = @RiskLevel)
          AND (@CampaignId IS NULL OR ara.CampaignId = @CampaignId)
          AND (@OnlyOverdue = 0 OR ara.DueDate < GETUTCDATE())
          AND (@SearchTerm IS NULL OR
               ara.ReviewTargetName LIKE '%' + @SearchTerm + '%' OR
               p.DisplayName LIKE '%' + @SearchTerm + '%' OR
               p.Email LIKE '%' + @SearchTerm + '%')

        UNION ALL

        -- Access Requests
        SELECT
            ar.Id AS ApprovalId,
            'AccessRequest' AS ApprovalType,
            ar.ResourceName AS TargetName,
            ar.ResourceType AS TargetType,
            CAST(ar.ResourceId AS UNIQUEIDENTIFIER) AS TargetId,
            NULL AS CampaignName,
            ar.ResourceName,
            ar.Justification AS RequestReason,
            0 AS RiskScore, -- Could be enhanced with ML risk scoring
            'Medium' AS RiskLevel,
            0 AS IsOverdue, -- Access requests don't have due dates currently
            999 AS DaysUntilDue,
            ar.RequestedAt AS AssignedDate,
            NULL AS DueDate,
            NULL AS LastAccessDate,
            ar.Justification AS ContextSummary,
            u.Department,
            NULL AS AccessFrequency
        FROM AccessRequests ar
        INNER JOIN AspNetUsers u ON ar.RequesterId = u.Id
        WHERE ar.ApproverId = CAST(@ApproverId AS NVARCHAR(450))
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
                    -- Default sort: overdue first, then by due date
                    IsOverdue DESC,
                    DaysUntilDue ASC
            ) AS RowNum
        FROM UnifiedApprovals
    )

    -- Return total count (result set 1)
    SELECT COUNT(*) AS TotalCount
    FROM SortedApprovals;

    -- Return paginated approvals (result set 2)
    SELECT
        ApprovalId,
        ApprovalType,
        TargetName,
        TargetType,
        TargetId,
        CampaignName,
        ResourceName,
        RequestReason,
        RiskScore,
        RiskLevel,
        IsOverdue,
        DaysUntilDue,
        AssignedDate,
        DueDate,
        LastAccessDate,
        ContextSummary,
        Department,
        AccessFrequency
    FROM SortedApprovals
    WHERE RowNum BETWEEN @Offset + 1 AND @Offset + @PageSize
    ORDER BY RowNum;

END
GO

-- Create index for performance
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
