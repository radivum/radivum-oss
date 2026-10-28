namespace Radivum.Oss.Commands;

/// <summary>
/// File names of the renderings a run writes beside its bundle. They are not artifact roles: the manifest
/// indexes the analysis, and a rendering is produced from it (analysis architecture section 8.3).
/// </summary>
internal static class AnalysisRenderingNames
{
    /// <summary>Mermaid topology rendered from the graph artifact.</summary>
    public const string TopologyDiagram = "network-topology.md";
}
