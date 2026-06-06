using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Composite interface for backward compatibility.
/// New code should inject the specific sub-interfaces instead.
/// </summary>
public interface ISyncRepository : ISyncObjectRepository, ISyncExecutionRepository,
    ISyncRelationshipRepository, ISyncScriptRepository
{
}

/// <summary>
/// Data statistics result for UI display.
/// </summary>
public class DataStatisticsResult
{
    public int ObjectCount { get; set; }
    public int IdentityCount { get; set; }
    public int GroupCount { get; set; }
    public int MembershipCount { get; set; }
}

/// <summary>
/// Script info with step assignment details.
/// </summary>
public class StepScriptInfo
{
    public Guid StepScriptId { get; set; }
    public Guid ScriptId { get; set; }
    public string ScriptName { get; set; } = string.Empty;
    public string ScriptType { get; set; } = string.Empty;
    public string ExecutionPhase { get; set; } = string.Empty;
    public int ExecutionOrder { get; set; }
    public bool IsEnabled { get; set; }
    public string? ParameterOverrides { get; set; }
    public string ScriptCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public int Version { get; set; }
}

/// <summary>
/// Result of an identity upsert operation.
/// </summary>
public class UpsertResult
{
    public Guid Id { get; set; }
    public bool IsNew { get; set; }
    public int AttributesInserted { get; set; }
}

/// <summary>
/// Result of bulk upsert operation.
/// </summary>
public class BulkUpsertResult
{
    public int ObjectsProcessed { get; set; }
    public int ObjectsCreated { get; set; }
    public int ObjectsUpdated { get; set; }
    public int ObjectsSkipped { get; set; }
    public int AttributesAffected { get; set; }
    /// <summary>
    /// SourceUniqueIds of objects that were skipped (no changes detected).
    /// Used to avoid creating "Updated" audit logs for unchanged objects.
    /// </summary>
    public HashSet<string> SkippedSourceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Object with its extended attributes loaded together.
/// </summary>
public class ObjectWithAttributes
{
    public IdentityObject Object { get; set; } = null!;
    public List<ObjectAttribute> Attributes { get; set; } = new();
}

/// <summary>
/// Group with its extended attributes loaded together.
/// </summary>
public class GroupWithAttributes
{
    public Group Group { get; set; } = null!;
    public List<GroupAttribute> Attributes { get; set; } = new();
}

/// <summary>
/// Sync run with all step runs loaded together.
/// </summary>
public class SyncRunDetailsData
{
    public SyncProjectRun Run { get; set; } = null!;
    public SyncProject? Project { get; set; }
    public List<SyncStepRun> StepRuns { get; set; } = new();
}

/// <summary>
/// Pre-loaded identity lookup cache for O(1) person matching.
/// </summary>
public class IdentityLookupCache
{
    public Dictionary<string, Identity> ByEmail { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<(string firstName, string lastName), List<Identity>> ByName { get; set; } = new();
}
