using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// PHASE 1: the ONLY implemented sink. Writes the resolved batch into the internal
    /// IdentityCenter Objects/identity store by delegating to
    /// ISyncObjectRepository.FastBulkUpsertObjectsAsync.
    ///
    /// This is a thin, behavior-preserving wrapper around the historical write site at
    /// SyncProjectOrchestrator line 1368. The arguments forwarded to the repository and
    /// the result mapping back are byte-identical to that prior call, so routing the
    /// null-target (identity-store) path through this sink is a no-op behaviorally.
    /// </summary>
    public class IdentityStoreSink : ISyncSink
    {
        private readonly ISyncRepository _syncRepository;

        public IdentityStoreSink(ISyncRepository syncRepository)
        {
            _syncRepository = syncRepository ?? throw new ArgumentNullException(nameof(syncRepository));
        }

        public string SinkType => "IdentityStore";

        public async Task<SinkWriteResult> WriteBatchAsync(
            SyncStep step,
            DirectoryConnection? targetConnection,
            IReadOnlyList<(IdentityObject obj, List<ObjectAttribute> attrs)> batch,
            SinkWriteOptions options,
            CancellationToken ct,
            Func<int, int, Task>? onProgress = null)
        {
            // Forward the exact list the orchestrator built. The orchestrator passes its
            // existing bulkUpsertList (a List<(IdentityObject, List<ObjectAttribute>)>), so
            // this cast reuses the SAME reference and no copy occurs -- preserving the
            // byte-identical call to FastBulkUpsertObjectsAsync. The fallback ToList() only
            // runs for callers that hand in a non-List enumerable.
            List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> list =
                batch as List<(IdentityObject identityObject, List<ObjectAttribute> attributes)>
                ?? batch.Select(t => (t.obj, t.attrs)).ToList();

            // BYTE-IDENTICAL to SyncProjectOrchestrator line 1368:
            //   await _syncRepository.FastBulkUpsertObjectsAsync(bulkUpsertList, cancellationToken, onProgress);
            BulkUpsertResult result = await _syncRepository.FastBulkUpsertObjectsAsync(list, ct, onProgress);

            return new SinkWriteResult
            {
                Processed = result.ObjectsProcessed,
                Created = result.ObjectsCreated,
                Updated = result.ObjectsUpdated,
                Skipped = result.ObjectsSkipped,
                Failed = 0,
                AttributesAffected = result.AttributesAffected,
                SkippedSourceIds = result.SkippedSourceIds
            };
        }
    }
}
