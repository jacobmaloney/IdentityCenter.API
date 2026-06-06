-- V028: Add per-policy SLA hours configuration
-- Allows each compliance policy to define custom SLA windows per severity level
-- Defaults match the previous hardcoded values (Critical: 4h, High: 24h, Medium: 72h, Low: 168h)
--
-- Each ALTER is guarded by a sys.columns check so a partial-apply crash (e.g. process killed
-- between batches before the version row was written) leaves the migration safely re-runnable.

IF COL_LENGTH(N'CompliancePolicies', N'SlaCriticalHours') IS NULL
BEGIN
    ALTER TABLE CompliancePolicies ADD SlaCriticalHours INT NOT NULL DEFAULT 4;
END
GO

IF COL_LENGTH(N'CompliancePolicies', N'SlaHighHours') IS NULL
BEGIN
    ALTER TABLE CompliancePolicies ADD SlaHighHours INT NOT NULL DEFAULT 24;
END
GO

IF COL_LENGTH(N'CompliancePolicies', N'SlaMediumHours') IS NULL
BEGIN
    ALTER TABLE CompliancePolicies ADD SlaMediumHours INT NOT NULL DEFAULT 72;
END
GO

IF COL_LENGTH(N'CompliancePolicies', N'SlaLowHours') IS NULL
BEGIN
    ALTER TABLE CompliancePolicies ADD SlaLowHours INT NOT NULL DEFAULT 168;
END
GO
