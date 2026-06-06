using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using DataAccessLibrary.Services.PersonMatching;
using DataAccessLibrary.Services.PersonMatching.Strategies;

namespace DataAccessLibrary.Services;

/// <summary>
/// Orchestrator for PersonMatch and PersonCreate sync project types.
/// Handles dedicated person matching workflows separate from ObjectSync.
/// Uses Dapper for all database operations for maximum performance.
/// </summary>
public class PersonMatchOrchestrator
{
    private readonly ISyncRepository _repository;
    private readonly string _defaultConnectionString;
    private readonly ILogger<PersonMatchOrchestrator> _logger;
    private readonly IProcessEventPublisher? _eventPublisher;
    private readonly Dictionary<string, IPersonMatchingStrategy> _strategies;

    // MULTI-TENANT SEAM (SaaS Day 4): invoked from the post-process drainer under a fixed tenant resolver
    // (and from the request path under the live resolver). Routing through the ambient accessor keeps it
    // pinned to the correct tenant DB; falls back to DefaultConnection when no resolver is installed.
    private string _connectionString =>
        DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

    public PersonMatchOrchestrator(
        ISyncRepository repository,
        IConfiguration configuration,
        ILogger<PersonMatchOrchestrator> logger,
        IProcessEventPublisher? eventPublisher = null)
    {
        _repository = repository;
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string not configured");
        _logger = logger;
        _eventPublisher = eventPublisher;

        // Register all available matching strategies
        _strategies = new Dictionary<string, IPersonMatchingStrategy>(StringComparer.OrdinalIgnoreCase)
        {
            { "composite", new CompositeMatchingStrategy() },
            { "configurable", new ConfigurableMatchingStrategy() },
            { "email", new EmailMatchingStrategy() },
            { "employeeid", new EmployeeIdMatchingStrategy() },
            { "upn", new UPNMatchingStrategy() },
            { "username", new UsernameMatchingStrategy() },
            { "name", new NameMatchingStrategy() }
        };
    }

    /// <summary>
    /// Execute a PersonMatch or PersonCreate sync project.
    /// OPTIMIZED: Pre-loads identity cache, matches in-memory, batches all updates.
    /// </summary>
    public async Task<PersonMatchRunResult> ExecuteAsync(
        SyncProject project,
        SyncProjectRun run,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new PersonMatchRunResult { ProjectType = project.ProjectType };

        _logger.LogInformation(
            "⚡ {ProjectType}: Starting for project '{Name}' (RunId: {RunId})",
            project.ProjectType, project.Name, run.Id);

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get unmatched objects to process
            var unmatchedObjects = await GetUnmatchedObjectsAsync(project, cancellationToken);
            result.TotalObjects = unmatchedObjects.Count;

            _logger.LogInformation(
                "⚡ {ProjectType}: Found {Count} unmatched objects to process",
                project.ProjectType, unmatchedObjects.Count);

            if (unmatchedObjects.Count == 0)
            {
                _logger.LogInformation("⚡ {ProjectType}: No objects require processing", project.ProjectType);
                return result;
            }

            // ⚡ OPTIMIZATION: Pre-load ALL identities into memory cache (single query instead of N queries)
            var identityCache = await LoadIdentityCacheAsync(connection, cancellationToken);
            _logger.LogInformation("⚡ Loaded {Count} identities into memory cache", identityCache.Total);

            // ⚡ OPTIMIZATION: Match all objects in-memory and collect results
            var linksToCreate = new List<(Guid ObjectId, Guid IdentityId)>();
            var identitiesToCreate = new List<IdentityCreateDto>();
            var objectToNewIdentityMap = new Dictionary<Guid, Guid>();

            foreach (var obj in unmatchedObjects)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Try in-memory matching
                var matchResult = TryMatchInMemory(obj, identityCache);

                if (matchResult.IdentityId.HasValue)
                {
                    linksToCreate.Add((obj.Id, matchResult.IdentityId.Value));
                    result.Matched++;
                }
                else if (project.ProjectType == "PersonCreate")
                {
                    // Create new identity
                    var newIdentityId = Guid.NewGuid();
                    objectToNewIdentityMap[obj.Id] = newIdentityId;

                    identitiesToCreate.Add(new IdentityCreateDto
                    {
                        Id = newIdentityId,
                        DisplayName = obj.DisplayName ?? $"{obj.FirstName} {obj.LastName}".Trim(),
                        FirstName = obj.FirstName,
                        LastName = obj.LastName,
                        PrimaryEmail = obj.Email,
                        Username = obj.Username,
                        Department = obj.Department,
                        JobTitle = obj.JobTitle,
                        Phone = obj.Phone
                    });
                    result.Created++;
                }
                else
                {
                    result.Skipped++;
                }
            }

            // ⚡ OPTIMIZATION: Batch INSERT new identities
            if (identitiesToCreate.Any())
            {
                _logger.LogInformation("⚡ Batch inserting {Count} new identities", identitiesToCreate.Count);

                const string insertSql = @"
                    INSERT INTO Identities (
                        Id, DisplayName, FirstName, LastName, PrimaryEmail, Username,
                        Department, JobTitle, PrimaryPhone, Status, IsActive, CreatedAt, ModifiedAt
                    ) VALUES (
                        @Id, @DisplayName, @FirstName, @LastName, @PrimaryEmail, @Username,
                        @Department, @JobTitle, @Phone, 'Active', 1, GETUTCDATE(), GETUTCDATE()
                    )";

                foreach (var batch in identitiesToCreate.Chunk(500))
                {
                    await connection.ExecuteAsync(insertSql, batch);
                }

                // Publish IdentityCreated events for workflow triggers
                if (_eventPublisher != null)
                {
                    _logger.LogInformation("Publishing IdentityCreated events for {Count} new identities", identitiesToCreate.Count);
                    foreach (var identity in identitiesToCreate)
                    {
                        await _eventPublisher.PublishAsync(
                            Models.WorkflowEventType.IdentityCreated,
                            identity.Id,
                            "Identity",
                            new Dictionary<string, object>
                            {
                                { "DisplayName", identity.DisplayName ?? "" },
                                { "Department", identity.Department ?? "" },
                                { "Status", "Active" },
                                { "Source", "PersonMatchOrchestrator" }
                            });
                    }
                }

                // Add new identity links to the batch
                foreach (var kvp in objectToNewIdentityMap)
                {
                    linksToCreate.Add((kvp.Key, kvp.Value));
                }
            }

            // ⚡ OPTIMIZATION: Batch UPDATE to link all objects
            if (linksToCreate.Any())
            {
                _logger.LogInformation("⚡ Batch linking {Count} objects to identities", linksToCreate.Count);

                foreach (var batch in linksToCreate.Chunk(500))
                {
                    await connection.ExecuteAsync(
                        "UPDATE Objects SET IdentityId = @IdentityId, MatchConfidence = 95, MatchMethod = 'PersonMatchProject' WHERE Id = @ObjectId",
                        batch.Select(l => new { l.ObjectId, l.IdentityId }));
                }
            }

            // Update final progress
            await UpdateRunProgressAsync(run.Id, result, cancellationToken);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ {ProjectType}: Failed for project '{Name}'", project.ProjectType, project.Name);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        stopwatch.Stop();
        result.DurationMs = (int)stopwatch.ElapsedMilliseconds;

        _logger.LogInformation(
            "⚡ {ProjectType}: Completed in {Duration}ms - {Matched} matched, {Created} created, {Skipped} skipped, {Errors} errors",
            project.ProjectType, result.DurationMs, result.Matched, result.Created, result.Skipped, result.Errors);

        return result;
    }

    /// <summary>
    /// Pre-load all identities into memory for fast in-memory matching.
    /// </summary>
    private async Task<IdentityMatchCache> LoadIdentityCacheAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var identities = await connection.QueryAsync<IdentityLookupDto>(
            @"SELECT Id, LOWER(PrimaryEmail) as Email, LOWER(Username) as Username,
                     LOWER(FirstName) as FirstName, LOWER(LastName) as LastName, Department
              FROM Identities WHERE IsActive = 1");

        var cache = new IdentityMatchCache();
        foreach (var i in identities)
        {
            cache.Total++;
            if (!string.IsNullOrWhiteSpace(i.Email))
                cache.ByEmail[i.Email] = i.Id;
            if (!string.IsNullOrWhiteSpace(i.Username))
                cache.ByUsername[i.Username] = i.Id;
            if (!string.IsNullOrWhiteSpace(i.FirstName) && !string.IsNullOrWhiteSpace(i.LastName))
            {
                var nameKey = $"{i.FirstName}|{i.LastName}";
                if (!cache.ByName.ContainsKey(nameKey))
                    cache.ByName[nameKey] = i.Id;
            }
        }
        return cache;
    }

    /// <summary>
    /// Try to match an object using in-memory cache (no database queries).
    /// </summary>
    private (Guid? IdentityId, string? Method, int Confidence) TryMatchInMemory(UnmatchedObjectDto obj, IdentityMatchCache cache)
    {
        // Email match (highest priority)
        if (!string.IsNullOrWhiteSpace(obj.Email) && cache.ByEmail.TryGetValue(obj.Email.ToLower(), out var emailMatch))
            return (emailMatch, "Email", 95);

        // Username match
        if (!string.IsNullOrWhiteSpace(obj.Username) && cache.ByUsername.TryGetValue(obj.Username.ToLower(), out var usernameMatch))
            return (usernameMatch, "Username", 90);

        // Name match (lowest priority)
        if (!string.IsNullOrWhiteSpace(obj.FirstName) && !string.IsNullOrWhiteSpace(obj.LastName))
        {
            var nameKey = $"{obj.FirstName.ToLower()}|{obj.LastName.ToLower()}";
            if (cache.ByName.TryGetValue(nameKey, out var nameMatch))
                return (nameMatch, "Name", 75);
        }

        return (null, null, 0);
    }

    private class IdentityMatchCache
    {
        public int Total { get; set; }
        public Dictionary<string, Guid> ByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> ByUsername { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private class IdentityLookupDto
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Department { get; set; }
    }

    private class IdentityCreateDto
    {
        public Guid Id { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PrimaryEmail { get; set; }
        public string? Username { get; set; }
        public string? Department { get; set; }
        public string? JobTitle { get; set; }
        public string? Phone { get; set; }
    }

    /// <summary>
    /// Get unmatched objects for processing.
    /// For PersonMatch/PersonCreate projects with SourceSyncProjectId: get objects from that project.
    /// Otherwise: get ALL unmatched objects from the source connection.
    /// </summary>
    private async Task<List<UnmatchedObjectDto>> GetUnmatchedObjectsAsync(
        SyncProject project,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql;
        object parameters;

        if (project.SourceSyncProjectId.HasValue)
        {
            // Get latest successful run from source project
            var latestRunSql = @"
                SELECT TOP 1 Id FROM SyncProjectRuns
                WHERE SyncProjectId = @SourceProjectId AND Status = 'Completed'
                ORDER BY CompletedAt DESC";

            var latestRunId = await connection.QueryFirstOrDefaultAsync<Guid?>(
                latestRunSql,
                new { SourceProjectId = project.SourceSyncProjectId.Value });

            if (!latestRunId.HasValue)
            {
                _logger.LogWarning("No completed run found for source project {SourceProjectId}", project.SourceSyncProjectId);
                return new List<UnmatchedObjectDto>();
            }

            // Get unmatched objects from the source project's latest run
            sql = @"
                SELECT o.Id, o.DisplayName, o.Email, o.Username, o.FirstName, o.LastName,
                       o.Department, o.JobTitle, o.Phone, o.ObjectClass, o.SourceConnectionId,
                       o.IsBuiltIn, o.IsActive
                FROM Objects o
                INNER JOIN SyncAuditLogs sal ON sal.ObjectId = o.Id
                INNER JOIN SyncStepRuns ssr ON ssr.Id = sal.SyncStepRunId
                WHERE ssr.SyncProjectRunId = @RunId
                  AND o.IdentityId IS NULL
                  AND o.IsBuiltIn = 0
                  AND o.ObjectClass IN ('user', 'contact')
                  AND o.IsActive = 1";
            parameters = new { RunId = latestRunId.Value };
        }
        else
        {
            // Get ALL unmatched objects from the source connection
            sql = @"
                SELECT o.Id, o.DisplayName, o.Email, o.Username, o.FirstName, o.LastName,
                       o.Department, o.JobTitle, o.Phone, o.ObjectClass, o.SourceConnectionId,
                       o.IsBuiltIn, o.IsActive
                FROM Objects o
                WHERE o.SourceConnectionId = @ConnectionId
                  AND o.IdentityId IS NULL
                  AND o.IsBuiltIn = 0
                  AND o.ObjectClass IN ('user', 'contact')
                  AND o.IsActive = 1";
            parameters = new { ConnectionId = project.SourceConnectionId };
        }

        var objects = await connection.QueryAsync<UnmatchedObjectDto>(sql, parameters);
        return objects.ToList();
    }

    /// <summary>
    /// Process a batch of objects for matching.
    /// </summary>
    private async Task ProcessBatchAsync(
        List<UnmatchedObjectDto> batch,
        IPersonMatchingStrategy strategy,
        PersonMatchingContext context,
        string projectType,
        PersonMatchRunResult result,
        CancellationToken cancellationToken)
    {
        foreach (var obj in batch)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Convert DTO to IdentityObject for strategy (minimal fields needed)
                var identityObject = new IdentityObject
                {
                    Id = obj.Id,
                    DisplayName = obj.DisplayName,
                    Email = obj.Email,
                    Username = obj.Username,
                    FirstName = obj.FirstName,
                    LastName = obj.LastName,
                    Department = obj.Department,
                    JobTitle = obj.JobTitle,
                    Phone = obj.Phone,
                    ObjectClass = obj.ObjectClass,
                    SourceConnectionId = obj.SourceConnectionId,
                    IsBuiltIn = obj.IsBuiltIn,
                    IsActive = obj.IsActive
                };

                // Try to find a match
                var matchResult = await strategy.MatchAsync(identityObject, context, cancellationToken);

                if (matchResult.MatchedIdentity != null)
                {
                    // Link object to existing identity
                    await LinkObjectToIdentityAsync(obj.Id, matchResult.MatchedIdentity.Id, cancellationToken);
                    result.Matched++;

                    _logger.LogDebug(
                        "⚡ Matched '{DisplayName}' to identity '{IdentityName}' (confidence: {Confidence}%)",
                        obj.DisplayName, matchResult.MatchedIdentity.DisplayName, matchResult.Confidence);
                }
                else if (projectType == "PersonCreate" && matchResult.ShouldCreateNew)
                {
                    // Create new identity and link
                    var identityId = await CreateIdentityFromObjectAsync(obj, cancellationToken);
                    await LinkObjectToIdentityAsync(obj.Id, identityId, cancellationToken);
                    result.Created++;

                    _logger.LogDebug("⚡ Created new identity for '{DisplayName}'", obj.DisplayName);
                }
                else
                {
                    result.Skipped++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error processing object {ObjectId}: {DisplayName}", obj.Id, obj.DisplayName);
                result.Errors++;
            }
        }
    }

    /// <summary>
    /// Link an object to an identity using Dapper.
    /// </summary>
    private async Task LinkObjectToIdentityAsync(
        Guid objectId,
        Guid identityId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            "UPDATE Objects SET IdentityId = @IdentityId, MatchConfidence = 100, MatchMethod = 'PersonMatchProject' WHERE Id = @ObjectId",
            new { ObjectId = objectId, IdentityId = identityId });
    }

    /// <summary>
    /// Create a new Identity from an object using Dapper.
    /// </summary>
    private async Task<Guid> CreateIdentityFromObjectAsync(
        UnmatchedObjectDto obj,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var identityId = Guid.NewGuid();
        var displayName = obj.DisplayName ?? $"{obj.FirstName} {obj.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = obj.Username ?? obj.Email ?? "Unknown";
        }

        await connection.ExecuteAsync(@"
            INSERT INTO Identities (Id, DisplayName, FirstName, LastName, PrimaryEmail, PrimaryPhone, Department, JobTitle, IsActive, CreatedAt)
            VALUES (@Id, @DisplayName, @FirstName, @LastName, @Email, @Phone, @Department, @JobTitle, @IsActive, @CreatedAt)",
            new
            {
                Id = identityId,
                DisplayName = displayName,
                obj.FirstName,
                obj.LastName,
                obj.Email,
                obj.Phone,
                obj.Department,
                obj.JobTitle,
                obj.IsActive,
                CreatedAt = DateTime.UtcNow
            });

        // Publish IdentityCreated event for workflow triggers
        if (_eventPublisher != null)
        {
            await _eventPublisher.PublishAsync(
                Models.WorkflowEventType.IdentityCreated,
                identityId,
                "Identity",
                new Dictionary<string, object>
                {
                    { "DisplayName", displayName },
                    { "Department", obj.Department ?? "" },
                    { "Status", "Active" },
                    { "Source", "PersonMatchOrchestrator" }
                });
        }

        return identityId;
    }

    /// <summary>
    /// Update the run progress.
    /// </summary>
    private async Task UpdateRunProgressAsync(
        Guid runId,
        PersonMatchRunResult result,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var progress = result.TotalObjects > 0
            ? (int)((result.Matched + result.Created + result.Skipped + result.Errors) * 100 / result.TotalObjects)
            : 0;

        await connection.ExecuteAsync(@"
            UPDATE SyncProjectRuns
            SET TotalObjectsProcessed = @Processed,
                TotalPersonsCreated = @Created,
                ProgressPercentage = @Progress
            WHERE Id = @RunId",
            new
            {
                RunId = runId,
                Processed = result.Matched + result.Created + result.Skipped,
                Created = result.Created,
                Progress = progress
            });
    }
}

/// <summary>
/// Lightweight DTO for unmatched objects.
/// </summary>
public class UnmatchedObjectDto
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public string? ObjectClass { get; set; }
    public Guid SourceConnectionId { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Results from a PersonMatch/PersonCreate sync project run.
/// </summary>
public class PersonMatchRunResult
{
    public string ProjectType { get; set; } = "";
    public int TotalObjects { get; set; }
    public int Matched { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public int DurationMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
