namespace Radivum.Model;

/// <summary>
/// Edition that produced an analysis bundle. The edition lets the Workbench explain who produced a
/// bundle; it never changes how the snapshot or the graph is read.
/// </summary>
public static class AnalysisEdition
{
    /// <summary>Open-source edition: collection, observed topology, and the deterministic checks.</summary>
    public const string Oss = "oss";

    /// <summary>Pro edition: the open-source surface plus premium interpretation.</summary>
    public const string Pro = "pro";
}
