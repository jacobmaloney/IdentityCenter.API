-- V150: Add an explicit provider/system-type to DirectoryConnections.
--
-- WHY: the License Center splits pools onto the M365 vs Google Workspace tabs by the
-- pool's owning connection. Until now the read keyed off DirectoryConnections.ConnectionType,
-- which is the TRANSPORT (e.g. 'Conduit' for connections synced through a Conduit agent), not
-- the identity PROVIDER. A real M365 tenant synced via a Conduit-typed connection therefore
-- classified as neither M365 nor GWS and dropped off both tabs.
--
-- ProviderType is the explicit provider signal. The read does
-- COALESCE(dc.ProviderType, dc.ConnectionType), so any connection left NULL here keeps its
-- exact prior behaviour; only connections stamped below change classification.
--
-- Backfill is scoped by Id to the three connections that actually own license pools:
--   certification-center.com (real Entra tenant, transport=Conduit) -> EntraID
--   Contoso Azure Cloud      (demo)                                  -> EntraID
--   Contoso GCP Directory    (demo)                                  -> GoogleCloudIdentity
-- All other connections stay NULL.
--
-- Idempotent; column add guarded on absence; UPDATEs are scoped, single-row by Id.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'DirectoryConnections' AND COLUMN_NAME = 'ProviderType')
BEGIN
    ALTER TABLE DirectoryConnections ADD ProviderType NVARCHAR(64) NULL;
    PRINT 'V150: Added DirectoryConnections.ProviderType.';
END
ELSE
BEGIN
    PRINT 'V150: DirectoryConnections.ProviderType already present -- nothing to do.';
END
GO

UPDATE DirectoryConnections SET ProviderType = 'EntraID'
WHERE Id = '97997856-4AD5-42CF-9549-19E74D5864DD' AND (ProviderType IS NULL OR ProviderType <> 'EntraID');
PRINT 'V150: Stamped certification-center.com ProviderType=EntraID (' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' row).';

UPDATE DirectoryConnections SET ProviderType = 'EntraID'
WHERE Id = 'D0000000-0000-0000-0000-000000000002' AND (ProviderType IS NULL OR ProviderType <> 'EntraID');
PRINT 'V150: Stamped Contoso Azure Cloud ProviderType=EntraID (' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' row).';

UPDATE DirectoryConnections SET ProviderType = 'GoogleCloudIdentity'
WHERE Id = 'D0000000-0000-0000-0000-000000000003' AND (ProviderType IS NULL OR ProviderType <> 'GoogleCloudIdentity');
PRINT 'V150: Stamped Contoso GCP Directory ProviderType=GoogleCloudIdentity (' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' row).';
