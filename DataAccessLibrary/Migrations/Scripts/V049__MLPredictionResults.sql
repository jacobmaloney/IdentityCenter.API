-- V049: Create MLPredictionResults table for batch-persisted ML model predictions.
-- Stores pre-computed predictions from PeerOutlier, RiskPrediction, and Disablement models.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MLPredictionResults')
BEGIN
    CREATE TABLE MLPredictionResults (
        Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        IdentityId UNIQUEIDENTIFIER NOT NULL,
        ModelName NVARCHAR(100) NOT NULL,
        PredictedValue FLOAT NOT NULL,
        PredictedLabel BIT NULL,
        Confidence FLOAT NULL,
        ScoredAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MLPredictionResults PRIMARY KEY (Id),
        CONSTRAINT FK_MLPredictionResults_Identity FOREIGN KEY (IdentityId)
            REFERENCES Identities(Id) ON DELETE CASCADE
    );

    CREATE UNIQUE NONCLUSTERED INDEX UX_MLPredictionResults_Identity_Model
        ON MLPredictionResults (IdentityId, ModelName);

    CREATE NONCLUSTERED INDEX IX_MLPredictionResults_Model_Label
        ON MLPredictionResults (ModelName, PredictedLabel)
        WHERE PredictedLabel = 1;

    CREATE NONCLUSTERED INDEX IX_MLPredictionResults_Model_Value
        ON MLPredictionResults (ModelName, PredictedValue DESC);
END
