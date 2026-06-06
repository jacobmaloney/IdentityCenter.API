-- =============================================
-- Author:      Chief Engineer Geordi La Forge
-- Create date: 2025-11-17
-- Description: Delegate an approval to another reviewer
--              Transfers ownership with audit trail
--              Performance target: <300ms
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DelegateApproval]
    @ApprovalId UNIQUEIDENTIFIER,
    @ApprovalType NVARCHAR(50), -- 'ReviewAssignment' or 'AccessRequest'
    @DelegateToId UNIQUEIDENTIFIER,
    @DelegatedById UNIQUEIDENTIFIER,
    @Reason NVARCHAR(MAX),
    @Success BIT OUTPUT,
    @ErrorMessage NVARCHAR(500) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @CurrentStatus NVARCHAR(50);
        DECLARE @CurrentReviewerId UNIQUEIDENTIFIER;
        DECLARE @DelegateToName NVARCHAR(200);
        DECLARE @DelegateToEmail NVARCHAR(256);
        DECLARE @NowUtc DATETIME2 = GETUTCDATE();

        IF @ApprovalType = 'ReviewAssignment'
        BEGIN
            -- Check current status
            SELECT
                @CurrentStatus = Status,
                @CurrentReviewerId = ReviewerId
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
                SET @ErrorMessage = 'Cannot delegate - assignment already processed with status: ' + @CurrentStatus;
                ROLLBACK TRANSACTION;
                RETURN;
            END

            IF @CurrentReviewerId != @DelegatedById
            BEGIN
                SET @Success = 0;
                SET @ErrorMessage = 'Only the assigned reviewer can delegate this assignment';
                ROLLBACK TRANSACTION;
                RETURN;
            END

            -- Get delegate information
            SELECT
                @DelegateToName = DisplayName,
                @DelegateToEmail = Email
            FROM Identities
            WHERE Id = @DelegateToId;

            IF @DelegateToName IS NULL
            BEGIN
                SET @Success = 0;
                SET @ErrorMessage = 'Delegate person not found';
                ROLLBACK TRANSACTION;
                RETURN;
            END

            -- Update review assignment
            -- Note: IsDelegated is a computed column (DelegatedTo IS NOT NULL), so we don't set it
            UPDATE AccessReviewAssignments
            SET
                ReviewerId = @DelegateToId,
                ReviewerName = @DelegateToName,
                ReviewerEmail = @DelegateToEmail,
                DelegatedTo = @DelegateToId,
                DelegatedAt = @NowUtc,
                DelegationReason = @Reason
            WHERE Id = @ApprovalId;

            SET @Success = 1;
            SET @ErrorMessage = NULL;
        END

        ELSE IF @ApprovalType = 'AccessRequest'
        BEGIN
            -- Check current status
            SELECT
                @CurrentStatus = Status,
                @CurrentReviewerId = CAST(ApproverId AS UNIQUEIDENTIFIER)
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
                SET @ErrorMessage = 'Cannot delegate - request already processed with status: ' + @CurrentStatus;
                ROLLBACK TRANSACTION;
                RETURN;
            END

            -- Update access request approver
            UPDATE AccessRequests
            SET
                ApproverId = CAST(@DelegateToId AS NVARCHAR(450))
            WHERE Id = @ApprovalId;

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
