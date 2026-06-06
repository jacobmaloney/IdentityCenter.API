-- V089: Network scan history table
-- Records every network scan run with results for auditing, trending, and debugging

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'NetworkScanHistory')
BEGIN
    CREATE TABLE NetworkScanHistory (
        Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        NetworkScanRangeId UNIQUEIDENTIFIER NULL,  -- nullable for ad-hoc scans not tied to a range
        CidrRange NVARCHAR(50) NOT NULL,
        StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt DATETIME2 NULL,
        DurationSeconds INT NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Running', -- Running, Success, Failed
        TotalScanned INT NOT NULL DEFAULT 0,
        FoundServers INT NOT NULL DEFAULT 0,
        NewServers INT NOT NULL DEFAULT 0,
        ExistingServers INT NOT NULL DEFAULT 0,
        NewObjectsCreated INT NOT NULL DEFAULT 0,
        ErrorMessage NVARCHAR(2000) NULL,
        DiscoveredServersJson NVARCHAR(MAX) NULL, -- JSON array of hits for details popup
        TriggeredBy NVARCHAR(256) NULL,
        CONSTRAINT PK_NetworkScanHistory PRIMARY KEY (Id)
    );

    CREATE INDEX IX_NetworkScanHistory_RangeId ON NetworkScanHistory (NetworkScanRangeId) WHERE NetworkScanRangeId IS NOT NULL;
    CREATE INDEX IX_NetworkScanHistory_StartedAt ON NetworkScanHistory (StartedAt DESC);

    PRINT 'V089: Created NetworkScanHistory table';
END
ELSE
BEGIN
    PRINT 'V089: NetworkScanHistory already exists - skipping';
END
