namespace Radivum.Oss.Commands;

/// <summary>
/// Signals that a stage of a multi-stage analysis command failed after the earlier stages had already
/// written their output, so the partial result must be preserved instead of discarded. The shared
/// dispatcher maps it to <see cref="CliExitCodes.AnalysisStageFailed"/>.
/// </summary>
/// <remarks>
/// This is commodity command orchestration rather than product intelligence: it carries the name of the
/// stage that failed and nothing about what the stage computed, so it says nothing about how any
/// host's analysis works. A host that runs staged work can raise it; a host that does not never will.
///
/// A staged run that degraded still wrote a result, and that result travels in <see cref="Outcome"/> so a
/// caller that can present a partial result does not have to discard one. The contract stays edition
/// neutral: what it carries is the shared collection result, not anything a particular edition computed.
/// </remarks>
/// <param name="stage">Name of the failed stage, for example "premium analysis".</param>
/// <param name="message">Human-readable description of the failure.</param>
/// <param name="innerException">Underlying exception, if any.</param>
public sealed class AnalysisStageFailedException(string stage, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>The stage that failed.</summary>
    public string Stage { get; } = stage;

    /// <summary>
    /// The result the failed run had already written, or <see langword="null"/> when there is nothing usable
    /// to hand on.
    /// </summary>
    /// <remarks>
    /// Set by a producer that knows the earlier stages wrote a complete, readable bundle. A caller that only
    /// reports the failure ignores it; a caller that can serve the partial result - a connected host - publishes
    /// it instead of discarding work the operator can still read.
    /// </remarks>
    public CollectionOutcome? Outcome { get; init; }
}
