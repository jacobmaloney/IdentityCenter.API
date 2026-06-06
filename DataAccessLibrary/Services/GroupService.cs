using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service for managing groups with direct management capabilities
    /// UC-GRP-01: Group Management with Access Review
    /// PHASE 2: Direct management service layer with AD write-back
    /// </summary>
    public class GroupService : IGroupService
    {
        private readonly string _connectionString;
        private readonly ILogger<GroupService> _logger;

        public GroupService(
            IConfiguration configuration,
            ILogger<GroupService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        // ====================================================================
        // BASIC CRUD OPERATIONS
        // ====================================================================

        /// <summary>
        /// Gets a group by ID with all related data loaded
        /// </summary>
        public async Task<Group?> GetByIdAsync(Guid id, bool includeMembers = false, bool includeAttributes = false)
        {
            _logger.LogDebug("Getting group {GroupId} (includeMembers: {IncludeMembers}, includeAttributes: {IncludeAttributes})",
                id, includeMembers, includeAttributes);

            using var connection = CreateConnection();

            // Query Objects table where ObjectClass='group' (modern architecture)
            const string sql = @"
                SELECT Id, ObjectClass, DN, CN, DisplayName, Email, IsActive,
                       SourceConnectionId, SourceUniqueId, SourceType, LastSyncedAt
                FROM Objects
                WHERE Id = @Id AND ObjectClass = 'group'";

            var groupObject = await connection.QueryFirstOrDefaultAsync<dynamic>(sql, new { Id = id });

            if (groupObject == null)
            {
                _logger.LogWarning("Group {GroupId} not found in Objects table", id);
                return null;
            }

            // Map IdentityObject to Group model for compatibility
            var group = new Group
            {
                Id = groupObject.Id,
                Name = !string.IsNullOrWhiteSpace((string?)groupObject.CN)
                    ? groupObject.CN
                    : !string.IsNullOrWhiteSpace((string?)groupObject.DisplayName)
                        ? groupObject.DisplayName
                        : groupObject.DN ?? "Unnamed Group",
                DistinguishedName = groupObject.DN,
                Email = groupObject.Email,
                IsActive = groupObject.IsActive,
                SourceConnectionId = groupObject.SourceConnectionId,
                SourceUniqueId = groupObject.SourceUniqueId,
                SourceType = groupObject.SourceType,
                LastSyncedAt = groupObject.LastSyncedAt
            };

            // Load members if requested
            if (includeMembers)
            {
                const string membersSql = @"
                    SELECT m.Id, m.GroupId, m.ObjectId, m.IsActive, m.AddedAt, m.AddedBy,
                           m.Justification, m.ExpirationDate, m.RemovedAt, m.RemovedBy, m.RemovalReason,
                           o.Id, o.ObjectClass, o.DN, o.CN, o.DisplayName, o.Email, o.IsActive,
                           o.SourceConnectionId, o.SourceUniqueId, o.SourceType, o.LastSyncedAt
                    FROM ObjectGroupMemberships m
                    INNER JOIN Objects o ON m.ObjectId = o.Id
                    WHERE m.GroupId = @GroupId";

                var members = await connection.QueryAsync<ObjectGroupMembership, IdentityObject, ObjectGroupMembership>(
                    membersSql,
                    (membership, obj) =>
                    {
                        membership.Object = obj;
                        return membership;
                    },
                    new { GroupId = id },
                    splitOn: "Id");

                group.Members = members.ToList();
            }

            // Load attributes if requested
            if (includeAttributes)
            {
                const string attributesSql = @"
                    SELECT Id, ObjectId, AttributeName, AttributeValue, CreatedAt, UpdatedAt
                    FROM ObjectAttributes
                    WHERE ObjectId = @ObjectId";

                var attributes = (await connection.QueryAsync<ObjectAttribute>(attributesSql, new { ObjectId = id })).ToList();

                // Map to GroupAttribute format if needed
                // For now, we'll populate Description and GroupType from attributes
                var descAttr = attributes.FirstOrDefault(a => a.AttributeName.Equals("description", StringComparison.OrdinalIgnoreCase));
                if (descAttr != null)
                {
                    group.Description = descAttr.AttributeValue;
                }

                var groupTypeAttr = attributes.FirstOrDefault(a => a.AttributeName.Equals("groupType", StringComparison.OrdinalIgnoreCase));
                if (groupTypeAttr != null)
                {
                    group.GroupType = groupTypeAttr.AttributeValue;
                }
            }

            _logger.LogDebug("Found group {GroupName} ({GroupId}) in Objects table", group.Name, id);
            return group;
        }

        /// <summary>
        /// Gets all groups with optional filtering
        /// </summary>
        public async Task<List<Group>> GetAllAsync(
            Guid? sourceConnectionId = null,
            bool? isActive = null,
            bool? requiresReview = null,
            string? riskLevel = null,
            int skip = 0,
            int take = 100)
        {
            _logger.LogDebug("Getting groups (skip: {Skip}, take: {Take}, sourceConnectionId: {SourceConnectionId}, isActive: {IsActive}, requiresReview: {RequiresReview}, riskLevel: {RiskLevel})",
                skip, take, sourceConnectionId, isActive, requiresReview, riskLevel);

            using var connection = CreateConnection();

            // Build dynamic SQL with filters
            var sql = @"
                SELECT Id, ObjectClass, DN, CN, DisplayName, Email, IsActive,
                       SourceConnectionId, SourceUniqueId, SourceType, LastSyncedAt
                FROM Objects
                WHERE ObjectClass = 'group'";

            var parameters = new DynamicParameters();

            if (sourceConnectionId.HasValue)
            {
                sql += " AND SourceConnectionId = @SourceConnectionId";
                parameters.Add("SourceConnectionId", sourceConnectionId.Value);
            }

            if (isActive.HasValue)
            {
                sql += " AND IsActive = @IsActive";
                parameters.Add("IsActive", isActive.Value);
            }

            // Note: requiresReview and riskLevel are not in Objects table
            // These are Group-specific attributes that would need to come from ObjectAttributes
            // For now, we'll skip these filters as they're not commonly used

            sql += " ORDER BY COALESCE(CN, DisplayName, DN) OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
            parameters.Add("Skip", skip);
            parameters.Add("Take", take);

            var groupObjects = await connection.QueryAsync<dynamic>(sql, parameters);

            // Map to Group model for compatibility
            var groups = groupObjects.Select(o => new Group
            {
                Id = o.Id,
                Name = !string.IsNullOrWhiteSpace((string?)o.CN)
                    ? o.CN
                    : !string.IsNullOrWhiteSpace((string?)o.DisplayName)
                        ? o.DisplayName
                        : o.DN ?? "Unnamed Group",
                DistinguishedName = o.DN,
                Email = o.Email,
                IsActive = o.IsActive,
                SourceConnectionId = o.SourceConnectionId,
                SourceUniqueId = o.SourceUniqueId,
                SourceType = o.SourceType,
                LastSyncedAt = o.LastSyncedAt
            }).ToList();

            _logger.LogInformation("Retrieved {Count} groups from Objects table", groups.Count);
            return groups;
        }

        /// <summary>
        /// Gets count of groups matching criteria (for pagination)
        /// </summary>
        public async Task<int> GetCountAsync(
            Guid? sourceConnectionId = null,
            bool? isActive = null,
            bool? requiresReview = null,
            string? riskLevel = null)
        {
            using var connection = CreateConnection();

            var sql = "SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'group'";
            var parameters = new DynamicParameters();

            if (sourceConnectionId.HasValue)
            {
                sql += " AND SourceConnectionId = @SourceConnectionId";
                parameters.Add("SourceConnectionId", sourceConnectionId.Value);
            }

            if (isActive.HasValue)
            {
                sql += " AND IsActive = @IsActive";
                parameters.Add("IsActive", isActive.Value);
            }

            // Note: requiresReview and riskLevel filters skipped (not in Objects table)

            var count = await connection.ExecuteScalarAsync<int>(sql, parameters);
            _logger.LogDebug("Counted {Count} groups matching criteria in Objects table", count);
            return count;
        }

        /// <summary>
        /// Searches for groups by name or email
        /// </summary>
        public async Task<List<Group>> SearchAsync(string searchTerm, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                _logger.LogWarning("Search called with empty search term");
                return new List<Group>();
            }

            _logger.LogDebug("Searching groups for term: {SearchTerm} (limit: {Limit})", searchTerm, limit);

            using var connection = CreateConnection();

            const string sql = @"
                SELECT TOP (@Limit) Id, ObjectClass, DN, CN, DisplayName, Email, IsActive,
                       SourceConnectionId, SourceUniqueId, SourceType, LastSyncedAt
                FROM Objects
                WHERE ObjectClass = 'group'
                  AND (CN LIKE @SearchPattern
                       OR DisplayName LIKE @SearchPattern
                       OR Email LIKE @SearchPattern)
                ORDER BY COALESCE(CN, DisplayName)";

            var groupObjects = await connection.QueryAsync<dynamic>(sql, new { Limit = limit, SearchPattern = $"%{searchTerm}%" });

            // Map to Group model for compatibility
            var groups = groupObjects.Select(o => new Group
            {
                Id = o.Id,
                Name = !string.IsNullOrWhiteSpace((string?)o.CN)
                    ? o.CN
                    : !string.IsNullOrWhiteSpace((string?)o.DisplayName)
                        ? o.DisplayName
                        : o.DN ?? "Unnamed Group",
                DistinguishedName = o.DN,
                Email = o.Email,
                IsActive = o.IsActive,
                SourceConnectionId = o.SourceConnectionId,
                SourceUniqueId = o.SourceUniqueId,
                SourceType = o.SourceType,
                LastSyncedAt = o.LastSyncedAt
            }).ToList();

            _logger.LogInformation("Found {Count} groups matching '{SearchTerm}' in Objects table", groups.Count, searchTerm);
            return groups;
        }

        // ====================================================================
        // UPDATE OPERATIONS WITH AD WRITE-BACK
        // UC-GRP-01-02: Edit Group Properties
        // ====================================================================

        /// <summary>
        /// Updates a group's properties with audit logging and AD write-back
        /// </summary>
        public async Task<Group> UpdateAsync(Group group, string modifiedBy, bool writeBackToAD = true)
        {
            _logger.LogInformation("Updating group {GroupName} ({GroupId}) by {ModifiedBy} (writeBackToAD: {WriteBackToAD})",
                group.Name, group.Id, modifiedBy, writeBackToAD);

            // Update audit fields
            group.LastSyncedAt = DateTime.UtcNow;

            // TODO: AD write-back will be implemented in Phase 2.5 via IDirectoryWriteService
            if (writeBackToAD)
            {
                _logger.LogWarning("AD write-back requested but not yet implemented for group {GroupId}", group.Id);
            }

            using var connection = CreateConnection();

            const string sql = @"
                UPDATE Groups
                SET Name = @Name,
                    Description = @Description,
                    DistinguishedName = @DistinguishedName,
                    Email = @Email,
                    GroupType = @GroupType,
                    IsActive = @IsActive,
                    RequiresReview = @RequiresReview,
                    RequiresJustification = @RequiresJustification,
                    ReviewFrequencyDays = @ReviewFrequencyDays,
                    RiskScore = @RiskScore,
                    RiskLevel = @RiskLevel,
                    LastRiskAssessment = @LastRiskAssessment,
                    NextReviewDate = @NextReviewDate,
                    LastSyncedAt = @LastSyncedAt,
                    SourceConnectionId = @SourceConnectionId,
                    SourceUniqueId = @SourceUniqueId,
                    SourceType = @SourceType
                WHERE Id = @Id";

            await connection.ExecuteAsync(sql, group);

            _logger.LogInformation("Successfully updated group {GroupName} ({GroupId})", group.Name, group.Id);
            return group;
        }

        /// <summary>
        /// Updates only specific properties of a group (partial update)
        /// </summary>
        public async Task<Group> UpdatePropertiesAsync(Guid groupId, Dictionary<string, object> propertyUpdates, string modifiedBy, bool writeBackToAD = true)
        {
            _logger.LogInformation("Updating {PropertyCount} properties for group {GroupId} by {ModifiedBy}",
                propertyUpdates.Count, groupId, modifiedBy);

            var group = await GetByIdAsync(groupId);
            if (group == null)
            {
                throw new InvalidOperationException($"Group {groupId} not found");
            }

            // Apply property updates using reflection
            var groupType = typeof(Group);
            foreach (var update in propertyUpdates)
            {
                var property = groupType.GetProperty(update.Key);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(group, update.Value);
                    _logger.LogDebug("Updated property {PropertyName} to {Value} for group {GroupId}",
                        update.Key, update.Value, groupId);
                }
                else
                {
                    _logger.LogWarning("Property {PropertyName} not found or not writable on Group entity", update.Key);
                }
            }

            return await UpdateAsync(group, modifiedBy, writeBackToAD);
        }

        // ====================================================================
        // MEMBER MANAGEMENT
        // UC-GRP-01-03: Manage Group Members
        // ====================================================================

        /// <summary>
        /// Gets all members of a group
        /// </summary>
        public async Task<List<ObjectGroupMembership>> GetMembersAsync(Guid groupId, bool includeInactive = false)
        {
            _logger.LogDebug("Getting members for group {GroupId} (includeInactive: {IncludeInactive})",
                groupId, includeInactive);

            using var connection = CreateConnection();

            var sql = @"
                SELECT m.Id, m.GroupId, m.ObjectId, m.IsActive, m.AddedAt, m.AddedBy,
                       m.Justification, m.ExpirationDate, m.RemovedAt, m.RemovedBy, m.RemovalReason,
                       o.Id, o.ObjectClass, o.DN, o.CN, o.DisplayName, o.Email, o.IsActive,
                       o.SourceConnectionId, o.SourceUniqueId, o.SourceType, o.LastSyncedAt
                FROM ObjectGroupMemberships m
                INNER JOIN Objects o ON m.ObjectId = o.Id
                WHERE m.GroupId = @GroupId";

            if (!includeInactive)
            {
                sql += " AND m.IsActive = 1";
            }

            sql += " ORDER BY o.DisplayName";

            var members = await connection.QueryAsync<ObjectGroupMembership, IdentityObject, ObjectGroupMembership>(
                sql,
                (membership, obj) =>
                {
                    membership.Object = obj;
                    return membership;
                },
                new { GroupId = groupId },
                splitOn: "Id");

            var membersList = members.ToList();
            _logger.LogInformation("Retrieved {Count} members for group {GroupId}", membersList.Count, groupId);
            return membersList;
        }

        /// <summary>
        /// Adds a member to a group with justification tracking
        /// </summary>
        public async Task<ObjectGroupMembership> AddMemberAsync(
            Guid groupId,
            Guid objectId,
            string? justification,
            string addedBy,
            DateTime? expirationDate = null,
            bool writeBackToAD = true)
        {
            _logger.LogInformation("Adding object {ObjectId} to group {GroupId} by {AddedBy}",
                objectId, groupId, addedBy);

            // Verify group exists and check if justification is required
            var group = await GetByIdAsync(groupId);
            if (group == null)
            {
                throw new InvalidOperationException($"Group {groupId} not found");
            }

            if (group.RequiresJustification && string.IsNullOrWhiteSpace(justification))
            {
                throw new ArgumentException("Justification is required for this group but was not provided", nameof(justification));
            }

            using var connection = CreateConnection();

            // Check if membership already exists
            const string checkSql = @"
                SELECT Id, GroupId, ObjectId, IsActive, AddedAt, AddedBy,
                       Justification, ExpirationDate, RemovedAt, RemovedBy, RemovalReason
                FROM ObjectGroupMemberships
                WHERE GroupId = @GroupId AND ObjectId = @ObjectId";

            var existingMembership = await connection.QueryFirstOrDefaultAsync<ObjectGroupMembership>(
                checkSql, new { GroupId = groupId, ObjectId = objectId });

            if (existingMembership != null)
            {
                if (existingMembership.IsActive)
                {
                    _logger.LogWarning("Object {ObjectId} is already an active member of group {GroupId}", objectId, groupId);
                    return existingMembership;
                }
                else
                {
                    // Reactivate existing membership
                    existingMembership.IsActive = true;
                    existingMembership.AddedAt = DateTime.UtcNow;
                    existingMembership.AddedBy = addedBy;
                    existingMembership.Justification = justification;
                    existingMembership.ExpirationDate = expirationDate;

                    const string updateSql = @"
                        UPDATE ObjectGroupMemberships
                        SET IsActive = @IsActive,
                            AddedAt = @AddedAt,
                            AddedBy = @AddedBy,
                            Justification = @Justification,
                            ExpirationDate = @ExpirationDate
                        WHERE Id = @Id";

                    await connection.ExecuteAsync(updateSql, existingMembership);

                    _logger.LogInformation("Reactivated membership for object {ObjectId} in group {GroupId}", objectId, groupId);
                    return existingMembership;
                }
            }

            // Create new membership
            var membership = new ObjectGroupMembership
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                ObjectId = objectId,
                IsActive = true,
                AddedAt = DateTime.UtcNow,
                AddedBy = addedBy,
                Justification = justification,
                ExpirationDate = expirationDate
            };

            const string insertSql = @"
                INSERT INTO ObjectGroupMemberships (Id, GroupId, ObjectId, IsActive, AddedAt, AddedBy, Justification, ExpirationDate)
                VALUES (@Id, @GroupId, @ObjectId, @IsActive, @AddedAt, @AddedBy, @Justification, @ExpirationDate)";

            await connection.ExecuteAsync(insertSql, membership);

            // TODO: AD write-back will be implemented in Phase 2.5 via IDirectoryWriteService
            if (writeBackToAD)
            {
                _logger.LogWarning("AD write-back requested but not yet implemented for membership {MembershipId}", membership.Id);
            }

            _logger.LogInformation("Successfully added object {ObjectId} to group {GroupId} (membership {MembershipId})",
                objectId, groupId, membership.Id);

            return membership;
        }

        /// <summary>
        /// Removes a member from a group
        /// </summary>
        public async Task<bool> RemoveMemberAsync(
            Guid groupId,
            Guid objectId,
            string? reason,
            string removedBy,
            bool writeBackToAD = true)
        {
            _logger.LogInformation("Removing object {ObjectId} from group {GroupId} by {RemovedBy}",
                objectId, groupId, removedBy);

            using var connection = CreateConnection();

            const string selectSql = @"
                SELECT Id, GroupId, ObjectId, IsActive, AddedAt, AddedBy,
                       Justification, ExpirationDate, RemovedAt, RemovedBy, RemovalReason
                FROM ObjectGroupMemberships
                WHERE GroupId = @GroupId AND ObjectId = @ObjectId AND IsActive = 1";

            var membership = await connection.QueryFirstOrDefaultAsync<ObjectGroupMembership>(
                selectSql, new { GroupId = groupId, ObjectId = objectId });

            if (membership == null)
            {
                _logger.LogWarning("Active membership not found for object {ObjectId} in group {GroupId}", objectId, groupId);
                return false;
            }

            // Soft delete by marking as inactive
            const string updateSql = @"
                UPDATE ObjectGroupMemberships
                SET IsActive = 0,
                    RemovedAt = @RemovedAt,
                    RemovedBy = @RemovedBy,
                    RemovalReason = @RemovalReason
                WHERE Id = @Id";

            await connection.ExecuteAsync(updateSql, new
            {
                Id = membership.Id,
                RemovedAt = DateTime.UtcNow,
                RemovedBy = removedBy,
                RemovalReason = reason
            });

            // TODO: AD write-back will be implemented in Phase 2.5 via IDirectoryWriteService
            if (writeBackToAD)
            {
                _logger.LogWarning("AD write-back requested but not yet implemented for membership {MembershipId}", membership.Id);
            }

            _logger.LogInformation("Successfully removed object {ObjectId} from group {GroupId}", objectId, groupId);
            return true;
        }

        /// <summary>
        /// Bulk adds multiple members to a group
        /// </summary>
        public async Task<List<ObjectGroupMembership>> BulkAddMembersAsync(
            Guid groupId,
            List<Guid> objectIds,
            string? justification,
            string addedBy,
            bool writeBackToAD = true)
        {
            _logger.LogInformation("Bulk adding {Count} members to group {GroupId} by {AddedBy}",
                objectIds.Count, groupId, addedBy);

            var memberships = new List<ObjectGroupMembership>();

            foreach (var objectId in objectIds)
            {
                try
                {
                    var membership = await AddMemberAsync(groupId, objectId, justification, addedBy, null, writeBackToAD);
                    memberships.Add(membership);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add object {ObjectId} to group {GroupId}", objectId, groupId);
                }
            }

            _logger.LogInformation("Successfully added {Count} of {Total} members to group {GroupId}",
                memberships.Count, objectIds.Count, groupId);

            return memberships;
        }

        // ====================================================================
        // RISK ASSESSMENT
        // UC-GRP-01-01: View Groups with Risk Assessment
        // ====================================================================

        /// <summary>
        /// Calculates and updates risk score for a group
        /// </summary>
        public async Task<Group> UpdateRiskScoreAsync(Guid groupId)
        {
            _logger.LogInformation("Updating risk score for group {GroupId}", groupId);

            var group = await GetByIdAsync(groupId, includeMembers: true);
            if (group == null)
            {
                throw new InvalidOperationException($"Group {groupId} not found");
            }

            // Call the domain method to calculate risk score
            group.UpdateRiskScore();
            group.LastRiskAssessment = DateTime.UtcNow;

            using var connection = CreateConnection();

            const string sql = @"
                UPDATE Groups
                SET RiskScore = @RiskScore,
                    RiskLevel = @RiskLevel,
                    LastRiskAssessment = @LastRiskAssessment
                WHERE Id = @Id";

            await connection.ExecuteAsync(sql, new
            {
                group.Id,
                group.RiskScore,
                group.RiskLevel,
                group.LastRiskAssessment
            });

            _logger.LogInformation("Updated risk score for group {GroupName} ({GroupId}) to {RiskScore} ({RiskLevel})",
                group.Name, groupId, group.RiskScore, group.RiskLevel);

            return group;
        }

        /// <summary>
        /// Calculates and updates risk scores for all groups
        /// Used by background job
        /// </summary>
        public async Task<int> UpdateAllRiskScoresAsync(Guid? sourceConnectionId = null)
        {
            _logger.LogInformation("Updating risk scores for all groups (sourceConnectionId: {SourceConnectionId})",
                sourceConnectionId);

            using var connection = CreateConnection();

            var sql = "SELECT * FROM Groups WHERE 1=1";
            var parameters = new DynamicParameters();

            if (sourceConnectionId.HasValue)
            {
                sql += " AND SourceConnectionId = @SourceConnectionId";
                parameters.Add("SourceConnectionId", sourceConnectionId.Value);
            }

            var groups = (await connection.QueryAsync<Group>(sql, parameters)).ToList();
            int updated = 0;

            foreach (var group in groups)
            {
                try
                {
                    group.UpdateRiskScore();
                    group.LastRiskAssessment = DateTime.UtcNow;
                    updated++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update risk score for group {GroupId}", group.Id);
                }
            }

            // Batch update all groups
            const string updateSql = @"
                UPDATE Groups
                SET RiskScore = @RiskScore,
                    RiskLevel = @RiskLevel,
                    LastRiskAssessment = @LastRiskAssessment
                WHERE Id = @Id";

            await connection.ExecuteAsync(updateSql, groups);

            _logger.LogInformation("Updated risk scores for {Updated} of {Total} groups", updated, groups.Count);
            return updated;
        }

        /// <summary>
        /// Gets groups that are high risk (risk level High or Critical)
        /// </summary>
        public async Task<List<Group>> GetHighRiskGroupsAsync(int limit = 100)
        {
            _logger.LogDebug("Getting high risk groups (limit: {Limit})", limit);

            using var connection = CreateConnection();

            const string sql = @"
                SELECT TOP (@Limit) *
                FROM Groups
                WHERE RiskLevel IN ('High', 'Critical')
                ORDER BY RiskScore DESC";

            var groups = (await connection.QueryAsync<Group>(sql, new { Limit = limit })).ToList();

            _logger.LogInformation("Retrieved {Count} high risk groups", groups.Count);
            return groups;
        }

        // ====================================================================
        // ACCESS REVIEW
        // UC-GRP-01-04: Conduct Access Review
        // ====================================================================

        /// <summary>
        /// Starts an access review for a group
        /// </summary>
        public async Task<Campaign> StartAccessReviewAsync(
            Guid groupId,
            Guid? reviewerId,
            DateTime dueDate,
            string createdBy)
        {
            _logger.LogInformation("Starting access review for group {GroupId} by {CreatedBy} (reviewer: {ReviewerId}, dueDate: {DueDate})",
                groupId, createdBy, reviewerId, dueDate);

            var group = await GetByIdAsync(groupId, includeMembers: true);
            if (group == null)
            {
                throw new InvalidOperationException($"Group {groupId} not found");
            }

            var review = new Campaign
            {
                Id = Guid.NewGuid(),
                Name = $"Access Review - {group.Name} - {DateTime.UtcNow:yyyy-MM-dd}",
                Description = $"Access review campaign for group {group.Name}",
                CampaignType = "GroupMembershipReview",
                ReviewType = "GroupMembership",
                StartDate = DateTime.UtcNow,
                EndDate = dueDate.AddDays(14),
                DueDate = dueDate,
                ReviewPeriodDays = 14,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy,
                EnableNotifications = true,
                ReminderDaysBefore = 3
            };

            // TODO: Use CampaignService or CampaignRepository to persist this campaign
            // This is a placeholder implementation - proper campaign creation should go through CampaignService

            _logger.LogInformation("Created access review campaign {CampaignId} for group {GroupName} ({GroupId})",
                review.Id, group.Name, groupId);

            return review;
        }

        /// <summary>
        /// Gets groups that are overdue for access review
        /// </summary>
        public async Task<List<Group>> GetOverdueForReviewAsync(int limit = 100)
        {
            _logger.LogDebug("Getting groups overdue for review (limit: {Limit})", limit);

            using var connection = CreateConnection();

            const string sql = @"
                SELECT TOP (@Limit) *
                FROM Groups
                WHERE RequiresReview = 1
                  AND (NextReviewDate IS NULL OR NextReviewDate < @Today)
                ORDER BY COALESCE(NextReviewDate, '1900-01-01')";

            var groups = (await connection.QueryAsync<Group>(sql, new { Limit = limit, Today = DateTime.UtcNow })).ToList();

            _logger.LogInformation("Retrieved {Count} groups overdue for review", groups.Count);
            return groups;
        }

        // ====================================================================
        // SYNC INTEGRATION
        // UC-GRP-01-05: Sync Group from AD
        // ====================================================================

        /// <summary>
        /// Syncs a specific group from AD on-demand
        /// </summary>
        public async Task<Group> SyncFromADAsync(Guid groupId, string requestedBy)
        {
            _logger.LogInformation("Syncing group {GroupId} from AD on-demand by {RequestedBy}", groupId, requestedBy);

            var group = await GetByIdAsync(groupId);
            if (group == null)
            {
                throw new InvalidOperationException($"Group {groupId} not found");
            }

            // TODO: Implement actual AD sync logic in Phase 2.5
            // This will integrate with existing SyncWorkflow infrastructure
            _logger.LogWarning("AD sync requested but not yet implemented for group {GroupId}", groupId);

            // For now, just update the LastSyncedAt timestamp
            group.LastSyncedAt = DateTime.UtcNow;

            using var connection = CreateConnection();

            const string sql = "UPDATE Groups SET LastSyncedAt = @LastSyncedAt WHERE Id = @Id";
            await connection.ExecuteAsync(sql, new { group.LastSyncedAt, group.Id });

            return group;
        }

        /// <summary>
        /// Calculates next review date based on review frequency
        /// </summary>
        public async Task<Group> CalculateNextReviewDateAsync(Guid groupId)
        {
            _logger.LogInformation("Calculating next review date for group {GroupId}", groupId);

            var group = await GetByIdAsync(groupId);
            if (group == null)
            {
                throw new InvalidOperationException($"Group {groupId} not found");
            }

            // Call the domain method to calculate next review date
            group.CalculateNextReviewDate();

            using var connection = CreateConnection();

            const string sql = "UPDATE Groups SET NextReviewDate = @NextReviewDate WHERE Id = @Id";
            await connection.ExecuteAsync(sql, new { group.NextReviewDate, group.Id });

            _logger.LogInformation("Calculated next review date for group {GroupName} ({GroupId}): {NextReviewDate}",
                group.Name, groupId, group.NextReviewDate);

            return group;
        }

        // ====================================================================
        // UI WRAPPER METHODS (for GroupsManagement.razor compatibility)
        // ====================================================================

        /// <summary>
        /// UI wrapper: Gets all groups (defaults to active only)
        /// </summary>
        public async Task<List<Group>> GetAllGroupsAsync()
        {
            return await GetAllAsync(isActive: true, take: 1000);
        }

        /// <summary>
        /// UI wrapper: Gets group by ID
        /// </summary>
        public async Task<Group?> GetGroupByIdAsync(Guid groupId)
        {
            return await GetByIdAsync(groupId, includeMembers: false);
        }

        /// <summary>
        /// UI wrapper: Gets group members with details
        /// </summary>
        public async Task<List<ObjectGroupMembership>> GetGroupMembersWithDetailsAsync(Guid groupId)
        {
            using var connection = CreateConnection();

            const string sql = @"
                SELECT m.Id, m.GroupId, m.ObjectId, m.IsActive, m.AddedAt, m.AddedBy,
                       m.Justification, m.ExpirationDate, m.RemovedAt, m.RemovedBy, m.RemovalReason,
                       o.Id, o.ObjectClass, o.DN, o.CN, o.DisplayName, o.Email, o.IsActive,
                       o.SourceConnectionId, o.SourceUniqueId, o.SourceType, o.LastSyncedAt
                FROM ObjectGroupMemberships m
                INNER JOIN Objects o ON m.ObjectId = o.Id
                WHERE m.GroupId = @GroupId
                ORDER BY o.DisplayName";

            var members = await connection.QueryAsync<ObjectGroupMembership, IdentityObject, ObjectGroupMembership>(
                sql,
                (membership, obj) =>
                {
                    membership.Object = obj;
                    return membership;
                },
                new { GroupId = groupId },
                splitOn: "Id");

            return members.ToList();
        }

        /// <summary>
        /// UI wrapper: Gets membership history (both active and removed)
        /// </summary>
        public async Task<List<ObjectGroupMembership>> GetMembershipHistoryAsync(Guid groupId)
        {
            using var connection = CreateConnection();

            const string sql = @"
                SELECT m.Id, m.GroupId, m.ObjectId, m.IsActive, m.AddedAt, m.AddedBy,
                       m.Justification, m.ExpirationDate, m.RemovedAt, m.RemovedBy, m.RemovalReason,
                       o.Id, o.ObjectClass, o.DN, o.CN, o.DisplayName, o.Email, o.IsActive,
                       o.SourceConnectionId, o.SourceUniqueId, o.SourceType, o.LastSyncedAt
                FROM ObjectGroupMemberships m
                INNER JOIN Objects o ON m.ObjectId = o.Id
                WHERE m.GroupId = @GroupId
                ORDER BY m.AddedAt DESC";

            var members = await connection.QueryAsync<ObjectGroupMembership, IdentityObject, ObjectGroupMembership>(
                sql,
                (membership, obj) =>
                {
                    membership.Object = obj;
                    return membership;
                },
                new { GroupId = groupId },
                splitOn: "Id");

            return members.ToList();
        }

        /// <summary>
        /// UI wrapper: Gets available users that can be added to group
        /// </summary>
        public async Task<List<IdentityObject>> GetAvailableUsersForGroupAsync(Guid groupId)
        {
            var group = await GetByIdAsync(groupId);
            if (group == null)
            {
                return new List<IdentityObject>();
            }

            using var connection = CreateConnection();

            const string sql = @"
                SELECT TOP 500 o.Id, o.ObjectClass, o.DN, o.CN, o.DisplayName, o.Email, o.IsActive,
                       o.SourceConnectionId, o.SourceUniqueId, o.SourceType, o.LastSyncedAt
                FROM Objects o
                WHERE o.SourceConnectionId = @SourceConnectionId
                  AND o.IsActive = 1
                  AND o.ObjectClass = 'user'
                  AND o.Id NOT IN (
                      SELECT ObjectId FROM ObjectGroupMemberships
                      WHERE GroupId = @GroupId AND IsActive = 1
                  )
                ORDER BY o.DisplayName";

            var users = await connection.QueryAsync<IdentityObject>(sql, new
            {
                SourceConnectionId = group.SourceConnectionId,
                GroupId = groupId
            });

            return users.ToList();
        }

        /// <summary>
        /// UI wrapper: Gets directory connections
        /// </summary>
        public async Task<List<Models.DirectoryConnection>> GetDirectoryConnectionsAsync()
        {
            using var connection = CreateConnection();

            const string sql = @"
                SELECT *
                FROM DirectoryConnections
                WHERE IsActive = 1
                ORDER BY Name";

            var connections = await connection.QueryAsync<Models.DirectoryConnection>(sql);
            return connections.ToList();
        }

        /// <summary>
        /// UI wrapper: Updates group
        /// </summary>
        public async Task<bool> UpdateGroupAsync(Group group)
        {
            try
            {
                await UpdateAsync(group, "Current User", writeBackToAD: true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
