namespace Radivum.Model;

/// <summary>
/// Records that one stage of a multi-stage analysis degraded the bundle without preventing it being written.
/// </summary>
/// <remarks>
/// The marker travels in the manifest rather than being inferred from the capability states, so a reader can
/// tell "this analysis is incomplete, and here is why" apart from "this analysis never claimed that
/// capability". It names the stage and repeats the producer's own message, so a consumer never has to invent a
/// reason the producer did not give. It is additive and optional: a manifest that omits it describes a run in
/// which every stage completed, which is why the manifest schema version is unchanged.
/// </remarks>
/// <param name="Stage">Name of the stage that degraded, for example "premium analysis".</param>
/// <param name="Message">Human-readable description of the failure, as the producer reported it.</param>
public sealed record AnalysisDegradation(string Stage, string Message);
