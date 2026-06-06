using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based Dev Center scripts seeding.
/// Seeds default system scripts for sync pre/post-processing.
/// This is essentially the same as the original DevCenterScriptsSeedService since it already used Dapper.
/// </summary>
public class DapperDevCenterScriptsSeedService : DapperSeedServiceBase
{
    public DapperDevCenterScriptsSeedService(
        IConfiguration configuration,
        ILogger<DapperDevCenterScriptsSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if scripts already exist
        var existingCount = await GetCountAsync(connection, transaction, "SyncProcessingScripts", "IsSystem = 1");
        if (existingCount >= 3)
        {
            _logger.LogDebug("Dev Center scripts already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var scripts = GetSystemScripts();

        const string insertSql = @"
            INSERT INTO SyncProcessingScripts (
                Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled,
                Version, Category, CompilationStatus, CreatedAt, CreatedBy
            )
            SELECT @Id, @Name, @Description, @ScriptType, @ScriptCode, @IsSystem, @IsEnabled,
                   @Version, @Category, @CompilationStatus, @CreatedAt, @CreatedBy
            WHERE NOT EXISTS (SELECT 1 FROM SyncProcessingScripts WHERE Name = @Name AND IsSystem = 1)";

        int created = 0;
        foreach (var script in scripts)
        {
            var rowsAffected = await InsertAsync(connection, transaction, insertSql, script);
            if (rowsAffected > 0) created++;
        }

        sw.Stop();
        LogSeedComplete("SyncProcessingScripts", created, scripts.Count - created, sw.Elapsed);
    }

    private static List<object> GetSystemScripts()
    {
        var now = DateTime.UtcNow;
        return new List<object>
        {
            new
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "ConvertBinaryValues",
                Description = "Converts objectGUID and objectSid from byte[] to readable strings. Uses case-insensitive attribute matching to handle LDAP attribute name variations.",
                ScriptType = "PreProcessing",
                ScriptCode = @"// ConvertBinaryValues - Pre-Processing Script
// Converts binary LDAP attributes to readable string formats

foreach (var obj in SourceObjects)
{
    // Case-insensitive objectGUID conversion
    var guidKey = obj.Keys.FirstOrDefault(k => k.Equals(""objectGuid"", StringComparison.OrdinalIgnoreCase));
    if (guidKey != null && obj[guidKey] is byte[] guidBytes && guidBytes.Length == 16)
    {
        obj[guidKey] = new Guid(guidBytes).ToString();
        Log.Debug($""Converted objectGUID for object"");
    }

    // Case-insensitive objectSid conversion
    var sidKey = obj.Keys.FirstOrDefault(k => k.Equals(""objectSid"", StringComparison.OrdinalIgnoreCase));
    if (sidKey != null && obj[sidKey] is byte[] sidBytes)
    {
        try
        {
            obj[sidKey] = new System.Security.Principal.SecurityIdentifier(sidBytes, 0).ToString();
            Log.Debug($""Converted objectSid for object"");
        }
        catch (Exception ex)
        {
            Log.Warning($""Failed to convert objectSid: {ex.Message}"");
        }
    }
}

Log.Info($""Converted binary values for {SourceObjects.Count} objects"");",
                IsSystem = true,
                IsEnabled = true,
                Version = 1,
                Category = "Attributes",
                CompilationStatus = "NotCompiled",
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "CreateOrUpdateIdentity",
                Description = "Creates or matches Identity (Person) records using configurable attribute matching. Uses Step.AttributeMappings with UseForMatching=true, sorted by MatchWeight.",
                ScriptType = "PostProcessing",
                ScriptCode = @"// CreateOrUpdateIdentity - Post-Processing Script (v2)
// Uses configurable attribute matching from Step.AttributeMappings

var userObjects = SyncedObjects.Where(o =>
    (o.ObjectClass == ""user"" || o.ObjectClass == ""contact"") &&
    !o.IdentityId.HasValue &&
    !o.IsBuiltIn
).ToList();

Log.Info($""Processing {userObjects.Count} objects for identity matching"");

// Get matching attributes from step configuration, sorted by priority
var matchingMappings = Step.AttributeMappings?
    .Where(m => m.UseForMatching && m.IsEnabled)
    .OrderByDescending(m => m.MatchWeight)
    .ToList() ?? new List<AttributeMapping>();

if (matchingMappings.Any())
{
    Log.Info($""Using {matchingMappings.Count} configured matching attributes"");
}
else
{
    Log.Info(""No matching attributes configured - using default Email > Name matching"");
}

foreach (var obj in userObjects)
{
    Identity identity = null;
    string matchMethod = null;

    // Try configured matching attributes first
    foreach (var mapping in matchingMappings)
    {
        if (identity != null) break;

        var attrName = mapping.TargetAttribute?.ToLowerInvariant() ?? """";

        switch (attrName)
        {
            case ""email"":
            case ""mail"":
            case ""primaryemail"":
                if (!string.IsNullOrEmpty(obj.Email))
                {
                    identity = await Repository.FindIdentityByEmailAsync(obj.Email, CancellationToken);
                    if (identity != null) matchMethod = ""Email"";
                }
                break;

            case ""employeeid"":
            case ""employeenumber"":
                if (!string.IsNullOrEmpty(obj.EmployeeId))
                {
                    identity = await Repository.FindIdentityByEmployeeIdAsync(obj.EmployeeId, CancellationToken);
                    if (identity != null) matchMethod = ""EmployeeId"";
                }
                break;
        }
    }

    if (identity != null)
    {
        Log.Debug($""Matched {obj.DisplayName} by {matchMethod}"");
        Metrics.IdentitiesUpdated++;
    }
    else
    {
        // Create new identity
        identity = new Identity
        {
            Id = Guid.NewGuid(),
            DisplayName = obj.DisplayName ?? $""{obj.FirstName} {obj.LastName}"".Trim(),
            FirstName = obj.FirstName,
            LastName = obj.LastName,
            PrimaryEmail = obj.Email,
            Department = obj.Department,
            JobTitle = obj.JobTitle,
            EmployeeId = obj.EmployeeId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await Repository.CreateIdentityAsync(identity, CancellationToken);
        Metrics.IdentitiesCreated++;
        Log.Info($""Created identity: {identity.DisplayName}"");
    }

    // Link object to identity
    await Repository.UpdateObjectIdentityLinkAsync(obj.Id, identity.Id, CancellationToken);
    Metrics.ObjectsModified++;
}

Log.Info($""Identity matching complete: {Metrics.IdentitiesCreated} created, {Metrics.IdentitiesUpdated} matched, {Metrics.ObjectsModified} linked"");",
                IsSystem = true,
                IsEnabled = true,
                Version = 2,
                Category = "Identity",
                CompilationStatus = "NotCompiled",
                CreatedAt = now,
                CreatedBy = "System"
            },
            new
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "ResolveManagerRelationships",
                Description = "Resolves manager relationships by matching ManagerSourceId (DN) to existing objects. Creates ManagerObjectId foreign key relationships.",
                ScriptType = "PostProcessing",
                ScriptCode = @"// ResolveManagerRelationships - Post-Processing Script
// Resolves manager DN references to actual object relationships

var objectsWithManager = SyncedObjects.Where(o =>
    !string.IsNullOrEmpty(o.ManagerSourceId) &&
    !o.ManagerObjectId.HasValue
).ToList();

Log.Info($""Resolving managers for {objectsWithManager.Count} objects"");

foreach (var obj in objectsWithManager)
{
    var manager = await Repository.FindObjectByDNAsync(
        obj.SourceConnectionId,
        obj.ManagerSourceId,
        CancellationToken
    );

    if (manager != null)
    {
        await Repository.UpdateObjectManagerIdAsync(obj.Id, manager.Object.Id, CancellationToken);
        Metrics.ManagersResolved++;
        Log.Debug($""Resolved manager for {obj.DisplayName}: {manager.Object.DisplayName}"");
    }
    else
    {
        Log.Warning($""Manager not found for {obj.DisplayName}: {obj.ManagerSourceId}"");
    }
}

Log.Info($""Manager resolution complete: {Metrics.ManagersResolved} resolved"");",
                IsSystem = true,
                IsEnabled = true,
                Version = 1,
                Category = "Manager",
                CompilationStatus = "NotCompiled",
                CreatedAt = now,
                CreatedBy = "System"
            }
        };
    }
}
