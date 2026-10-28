namespace Radivum.Oss.Analysis;

using Radivum.Model;

/// <summary>
/// Indexes a directory of analysis artifacts as one portable, self-describing bundle by writing its
/// manifest last.
/// </summary>
public interface IAnalysisManifestComposer
{
    /// <summary>
    /// Indexes the artifacts currently present in a bundle directory into <c>analysis-manifest.json</c>,
    /// so a consumer can discover which capabilities the bundle can serve without reading its content.
    /// </summary>
    /// <param name="bundleDirectory">Directory holding the artifacts of a single analysis.</param>
    /// <param name="snapshot">Snapshot the bundle was produced from.</param>
    /// <param name="edition">Edition producing the bundle, one of <see cref="AnalysisEdition"/>.</param>
    /// <param name="sourceKind">Transport the analysis came from, one of <see cref="AnalysisSourceKind"/>.</param>
    /// <param name="degradation">
    /// Stage that degraded the bundle, or <see langword="null"/> when every stage completed. It is recorded in
    /// the manifest rather than left to be inferred from the artifacts, so a reader can tell an incomplete
    /// analysis apart from one that never claimed the capability.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The manifest that was written.</returns>
    Task<AnalysisManifest> ComposeAsync(
        string bundleDirectory,
        NetworkSnapshot snapshot,
        string edition,
        string sourceKind,
        AnalysisDegradation? degradation = null,
        CancellationToken cancellationToken = default);
}
