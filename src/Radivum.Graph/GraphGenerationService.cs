using Radivum.Model;

namespace Radivum.Graph;

/// <summary>
/// Builds graphs from snapshots. The generation timestamp comes from the injected
/// <see cref="TimeProvider"/> so graph output stays reproducible under test.
/// </summary>
public sealed class GraphGenerationService(TimeProvider timeProvider) : IGraphGenerationService
{
    /// <inheritdoc />
    public NetworkGraph Generate(NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new NetworkGraphBuilder();
        return builder.Build(snapshot, timeProvider.GetUtcNow());
    }
}
