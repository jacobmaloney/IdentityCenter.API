-- V080: License compliance violations log

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LicenseComplianceViolations')
BEGIN
    CREATE TABLE LicenseComplianceViolations (
        Id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
        SqlServerInventoryId    UNIQUEIDENTIFIER NULL,
        ObjectId                NVARCHAR(450) NULL,
        ViolationType           NVARCHAR(100) NOT NULL,     -- 'DeveloperInProd' | 'Unlicensed' | 'EndOfLife' | 'NoOwner' | 'CoreDeficit'
        Severity                NVARCHAR(20) NOT NULL DEFAULT 'Warning',  -- 'Info' | 'Warning' | 'Critical'
        Title                   NVARCHAR(500) NOT NULL,
        Detail                  NVARCHAR(MAX) NULL,
        IsResolved              BIT NOT NULL DEFAULT 0,
        ResolvedAt              DATETIME2 NULL,
        ResolvedBy              NVARCHAR(256) NULL,
        ResolutionNote          NVARCHAR(MAX) NULL,
        DetectedAt              DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CertificationCampaignId UNIQUEIDENTIFIER NULL       -- linked AccessReview campaign if one was opened
    );
    CREATE INDEX IX_LicenseComplianceViolations_Object ON LicenseComplianceViolations(ObjectId) WHERE ObjectId IS NOT NULL;
    CREATE INDEX IX_LicenseComplianceViolations_Unresolved ON LicenseComplianceViolations(ViolationType) WHERE IsResolved = 0;
END
