namespace Radivum.Model;

/// <summary>
/// Collection coverage summarized from <see cref="NetworkSnapshot.CollectionMetadata"/>.
/// </summary>
/// <param name="OverallCompleteness">Mean completeness across evidence sources, clamped to the range 0..1.</param>
/// <param name="Status">One of <see cref="AnalysisCapabilityStatus"/>.</param>
/// <param name="UnavailableSourceCount">Number of evidence sources that are not fully available.</param>
/// <param name="SnapshotResourceCount">Number of resources the snapshot carries at its top level.</param>
/// <param name="EffectiveEvidenceCount">Number of effective-route, effective-NSG, and next-hop evidence entries.</param>
public sealed record AnalysisCollectionCoverage(
    double OverallCompleteness,
    string Status,
    int UnavailableSourceCount,
    int SnapshotResourceCount = 0,
    int EffectiveEvidenceCount = 0);
