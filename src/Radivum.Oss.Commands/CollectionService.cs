using Radivum.Oss.Analysis;
using Radivum.Azure;
using Radivum.Graph;
using Radivum.Model;
using Radivum.Oss.Reporting;

namespace Radivum.Oss.Commands;

internal sealed class CollectionService(
    ISnapshotCollector snapshotCollector,
    INetworkSnapshotSerializer snapshotSerializer,
    ICheckEngine checkEngine,
    IReportWriter reportWriter,
    IGraphGenerationService graphGenerationService,
    IGraphArtifactWriter graphArtifactWriter,
    IDiagramRenderer diagramRenderer,
    IArtifactWriter artifactWriter,
    IAnalysisManifestComposer manifestComposer) : ICollectionService
{
    /// <inheritdoc />
    public Task<CollectionOutcome> CollectAsync(
        string scope,
        IReadOnlyList<string> subscriptionIds,
        string? resourceGroup,
        string outputDirectory,
        bool resolveLinkedResources = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(subscriptionIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (subscriptionIds.Count == 0)
        {
            throw new ArgumentException("At least one subscription id is required.", nameof(subscriptionIds));
        }

        // The request keeps the first id in its singular member so a single-subscription request reads exactly as it
        // always did, and carries the whole selection for the collector to validate and read.
        var request = new SnapshotCollectionRequest(
            scope,
            subscriptionIds[0],
            resourceGroup,
            resolveLinkedResources,
            subscriptionIds);
        return CollectAsync(request, outputDirectory, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CollectionOutcome> CollectAsync(
        string scope,
        string subscriptionId,
        string? resourceGroup,
        string outputDirectory,
        bool resolveLinkedResources = false,
        CancellationToken cancellationToken = default)
        => CollectAsync(
            scope,
            [subscriptionId],
            resourceGroup,
            outputDirectory,
            resolveLinkedResources,
            cancellationToken);

    private async Task<CollectionOutcome> CollectAsync(
        SnapshotCollectionRequest request,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshotCollector.CollectAsync(request, cancellationToken).ConfigureAwait(false);

        return await WriteAnalysisAsync(
            snapshot,
            outputDirectory,
            AnalysisSourceKind.OssCollection,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CollectionOutcome> ReportAsync(
        string snapshotPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        // The stream is closed before writing, because the writer emits its own snapshot artifact and
        // an output directory that contains the input file would otherwise fail with a sharing violation.
        NetworkSnapshot snapshot;
        await using (var stream = File.OpenRead(snapshotPath))
        {
            snapshot = await snapshotSerializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        return await WriteAnalysisAsync(
            snapshot,
            outputDirectory,
            AnalysisSourceKind.LocalFile,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CollectionOutcome> WriteAnalysisAsync(
        NetworkSnapshot snapshot,
        string outputDirectory,
        string sourceKind,
        CancellationToken cancellationToken)
    {
        var findings = checkEngine.Check(snapshot);
        await reportWriter.WriteAsync(outputDirectory, snapshot, findings, cancellationToken).ConfigureAwait(false);

        // The graph and its topology are bundle outputs, not an optional export: the manifest can only declare
        // topology available when the artifact it indexes already exists on disk.
        var graph = graphGenerationService.Generate(snapshot);
        await graphArtifactWriter
            .WriteAsync(graph, Path.Combine(outputDirectory, AnalysisArtifactRoles.NetworkGraph), cancellationToken)
            .ConfigureAwait(false);
        await artifactWriter
            .WriteTextAsync(
                Path.Combine(outputDirectory, AnalysisRenderingNames.TopologyDiagram),
                diagramRenderer.RenderMarkdown(graph),
                cancellationToken)
            .ConfigureAwait(false);

        var manifest = await manifestComposer.ComposeAsync(
            outputDirectory,
            snapshot,
            AnalysisEdition.Oss,
            sourceKind,
            degradation: null,
            cancellationToken).ConfigureAwait(false);

        return new CollectionOutcome(snapshot, findings, manifest);
    }
}