namespace Radivum.Model;

/// <summary>
/// Identifiers of the analysis capabilities declared by an analysis bundle. The values reuse the
/// Workbench's existing section vocabulary verbatim.
/// </summary>
public static class AnalysisCapabilityIds
{
    /// <summary>Analysis level summary of scope, coverage, and versions.</summary>
    public const string Overview = "overview";

    /// <summary>Topology graph projection.</summary>
    public const string Topology = "topology";

    /// <summary>Deterministic findings produced from the snapshot.</summary>
    public const string Findings = "findings";

    /// <summary>Routing evidence, and effective routing when it was collected.</summary>
    public const string Routing = "routing";

    /// <summary>Security evidence, and effective network security groups when they were collected.</summary>
    public const string Security = "security";

    /// <summary>Resource inventory carried by the network snapshot.</summary>
    public const string Inventory = "inventory";

    /// <summary>Reachability trace results.</summary>
    public const string Trace = "trace";
}
