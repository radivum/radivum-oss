using Radivum.Model;

namespace Radivum.Graph;

/// <summary>
/// Produces the deterministic topology projection of a snapshot.
/// </summary>
public interface IGraphGenerationService
{
    /// <summary>
    /// Builds the graph for a snapshot.
    /// </summary>
    /// <param name="snapshot">Snapshot to project.</param>
    /// <returns>Deterministic graph projection stamped with the current time.</returns>
    NetworkGraph Generate(NetworkSnapshot snapshot);
}
