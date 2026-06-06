using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service responsible for syncing group memberships between Identities and Groups.
    /// Processes the 'memberOf' attribute from AD to populate IdentityGroupMembership table.
    /// </summary>
    public class GroupMembershipSyncService
    {
        private readonly IGlobalLogger _logger;
        private readonly Repositories.ISyncRepository _syncRepository;
        private readonly string _connectionString;

        public GroupMembershipSyncService(
            IGlobalLogger logger,
            Repositories.ISyncRepository syncRepository,
            IConfiguration configuration)
        {
            _logger = logger;
            _syncRepository = syncRepository;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        /// <summary>
        /// Syncs group memberships for an identity based on the memberOf attribute from AD.
        /// </summary>
        public async Task SyncGroupMembershipsAsync(
            IdentityObject identityObject,
            Dictionary<string, object> sourceObject,
            CancellationToken cancellationToken)
        {
            // Extract memberOf attribute (multi-valued) - case-insensitive lookup
            var memberOfValue = GetSourceValue(sourceObject, "memberOf");
            if (memberOfValue == null)
            {
                _logger.LogDebug("No memberOf attribute for identity {SourceUniqueId}", identityObject.SourceUniqueId);
                return;
            }
            var groupDNs = new List<string>();

            // Handle both string[] and single string values
            if (memberOfValue is string[] dnArray)
            {
                groupDNs.AddRange(dnArray.Where(dn => !string.IsNullOrWhiteSpace(dn)));
            }
            else if (memberOfValue is string dn && !string.IsNullOrWhiteSpace(dn))
            {
                groupDNs.Add(dn);
            }

            if (!groupDNs.Any())
            {
                _logger.LogDebug("Identity {SourceUniqueId} has empty memberOf attribute", identityObject.SourceUniqueId);
                return;
            }

            _logger.LogDebug("Processing {Count} group memberships for identity {SourceUniqueId}",
                groupDNs.Count, identityObject.SourceUniqueId);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Load existing groups by DistinguishedName for lookup
            const string groupsSql = @"
                SELECT Id, DN
                FROM Objects
                WHERE ObjectClass = 'group'
                  AND SourceConnectionId = @SourceConnectionId
                  AND IsActive = 1";

            var groupsList = await connection.QueryAsync<(Guid Id, string? DN)>(
                new CommandDefinition(groupsSql, new { identityObject.SourceConnectionId }, cancellationToken: cancellationToken));
            var existingGroups = groupsList.ToDictionary(g => g.DN ?? "", g => g.Id, StringComparer.OrdinalIgnoreCase);

            // Load existing memberships for this identity
            const string membershipsSql = @"
                SELECT Id, ObjectId, GroupId, IsDirect, IsPrimary, AddedAt, RemovedAt, LastSyncedAt
                FROM ObjectGroupMemberships
                WHERE ObjectId = @ObjectId
                  AND RemovedAt IS NULL";

            var existingMemberships = (await connection.QueryAsync<ObjectGroupMembership>(
                new CommandDefinition(membershipsSql, new { ObjectId = identityObject.Id }, cancellationToken: cancellationToken))).ToList();

            var processedGroupIds = new HashSet<Guid>();
            int added = 0;
            int updated = 0;

            // Use a transaction for atomic operations
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                foreach (var groupDN in groupDNs)
                {
                    // Look up the group by DN
                    if (!existingGroups.TryGetValue(groupDN, out var groupId))
                    {
                        _logger.LogDebug("Group with DN '{DN}' not found in Groups table, skipping membership", groupDN);
                        continue;
                    }

                    processedGroupIds.Add(groupId);

                    // Check if membership already exists
                    var existingMembership = existingMemberships.FirstOrDefault(m => m.GroupId == groupId);

                    if (existingMembership == null)
                    {
                        // Create new membership
                        const string insertSql = @"
                            INSERT INTO ObjectGroupMemberships (Id, ObjectId, GroupId, IsDirect, AddedAt, LastSyncedAt)
                            VALUES (@Id, @ObjectId, @GroupId, @IsDirect, @AddedAt, @LastSyncedAt)";

                        await connection.ExecuteAsync(
                            new CommandDefinition(insertSql, new
                            {
                                Id = Guid.NewGuid(),
                                ObjectId = identityObject.Id,
                                GroupId = groupId,
                                IsDirect = true, // We only sync direct memberships from memberOf
                                AddedAt = DateTime.UtcNow,
                                LastSyncedAt = DateTime.UtcNow
                            }, transaction, cancellationToken: cancellationToken));

                        added++;
                        _logger.LogDebug("Added membership: Identity {IdentityId} -> Group {GroupId}", identityObject.Id, groupId);
                    }
                    else
                    {
                        // Update existing membership
                        const string updateSql = @"
                            UPDATE ObjectGroupMemberships
                            SET LastSyncedAt = @LastSyncedAt
                            WHERE Id = @Id";

                        await connection.ExecuteAsync(
                            new CommandDefinition(updateSql, new
                            {
                                Id = existingMembership.Id,
                                LastSyncedAt = DateTime.UtcNow
                            }, transaction, cancellationToken: cancellationToken));

                        updated++;
                    }
                }

                // Mark removed memberships (groups that are no longer in memberOf)
                int removed = 0;
                foreach (var membership in existingMemberships.Where(m => !processedGroupIds.Contains(m.GroupId)))
                {
                    const string removeSql = @"
                        UPDATE ObjectGroupMemberships
                        SET RemovedAt = @RemovedAt
                        WHERE Id = @Id";

                    await connection.ExecuteAsync(
                        new CommandDefinition(removeSql, new
                        {
                            Id = membership.Id,
                            RemovedAt = DateTime.UtcNow
                        }, transaction, cancellationToken: cancellationToken));

                    removed++;
                    _logger.LogDebug("Removed membership: Identity {IdentityId} -X- Group {GroupId}",
                        identityObject.Id, membership.GroupId);
                }

                await transaction.CommitAsync(cancellationToken);

                if (added > 0 || updated > 0 || removed > 0)
                {
                    _logger.LogInformation(
                        "Synced group memberships for identity {SourceUniqueId}: {Added} added, {Updated} updated, {Removed} removed",
                        identityObject.SourceUniqueId, added, updated, removed);
                }
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// Batch syncs group memberships for multiple identities.
        /// More efficient than calling SyncGroupMembershipsAsync individually.
        /// Includes PRIMARY GROUP handling (Domain Users, etc.) which is NOT in memberOf attribute.
        /// </summary>
        public async Task BatchSyncGroupMembershipsAsync(
            List<(IdentityObject identityObject, Dictionary<string, object> sourceObject)> identitiesWithSource,
            CancellationToken cancellationToken)
        {
            if (!identitiesWithSource.Any())
                return;

            _logger.LogInformation("🚀 FAST BATCH: Syncing group memberships for {Count} identities (including primary groups)", identitiesWithSource.Count);

            var connectionId = identitiesWithSource.First().identityObject.SourceConnectionId;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Load all groups for this connection once - by DN and by SID for primary group lookup
            const string groupsSql = @"
                SELECT Id, DN
                FROM Objects
                WHERE ObjectClass = 'group'
                  AND SourceConnectionId = @SourceConnectionId
                  AND IsActive = 1";

            var allGroupsList = (await connection.QueryAsync<(Guid Id, string? DN)>(
                new CommandDefinition(groupsSql, new { SourceConnectionId = connectionId }, cancellationToken: cancellationToken))).ToList();
            var allGroups = allGroupsList.ToDictionary(g => g.DN ?? "", g => g.Id, StringComparer.OrdinalIgnoreCase);

            // Build SID-to-Group lookup for primary group resolution
            // SID is stored in GroupAttributes table, not on Group directly
            var groupIds = allGroupsList.Select(g => g.Id).ToList();

            const string groupSidSql = @"
                SELECT ObjectId, AttributeValue
                FROM ObjectAttributes
                WHERE AttributeName = 'objectSid'
                  AND ObjectId IN @GroupIds";

            var groupSidQuery = await connection.QueryAsync<(Guid ObjectId, string? AttributeValue)>(
                new CommandDefinition(groupSidSql, new { GroupIds = groupIds }, cancellationToken: cancellationToken));

            var groupsBySid = groupSidQuery
                .Where(x => !string.IsNullOrEmpty(x.AttributeValue))
                .ToDictionary(x => x.AttributeValue!, x => x.ObjectId, StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Loaded {Count} group SIDs for primary group resolution", groupsBySid.Count);

            _logger.LogInformation("Loaded {Count} groups for membership matching", allGroups.Count);

            // CRITICAL FIX: Resolve ACTUAL Objects.Id from database using SourceUniqueId
            // The IdentityObject.Id may be a NEW GUID that doesn't exist in the database yet
            // (it's generated by AttributeMappingService, but MERGE uses SourceConnectionId+SourceUniqueId as key)
            var sourceUniqueIds = identitiesWithSource
                .Select(x => x.identityObject.SourceUniqueId)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToList();

            const string objectIdsSql = @"
                SELECT SourceUniqueId, Id
                FROM Objects
                WHERE SourceConnectionId = @SourceConnectionId
                  AND SourceUniqueId IN @SourceUniqueIds";

            var objectIdsQuery = await connection.QueryAsync<(string? SourceUniqueId, Guid Id)>(
                new CommandDefinition(objectIdsSql, new { SourceConnectionId = connectionId, SourceUniqueIds = sourceUniqueIds }, cancellationToken: cancellationToken));

            var actualObjectIds = objectIdsQuery.ToDictionary(o => o.SourceUniqueId ?? "", o => o.Id);

            _logger.LogInformation("Resolved {Count}/{Total} actual ObjectIds from database",
                actualObjectIds.Count, sourceUniqueIds.Count);

            // Build list of ALL memberships to upsert (bulk operation)
            // IsPrimary = true for primary group (from primaryGroupID), false for memberOf groups
            var allMembershipsToUpsert = new List<(Guid ObjectId, Guid GroupId, bool IsDirect, bool IsPrimary)>();

            int primaryGroupsResolved = 0;
            int primaryGroupsMissing = 0;
            int objectsNotFoundInDb = 0;

            foreach (var (identityObject, sourceObject) in identitiesWithSource)
            {
                // CRITICAL: Use the ACTUAL database ObjectId, not the in-memory IdentityObject.Id
                if (!actualObjectIds.TryGetValue(identityObject.SourceUniqueId ?? "", out var resolvedObjectId))
                {
                    objectsNotFoundInDb++;
                    _logger.LogDebug("Object with SourceUniqueId {SourceUniqueId} not found in Objects table, skipping",
                        identityObject.SourceUniqueId);
                    continue;
                }

                // ===========================================
                // PART 1: Handle PRIMARY GROUP (Domain Users, etc.)
                // Primary group is NOT in memberOf - it's stored in primaryGroupID attribute
                // CRITICAL: Use case-insensitive lookup - LDAP returns lowercase names
                // ===========================================
                var primaryGroupIdObj = GetSourceValue(sourceObject, "primaryGroupID");
                var objectSidObj = GetSourceValue(sourceObject, "objectSid");

                if (primaryGroupIdObj != null && objectSidObj != null)
                {
                    var primaryGroupIdStr = primaryGroupIdObj.ToString();
                    var objectSidStr = objectSidObj.ToString();

                    if (!string.IsNullOrWhiteSpace(primaryGroupIdStr) && !string.IsNullOrWhiteSpace(objectSidStr))
                    {
                        // Extract domain SID by removing the last RID from objectSid
                        // e.g., "S-1-5-21-123456789-987654321-111111111-1001" -> "S-1-5-21-123456789-987654321-111111111"
                        var sidParts = objectSidStr.Split('-');
                        if (sidParts.Length > 4)
                        {
                            var domainSid = string.Join("-", sidParts.Take(sidParts.Length - 1));
                            var primaryGroupSid = $"{domainSid}-{primaryGroupIdStr}";

                            // Look up the group by its SID
                            if (groupsBySid.TryGetValue(primaryGroupSid, out var primaryGroupId))
                            {
                                allMembershipsToUpsert.Add((resolvedObjectId, primaryGroupId, IsDirect: true, IsPrimary: true));
                                primaryGroupsResolved++;
                            }
                            else
                            {
                                primaryGroupsMissing++;
                                _logger.LogDebug("Primary group SID {SID} not found for identity {Id}",
                                    primaryGroupSid, identityObject.SourceUniqueId);
                            }
                        }
                    }
                }

                // ===========================================
                // PART 2: Handle memberOf attribute (regular group memberships)
                // CRITICAL: Use case-insensitive lookup - LDAP returns lowercase names
                // ===========================================
                var memberOfValue = GetSourceValue(sourceObject, "memberOf");
                if (memberOfValue == null)
                    continue;

                var groupDNs = new List<string>();

                // Handle both string[] and single string values, splitting on semicolons
                // ObjectAttributes may store memberOf as "CN=a;CN=b;CN=c" so we split
                if (memberOfValue is string[] dnArray)
                {
                    foreach (var item in dnArray.Where(d => !string.IsNullOrWhiteSpace(d)))
                    {
                        // Split semicolon-separated values
                        groupDNs.AddRange(item.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(d => d.Trim())
                            .Where(d => !string.IsNullOrWhiteSpace(d)));
                    }
                }
                else if (memberOfValue is string dn && !string.IsNullOrWhiteSpace(dn))
                {
                    // Split semicolon-separated values
                    groupDNs.AddRange(dn.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .Where(d => !string.IsNullOrWhiteSpace(d)));
                }

                if (!groupDNs.Any())
                    continue;

                // Convert DNs to group IDs and add to bulk list
                foreach (var groupDN in groupDNs)
                {
                    if (allGroups.TryGetValue(groupDN, out var groupId))
                    {
                        allMembershipsToUpsert.Add((resolvedObjectId, groupId, IsDirect: true, IsPrimary: false));
                    }
                }
            }

            if (objectsNotFoundInDb > 0)
            {
                _logger.LogWarning("{Count} objects not found in database (may still be syncing), skipped membership sync",
                    objectsNotFoundInDb);
            }

            _logger.LogInformation("Primary groups: {Resolved} resolved, {Missing} missing (group not synced yet)",
                primaryGroupsResolved, primaryGroupsMissing);

            // CRITICAL: Deduplicate memberships before bulk upsert!
            // Duplicates can occur when:
            // 1. Primary group (from primaryGroupID) is also in memberOf
            // 2. Same DN appears multiple times in memberOf (semicolon parsing, AD quirks)
            // The MERGE statement fails if the same row appears twice in the source.
            // Keep IsPrimary=true version if there's a conflict (primary group takes precedence)
            var deduplicatedMemberships = allMembershipsToUpsert
                .GroupBy(m => (m.ObjectId, m.GroupId))
                .Select(g => g.OrderByDescending(m => m.IsPrimary).First())
                .ToList();

            _logger.LogInformation("⚡ Bulk upserting {Count} memberships using stored procedure... (deduplicated from {Original})",
                deduplicatedMemberships.Count, allMembershipsToUpsert.Count);

            // BULK UPSERT: Single stored procedure call for ALL memberships (<1 second!)
            int affected = 0;
            if (deduplicatedMemberships.Any())
            {
                affected = await _syncRepository.BulkUpsertObjectGroupMembershipsAsync(
                    deduplicatedMemberships,
                    cancellationToken);
            }

            _logger.LogInformation(
                "✅ FAST BATCH COMPLETE: {Affected} memberships affected in bulk operation",
                affected);
        }

        /// <summary>
        /// Case-insensitive lookup for source object attributes.
        /// LDAP returns attribute names in lowercase (e.g., "primarygroupid", "objectsid")
        /// but code often uses camelCase (e.g., "primaryGroupID", "objectSid").
        /// </summary>
        private static object? GetSourceValue(Dictionary<string, object> sourceObject, string attributeName)
        {
            var key = sourceObject.Keys.FirstOrDefault(k => k.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
            return key != null ? sourceObject[key] : null;
        }

        /// <summary>
        /// Case-insensitive check if source object contains an attribute.
        /// </summary>
        private static bool HasSourceAttribute(Dictionary<string, object> sourceObject, string attributeName)
        {
            return sourceObject.Keys.Any(k => k.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
