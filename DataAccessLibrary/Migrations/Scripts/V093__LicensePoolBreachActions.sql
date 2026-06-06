-- V093: Add breach action fields to LicensePools
-- Allows each pool to define what happens when a threshold is breached:
-- create access review, send email, notify Teams, assign to specific reviewer.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'OnBreachCreateReview')
    ALTER TABLE LicensePools ADD OnBreachCreateReview BIT NOT NULL DEFAULT 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'OnBreachSendEmail')
    ALTER TABLE LicensePools ADD OnBreachSendEmail BIT NOT NULL DEFAULT 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'OnBreachNotifyTeams')
    ALTER TABLE LicensePools ADD OnBreachNotifyTeams BIT NOT NULL DEFAULT 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'BreachReviewerId')
    ALTER TABLE LicensePools ADD BreachReviewerId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'BreachReviewerName')
    ALTER TABLE LicensePools ADD BreachReviewerName NVARCHAR(256) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LicensePools') AND name = 'BreachEmailTemplateId')
    ALTER TABLE LicensePools ADD BreachEmailTemplateId UNIQUEIDENTIFIER NULL;
GO

-- Seed 3 license email templates (only if EmailTemplates table exists)
IF OBJECT_ID('EmailTemplates', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM EmailTemplates WHERE Name = 'LICENSE_THRESHOLD_BREACH')
INSERT INTO EmailTemplates (Id, Name, Subject, Body, Category, IsActive, IsBuiltIn, CreatedAt)
VALUES (
    'E0930000-0000-0000-0000-000000000001',
    'LICENSE_THRESHOLD_BREACH',
    'License Alert: {{PoolName}} has breached its threshold',
    '<h2>License Threshold Breach</h2>
<p>The license pool <strong>{{PoolName}}</strong> has breached its configured threshold.</p>
<table>
<tr><td><strong>Threshold Type:</strong></td><td>{{ThresholdType}}</td></tr>
<tr><td><strong>Configured Limit:</strong></td><td>{{ThresholdValue}}%</td></tr>
<tr><td><strong>Current Value:</strong></td><td>{{ActualValue}}%</td></tr>
<tr><td><strong>Severity:</strong></td><td>{{Severity}}</td></tr>
<tr><td><strong>Owned:</strong></td><td>{{TotalUnits}}</td></tr>
<tr><td><strong>In Use:</strong></td><td>{{ConsumedUnits}}</td></tr>
</table>
<p>Please review this pool and take corrective action.</p>',
    'License',
    1, 1,
    GETUTCDATE()
);
GO

IF OBJECT_ID('EmailTemplates', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM EmailTemplates WHERE Name = 'LICENSE_REVIEW_ASSIGNED')
INSERT INTO EmailTemplates (Id, Name, Subject, Body, Category, IsActive, IsBuiltIn, CreatedAt)
VALUES (
    'E0930000-0000-0000-0000-000000000002',
    'LICENSE_REVIEW_ASSIGNED',
    'License Review: {{CampaignName}} requires your attention',
    '<h2>License Review Assignment</h2>
<p>You have been assigned to review license compliance for <strong>{{CampaignName}}</strong>.</p>
<p><strong>Assignments:</strong> {{AssignmentCount}}</p>
<p><strong>Due Date:</strong> {{DueDate}}</p>
<p>Please log in to Certification Center to complete your review.</p>',
    'License',
    1, 1,
    GETUTCDATE()
);
GO

IF OBJECT_ID('EmailTemplates', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM EmailTemplates WHERE Name = 'LICENSE_VIOLATION_RESOLVED')
INSERT INTO EmailTemplates (Id, Name, Subject, Body, Category, IsActive, IsBuiltIn, CreatedAt)
VALUES (
    'E0930000-0000-0000-0000-000000000003',
    'LICENSE_VIOLATION_RESOLVED',
    'License Alert Resolved: {{PoolName}} is back within thresholds',
    '<h2>Threshold Breach Resolved</h2>
<p>The license pool <strong>{{PoolName}}</strong> is now back within its configured thresholds.</p>
<p><strong>Resolution:</strong> {{ResolvedReason}}</p>
<p>No further action is required.</p>',
    'License',
    1, 1,
    GETUTCDATE()
);
GO
