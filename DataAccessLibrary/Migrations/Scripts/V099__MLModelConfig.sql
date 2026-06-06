-- V099: ML Model Configuration — per-model scheduling, server assignment, champion/challenger, drift detection

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('MLModelConfig') AND type = 'U')
BEGIN
    CREATE TABLE MLModelConfig (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        ModelName NVARCHAR(100) NOT NULL,
        DisplayName NVARCHAR(200) NOT NULL,
        Description NVARCHAR(500) NULL,
        IsEnabled BIT NOT NULL DEFAULT 1,
        CronSchedule NVARCHAR(100) NOT NULL DEFAULT '0 0 2 ? * SUN',
        TargetServerId UNIQUEIDENTIFIER NULL,
        LastTrainedAt DATETIME2 NULL,
        LastTrainedDuration INT NULL,
        LastSampleCount INT NULL,
        LastAccuracy FLOAT NULL,
        LastRSquared FLOAT NULL,
        AutoScoreAfterTraining BIT NOT NULL DEFAULT 1,
        MinimumSamples INT NOT NULL DEFAULT 30,

        -- Champion/Challenger support
        IsChampion BIT NOT NULL DEFAULT 1,
        ChampionModelVersion NVARCHAR(20) NULL,
        ChallengerAccuracy FLOAT NULL,
        PromotionThreshold FLOAT NOT NULL DEFAULT 0.02,

        -- Concept drift detection
        LastScoreHistogramJson NVARCHAR(MAX) NULL,
        PreviousScoreHistogramJson NVARCHAR(MAX) NULL,
        LastDriftCheckAt DATETIME2 NULL,
        DriftAlertThreshold FLOAT NOT NULL DEFAULT 0.15,

        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT UQ_MLModelConfig_ModelName UNIQUE (ModelName)
    );

    -- Seed all 6 models
    INSERT INTO MLModelConfig (ModelName, DisplayName, Description, CronSchedule, MinimumSamples) VALUES
        ('PeerOutlierDetection',     'Peer Outlier Detection',       'Detects users with access patterns significantly different from their peer group',          '0 0 2 ? * SUN',     50),
        ('RiskPrediction',           'Risk Prediction',              'Predicts a continuous risk score (0-100) based on access, behavior, and compliance signals',  '0 0 2 ? * WED,SUN', 50),
        ('DisablementPrediction',    'Disablement Prediction',       'Predicts whether an account should be disabled based on inactivity and access patterns',     '0 30 2 ? * SUN',    50),
        ('LicenseWasteDetection',    'License Waste Detection',      'Detects unused licenses assigned to inactive users',                                          '0 0 3 * * ?',       30),
        ('LicenseExhaustionForecast','License Exhaustion Forecast',  'Forecasts license pool exhaustion from growth trends',                                        '0 0 4 ? * MON',     20),
        ('ComplianceRisk',           'Compliance Risk Prediction',   'Predicts compliance risk based on access review history and policy violation patterns',        '0 0 3 ? * SUN',     30);
END
