-- V110: Non-Human Identity (NHI) Governance tables
--
-- Adds two tables that back the NHI Governance page (/admin/nhi):
--   * NHIOwnership   — one human "owner" per service-account Object
--   * NHIAttestation — periodic certifications that the NHI is still needed
--
-- Both tables are idempotent: only created on a fresh database. Indexes are
-- guarded the same way so re-running the migration on an existing install is
-- a no-op.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'NHIOwnership')
BEGIN
    CREATE TABLE NHIOwnership (
        Id          UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        ObjectId    UNIQUEIDENTIFIER    NOT NULL,
        OwnerId     UNIQUEIDENTIFIER    NULL,
        OwnerName   NVARCHAR(200)       NULL,
        AssignedAt  DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
        AssignedBy  NVARCHAR(200)       NULL,
        Notes       NVARCHAR(MAX)       NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NHIOwnership_ObjectId' AND object_id = OBJECT_ID('NHIOwnership'))
BEGIN
    CREATE UNIQUE INDEX IX_NHIOwnership_ObjectId ON NHIOwnership (ObjectId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'NHIAttestation')
BEGIN
    CREATE TABLE NHIAttestation (
        Id            UNIQUEIDENTIFIER  NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        ObjectId      UNIQUEIDENTIFIER  NOT NULL,
        AttestedBy    NVARCHAR(200)     NOT NULL,
        AttestedAt    DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        Notes         NVARCHAR(MAX)     NULL,
        NextDueDate   DATETIME2         NOT NULL  -- AttestedAt + 90 days
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NHIAttestation_ObjectId_AttestedAt' AND object_id = OBJECT_ID('NHIAttestation'))
BEGIN
    CREATE INDEX IX_NHIAttestation_ObjectId_AttestedAt ON NHIAttestation (ObjectId, AttestedAt DESC);
END
GO
