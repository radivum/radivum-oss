using System.Text;
using Radivum.Graph;
using Radivum.Model;
using Radivum.Oss.Analysis;
using Radivum.Oss.Reporting;

namespace Radivum.Tests;

/// <summary>
/// Builds the canonical analysis bundles the manifest tests use: an open-source bundle written exactly
/// as the collect and report commands write it, and a Pro bundle carrying every artifact role except
/// security evidence, which stays absent so the evaluator's unavailable branch is exercised.
/// </summary>
internal static class TestAnalysisBundle
{
    /// <summary>Instant the manifests under test are composed at.</summary>
    public static readonly DateTimeOffset ComposedAt = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

    private const string RoutingEvidenceJson = """{"schemaVersion":"0.1","routes":[{"source":"UDR","addressPrefix":"10.1.0.0/16","nextHopType":"VirtualNetworkGateway"}]}""";
    private const string TraceResultsJson = """{"schemaVersion":"0.1","source":"vnet-1/app","destination":"vnet-1/app","reachable":true,"paths":[]}""";

    private static readonly DateTimeOffset GraphGeneratedAt = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes the snapshot, findings, and report artifacts of an open-source run and composes the
    /// manifest that indexes them.
    /// </summary>
    /// <param name="directory">Bundle directory; created if it does not exist.</param>
    /// <returns>The composed manifest.</returns>
    public static async Task<AnalysisManifest> ComposeOssBundleAsync(string directory)
    {
        var snapshot = TestSnapshotFactory.Create();

        await WriteAnalysisArtifactsAsync(directory, snapshot, [CreateOssFinding()]).ConfigureAwait(false);

        return await ComposeAsync(directory, snapshot, AnalysisEdition.Oss, AnalysisSourceKind.OssCollection)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes and composes an open-source bundle from a caller-supplied snapshot, for the tests that vary the
    /// scope rather than the artifacts.
    /// </summary>
    /// <param name="directory">Bundle directory; created if it does not exist.</param>
    /// <param name="snapshot">Snapshot the bundle describes.</param>
    /// <returns>The composed manifest.</returns>
    public static async Task<AnalysisManifest> ComposeOssBundleAsync(string directory, NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await WriteAnalysisArtifactsAsync(directory, snapshot, [CreateOssFinding()]).ConfigureAwait(false);

        return await ComposeAsync(directory, snapshot, AnalysisEdition.Oss, AnalysisSourceKind.OssCollection)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a Pro bundle with graph, findings, routing evidence, trace results, and the consultant
    /// report, and composes the manifest that indexes them.
    /// </summary>
    /// <param name="directory">Bundle directory; created if it does not exist.</param>
    /// <param name="subscriptionName">
    /// Subscription name the snapshot declares, or <see langword="null"/> for a snapshot that names only
    /// the identifier.
    /// </param>
    /// <returns>The composed manifest.</returns>
    public static async Task<AnalysisManifest> ComposeProBundleAsync(
        string directory,
        string? subscriptionName = null)
    {
        var snapshot = TestSnapshotFactory.Create(
            collectionMetadata:
            [
                CreateMetadata("Resources"),
                CreateMetadata("EffectiveRoutes")
            ],
            subscriptionName: subscriptionName);

        await WriteAnalysisArtifactsAsync(directory, snapshot, [CreateOssFinding(), CreateProFinding()])
            .ConfigureAwait(false);

        var graph = new NetworkGraphBuilder().Build(snapshot, GraphGeneratedAt);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, AnalysisArtifactRoles.NetworkGraph),
            new NetworkGraphSerializer().Serialize(graph)).ConfigureAwait(false);

        await WriteTextAsync(directory, AnalysisArtifactRoles.RoutingEvidence, RoutingEvidenceJson).ConfigureAwait(false);
        await WriteTextAsync(directory, AnalysisArtifactRoles.TraceResults, TraceResultsJson).ConfigureAwait(false);
        await WriteTextAsync(directory, "pro-report.md", "# Pro consultant report").ConfigureAwait(false);

        return await ComposeAsync(directory, snapshot, AnalysisEdition.Pro, AnalysisSourceKind.ProCli)
            .ConfigureAwait(false);
    }

    /// <summary>Finding produced by the shared deterministic checks.</summary>
    public static Finding CreateOssFinding() => new(
        Code: "NET-901",
        Severity: "Warning",
        Title: "Subnet is not associated with a network security group",
        Description: "Subnet 'app' has no network security group association.",
        Confidence: "High",
        Evidence: [],
        Resources: ["vnet-1/app"]);

    /// <summary>Finding produced by premium analysis, so the merged set carries both origins.</summary>
    public static Finding CreateProFinding() => new(
        Code: "PRO-901",
        Severity: "Medium",
        Title: "Effective route bypasses the inspected path",
        Description: "10.1.0.0/16 reaches the gateway without traversing the inspected path.",
        Confidence: "Medium",
        Evidence: [],
        Resources: ["vnet-1/app"])
    {
        Origin = FindingOrigin.Pro,
        Source = "Trace"
    };

    private static Task<AnalysisManifest> ComposeAsync(
        string directory,
        NetworkSnapshot snapshot,
        string edition,
        string sourceKind)
        => new AnalysisManifestComposer(new FixedTimeProvider(ComposedAt))
            .ComposeAsync(directory, snapshot, edition, sourceKind, cancellationToken: CancellationToken.None);

    /// <summary>Writes the artifacts the shared reporting writer produces for both editions.</summary>
    private static Task WriteAnalysisArtifactsAsync(
        string directory,
        NetworkSnapshot snapshot,
        IReadOnlyList<Finding> findings)
        => new ReportWriter(new NetworkSnapshotSerializer())
            .WriteAsync(directory, snapshot, findings, CancellationToken.None);

    private static Task WriteTextAsync(string directory, string fileName, string content)
        => File.WriteAllBytesAsync(Path.Combine(directory, fileName), Utf8NoBom.GetBytes(content));

    private static CollectionMetadata CreateMetadata(string itemType) => new(
        ItemType: itemType,
        Source: "AzureResourceGraph",
        Timestamp: new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero),
        Permission: "Reader",
        Status: "Available",
        Completeness: 1d,
        Errors: []);
}
