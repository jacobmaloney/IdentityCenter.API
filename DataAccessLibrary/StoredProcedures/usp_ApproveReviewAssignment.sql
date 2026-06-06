-- =============================================
-- Author:      Chief Engineer Geordi La Forge
-- Create date: 2025-11-17
-- Description: Approve a review assignment or access request with full audit trail
--              Handles both ReviewAssignment and AccessRequest types
--              Performance target: <500ms with audit
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_ApproveReviewAssignment]
    @ApprovalId UNIQUEIDENTIFIER,
    @ApprovalType NVARCHAR(50), -- 'ReviewAssignment' or 'AccessRequest'
    @Justification NVARCHAR(MAX),
    @Comments NVARCHAR(MAX) = NULL,
    @DecisionMakerId UNIQUEIDENTIFIER,
    @DecisionMakerName NVARCHAR(200),
    @DecisionMakerEmail NVARCHAR(256) = NULL,
    @IpAddress NVARCHAR(50) = NULL,
    @UserAgent NVARCHAR(500) = NULL,
    @Success BIT OUTPUT,
    @ErrorMessage NVARCHAR(500) OUTPUT,
    @ProcessedAt DATETIME2 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @CurrentStatus NVARCHAR(50);
        DECLARE @CampaignId UNIQUEIDENTIFIER;
        DECLARE @PreviousDecision NVARCHAR(50);
        DECLARE @RiskScore INT;
        DECLARE @RiskLevel NVARCHAR(50);
        DECLARE @NowUtc DATETIME2 = GETUTCDATE();

        SET @ProcessedAt = @NowUtc;

        IF @ApprovalType = 'ReviewAssignment'
        BEGIN
            -- Check current status and get campaign info
            SELECT
                @CurrentStatus = Status,
                @CampaignId = CampaignId,
                @PreviousDecision = Decision,
                @RiskScore = RiskScore,
                @RiskLevel = RiskLevel
            FROM AccessReviewAssignments WITH (UPDLOCK)
            WHERE Id = @ApprovalId;

            IF @CurrentStatus IS NULL
            BEGIN
                SET @Success = 0;
                SET @ErrorMessage = 'Review assignment not found';
                ROLLBACK TRANSACTION;
                RETURN;
            END

            IF @CurrentStatus != 'Pending'
            BEGIN
                SET @Success = 0;
                SET @ErrorMessage = 'Review assignment already processed with status: ' + @CurrentStatus;
                ROLLBACK TRANSACTION;
                RETURN;
            END

            -- Update review assignment
            -- Note: IpAddress and UserAgent are stored in ReviewDecisionHistory, not here
            UPDATE AccessReviewAssignments
            SET
                Status = 'Completed',
                Decision = 'Approved',
                Justification = @Justification,
                Comments = @Comments,
                CompletedAt = @NowUtc
            WHERE Id = @ApprovalId;

            -- Update campaign completion percentage
            UPDATE Campaigns
            SET
                CompletedAssignments = (
                    SELECT COUNT(*)
                    FROM AccessReviewAssignments
                    WHERE CampaignId = @CampaignId
                      AND Status = 'Completed'
                ),
                CompletionPercentage = (
                    SELECT CAST(COUNT(*) AS DECIMAL(5,2)) * 100.0 / NULLIF(TotalAssignments, 0)
                    FROM AccessReviewAssignments
                    WHERE CampaignId = @CampaignId
                      AND Status = 'Completed'
                ),
                ModifiedAt = @NowUtc,
                ModifiedBy = @DecisionMakerName
            WHERE Id = @CampaignId;

            -- Create immutable decision history record
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
                DecisionMakerEmail,
                DecisionDate,
                IpAddress,
                UserAgent,
                RiskScoreAtDecision,
                RiskLevelAtDecision,
                WasEscalated,
                WasDelegated
            )
            VALUES (
                NEWID(),
                @ApprovalId,
                @CampaignId,
                'Approved',
                @PreviousDecision,
                @Justification,
                ISNULL(@Comments, 'Approved by ' + @DecisionMakerName),
                @DecisionMakerId,
                @DecisionMakerName,
                @DecisionMakerEmail,
                @NowUtc,
                @IpAddress,
                @UserAgent,
                ISNULL(@RiskScore, 0),
                @RiskLevel,
                0,
                0
            );

            SET @Success = 1;
            SET @ErrorMessage = NULL;
        END

        ELSE IF @ApprovalType = 'AccessRequest'
        BEGIN
            -- Check current status
            SELECT @CurrentStatus = Status
            FROM AccessRequests WITH (UPDLOCK)
            WHERE Id = @ApprovalId;

            IF @CurrentStatus IS NULL
            BEGIN
                SET @Success = 0;
                SET @ErrorMessage = 'Access request not found';
                ROLLBACK TRANSACTION;
                RETURN;
            END

            IF @CurrentStatus != 'Pending'
            BEGIN
                SET @Success = 0;
                SET @ErrorMessage = 'Access request already processed with status: ' + @CurrentStatus;
                ROLLBACK TRANSACTION;
                RETURN;
            END

            -- Update access request
            UPDATE AccessRequests
            SET
                Status = 'Approved',
                ApproverId = CAST(@DecisionMakerId AS NVARCHAR(450)),
                ApprovedAt = @NowUtc,
                ApprovalComments = @Justification + ISNULL(' | ' + @Comments, '')
            WHERE Id = @ApprovalId;

            -- Create user access grant record
            DECLARE @RequesterId NVARCHAR(450);
            DECLARE @ResourceType NVARCHAR(100);
            DECLARE @ResourceId NVARCHAR(256);
            DECLARE @ResourceName NVARCHAR(200);
            DECLARE @DurationDays INT;

            SELECT
                @RequesterId = RequesterId,
                @ResourceType = ResourceType,
                @ResourceId = ResourceId,
                @ResourceName = ResourceName,
                @DurationDays = DurationDays
            FROM AccessRequests
            WHERE Id = @ApprovalId;

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
            VALUES (
                NEWID(),
                @RequesterId,
                @ResourceType,
                @ResourceId,
                @ResourceName,
                @NowUtc,
                CAST(@DecisionMakerId AS NVARCHAR(450)),
                CASE WHEN @DurationDays > 0 THEN DATEADD(DAY, @DurationDays, @NowUtc) ELSE NULL END,
                1,
                @ApprovalId
            );

            SET @Success = 1;
            SET @ErrorMessage = NULL;
        END

        ELSE
        BEGIN
            SET @Success = 0;
            SET @ErrorMessage = 'Invalid approval type: ' + @ApprovalType;
            ROLLBACK TRANSACTION;
            RETURN;
        END

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        SET @Success = 0;
        SET @ErrorMessage = ERROR_MESSAGE();

        -- Log error
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END
GO
