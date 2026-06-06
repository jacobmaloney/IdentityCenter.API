-- Fix email template character encoding issues and ensure consistent placeholder format
-- This migration updates existing templates to fix any character issues

-- Update REVIEW_ASSIGNED template with clean HTML
UPDATE EmailTemplates
SET Body = '<!DOCTYPE html>
<html>
<head>
<style>
body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }
.header { background: #0d6efd; color: white; padding: 20px; text-align: center; }
.header h1 { margin: 0; }
.content { padding: 20px; }
.button { background: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; }
.footer { background: #f8f9fa; padding: 15px; text-align: center; font-size: 12px; color: #666; }
ul { list-style: none; padding: 0; }
ul li { padding: 8px 0; border-bottom: 1px solid #eee; }
ul li:last-child { border-bottom: none; }
</style>
</head>
<body>
<div class="header"><h1>Access Review Assigned</h1></div>
<div class="content">
<p>Hello {ReviewerName},</p>
<p>You have been assigned to review access for the following campaign:</p>
<ul>
<li><strong>Campaign:</strong> {CampaignName}</li>
<li><strong>Due Date:</strong> {DueDate}</li>
<li><strong>Items to Review:</strong> {ItemCount}</li>
</ul>
<p>Please complete your review before the due date to ensure compliance.</p>
<p><a href="{ReviewUrl}" class="button">Start Review</a></p>
</div>
<div class="footer">This is an automated message from Identity Center.</div>
</body>
</html>',
    ModifiedAt = GETUTCDATE()
WHERE Name = 'REVIEW_ASSIGNED';

-- Update REVIEW_REMINDER template
UPDATE EmailTemplates
SET Body = '<!DOCTYPE html>
<html>
<head>
<style>
body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }
.header { background: #f59e0b; color: white; padding: 20px; text-align: center; }
.header h1 { margin: 0; }
.content { padding: 20px; }
.button { background: #f59e0b; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; }
.footer { background: #f8f9fa; padding: 15px; text-align: center; font-size: 12px; color: #666; }
ul { list-style: none; padding: 0; }
ul li { padding: 8px 0; border-bottom: 1px solid #eee; }
ul li:last-child { border-bottom: none; }
</style>
</head>
<body>
<div class="header"><h1>Review Reminder</h1></div>
<div class="content">
<p>Hello {ReviewerName},</p>
<p>This is a reminder that your access review is due soon:</p>
<ul>
<li><strong>Campaign:</strong> {CampaignName}</li>
<li><strong>Due Date:</strong> {DueDate}</li>
<li><strong>Remaining Items:</strong> {RemainingCount}</li>
</ul>
<p>Please complete your review to maintain compliance.</p>
<p><a href="{ReviewUrl}" class="button">Continue Review</a></p>
</div>
<div class="footer">This is an automated message from Identity Center.</div>
</body>
</html>',
    ModifiedAt = GETUTCDATE()
WHERE Name = 'REVIEW_REMINDER';

-- Update REVIEW_OVERDUE template
UPDATE EmailTemplates
SET Body = '<!DOCTYPE html>
<html>
<head>
<style>
body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }
.header { background: #dc2626; color: white; padding: 20px; text-align: center; }
.header h1 { margin: 0; }
.content { padding: 20px; }
.button { background: #dc2626; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; }
.footer { background: #f8f9fa; padding: 15px; text-align: center; font-size: 12px; color: #666; }
.urgent { color: #dc2626; font-weight: bold; }
ul { list-style: none; padding: 0; }
ul li { padding: 8px 0; border-bottom: 1px solid #eee; }
ul li:last-child { border-bottom: none; }
</style>
</head>
<body>
<div class="header"><h1>URGENT: Review Overdue</h1></div>
<div class="content">
<p>Hello {ReviewerName},</p>
<p class="urgent">Your access review is now overdue and requires immediate attention!</p>
<ul>
<li><strong>Campaign:</strong> {CampaignName}</li>
<li><strong>Original Due Date:</strong> {DueDate}</li>
<li><strong>Days Overdue:</strong> {DaysOverdue}</li>
<li><strong>Remaining Items:</strong> {RemainingCount}</li>
</ul>
<p>Please complete this review immediately to avoid compliance issues.</p>
<p><a href="{ReviewUrl}" class="button">Complete Review Now</a></p>
</div>
<div class="footer">This is an automated message from Identity Center.</div>
</body>
</html>',
    ModifiedAt = GETUTCDATE()
WHERE Name = 'REVIEW_OVERDUE';

-- Update REVIEW_COMPLETE template
UPDATE EmailTemplates
SET Body = '<!DOCTYPE html>
<html>
<head>
<style>
body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; }
.header { background: #10b981; color: white; padding: 20px; text-align: center; }
.header h1 { margin: 0; }
.content { padding: 20px; }
.footer { background: #f8f9fa; padding: 15px; text-align: center; font-size: 12px; color: #666; }
ul { list-style: none; padding: 0; }
ul li { padding: 8px 0; border-bottom: 1px solid #eee; }
ul li:last-child { border-bottom: none; }
</style>
</head>
<body>
<div class="header"><h1>Review Completed</h1></div>
<div class="content">
<p>Hello {ReviewerName},</p>
<p>Thank you for completing your access review!</p>
<ul>
<li><strong>Campaign:</strong> {CampaignName}</li>
<li><strong>Items Reviewed:</strong> {ItemCount}</li>
<li><strong>Approved:</strong> {ApprovedCount}</li>
<li><strong>Revoked:</strong> {RevokedCount}</li>
<li><strong>Completion Date:</strong> {CompletionDate}</li>
</ul>
<p>Your review decisions have been recorded and any revocations will be processed.</p>
</div>
<div class="footer">This is an automated message from Identity Center.</div>
</body>
</html>',
    ModifiedAt = GETUTCDATE()
WHERE Name = 'REVIEW_COMPLETE';

PRINT 'Email templates updated with clean HTML formatting';
