using System.Text.Json.Serialization;

namespace Radivum.Model;

/// <summary>
/// Index of one analysis: what was analyzed, which artifacts carry the result, and which capabilities
/// the bundle can serve. The manifest indexes the artifacts instead of embedding them, so findings and
/// graph content have exactly one home. It is written last, which makes its presence the completion
/// signal of a run.
/// </summary>
/// <param name="AnalysisId">
/// Stable identifier of the analysis, derived from the scope, the collection time, and the tool
/// version, so identical inputs produce the same identifier.
/// </param>
/// <param name="SchemaVersion">Version of this contract, one of <see cref="AnalysisManifestSchema"/>.</param>
/// <param name="ToolVersion">Version of the tool that produced the bundle.</param>
/// <param name="Edition">Edition that produced the bundle, one of <see cref="AnalysisEdition"/>.</param>
/// <param name="GeneratedAt">
/// Time the manifest was written. Deliberately excluded from the analysis identifier so re-running the
/// same analysis keeps the same identity.
/// </param>
/// <param name="Metadata">Scope and provenance of the analyzed snapshot.</param>
/// <param name="Artifacts">Artifacts the bundle carries, in canonical order.</param>
/// <param name="Capabilities">Declared state of every capability, in canonical order.</param>
/// <param name="Coverage">Collection coverage the capability states are derived from.</param>
/// <param name="Degradation">
/// Stage that degraded the analysis without stopping the bundle being written, or <see langword="null"/> when
/// every stage completed. It is optional and additive, so a manifest that omits it describes a complete run and
/// a reader that does not know the member ignores it (manifest schema 0.1 is unchanged).
/// </param>
/// <param name="SubscriptionCoverage">
/// Collection coverage of each subscription a multi-subscription analysis covers, or <see langword="null"/> for a
/// single-subscription analysis. Optional and additive for the same reason as the degradation marker: a manifest
/// that omits it keeps the bytes it had, and a reader that does not know the member ignores it.
/// </param>
public sealed record AnalysisManifest(
    string AnalysisId,
    string SchemaVersion,
    string ToolVersion,
    string Edition,
    DateTimeOffset GeneratedAt,
    AnalysisMetadata Metadata,
    IReadOnlyList<AnalysisArtifactReference> Artifacts,
    IReadOnlyList<AnalysisCapability> Capabilities,
    AnalysisCollectionCoverage? Coverage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AnalysisDegradation? Degradation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AnalysisSubscriptionCoverage>? SubscriptionCoverage = null);

/// <summary>
/// Collection coverage of one subscription of a multi-subscription analysis.
/// </summary>
/// <remarks>
/// A reader needs this to say which scope is incomplete rather than only that something was: the overall coverage
/// states that a run was partial, and this states which subscription it was partial for. It mirrors the
/// per-subscription row the snapshot carries, so the browser never has to parse the snapshot artifact.
/// </remarks>
/// <param name="SubscriptionId">Subscription the row describes.</param>
/// <param name="Status">One of <c>Available</c>, <c>Partial</c>, or <c>Unavailable</c>.</param>
/// <param name="Completeness">Estimated share of that subscription's evidence that was read, in the range 0..1.</param>
/// <param name="Errors">Why the scope is not fully available.</param>
public sealed record AnalysisSubscriptionCoverage(
    string SubscriptionId,
    string Status,
    double Completeness,
    IReadOnlyList<string> Errors);
