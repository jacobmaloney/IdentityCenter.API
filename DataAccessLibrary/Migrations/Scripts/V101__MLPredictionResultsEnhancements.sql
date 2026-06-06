-- V101: MLPredictionResults enhancements — model version tracking and TTL support

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MLPredictionResults') AND name = 'ModelVersion')
    ALTER TABLE MLPredictionResults ADD ModelVersion NVARCHAR(20) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MLPredictionResults_ScoredAt' AND object_id = OBJECT_ID('MLPredictionResults'))
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MLPredictionResults') AND name = 'ScoredAt')
        CREATE INDEX IX_MLPredictionResults_ScoredAt ON MLPredictionResults (ScoredAt) WHERE ScoredAt IS NOT NULL;
END
