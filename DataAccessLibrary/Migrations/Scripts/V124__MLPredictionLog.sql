-- V124: ML prediction telemetry log for drift detection (Forecast Slice D)
-- One row per (ModelName, EntityId, HorizonDays, PredictedDate). HorizonDays NULL = headline
-- prediction; non-null rows are backfilled with ActualValue once their target date passes.
-- The drift-detection job compares rolling 7-day MAE to training-time MAE.

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('MLPredictionLog') AND type = 'U')
BEGIN
    CREATE TABLE MLPredictionLog (
        Id                    UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        ModelName             NVARCHAR(100)    NOT NULL,
        ModelVersion          NVARCHAR(20)     NULL,
        EntityId              UNIQUEIDENTIFIER NOT NULL,
        EntityType            NVARCHAR(50)     NOT NULL,
        PredictedAt           DATETIME2        NOT NULL,
        PredictedDate         AS CAST(PredictedAt AS DATE) PERSISTED,
        PredictionValue       FLOAT            NOT NULL,
        FeatureSnapshotJson   NVARCHAR(MAX)    NULL,
        HorizonDays           INT              NULL,
        ActualValue           FLOAT            NULL,
        ActualMeasuredAt      DATETIME2        NULL,
        CONSTRAINT PK_MLPredictionLog PRIMARY KEY (Id)
    );

    -- One prediction per (model, entity, horizon, day). HorizonDays may be NULL for headline
    -- rows; SQL Server unique indexes treat NULLs as distinct so two headline rows for the
    -- same entity on the same day would still violate the constraint — desired.
    CREATE UNIQUE NONCLUSTERED INDEX IX_MLPredictionLog_Day
        ON MLPredictionLog (ModelName, EntityId, HorizonDays, PredictedDate);

    -- Backfill scan: rows still awaiting an actual measurement.
    CREATE NONCLUSTERED INDEX IX_MLPredictionLog_BackfillPending
        ON MLPredictionLog (ModelName, PredictedAt)
        WHERE ActualValue IS NULL;

    -- Drift window scan: rows that already have actuals, ordered by measurement time.
    CREATE NONCLUSTERED INDEX IX_MLPredictionLog_DriftWindow
        ON MLPredictionLog (ModelName, ActualMeasuredAt DESC)
        INCLUDE (PredictionValue, ActualValue, EntityId)
        WHERE ActualValue IS NOT NULL;

    PRINT 'V124: Created MLPredictionLog with 3 indexes';
END
ELSE
BEGIN
    PRINT 'V124: MLPredictionLog already present - skipping';
END
GO

-- Per-model drift threshold semantic: LicenseExhaustionForecast uses MAE-ratio (1.5 = rolling
-- MAE is 1.5x training MAE). Other models still use the original 15% histogram-shift semantic.
UPDATE MLModelConfig SET DriftAlertThreshold = 1.5 WHERE ModelName = 'LicenseExhaustionForecast';
GO

PRINT 'Schema version 124 applied - MLPredictionLog + LicenseExhaustionForecast drift threshold';
