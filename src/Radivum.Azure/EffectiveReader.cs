using Radivum.Model;

namespace Radivum.Azure;

/// <summary>
/// Deferred implementation for effective route/NSG/next-hop collection.
/// TODO: implement in a later step once permission detection and live ARM/Network Watcher reads are enabled.
/// </summary>
internal sealed class EffectiveReader : IEffectiveReader
{
    public Task<EffectiveEvidenceSet> CollectAsync(
        SnapshotCollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        throw new NotSupportedException(
            "Effective analysis is not implemented yet. This requires Level 2/3 permissions and will be added in a later step.");
    }
}
