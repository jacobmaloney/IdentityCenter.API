-- V112: Create PendingPasswordResets queue table for the Password Policy admin UI.
--
-- The /admin/password-policy page (Prompt 8) lets an admin queue user accounts for
-- "must change password at next logon". When an AD write-back connector is wired,
-- the queue is drained automatically; until then, rows stay in 'Pending' status.
--
-- Schema notes:
--   * Idempotent — re-running this migration is a no-op.
--   * Status is one of: Pending / Applied / Failed (varchar to keep migrations cheap;
--     enforced in repo code, not via CHECK constraint).
--   * Index on (ObjectId, RequestedAt DESC) supports the per-object lookup pattern
--     used by the UI to show "queued reset" pills next to a user.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PendingPasswordResets')
BEGIN
    CREATE TABLE PendingPasswordResets (
        Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_PendingPasswordResets_Id DEFAULT NEWSEQUENTIALID(),
        ObjectId     UNIQUEIDENTIFIER NOT NULL,
        RequestedAt  DATETIME2        NOT NULL CONSTRAINT DF_PendingPasswordResets_RequestedAt DEFAULT SYSUTCDATETIME(),
        RequestedBy  NVARCHAR(200)    NULL,
        Status       NVARCHAR(20)     NOT NULL CONSTRAINT DF_PendingPasswordResets_Status DEFAULT 'Pending',
        AppliedAt    DATETIME2        NULL,
        Notes        NVARCHAR(MAX)    NULL,
        CONSTRAINT PK_PendingPasswordResets PRIMARY KEY CLUSTERED (Id)
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_PendingPasswordResets_ObjectId_RequestedAt'
      AND object_id = OBJECT_ID('PendingPasswordResets'))
BEGIN
    CREATE INDEX IX_PendingPasswordResets_ObjectId_RequestedAt
        ON PendingPasswordResets (ObjectId, RequestedAt DESC);
END
GO
