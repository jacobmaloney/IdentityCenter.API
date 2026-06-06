-- =============================================
-- Author:      Chief Engineer Geordi La Forge
-- Create date: 2025-11-17
-- Description: Bulk approve multiple assignments with single justification
--              Performance target: <3 seconds for 10 items
-- =============================================

-- First, create the table-valued parameter type if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'GuidListTableType' AND is_table_type = 1)
BEGIN
    CREATE TYPE [dbo].[GuidListTableType] AS TABLE
    (
        ApprovalId UNIQUEIDENTIFIER NOT NULL
    );
END
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_BulkApproveAssignments]
    @ApprovalIds [dbo].[GuidListTableType] READONLY,
    @ApprovalType NVARCHAR(50), -- 'ReviewAssignment' or 'AccessRequest'
    @Justification NVARCHAR(MAX),
    @ApproverId UNIQUEIDENTIFIER,
    @ApproverName NVARCHAR(200),
    @IpAddress NVARCHAR(50) = NULL,
    @UserAgent NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @NowUtc DATETIME2 = GETUTCDATE();
        DECLARE @SuccessCount INT = 0;

        -- Create temp table for failures
        CREATE TABLE #Failures (
            ApprovalId UNIQUEIDENTIFIER,
            ErrorMessage NVARCHAR(500)
        );

        IF @ApprovalType = 'ReviewAssignment'
        BEGIN
            -- Update all valid pending assignments
            -- Note: IpAddress and UserAgent are stored in ReviewDecisionHistory, not here
            UPDATE ara
            SET
                Status = 'Completed',
                Decision = 'Approved',
                Justification = @Justification,
                Comments = 'Bulk approved',
                CompletedAt = @NowUtc
            FROM AccessReviewAssignments ara
            INNER JOIN @ApprovalIds ids ON ara.Id = ids.ApprovalId
            WHERE ara.Status = 'Pending';

            SET @SuccessCount = @@ROWCOUNT;

            -- Insert decision history for all successful approvals
            INSERT INTO ReviewDecisionHistory (
                Id,
                AssignmentId,
                CampaignId,
                Decision,
                PreviousDecision,
                Justification,
                Comments,
                DecisionMakerId,
                DecisionMakerName,
                DecisionDate,
                IpAddress,
                UserAgent,
                RiskScoreAtDecision,
                RiskLevelAtDecision,
                WasEscalated,
                WasDelegated
            )
            SELECT
                NEWID(),
                ara.Id,
                ara.CampaignId,
                'Approved',
                NULL, -- PreviousDecision
                @Justification,
                'Bulk approved by ' + @ApproverName,
                @ApproverId,
                @ApproverName,
                @NowUtc,
                @IpAddress,
                @UserAgent,
                ISNULL(ara.RiskScore, 0),
                ara.RiskLevel,
                0,
                0
            FROM AccessReviewAssignments ara
            INNER JOIN @ApprovalIds ids ON ara.Id = ids.ApprovalId
            WHERE ara.Status = 'Completed'
              AND ara.Decision = 'Approved'
              AND ara.CompletedAt = @NowUtc; -- Only records we just updated

            -- Update campaign completion percentages for all affected campaigns
            UPDATE c
            SET
                CompletedAssignments = (
                    SELECT COUNT(*)
                    FROM AccessReviewAssignments
                    WHERE CampaignId = c.Id
                      AND Status = 'Completed'
                ),
                CompletionPercentage = (
                    SELECT CAST(COUNT(*) AS DECIMAL(5,2)) * 100.0 / NULLIF(c.TotalAssignments, 0)
                    FROM AccessReviewAssignments
                    WHERE CampaignId = c.Id
                      AND Status = 'Completed'
                ),
                ModifiedAt = @NowUtc,
                ModifiedBy = @ApproverName
            FROM Campaigns c
            WHERE c.Id IN (
                SELECT DISTINCT CampaignId
                FROM AccessReviewAssignments ara
                INNER JOIN @ApprovalIds ids ON ara.Id = ids.ApprovalId
            );

            -- Identify failures (already processed or not found)
            INSERT INTO #Failures (ApprovalId, ErrorMessage)
            SELECT
                ids.ApprovalId,
                CASE
                    WHEN ara.Id IS NULL THEN 'Review assignment not found'
                    WHEN ara.Status != 'Pending' THEN 'Already processed with status: ' + ara.Status
                    ELSE 'Unknown error'
                END
            FROM @ApprovalIds ids
            LEFT JOIN AccessReviewAssignments ara ON ids.ApprovalId = ara.Id
            WHERE ara.Id IS NULL
               OR (ara.Status != 'Completed' OR ara.CompletedAt != @NowUtc);
        END

        ELSE IF @ApprovalType = 'AccessRequest'
        BEGIN
            -- Update all valid pending requests
            UPDATE ar
            SET
                Status = 'Approved',
                ApproverId = CAST(@ApproverId AS NVARCHAR(450)),
                ApprovedAt = @NowUtc,
                ApprovalComments = @Justification + ' | Bulk approved'
            FROM AccessRequests ar
            INNER JOIN @ApprovalIds ids ON ar.Id = ids.ApprovalId
            WHERE ar.Status = 'Pending';

            SET @SuccessCount = @@ROWCOUNT;

            -- Create user access grant records for all approved requests
            INSERT INTO UserAccess (
                Id,
                UserId,
                ResourceType,
                ResourceId,
                ResourceName,
                GrantedAt,
                GrantedBy,
                ExpiresAt,
                IsActive,
                AccessRequestId
            )
            SELECT
                NEWID(),
                ar.RequesterId,
                ar.ResourceType,
                ar.ResourceId,
                ar.ResourceName,
                @NowUtc,
                CAST(@ApproverId AS NVARCHAR(450)),
                CASE WHEN ar.DurationDays > 0 THEN DATEADD(DAY, ar.DurationDays, @NowUtc) ELSE NULL END,
                1,
                ar.Id
            FROM AccessRequests ar
            INNER JOIN @ApprovalIds ids ON ar.Id = ids.ApprovalId
            WHERE ar.Status = 'Approved'
              AND ar.ApprovedAt = @NowUtc; -- Only records we just updated

            -- Identify failures
            INSERT INTO #Failures (ApprovalId, ErrorMessage)
            SELECT
                ids.ApprovalId,
                CASE
                    WHEN ar.Id IS NULL THEN 'Access request not found'
                    WHEN ar.Status != 'Pending' THEN 'Already processed with status: ' + ar.Status
                    ELSE 'Unknown error'
                END
            FROM @ApprovalIds ids
            LEFT JOIN AccessRequests ar ON ids.ApprovalId = ar.Id
            WHERE ar.Id IS NULL
               OR (ar.Status != 'Approved' OR ar.ApprovedAt != @NowUtc);
        END

        COMMIT TRANSACTION;

        -- Return success count (result set 1)
        SELECT @SuccessCount AS SuccessCount;

        -- Return failures (result set 2)
        SELECT
            ApprovalId,
            ErrorMessage
        FROM #Failures;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        -- Return error
        DECLARE @ErrorMessage NVARCHAR(500) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END
GO
