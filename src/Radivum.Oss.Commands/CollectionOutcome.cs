using Radivum.Model;

namespace Radivum.Oss.Commands;

/// <summary>
/// Result of a collect or report run: the snapshot that was analyzed, the findings it produced, and
/// the manifest that indexes the artifacts written for it.
/// </summary>
/// <param name="Snapshot">Snapshot that was collected or loaded.</param>
/// <param name="Findings">Findings returned by the OSS check engine.</param>
/// <param name="Manifest">Manifest indexing the artifacts written to the output directory.</param>
public sealed record CollectionOutcome(
    NetworkSnapshot Snapshot,
    IReadOnlyList<Finding> Findings,
    AnalysisManifest Manifest);