namespace Radivum.Model;

/// <summary>
/// Transport an analysis bundle was produced from. The contract describes the analysis rather than the
/// transport, so this value only records provenance and never changes how a bundle is read.
/// </summary>
public static class AnalysisSourceKind
{
    /// <summary>Fixture supplied by the Workbench for demonstration, with no Azure access.</summary>
    public const string Demo = "demo";

    /// <summary>Analysis produced from a saved snapshot file on the operator's machine.</summary>
    public const string LocalFile = "local-file";

    /// <summary>Analysis produced by an open-source collection run against Azure.</summary>
    public const string OssCollection = "oss-collection";

    /// <summary>Analysis produced by the Pro command line host.</summary>
    public const string ProCli = "pro-cli";
}
