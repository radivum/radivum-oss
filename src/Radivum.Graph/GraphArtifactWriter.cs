using Radivum.Model;
using Radivum.Oss.Reporting;

namespace Radivum.Graph;

/// <summary>
/// Serializes graphs and writes them through the shared artifact writer.
/// </summary>
public sealed class GraphArtifactWriter(
    INetworkGraphSerializer networkGraphSerializer,
    IArtifactWriter artifactWriter) : IGraphArtifactWriter
{
    /// <inheritdoc />
    public Task<ArtifactWriteResult> WriteAsync(
        NetworkGraph graph,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var content = networkGraphSerializer.Serialize(graph);
        return artifactWriter.WriteAsync(outputPath, content, cancellationToken);
    }
}
