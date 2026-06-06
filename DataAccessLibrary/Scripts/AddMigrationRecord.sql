-- Add missing migration records to IdentityCenter13
-- These migrations were applied to IdentityCenter but need to be tracked in IdentityCenter13

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251204053904_AddTotalPersonsCreatedToSyncProjectRun')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251204053904_AddTotalPersonsCreatedToSyncProjectRun', '8.0.0');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251210000000_DisableBuiltInComplianceItemsByDefault')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251210000000_DisableBuiltInComplianceItemsByDefault', '8.0.0');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251211060858_AddReportingSystem')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251211060858_AddReportingSystem', '8.0.0');

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251212042626_AddBusinessRolesTables')
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20251212042626_AddBusinessRolesTables', '8.0.0');

PRINT 'Migration records added';

SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
