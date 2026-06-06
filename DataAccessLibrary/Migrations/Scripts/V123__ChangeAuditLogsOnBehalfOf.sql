-- V123: Add OnBehalfOf columns to ChangeAuditLogs.
-- Replaces the Source-suffix encoding (";onBehalfOf={guid}") shipped in
-- the H2 license-reclaim audit work with dedicated, queryable columns.
-- Also backfills existing rows that still carry the suffix in Source.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('ChangeAuditLogs') AND name = 'OnBehalfOfUserId')
BEGIN
    ALTER TABLE [ChangeAuditLogs] ADD [OnBehalfOfUserId] NVARCHAR(256) NULL;
    PRINT 'V123: Added ChangeAuditLogs.OnBehalfOfUserId column';
END
ELSE
BEGIN
    PRINT 'V123: ChangeAuditLogs.OnBehalfOfUserId already present - skipping';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('ChangeAuditLogs') AND name = 'OnBehalfOfDisplayName')
BEGIN
    ALTER TABLE [ChangeAuditLogs] ADD [OnBehalfOfDisplayName] NVARCHAR(256) NULL;
    PRINT 'V123: Added ChangeAuditLogs.OnBehalfOfDisplayName column';
END
ELSE
BEGIN
    PRINT 'V123: ChangeAuditLogs.OnBehalfOfDisplayName already present - skipping';
END
GO

-- Filtered index for "who authorized this" queries (SOX/HIPAA forensics).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ChangeAuditLogs_OnBehalfOfUserId'
      AND object_id = OBJECT_ID('ChangeAuditLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ChangeAuditLogs_OnBehalfOfUserId]
        ON [ChangeAuditLogs] ([OnBehalfOfUserId])
        WHERE [OnBehalfOfUserId] IS NOT NULL;
    PRINT 'V123: Created IX_ChangeAuditLogs_OnBehalfOfUserId';
END
GO

-- Backfill: parse ";onBehalfOf={guid}" out of Source for any row that
-- has the suffix but no OnBehalfOfUserId. Idempotent — re-running matches
-- zero rows after first success. OnBehalfOfDisplayName is intentionally
-- left NULL on backfill (parsing the human name out of UserDisplayName's
-- "System on behalf of {name}" string is too fragile to be reliable).
-- The GUID after ";onBehalfOf=" is exactly 36 chars (8-4-4-4-12 form).
--
-- ChangeAuditLogs.Source is nvarchar(50). The suffix ";onBehalfOf={36-char-guid}"
-- is itself 48 chars, so any prefix longer than ~2 chars caused silent truncation
-- on write. Validate the shape (length 36 + 8-4-4-4-12 hyphen layout) before
-- backfilling so we never write a truncated GUID + trailing garbage into
-- OnBehalfOfUserId. Rows that don't pass the shape stay NULL; the historical
-- Source-suffix record is preserved for forensic recovery.
UPDATE [ChangeAuditLogs]
SET [OnBehalfOfUserId] = SUBSTRING(
        [Source],
        CHARINDEX(';onBehalfOf=', [Source]) + 12,
        36)
WHERE [OnBehalfOfUserId] IS NULL
  AND [Source] LIKE '%;onBehalfOf=%'
  AND LEN(SUBSTRING([Source], CHARINDEX(';onBehalfOf=', [Source]) + 12, 36)) = 36
  AND SUBSTRING([Source], CHARINDEX(';onBehalfOf=', [Source]) + 12, 36)
      LIKE '________-____-____-____-____________';

PRINT 'Schema version 123 applied - ChangeAuditLogs.OnBehalfOf columns + backfill';
GO
