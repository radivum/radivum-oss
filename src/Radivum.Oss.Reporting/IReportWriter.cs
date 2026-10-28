using Radivum.Model;

namespace Radivum.Oss.Reporting;

/// <summary>
/// Writes the snapshot and Markdown report produced by the open/shared analysis pipeline.
/// </summary>
public interface IReportWriter
{
    /// <summary>
    /// Writes network snapshot and Markdown report into the output directory.
    /// </summary>
    /// <param name="outputDirectory">Output directory path.</param>
    /// <param name="snapshot">Collected network snapshot.</param>
    /// <param name="findings">Deterministic findings computed from the snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(
        string outputDirectory,
        NetworkSnapshot snapshot,
        IReadOnlyList<Finding> findings,
        CancellationToken cancellationToken = default);
}
