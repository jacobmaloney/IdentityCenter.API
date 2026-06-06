using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default system scripts for the Dev Center.
/// These scripts provide common pre/post-processing functionality
/// that users can copy and customize.
/// </summary>
public class DevCenterScriptsSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<DevCenterScriptsSeedService> _logger;

    public DevCenterScriptsSeedService(
        IConfiguration configuration,
        ILogger<DevCenterScriptsSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds the 3 essential system scripts for sync processing.
    /// </summary>
    public async Task SeedSystemScriptsAsync()
    {
        _logger.LogInformation("Starting Dev Center system scripts seeding...");

        var systemScripts = GetSystemScripts();
        int created = 0;
        int skipped = 0;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var script in systemScripts)
        {
            // Check if script already exists by name and IsSystem flag
            var existing = await connection.QueryFirstOrDefaultAsync<SyncProcessingScript>(
                "SELECT Id FROM SyncProcessingScripts WHERE Name = @Name AND IsSystem = 1",
                new { script.Name });

            if (existing != null)
            {
                _logger.LogDebug("System script '{ScriptName}' already exists, skipping", script.Name);
                skipped++;
                continue;
            }

            // Insert new script
            const string insertSql = @"
                INSERT INTO SyncProcessingScripts (
                    Id, Name, Description, ScriptType, ScriptCode, IsSystem, IsEnabled,
                    Version, Category, CompilationStatus, CreatedAt, CreatedBy
                )
                VALUES (
                    @Id, @Name, @Description, @ScriptType, @ScriptCode, @IsSystem, @IsEnabled,
                    @Version, @Category, @CompilationStatus, @CreatedAt, @CreatedBy
                )";

            await connection.ExecuteAsync(insertSql, script);
            _logger.LogInformation("Created system script '{ScriptName}' ({ScriptType}) - {Category}",
                script.Name, script.ScriptType, script.Category);
            created++;
        }

        _logger.LogInformation("Dev Center scripts seeding complete! Created: {Created}, Skipped: {Skipped}", created, skipped);
    }

    private static List<SyncProcessingScript> GetSystemScripts()
    {
        return new List<SyncProcessingScript>
        {
            // 1. ConvertBinaryValues (PreProcessing)
            new SyncProcessingScript
            {
                Id = new Guid("11111111-1111-1111-1111-111111111111"),
                Name = "ConvertBinaryValues",
                Description = "Converts objectGUID and objectSid from byte[] to readable strings. Uses case-insensitive attribute matching to handle LDAP attribute name variations.",
                ScriptType = ScriptTypes.PreProcessing,
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
                Category = ScriptCategories.Attributes,
                CompilationStatus = CompilationStatus.NotCompiled,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            },

            // 2. CreateOrUpdateIdentity (PostProcessing)
            new SyncProcessingScript
            {
                Id = new Guid("22222222-2222-2222-2222-222222222222"),
                Name = "CreateOrUpdateIdentity",
                Description = "Creates or matches Identity (Person) records using configurable attribute matching. Uses Step.AttributeMappings with UseForMatching=true, sorted by MatchWeight. Falls back to Email > Name matching if no mappings configured.",
                ScriptType = ScriptTypes.PostProcessing,
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

            case ""samaccountname"":
            case ""username"":
                if (!string.IsNullOrEmpty(obj.Username))
                {
                    identity = await Repository.FindIdentityByUsernameAsync(obj.Username, CancellationToken);
                    if (identity != null) matchMethod = ""Username"";
                }
                break;

            case ""userprincipalname"":
            case ""upn"":
                if (!string.IsNullOrEmpty(obj.UserPrincipalName))
                {
                    identity = await Repository.FindIdentityByUPNAsync(obj.UserPrincipalName, CancellationToken);
                    if (identity != null) matchMethod = ""UPN"";
                }
                break;

            case ""displayname"":
                if (!string.IsNullOrEmpty(obj.DisplayName))
                {
                    identity = await Repository.FindIdentityByDisplayNameAsync(obj.DisplayName, CancellationToken);
                    if (identity != null) matchMethod = ""DisplayName"";
                }
                break;

            case ""firstname"":
            case ""givenname"":
            case ""lastname"":
            case ""sn"":
                if (!string.IsNullOrEmpty(obj.FirstName) && !string.IsNullOrEmpty(obj.LastName))
                {
                    var matches = await Repository.FindIdentitiesByNameAsync(obj.FirstName, obj.LastName, CancellationToken);
                    if (matches.Count == 1)
                    {
                        identity = matches[0];
                        matchMethod = ""Name"";
                    }
                }
                break;
        }
    }

    // Default matching if no configured mappings or no match found
    if (identity == null && !matchingMappings.Any())
    {
        // Try email first
        if (!string.IsNullOrEmpty(obj.Email))
        {
            identity = await Repository.FindIdentityByEmailAsync(obj.Email, CancellationToken);
            if (identity != null) matchMethod = ""Email"";
        }

        // Try name match
        if (identity == null && !string.IsNullOrEmpty(obj.FirstName) && !string.IsNullOrEmpty(obj.LastName))
        {
            var matches = await Repository.FindIdentitiesByNameAsync(obj.FirstName, obj.LastName, CancellationToken);
            if (matches.Count == 1)
            {
                identity = matches[0];
                matchMethod = ""Name"";
            }
            else if (matches.Count > 1)
            {
                Log.Warning($""Multiple identity matches for {obj.FirstName} {obj.LastName} - skipping"");
                Metrics.Warnings++;
                continue;
            }
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
        matchMethod = ""Created"";
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
                Category = ScriptCategories.Identity,
                CompilationStatus = CompilationStatus.NotCompiled,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            },

            // 3. ResolveManagerRelationships (PostProcessing)
            new SyncProcessingScript
            {
                Id = new Guid("33333333-3333-3333-3333-333333333333"),
                Name = "ResolveManagerRelationships",
                Description = "Resolves manager relationships by matching ManagerSourceId (DN) to existing objects. Creates ManagerObjectId foreign key relationships.",
                ScriptType = ScriptTypes.PostProcessing,
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
                Category = ScriptCategories.Manager,
                CompilationStatus = CompilationStatus.NotCompiled,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            }
        };
    }
}
