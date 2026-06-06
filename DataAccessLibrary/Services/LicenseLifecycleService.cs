using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Services;

public class LicenseLifecycleService : ILicenseLifecycleService
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public LicenseLifecycleService(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public async Task EmitEventAsync(Guid assignmentId, Guid poolId, Guid objectId, string eventType,
        string? actor = null, string? reason = null, string? metadata = null, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO LicenseAssignmentEvents
                (Id, AssignmentId, LicensePoolId, ObjectId, EventType, Actor, Reason, Metadata, CreatedAt)
            VALUES
                (NEWID(), @assignmentId, @poolId, @objectId, @eventType, @actor, @reason, @metadata, GETUTCDATE())",
            new { assignmentId, poolId, objectId, eventType, actor, reason, metadata });
    }

    public async Task<List<LicenseAssignmentEvent>> GetEventsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<LicenseAssignmentEvent>(@"
            SELECT Id, AssignmentId, LicensePoolId, ObjectId, EventType, Actor, Reason, Metadata, CreatedAt
            FROM LicenseAssignmentEvents
            WHERE AssignmentId = @assignmentId
            ORDER BY CreatedAt DESC",
            new { assignmentId });
        return rows.ToList();
    }

    public async Task<List<LicenseAssignmentEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<LicenseAssignmentEvent>(@"
            SELECT TOP (@limit) Id, AssignmentId, LicensePoolId, ObjectId, EventType, Actor, Reason, Metadata, CreatedAt
            FROM LicenseAssignmentEvents
            ORDER BY CreatedAt DESC",
            new { limit });
        return rows.ToList();
    }

    public async Task<int> EvaluateStateTransitionsAsync(int dormantDays = 90, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var eventsEmitted = 0;
        var now = DateTime.UtcNow;
        var dormantCutoff = now.AddDays(-dormantDays);

        // 1. Emit "Assigned" event for new assignments (those with no prior event)
        var newAssignments = await conn.QueryAsync<(Guid AssignmentId, Guid LicensePoolId, Guid ObjectId)>(@"
            SELECT la.Id AS AssignmentId, la.LicensePoolId, la.ObjectId
            FROM LicenseAssignments la
            WHERE la.IsActive = 1
              AND NOT EXISTS (
                SELECT 1 FROM LicenseAssignmentEvents e
                WHERE e.AssignmentId = la.Id AND e.EventType = 'Assigned'
              )");

        foreach (var a in newAssignments)
        {
            await EmitEventAsync(a.AssignmentId, a.LicensePoolId, a.ObjectId,
                LicenseAssignmentEventTypes.Assigned, "System (Lifecycle)", "Initial assignment detected",
                ct: ct);
            eventsEmitted++;
        }

        // 2. Emit "FirstUsed" event when LastUsedAt becomes non-null (after assignment)
        var firstUsedCandidates = await conn.QueryAsync<(Guid AssignmentId, Guid LicensePoolId, Guid ObjectId, DateTime? LastUsedAt)>(@"
            SELECT la.Id AS AssignmentId, la.LicensePoolId, la.ObjectId, la.LastUsedAt
            FROM LicenseAssignments la
            WHERE la.IsActive = 1 AND la.LastUsedAt IS NOT NULL
              AND NOT EXISTS (
                SELECT 1 FROM LicenseAssignmentEvents e
                WHERE e.AssignmentId = la.Id AND e.EventType = 'FirstUsed'
              )");

        foreach (var a in firstUsedCandidates)
        {
            await EmitEventAsync(a.AssignmentId, a.LicensePoolId, a.ObjectId,
                LicenseAssignmentEventTypes.FirstUsed, "System (Lifecycle)",
                $"First activity detected on {a.LastUsedAt:yyyy-MM-dd}", ct: ct);
            eventsEmitted++;
        }

        // 3. Emit "Dormant" event when LastUsedAt < cutoff AND not already dormant
        var dormantCandidates = await conn.QueryAsync<(Guid AssignmentId, Guid LicensePoolId, Guid ObjectId, DateTime? LastUsedAt)>(@"
            SELECT la.Id AS AssignmentId, la.LicensePoolId, la.ObjectId, la.LastUsedAt
            FROM LicenseAssignments la
            WHERE la.IsActive = 1
              AND (la.LastUsedAt IS NULL OR la.LastUsedAt < @dormantCutoff)
              AND EXISTS (SELECT 1 FROM LicenseAssignmentEvents e WHERE e.AssignmentId = la.Id)
              AND NOT EXISTS (
                SELECT 1 FROM LicenseAssignmentEvents e
                WHERE e.AssignmentId = la.Id
                  AND e.EventType IN ('Dormant', 'Revoked', 'Removed')
                  AND e.CreatedAt > ISNULL((
                    SELECT MAX(CreatedAt) FROM LicenseAssignmentEvents e2
                    WHERE e2.AssignmentId = la.Id AND e2.EventType = 'Reactivated'
                  ), '1900-01-01')
              )",
            new { dormantCutoff });

        foreach (var a in dormantCandidates)
        {
            var days = a.LastUsedAt.HasValue ? (int)(now - a.LastUsedAt.Value).TotalDays : dormantDays;
            await EmitEventAsync(a.AssignmentId, a.LicensePoolId, a.ObjectId,
                LicenseAssignmentEventTypes.Dormant, "System (Lifecycle)",
                $"No activity for {days} days", ct: ct);
            eventsEmitted++;
        }

        // 4. Emit "Revoked" event when IsActive transitions to false
        var revokedCandidates = await conn.QueryAsync<(Guid AssignmentId, Guid LicensePoolId, Guid ObjectId)>(@"
            SELECT la.Id AS AssignmentId, la.LicensePoolId, la.ObjectId
            FROM LicenseAssignments la
            WHERE la.IsActive = 0
              AND NOT EXISTS (
                SELECT 1 FROM LicenseAssignmentEvents e
                WHERE e.AssignmentId = la.Id AND e.EventType IN ('Revoked', 'Removed')
              )");

        foreach (var a in revokedCandidates)
        {
            await EmitEventAsync(a.AssignmentId, a.LicensePoolId, a.ObjectId,
                LicenseAssignmentEventTypes.Revoked, "System (Lifecycle)",
                "License assignment deactivated", ct: ct);
            eventsEmitted++;
        }

        _logger.LogInformation($"LicenseLifecycle: emitted {eventsEmitted} state transition event(s)");
        return eventsEmitted;
    }
}
