using Radivum.Model;

namespace Radivum.Azure;

/// <summary>
/// TODO (Step 10 placeholder): collects Level 2 / Level 3 effective network evidence.
/// This interface intentionally exists before implementation so the permission boundary is explicit.
/// </summary>
public interface IEffectiveReader
{
    /// <summary>
    /// Collects effective route, effective NSG, and next-hop evidence for the requested scope.
    /// </summary>
    /// <param name="request">Collection scope request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Effective evidence available for the scope.</returns>
    Task<EffectiveEvidenceSet> CollectAsync(
        SnapshotCollectionRequest request,
        CancellationToken cancellationToken = default);
}
