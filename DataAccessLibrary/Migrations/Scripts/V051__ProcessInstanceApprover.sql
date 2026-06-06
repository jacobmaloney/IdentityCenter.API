-- V051: Add ApproverId to ProcessInstances and extend approval stored procedures
-- Enables ProcessInstances (WaitForApproval) to appear in the unified approval inbox.

-- =============================================
-- Step 1: Add ApproverId column to ProcessInstances
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ProcessInstances') AND name = 'ApproverId')
BEGIN
    ALTER TABLE ProcessInstances ADD ApproverId NVARCHAR(450) NULL;
END
GO

-- Index for approval inbox queries (filtered to WaitingForApproval status)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProcessInstances_ApproverId_Status')
BEGIN
    CREATE NONCLUSTERED INDEX IX_ProcessInstances_ApproverId_Status
        ON ProcessInstances (ApproverId, Status)
        INCLUDE (TargetEntityName, TargetEntityType, WorkflowId, StartedAt, WaitCondition)
        WHERE Status = 'WaitingForApproval';
END
GO

-- Pre-requisite: Ensure SortOrder column exists on ApprovalWorkflowNodes
-- (used by usp_GetApprovalDetails for "next steps" preview)
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'ApprovalWorkflowNodes' AND COLUMN_NAME = 'SortOrder'
)
BEGIN
    ALTER TABLE [ApprovalWorkflowNodes] ADD [SortOrder] INT NOT NULL DEFAULT 0;
    PRINT 'Added ApprovalWorkflowNodes.SortOrder column';
END;
GO

-- Also ensure IsHighRisk on Objects (V050 should have added it, but guard just in case)
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Objects' AND COLUMN_NAME = 'IsHighRisk'
)
BEGIN
    ALTER TABLE [Objects] ADD [IsHighRisk] BIT NOT NULL DEFAULT 0;
END;
GO

-- =============================================
-- Step 2: Extend usp_GetPendingApprovals with ProcessApproval UNION ALL
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

        UNION ALL

        -- Process Approval Requests (WaitForApproval instances)
        SELECT
            pi.Id AS ApprovalId,
            'ProcessApproval' AS ApprovalType,
            COALESCE(pi.TargetEntityName, 'Process Instance') AS TargetName,
            COALESCE(pi.TargetEntityType, 'Process') AS TargetType,
            COALESCE(pi.TargetEntityId, '00000000-0000-0000-0000-000000000000') AS TargetId,
            aw.Name AS CampaignName,
            pi.TargetEntityName AS ResourceName,
            JSON_VALUE(pi.WaitCondition, '$.Instructions') AS RequestReason,
            0 AS RiskScore,
            'Medium' AS RiskLevel,
            CASE WHEN JSON_VALUE(pi.WaitCondition, '$.TimeoutHours') IS NOT NULL
                 AND DATEADD(HOUR, CAST(JSON_VALUE(pi.WaitCondition, '$.TimeoutHours') AS INT), pi.StartedAt) < GETUTCDATE()
                 THEN 1 ELSE 0 END AS IsOverdue,
            CASE WHEN JSON_VALUE(pi.WaitCondition, '$.TimeoutHours') IS NOT NULL
                 THEN DATEDIFF(DAY, GETUTCDATE(),
                      DATEADD(HOUR, CAST(JSON_VALUE(pi.WaitCondition, '$.TimeoutHours') AS INT), pi.StartedAt))
                 ELSE 999 END AS DaysUntilDue,
            pi.StartedAt AS AssignedDate,
            CASE WHEN JSON_VALUE(pi.WaitCondition, '$.TimeoutHours') IS NOT NULL
                 THEN DATEADD(HOUR, CAST(JSON_VALUE(pi.WaitCondition, '$.TimeoutHours') AS INT), pi.StartedAt)
                 ELSE NULL END AS DueDate,
            NULL AS LastAccessDate,
            JSON_VALUE(pi.WaitCondition, '$.Instructions') AS ContextSummary,
            NULL AS Department,
            NULL AS AccessFrequency
        FROM ProcessInstances pi
        INNER JOIN ApprovalWorkflows aw ON pi.WorkflowId = aw.Id
        WHERE (@IsAdminView = 1 OR pi.ApproverId = CAST(@ApproverId AS NVARCHAR(450)))
          AND pi.Status = 'WaitingForApproval'
          AND (@ApprovalType IS NULL OR @ApprovalType = 'ProcessApproval')
          AND (@SearchTerm IS NULL OR
               pi.TargetEntityName LIKE '%' + @SearchTerm + '%' OR
               aw.Name LIKE '%' + @SearchTerm + '%')
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
-- Step 3: Extend usp_GetApprovalDetails with ProcessApproval branch
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
    ELSE IF @ApprovalType = 'ProcessApproval'
    BEGIN
        -- Set 1: Process instance details
        SELECT pi.Id, pi.WorkflowId, pi.CurrentNodeId, pi.Status,
               pi.TargetEntityType, pi.TargetEntityId, pi.TargetEntityName,
               pi.WaitCondition, pi.StartedAt, pi.ApproverId,
               aw.Name AS WorkflowName, aw.Description AS WorkflowDescription,
               n.NodeName AS CurrentNodeName
        FROM ProcessInstances pi
        INNER JOIN ApprovalWorkflows aw ON pi.WorkflowId = aw.Id
        LEFT JOIN ApprovalWorkflowNodes n ON n.Id = pi.CurrentNodeId
        WHERE pi.Id = @ApprovalId;

        -- Set 2: Step logs (completed + waiting steps)
        SELECT psl.Id, psl.NodeId, psl.NodeType, psl.NodeName,
               psl.Status, psl.StartedAt, psl.CompletedAt, psl.DurationMs,
               psl.ApprovedBy, psl.ApprovalComments, psl.ErrorMessage
        FROM ProcessStepLogs psl
        WHERE psl.ProcessInstanceId = @ApprovalId
        ORDER BY psl.StartedAt ASC;

        -- Set 3: Remaining workflow nodes after current node (next steps preview)
        SELECT n.Id, n.NodeName, n.NodeType, n.SortOrder
        FROM ApprovalWorkflowNodes n
        INNER JOIN ProcessInstances pi ON n.WorkflowId = pi.WorkflowId
        WHERE pi.Id = @ApprovalId
          AND n.SortOrder > ISNULL((
              SELECT cn.SortOrder FROM ApprovalWorkflowNodes cn
              WHERE cn.Id = pi.CurrentNodeId
          ), 0)
        ORDER BY n.SortOrder ASC;
    END
END
GO
