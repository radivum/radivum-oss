using Radivum.Model;

namespace Radivum.Oss.Reporting;

/// <summary>
/// Renders a graph into a human-readable diagram document.
/// </summary>
public interface IDiagramRenderer
{
    /// <summary>
    /// Renders a graph as a markdown document containing an embedded diagram.
    /// </summary>
    /// <param name="graph">Graph to render.</param>
    /// <returns>Markdown content with the embedded diagram.</returns>
    string RenderMarkdown(NetworkGraph graph);
}
