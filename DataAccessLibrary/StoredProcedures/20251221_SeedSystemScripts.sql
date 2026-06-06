-- ============================================
-- Seed System Processing Scripts
-- Run this on IdentityCenter13 database
-- ============================================

USE IdentityCenter13;
GO

-- Check if scripts already exist
IF (SELECT COUNT(*) FROM SyncProcessingScripts WHERE IsSystem = 1) > 0
BEGIN
    PRINT 'System scripts already exist. Skipping seed.';
    RETURN;
END

PRINT 'Seeding system processing scripts...';

-- Stable GUIDs for referential integrity
DECLARE @scriptConvertBinary UNIQUEIDENTIFIER = 'A2000001-0001-0001-0001-000000000001';
DECLARE @scriptCreateIdentity UNIQUEIDENTIFIER = 'A2000002-0001-0001-0001-000000000001';
DECLARE @scriptResolveManager UNIQUEIDENTIFIER = 'A2000003-0001-0001-0001-000000000001';
DECLARE @scriptNormalizeAttributes UNIQUEIDENTIFIER = 'A2000004-0001-0001-0001-000000000001';

-- ================================================================
-- PRE-PROCESSING SCRIPTS
-- ================================================================

-- 1. Convert Binary Values (objectGUID, objectSid)
INSERT INTO SyncProcessingScripts (Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled, Category, CompilationStatus, CreatedBy)
VALUES (
    @scriptConvertBinary,
    'Convert Binary Values',
    'Converts binary attributes like objectGUID and objectSid from byte[] to readable string format. Essential for proper ID handling.',
    'PreProcessing',
    '// Convert Binary Values Script
// Converts objectGUID and objectSid from byte[] to readable strings

foreach (var obj in SourceObjects)
{
    // Case-insensitive attribute matching for objectGuid
    var guidKey = obj.Keys.FirstOrDefault(k => k.Equals("objectGuid", StringComparison.OrdinalIgnoreCase));
    if (guidKey != null && obj[guidKey] is byte[] guidBytes && guidBytes.Length == 16)
    {
        obj[guidKey] = new Guid(guidBytes).ToString();
        Log.Debug($"Converted objectGuid for object");
    }

    // Case-insensitive attribute matching for objectSid
    var sidKey = obj.Keys.FirstOrDefault(k => k.Equals("objectSid", StringComparison.OrdinalIgnoreCase));
    if (sidKey != null && obj[sidKey] is byte[] sidBytes)
    {
        try
        {
            obj[sidKey] = new System.Security.Principal.SecurityIdentifier(sidBytes, 0).ToString();
            Log.Debug($"Converted objectSid for object");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to convert objectSid: {ex.Message}");
        }
    }
}

Log.Info($"Converted binary values for {SourceObjects.Count} objects");
',
    1, 1, 'Attributes', 'NotCompiled', 'System'
);

-- 2. Normalize Attribute Values
INSERT INTO SyncProcessingScripts (Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled, Category, CompilationStatus, CreatedBy)
VALUES (
    @scriptNormalizeAttributes,
    'Normalize Attribute Values',
    'Trims whitespace, normalizes empty strings to null, and standardizes common attribute formats.',
    'PreProcessing',
    '// Normalize Attribute Values Script
// Cleans up attribute values for consistency

var trimmedCount = 0;
var nullifiedCount = 0;

foreach (var obj in SourceObjects)
{
    var keysToUpdate = obj.Keys.ToList();

    foreach (var key in keysToUpdate)
    {
        if (obj[key] is string strValue)
        {
            // Trim whitespace
            var trimmed = strValue.Trim();
            if (trimmed != strValue)
            {
                obj[key] = trimmed;
                trimmedCount++;
            }

            // Nullify empty strings
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                obj[key] = null;
                nullifiedCount++;
            }
        }
    }
}

Log.Info($"Normalized {trimmedCount} trimmed values, {nullifiedCount} empty values nullified");
',
    1, 1, 'Attributes', 'NotCompiled', 'System'
);

-- ================================================================
-- POST-PROCESSING SCRIPTS
-- ================================================================

-- 3. Create or Update Identity
INSERT INTO SyncProcessingScripts (Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled, Category, CompilationStatus, CreatedBy)
VALUES (
    @scriptCreateIdentity,
    'Create or Update Identity',
    'Matches synced objects to existing identities or creates new identity records. Links objects to their identity for person-centric management.',
    'PostProcessing',
    '// Create or Update Identity Script
// Matches synced objects to identities or creates new ones

foreach (var obj in SyncedObjects.Where(o =>
    (o.ObjectClass == "user" || o.ObjectClass == "contact") &&
    !o.IdentityId.HasValue &&
    !o.IsBuiltIn))
{
    Identity identity = null;

    // Strategy 1: Match by email
    if (!string.IsNullOrEmpty(obj.Email))
    {
        identity = await Repository.FindIdentityByEmailAsync(obj.Email, CancellationToken);
        if (identity != null)
        {
            Log.Debug($"Matched identity by email: {obj.Email}");
        }
    }

    // Strategy 2: Match by name (first + last)
    if (identity == null && !string.IsNullOrEmpty(obj.FirstName) && !string.IsNullOrEmpty(obj.LastName))
    {
        var matches = await Repository.FindIdentitiesByNameAsync(obj.FirstName, obj.LastName, CancellationToken);
        if (matches.Count == 1)
        {
            identity = matches[0];
            Log.Debug($"Matched identity by name: {obj.FirstName} {obj.LastName}");
        }
        else if (matches.Count > 1)
        {
            Log.Warning($"Multiple identity matches for {obj.FirstName} {obj.LastName}, skipping auto-match");
        }
    }

    // Strategy 3: Create new identity
    if (identity == null)
    {
        identity = new Identity
        {
            DisplayName = obj.DisplayName ?? $"{obj.FirstName} {obj.LastName}".Trim(),
            Email = obj.Email,
            FirstName = obj.FirstName,
            LastName = obj.LastName,
            Department = obj.Department,
            Title = obj.Title,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await Repository.CreateIdentityAsync(identity, CancellationToken);
        Metrics.IdentitiesCreated++;
        Log.Info($"Created new identity: {identity.DisplayName}");
    }

    // Link object to identity
    await Repository.UpdateObjectIdentityLinkAsync(obj.Id, identity.Id, CancellationToken);
    Metrics.ObjectsModified++;
}

Log.Info($"Processed identities - Created: {Metrics.IdentitiesCreated}, Modified: {Metrics.ObjectsModified}");
',
    1, 1, 'Identity', 'NotCompiled', 'System'
);

-- 4. Resolve Manager Relationships
INSERT INTO SyncProcessingScripts (Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled, Category, CompilationStatus, CreatedBy)
VALUES (
    @scriptResolveManager,
    'Resolve Manager Relationships',
    'Resolves manager distinguished names to database object references. Runs after all objects are synced to ensure managers exist.',
    'PostProcessing',
    '// Resolve Manager Relationships Script
// Links objects to their managers using DN references

var resolvedCount = 0;
var unresolvedCount = 0;

foreach (var obj in SyncedObjects.Where(o =>
    !string.IsNullOrEmpty(o.ManagerSourceId) &&
    !o.ManagerObjectId.HasValue))
{
    // Find manager object by DN
    var manager = await Repository.FindObjectByDNAsync(
        obj.SourceConnectionId,
        obj.ManagerSourceId,
        CancellationToken);

    if (manager != null)
    {
        await Repository.UpdateObjectManagerIdAsync(obj.Id, manager.Id, CancellationToken);
        resolvedCount++;
        Metrics.ManagersResolved++;
        Log.Debug($"Resolved manager for {obj.DisplayName}: {manager.DisplayName}");
    }
    else
    {
        unresolvedCount++;
        Log.Warning($"Manager not found for {obj.DisplayName}: {obj.ManagerSourceId}");
    }
}

Log.Info($"Manager resolution complete - Resolved: {resolvedCount}, Unresolved: {unresolvedCount}");
',
    1, 1, 'Manager', 'NotCompiled', 'System'
);

-- Verify results
SELECT COUNT(*) AS ScriptCount FROM SyncProcessingScripts WHERE IsSystem = 1;
SELECT Name, ScriptType, Category, IsEnabled FROM SyncProcessingScripts WHERE IsSystem = 1 ORDER BY ScriptType, Name;

PRINT 'Successfully seeded 4 system processing scripts!';
GO
