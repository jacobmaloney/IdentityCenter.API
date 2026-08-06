-- V159: Add the columns runtime writers UPDATE on AccessReviewAssignments but V004 never created.
--
-- WHY (H4 rehearsal defect): V004 creates AccessReviewAssignments WITHOUT these columns (Campaigns
-- has ModifiedAt/ModifiedBy; legacy databases grew columns by hand outside migrations). Every
-- UPDATE in CampaignCompletionJob sets ModifiedAt/ModifiedBy, so on a V004-provisioned tenant DB
-- the statement throws "Invalid column name", the per-campaign catch swallows it, and the sweep
-- reports success while completing nothing — campaigns never auto-complete in multi-tenant.
--
-- The full sweep of UPDATE AccessReviewAssignments writers found two more missing-column
-- assumptions, covered here so they stop failing silently on migration-provisioned DBs:
--   - ApprovalRepository.DelegateApproval sets DelegatedBy (a Guid) — in NO migration anywhere.
--   - ReviewPreClassificationPanel batch-certify sets DecisionDate/DecisionBy/DecisionComment —
--     in NO migration anywhere (ReviewDecisionHistory has these semantics, not this table).
--
-- Shapes: ModifiedAt/ModifiedBy match Campaigns exactly (datetime2 NULL / nvarchar(256) NULL).
-- DelegatedBy matches DelegatedTo (uniqueidentifier NULL). DecisionDate matches
-- ReviewDecisionHistory.DecisionDate (datetime2, NULL here — absent for undecided rows);
-- DecisionBy mirrors DecisionMakerName (nvarchar(200) NULL); DecisionComment nvarchar(max) NULL.
-- NULL for all existing rows — no backfill; writers stamp on next touch.
--
-- Idempotent: COL_LENGTH-guarded per column, so legacy databases that already added any of these
-- by hand re-run as a clean no-op (shared-DB rule: guard EVERY column independently).

IF COL_LENGTH('dbo.AccessReviewAssignments', 'ModifiedAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[AccessReviewAssignments]
        ADD [ModifiedAt] datetime2 NULL;
    PRINT 'V159: Added AccessReviewAssignments.ModifiedAt.';
END
ELSE
BEGIN
    PRINT 'V159: AccessReviewAssignments.ModifiedAt already present -- nothing to do.';
END
GO

IF COL_LENGTH('dbo.AccessReviewAssignments', 'ModifiedBy') IS NULL
BEGIN
    ALTER TABLE [dbo].[AccessReviewAssignments]
        ADD [ModifiedBy] nvarchar(256) NULL;
    PRINT 'V159: Added AccessReviewAssignments.ModifiedBy.';
END
ELSE
BEGIN
    PRINT 'V159: AccessReviewAssignments.ModifiedBy already present -- nothing to do.';
END
GO

IF COL_LENGTH('dbo.AccessReviewAssignments', 'DelegatedBy') IS NULL
BEGIN
    ALTER TABLE [dbo].[AccessReviewAssignments]
        ADD [DelegatedBy] uniqueidentifier NULL;
    PRINT 'V159: Added AccessReviewAssignments.DelegatedBy.';
END
ELSE
BEGIN
    PRINT 'V159: AccessReviewAssignments.DelegatedBy already present -- nothing to do.';
END
GO

IF COL_LENGTH('dbo.AccessReviewAssignments', 'DecisionDate') IS NULL
BEGIN
    ALTER TABLE [dbo].[AccessReviewAssignments]
        ADD [DecisionDate] datetime2 NULL;
    PRINT 'V159: Added AccessReviewAssignments.DecisionDate.';
END
ELSE
BEGIN
    PRINT 'V159: AccessReviewAssignments.DecisionDate already present -- nothing to do.';
END
GO

IF COL_LENGTH('dbo.AccessReviewAssignments', 'DecisionBy') IS NULL
BEGIN
    ALTER TABLE [dbo].[AccessReviewAssignments]
        ADD [DecisionBy] nvarchar(200) NULL;
    PRINT 'V159: Added AccessReviewAssignments.DecisionBy.';
END
ELSE
BEGIN
    PRINT 'V159: AccessReviewAssignments.DecisionBy already present -- nothing to do.';
END
GO

IF COL_LENGTH('dbo.AccessReviewAssignments', 'DecisionComment') IS NULL
BEGIN
    ALTER TABLE [dbo].[AccessReviewAssignments]
        ADD [DecisionComment] nvarchar(max) NULL;
    PRINT 'V159: Added AccessReviewAssignments.DecisionComment.';
END
ELSE
BEGIN
    PRINT 'V159: AccessReviewAssignments.DecisionComment already present -- nothing to do.';
END
GO
