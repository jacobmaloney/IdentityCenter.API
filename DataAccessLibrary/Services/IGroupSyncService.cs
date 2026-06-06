using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Interface for V2 Group membership synchronization service
    /// </summary>
    public interface IGroupSyncService
    {
        /// <summary>
        /// Syncs group memberships for a list of groups
        /// </summary>
        Task<int> SyncGroupMembershipsAsync(
            List<Dictionary<string, object>> groupsWithMembers,
            string sourceConnectionId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Syncs primary group memberships
        /// </summary>
        Task<int> SyncPrimaryGroupMembershipsAsync(
            string sourceConnectionId,
            CancellationToken cancellationToken);
    }
}
