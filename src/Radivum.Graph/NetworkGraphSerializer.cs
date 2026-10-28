using System.Text.Json;
using Radivum.Model;

namespace Radivum.Graph;

/// <summary>
/// Serializes graphs with the camelCase, indented options declared by
/// <see cref="NetworkSnapshotJsonContext"/>.
/// </summary>
public sealed class NetworkGraphSerializer : INetworkGraphSerializer
{
    /// <inheritdoc />
    public byte[] Serialize(NetworkGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return JsonSerializer.SerializeToUtf8Bytes(graph, NetworkSnapshotJsonContext.Default.NetworkGraph);
    }
}
