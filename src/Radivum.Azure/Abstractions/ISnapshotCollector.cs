using Radivum.Model;

namespace Radivum.Azure;

/// <summary>
/// Collects a normalized NetworkSnapshot from Azure scope inputs.
/// </summary>
public interface ISnapshotCollector
{
    /// <summary>
    /// Collects a snapshot for the provided request scope.
    /// </summary>
    /// <param name="request">Collection request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collected snapshot.</returns>
    Task<NetworkSnapshot> CollectAsync(
        SnapshotCollectionRequest request,
        CancellationToken cancellationToken = default);
}
