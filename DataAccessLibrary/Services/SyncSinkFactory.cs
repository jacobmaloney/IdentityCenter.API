using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// PHASE 1 SEAM. Resolves the <see cref="ISyncSink"/> for a sync project based on its
    /// TargetConnectionId.
    ///
    /// Routing:
    ///   - TargetConnectionId == null  -> the internal IdentityCenter identity store
    ///     (<see cref="IdentityStoreSink"/>). This is the ONLY supported sink in IC.
    ///   - TargetConnectionId != null  -> an EXTERNAL directory. No external sink is
    ///     implemented in IdentityCenter (that outbound write engine is Conduit's job),
    ///     so <see cref="HasSink"/> returns false and <see cref="ResolveSinkAsync"/> throws
    ///     a clear, friendly exception. There is NO silent no-op and NO fallback to writing
    ///     the Objects store for a non-null target.
    ///
    /// This factory introduces NO live-directory-write code.
    /// </summary>
    public class SyncSinkFactory
    {
        private readonly ISyncRepository _syncRepository;
        private readonly string _connectionString;

        public SyncSinkFactory(ISyncRepository syncRepository, string connectionString)
        {
            _syncRepository = syncRepository ?? throw new ArgumentNullException(nameof(syncRepository));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <summary>
        /// Whether IdentityCenter has an implemented sink for a given target connection type.
        /// Today only the internal identity store is implemented; every external connector
        /// type returns false (its outbound write lives in Conduit).
        /// </summary>
        public bool HasSink(string? connectionType)
        {
            // No external sink type is implemented in IdentityCenter.
            return false;
        }

        /// <summary>
        /// Resolves the sink for a project. For an external target this THROWS
        /// (fail-fast at run start) rather than returning a no-op sink.
        /// </summary>
        public async Task<ISyncSink> ResolveSinkAsync(SyncProject project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            // Null target => internal identity store (historical line-1368 behavior).
            if (!project.TargetConnectionId.HasValue)
            {
                return new IdentityStoreSink(_syncRepository);
            }

            // Non-null target => external directory. Look it up to produce an accurate,
            // friendly message naming the actual connection type.
            DirectoryConnection? target = await LoadTargetConnectionAsync(project.TargetConnectionId.Value, ct);
            string typeLabel = target?.ConnectionType ?? "Unknown";

            if (!HasSink(target?.ConnectionType))
            {
                throw new NotSupportedException(
                    $"Outbound write to '{typeLabel}' is handled by Conduit and is not yet available in IdentityCenter.");
            }

            // Unreachable in Phase 1 (HasSink is always false for external types). Left as a
            // guard so this stays correct if/when an in-IC external sink is ever added.
            throw new NotSupportedException(
                $"Outbound write to '{typeLabel}' is handled by Conduit and is not yet available in IdentityCenter.");
        }

        private async Task<DirectoryConnection?> LoadTargetConnectionAsync(Guid connectionId, CancellationToken ct)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            return await connection.QueryFirstOrDefaultAsync<DirectoryConnection>(
                "SELECT * FROM DirectoryConnections WHERE Id = @Id",
                new { Id = connectionId });
        }
    }
}
