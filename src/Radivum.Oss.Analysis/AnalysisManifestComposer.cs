namespace Radivum.Oss.Analysis;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Radivum.Model;

/// <summary>
/// Builds the manifest of an analysis bundle from the artifacts already present in its directory.
/// </summary>
/// <param name="timeProvider">Clock used for the manifest timestamp, so runs stay reproducible under test.</param>
public sealed class AnalysisManifestComposer(TimeProvider timeProvider) : IAnalysisManifestComposer
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // The manifest itself is excluded: it is the index, not an artifact of the analysis. Reports are
    // excluded too: section 8.3 makes them renderings of the bundle rather than part of it.
    private static readonly string[] IndexedRoles =
    [
        AnalysisArtifactRoles.NetworkSnapshot,
        AnalysisArtifactRoles.NetworkGraph,
        AnalysisArtifactRoles.Findings,
        AnalysisArtifactRoles.RoutingEvidence,
        AnalysisArtifactRoles.SecurityEvidence,
        AnalysisArtifactRoles.TraceResults
    ];

    /// <inheritdoc />
    public async Task<AnalysisManifest> ComposeAsync(
        string bundleDirectory,
        NetworkSnapshot snapshot,
        string edition,
        string sourceKind,
        AnalysisDegradation? degradation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);

        var directory = Path.GetFullPath(bundleDirectory);
        var artifacts = await ReadArtifactsAsync(directory, cancellationToken).ConfigureAwait(false);
        var origins = await ReadFindingOriginsAsync(directory, cancellationToken).ConfigureAwait(false);
        var roles = artifacts.Select(static artifact => artifact.Role).ToList();
        var isMultiSubscription = snapshot.Scope.SubscriptionIds.Count > 1;

        var manifest = new AnalysisManifest(
            CreateAnalysisId(snapshot),
            AnalysisManifestSchema.Version,
            snapshot.ToolVersion,
            edition,
            timeProvider.GetUtcNow(),
            new AnalysisMetadata(
                snapshot.Scope.ScopeDescription,
                snapshot.Scope.SubscriptionId,
                snapshot.CollectedAt,
                sourceKind,
                snapshot.Scope.SubscriptionName,
                isMultiSubscription ? snapshot.Scope.SubscriptionIds : null,
                isMultiSubscription ? snapshot.Scope.Subscriptions : null),
            artifacts,
            AnalysisCapabilityEvaluator.EvaluateCapabilities(snapshot, roles, origins, edition),
            AnalysisCapabilityEvaluator.EvaluateCoverage(snapshot),
            degradation,
            ProjectSubscriptionCoverage(snapshot));

        // Written last, so the presence of the manifest means the run completed (DEC-06).
        var manifestPath = Path.Combine(directory, AnalysisArtifactRoles.Manifest);
        await WriteAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);

        return manifest;
    }

    /// <summary>
    /// Projects the snapshot's per-subscription coverage rows into the manifest.
    /// </summary>
    /// <remarks>
    /// A single-subscription snapshot carries no such rows, so the member stays absent and an existing manifest
    /// keeps the bytes it had. The rows are ordered by identifier, so two runs over one scope produce one manifest.
    /// </remarks>
    /// <param name="snapshot">Snapshot the manifest is composed for.</param>
    /// <returns>One entry per scoped subscription, or <see langword="null"/> when the snapshot declares none.</returns>
    private static IReadOnlyList<AnalysisSubscriptionCoverage>? ProjectSubscriptionCoverage(NetworkSnapshot snapshot)
    {
        var coverage = snapshot.CollectionMetadata
            .Where(static item => item.ScopeId is { Length: > 0 })
            .OrderBy(static item => item.ScopeId, StringComparer.Ordinal)
            .Select(static item => new AnalysisSubscriptionCoverage(
                item.ScopeId!,
                item.Status,
                item.Completeness,
                item.Errors))
            .ToList();

        return coverage.Count > 0 ? coverage : null;
    }

    /// <summary>
    /// Derives the stable identifier of an analysis from its inputs. <see cref="AnalysisManifest.GeneratedAt"/>
    /// is deliberately not an input, so re-running the same analysis keeps the same identity.
    /// </summary>
    /// <remarks>
    /// A single-subscription analysis hashes exactly the inputs it always hashed, so its identifier does not change
    /// with this feature. A multi-subscription analysis appends its ordered subscription set, because its scope
    /// description is composed text and the set is what actually distinguishes it.
    /// </remarks>
    private static string CreateAnalysisId(NetworkSnapshot snapshot)
    {
        var key = new StringBuilder()
            .Append(snapshot.Scope.ScopeDescription).Append('\u001f')
            .Append(snapshot.Scope.SubscriptionId).Append('\u001f')
            .Append(snapshot.CollectedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Append('\u001f')
            .Append(snapshot.ToolVersion);

        if (snapshot.Scope.SubscriptionIds.Count > 1)
        {
            // Ordered and joined, so the identity follows the set rather than the order the caller happened to
            // write it in.
            key.Append('\u001f').Append(string.Join(
                '\u001e',
                snapshot.Scope.SubscriptionIds.OrderBy(static id => id, StringComparer.Ordinal)));
        }

        return "sha256-" + Convert.ToHexStringLower(SHA256.HashData(Utf8NoBom.GetBytes(key.ToString())));
    }

    private static async Task<IReadOnlyList<AnalysisArtifactReference>> ReadArtifactsAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<AnalysisArtifactReference>();

        foreach (var role in IndexedRoles)
        {
            var path = Path.Combine(directory, role);
            if (!File.Exists(path))
            {
                continue;
            }

            var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            artifacts.Add(new AnalysisArtifactReference(
                role,
                Path.GetFileName(path),
                Convert.ToHexStringLower(SHA256.HashData(content)),
                content.LongLength));
        }

        return artifacts;
    }

    private static async Task<IReadOnlyList<FindingOrigin>> ReadFindingOriginsAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, AnalysisArtifactRoles.Findings);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        var findings = await JsonSerializer
            .DeserializeAsync(stream, NetworkSnapshotJsonContext.Default.ListFinding, cancellationToken)
            .ConfigureAwait(false);

        return findings is null
            ? []
            : findings.Select(static finding => finding.Origin).Distinct().ToList();
    }

    private static async Task WriteAsync(string path, AnalysisManifest manifest, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer
            .SerializeAsync(stream, manifest, NetworkSnapshotJsonContext.Default.AnalysisManifest, cancellationToken)
            .ConfigureAwait(false);
    }
}
