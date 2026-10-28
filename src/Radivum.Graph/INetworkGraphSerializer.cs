using Radivum.Model;

namespace Radivum.Graph;

/// <summary>
/// Serializes <see cref="NetworkGraph"/> instances using the shared source-generated contract.
/// </summary>
public interface INetworkGraphSerializer
{
    /// <summary>
    /// Serializes a graph to its canonical UTF-8 JSON representation.
    /// </summary>
    /// <param name="graph">Graph to serialize.</param>
    /// <returns>Canonical UTF-8 JSON bytes, without a byte order mark.</returns>
    byte[] Serialize(NetworkGraph graph);
}
