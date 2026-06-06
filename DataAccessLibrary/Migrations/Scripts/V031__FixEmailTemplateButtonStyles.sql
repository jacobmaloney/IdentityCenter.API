-- V031: Fix email template button styles - add inline styles for readability
-- The .button CSS class alone doesn't render in many email clients/preview iframes

UPDATE EmailTemplates
SET Body = REPLACE(Body,
    '<a href="{ReviewUrl}" class="button">Start Review</a>',
    '<a href="{ReviewUrl}" class="button" style="background: #0d6efd; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; font-weight: bold; font-size: 16px;">Start Review</a>'),
    ModifiedAt = GETUTCDATE()
WHERE Name = 'REVIEW_ASSIGNED'
  AND Body LIKE '%class="button">Start Review</a>%'
  AND Body NOT LIKE '%style="background%">Start Review</a>%';

UPDATE EmailTemplates
SET Body = REPLACE(Body,
    '<a href="{ReviewUrl}" class="button">Continue Review</a>',
    '<a href="{ReviewUrl}" class="button" style="background: #f59e0b; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; font-weight: bold; font-size: 16px;">Continue Review</a>'),
    ModifiedAt = GETUTCDATE()
WHERE Name = 'REVIEW_REMINDER'
  AND Body LIKE '%class="button">Continue Review</a>%'
  AND Body NOT LIKE '%style="background%">Continue Review</a>%';

UPDATE EmailTemplates
SET Body = REPLACE(Body,
    '<a href="{ReviewUrl}" class="button">Complete Review Now</a>',
    '<a href="{ReviewUrl}" class="button" style="background: #dc3545; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; font-weight: bold; font-size: 16px;">Complete Review Now</a>'),
    ModifiedAt = GETUTCDATE()
WHERE Name = 'REVIEW_ESCALATION'
  AND Body LIKE '%class="button">Complete Review Now</a>%'
  AND Body NOT LIKE '%style="background%">Complete Review Now</a>%';
