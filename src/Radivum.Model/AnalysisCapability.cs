namespace Radivum.Model;

/// <summary>
/// Declared state of one analysis capability.
/// </summary>
/// <param name="Id">Capability identifier, one of <see cref="AnalysisCapabilityIds"/>.</param>
/// <param name="Status">Declared state, one of <see cref="AnalysisCapabilityStatus"/>.</param>
/// <param name="Detail">Optional machine-readable explanation, required when the state is not available.</param>
public sealed record AnalysisCapability(string Id, string Status, string? Detail = null);
