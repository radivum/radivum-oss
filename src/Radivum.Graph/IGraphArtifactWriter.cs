using Radivum.Model;
using Radivum.Oss.Reporting;

namespace Radivum.Graph;

/// <summary>
/// Writes a graph projection to disk as <c>network-graph.json</c>.
/// </summary>
public interface IGraphArtifactWriter
{
    /// <summary>
    /// Writes a graph to the given file path.
    /// </summary>
    /// <param name="graph">Graph to write.</param>
    /// <param name="outputPath">Full path of the file to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path, size, and digest of the written artifact.</returns>
    Task<ArtifactWriteResult> WriteAsync(
        NetworkGraph graph,
        string outputPath,
        CancellationToken cancellationToken = default);
}
