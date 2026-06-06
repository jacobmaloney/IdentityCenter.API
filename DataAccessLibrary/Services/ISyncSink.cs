using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// PHASE 1 SEAM (sync "to/from").
    ///
    /// A sink is the WRITE destination for a sync run's resolved batch. Today the only
    /// implemented sink is <see cref="IdentityStoreSink"/>, which writes into the internal
    /// IdentityCenter Objects/identity store (the historical behavior at
    /// SyncProjectOrchestrator line 1368).
    ///
    /// Outbound writes to EXTERNAL directories (AD, Entra, SCIM, etc.) are deliberately
    /// NOT implemented in IdentityCenter. That cross-directory outbound write engine is
    /// Conduit's responsibility. When a SyncProject targets an external connection, the
    /// factory reports HasSink == false and the orchestrator fails the run fast with a
    /// clear message rather than silently writing to the Objects store.
    ///
    /// This interface introduces NO live-directory-write code.
    /// </summary>
    public interface ISyncSink
    {
        /// <summary>
        /// Stable identifier for the sink kind (e.g. "IdentityStore"). Used for logging
        /// and diagnostics.
        /// </summary>
        string SinkType { get; }

        /// <summary>
        /// Writes a resolved batch of objects (with their extended attributes) to the sink.
        /// </summary>
        /// <param name="step">The sync step producing this batch (carries mapping/config context).</param>
        /// <param name="targetConnection">
        /// The resolved target directory connection, or null when the sink is the internal
        /// IdentityCenter identity store.
        /// </param>
        /// <param name="batch">The objects and their attributes to write.</param>
        /// <param name="options">Write options (see <see cref="SinkWriteOptions"/>).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="onProgress">
        /// Optional progress callback invoked as (processedSoFar, batchTotal). Mirrors the
        /// callback contract of ISyncObjectRepository.FastBulkUpsertObjectsAsync.
        /// </param>
        Task<SinkWriteResult> WriteBatchAsync(
            SyncStep step,
            DirectoryConnection? targetConnection,
            IReadOnlyList<(IdentityObject obj, List<ObjectAttribute> attrs)> batch,
            SinkWriteOptions options,
            CancellationToken ct,
            Func<int, int, Task>? onProgress = null);
    }

    /// <summary>
    /// Result of a sink write. Mirrors the counters surfaced by BulkUpsertResult so the
    /// orchestrator's existing run-metrics logic can consume sink output unchanged.
    /// </summary>
    public class SinkWriteResult
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Total objects the sink processed in this batch. On the identity-store path this
        /// mirrors BulkUpsertResult.ObjectsProcessed (the input batch count), preserving the
        /// exact value the orchestrator logged and accumulated before the seam existed.
        /// </summary>
        public int Processed { get; set; }

        /// <summary>
        /// SourceUniqueIds the sink reported as skipped (no changes). Used by the
        /// orchestrator to classify per-object audit operations as "Skipped". Preserves
        /// the BulkUpsertResult.SkippedSourceIds semantics on the identity-store path.
        /// </summary>
        public HashSet<string> SkippedSourceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Total attributes affected, where the sink can report it (identity store does).
        /// </summary>
        public int AttributesAffected { get; set; }
    }

    /// <summary>
    /// Options controlling a sink write. Intentionally minimal in Phase 1.
    /// </summary>
    public class SinkWriteOptions
    {
        /// <summary>
        /// Reserved for the Conduit outbound phase: when true, an external sink would
        /// compute the write plan without applying it. The IdentityStore sink ignores
        /// this flag (it has always applied writes).
        /// </summary>
        public bool DryRun { get; set; } = false;
    }
}
