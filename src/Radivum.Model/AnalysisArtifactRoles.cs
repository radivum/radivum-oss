namespace Radivum.Model;

/// <summary>
/// Artifact roles an analysis bundle can index. The values are the artifact file names, so a
/// manifest doubles as documentation of what it references.
/// </summary>
public static class AnalysisArtifactRoles
{
    /// <summary>Analysis manifest that indexes every other artifact.</summary>
    public const string Manifest = "analysis-manifest.json";

    /// <summary>Network snapshot contract, unchanged from the OSS output.</summary>
    public const string NetworkSnapshot = "network-snapshot.json";

    /// <summary>Network graph contract, unchanged from the OSS output.</summary>
    public const string NetworkGraph = "network-graph.json";

    /// <summary>Findings produced by the analysis layer.</summary>
    public const string Findings = "findings.json";

    /// <summary>Routing evidence, including effective routes when collected.</summary>
    public const string RoutingEvidence = "routing-evidence.json";

    /// <summary>Security evidence, including effective network security groups when collected.</summary>
    public const string SecurityEvidence = "security-evidence.json";

    /// <summary>Reachability trace results.</summary>
    public const string TraceResults = "trace-results.json";
}
