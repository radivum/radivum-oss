using Radivum.Model;

namespace Radivum.Oss.Analysis;

/// <summary>
/// Maps snapshot collection metadata and produced artifacts onto the declared capability states of an
/// analysis bundle, so edition behaviour is derived rather than declared.
/// </summary>
public static class AnalysisCapabilityEvaluator
{
    /// <summary>
    /// Summarizes collection coverage from a snapshot's collection metadata.
    /// </summary>
    /// <param name="snapshot">Snapshot to summarize.</param>
    /// <returns>Coverage of the evidence sources the snapshot was collected from.</returns>
    public static AnalysisCollectionCoverage EvaluateCoverage(NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var resourceCount = CountResources(snapshot.Resources);
        var evidenceCount = snapshot.EffectiveEvidence.EffectiveRoutes.Count
            + snapshot.EffectiveEvidence.EffectiveNetworkSecurityGroups.Count
            + snapshot.EffectiveEvidence.NextHopResults.Count;

        var metadata = snapshot.CollectionMetadata;
        if (metadata.Count == 0)
        {
            return new AnalysisCollectionCoverage(
                0d,
                AnalysisCapabilityStatus.Unavailable,
                0,
                resourceCount,
                evidenceCount);
        }

        var overall = metadata.Average(static item => Math.Clamp(item.Completeness, 0d, 1d));

        // Aggregation is decision logic over collection status, not normalization of the status value
        // itself, so the comparison stays ordinal.
        var unavailable = metadata.Count(static item => !string.Equals(item.Status, "Available", StringComparison.Ordinal));

        var status = unavailable == 0
            ? AnalysisCapabilityStatus.Available
            : AnalysisCapabilityStatus.Limited;

        return new AnalysisCollectionCoverage(overall, status, unavailable, resourceCount, evidenceCount);
    }

    /// <summary>
    /// Evaluates the state of every capability an edition can declare.
    /// </summary>
    /// <param name="snapshot">Snapshot the bundle was produced from.</param>
    /// <param name="artifactRoles">Roles of the artifacts present in the bundle.</param>
    /// <param name="findingOrigins">Editions that contributed findings to the bundle.</param>
    /// <param name="edition">
    /// Edition that produced the bundle, one of <see cref="AnalysisEdition"/>. Premium capabilities
    /// cannot be available for a bundle the open-source edition produced, however complete its
    /// configuration evidence is.
    /// </param>
    /// <returns>One entry per capability, in canonical order.</returns>
    public static IReadOnlyList<AnalysisCapability> EvaluateCapabilities(
        NetworkSnapshot snapshot,
        IEnumerable<string> artifactRoles,
        IEnumerable<FindingOrigin> findingOrigins,
        string edition)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(artifactRoles);
        ArgumentNullException.ThrowIfNull(findingOrigins);
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);

        var roles = new HashSet<string>(artifactRoles, StringComparer.OrdinalIgnoreCase);
        var origins = findingOrigins.Distinct().ToList();
        var coverage = EvaluateCoverage(snapshot);
        var context = CheckContext.Create(snapshot.CollectionMetadata);
        var premium = IsPremiumEdition(edition);

        return
        [
            new AnalysisCapability(AnalysisCapabilityIds.Overview, AnalysisCapabilityStatus.Available, null),
            EvaluateInventory(coverage),
            EvaluateArtifactCapability(
                AnalysisCapabilityIds.Topology,
                roles,
                AnalysisArtifactRoles.NetworkGraph,
                AnalysisCapabilityStatus.Unavailable),
            EvaluateFindings(roles, origins, premium),
            EvaluateEvidenceCapability(
                AnalysisCapabilityIds.Routing,
                AnalysisArtifactRoles.RoutingEvidence,
                roles,
                context,
                "EffectiveRoutes",
                snapshot.Resources.RouteTables.Count > 0,
                premium),
            EvaluateEvidenceCapability(
                AnalysisCapabilityIds.Security,
                AnalysisArtifactRoles.SecurityEvidence,
                roles,
                context,
                "EffectiveNSGs",
                snapshot.Resources.NetworkSecurityGroups.Count > 0,
                premium),
            EvaluateArtifactCapability(
                AnalysisCapabilityIds.Trace,
                roles,
                AnalysisArtifactRoles.TraceResults,
                AnalysisCapabilityStatus.Unavailable)
        ];
    }

    private static AnalysisCapability EvaluateInventory(AnalysisCollectionCoverage coverage)
    {
        if (coverage.Status == AnalysisCapabilityStatus.Unavailable)
        {
            return new AnalysisCapability(
                AnalysisCapabilityIds.Inventory,
                AnalysisCapabilityStatus.Unavailable,
                "network snapshot carries no collection metadata");
        }

        var status = coverage.OverallCompleteness >= 1d
            ? AnalysisCapabilityStatus.Available
            : AnalysisCapabilityStatus.Limited;

        return new AnalysisCapability(
            AnalysisCapabilityIds.Inventory,
            status,
            FormattableString.Invariant($"collection coverage {coverage.OverallCompleteness * 100d:0}%"));
    }

    private static AnalysisCapability EvaluateArtifactCapability(
        string capabilityId,
        HashSet<string> roles,
        string requiredRole,
        string missingStatus)
        => roles.Contains(requiredRole)
            ? new AnalysisCapability(capabilityId, AnalysisCapabilityStatus.Available, null)
            : new AnalysisCapability(capabilityId, missingStatus, $"missing {requiredRole}");

    private static AnalysisCapability EvaluateFindings(HashSet<string> roles, List<FindingOrigin> origins, bool premium)
    {
        if (!roles.Contains(AnalysisArtifactRoles.Findings))
        {
            return new AnalysisCapability(
                AnalysisCapabilityIds.Findings,
                AnalysisCapabilityStatus.Unavailable,
                $"missing {AnalysisArtifactRoles.Findings}");
        }

        // With both editions contributing, the finding set covers everything either edition can detect.
        if (origins.Count > 1)
        {
            return new AnalysisCapability(AnalysisCapabilityIds.Findings, AnalysisCapabilityStatus.Available, null);
        }

        var reason = origins.Count == 0
            ? $"no findings recorded in {AnalysisArtifactRoles.Findings}"
            : origins.Contains(FindingOrigin.Oss) && premium
                ? "premium finding rules did not contribute findings"
                : $"findings produced by {Describe(origins)} only";

        return new AnalysisCapability(AnalysisCapabilityIds.Findings, AnalysisCapabilityStatus.Limited, reason);
    }

    private static AnalysisCapability EvaluateEvidenceCapability(
        string capabilityId,
        string artifactRole,
        HashSet<string> roles,
        CheckContext context,
        string collectionItemType,
        bool configurationPresent,
        bool premium)
    {
        // Reasoning over collected configuration is the premium edition's contribution, so an
        // open-source bundle can never declare an interpretation capability available.
        if (!premium)
        {
            return new AnalysisCapability(
                capabilityId,
                AnalysisCapabilityStatus.Unavailable,
                $"requires the {AnalysisEdition.Pro} edition");
        }

        var artifactPresent = roles.Contains(artifactRole);
        var effectiveCollected = context.IsAvailable(collectionItemType);

        if (artifactPresent && effectiveCollected)
        {
            return new AnalysisCapability(capabilityId, AnalysisCapabilityStatus.Available, null);
        }

        if (artifactPresent || configurationPresent)
        {
            var reason = artifactPresent
                ? $"effective configuration not collected ({collectionItemType})"
                : $"missing {artifactRole}";

            return new AnalysisCapability(capabilityId, AnalysisCapabilityStatus.Limited, reason);
        }

        return new AnalysisCapability(capabilityId, AnalysisCapabilityStatus.Unavailable, $"missing {artifactRole}");
    }

    private static string Describe(List<FindingOrigin> origins)
        => string.Join("+", origins.Order()).ToLowerInvariant();

    private static int CountResources(NetworkResourceSet resources)
        => resources.VNets.Count
            + resources.NetworkSecurityGroups.Count
            + resources.RouteTables.Count
            + resources.NetworkInterfaces.Count
            + resources.PublicIpAddresses.Count
            + resources.PrivateEndpoints.Count
            + resources.PrivateDnsZones.Count
            + resources.AzureFirewalls.Count
            + resources.VirtualHubs.Count
            + resources.VirtualWans.Count
            + resources.ExpressRouteCircuits.Count
            + resources.VirtualNetworkGateways.Count;

    private static bool IsPremiumEdition(string edition)
        => string.Equals(edition, AnalysisEdition.Pro, StringComparison.OrdinalIgnoreCase);
}
