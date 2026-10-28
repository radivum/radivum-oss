using Radivum.Model;

namespace Radivum.Oss.Commands;

/// <summary>
/// Writes the console summary shared by the collect and report commands.
/// </summary>
internal static class CollectionSummary
{
    private static readonly string[] SeverityOrder = ["High", "Medium", "Warning", "Info"];

    public static void Write(TextWriter output, string outputDirectory, CollectionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(outcome);

        var snapshot = outcome.Snapshot;
        var findings = outcome.Findings;
        var nonAvailableEntries = snapshot.CollectionMetadata
            .Where(metadata => !string.Equals(metadata.Status, "Available", StringComparison.OrdinalIgnoreCase))
            .ToList();

        output.WriteLine();
        output.WriteLine(
            $"Inventory: {snapshot.Resources.VNets.Count} VNets, {snapshot.Resources.NetworkSecurityGroups.Count} NSGs, {snapshot.Resources.RouteTables.Count} route tables, {snapshot.Resources.PrivateEndpoints.Count} private endpoints, {snapshot.Resources.ExpressRouteCircuits.Count} ExpressRoute circuits.");
        WriteLinkedResourceSummary(output, snapshot);
        output.WriteLine(nonAvailableEntries.Count == 0
            ? "Collection status: complete"
            : "Collection status: partial");
        WriteCollectionCoverage(output, snapshot);

        if (nonAvailableEntries.Count > 0)
        {
            WriteCollectionWarnings(output, nonAvailableEntries);
        }

        output.WriteLine($"Findings: {findings.Count} ({Summary(findings)})");
        output.WriteLine($"Report:   {Path.GetFullPath(Path.Combine(outputDirectory, "network-report.md"))}");
        output.WriteLine($"Snapshot: {Path.GetFullPath(Path.Combine(outputDirectory, AnalysisArtifactRoles.NetworkSnapshot))}");
        output.WriteLine($"Graph:    {Path.GetFullPath(Path.Combine(outputDirectory, AnalysisArtifactRoles.NetworkGraph))}");
        output.WriteLine($"Topology: {Path.GetFullPath(Path.Combine(outputDirectory, AnalysisRenderingNames.TopologyDiagram))}");
        output.WriteLine($"Manifest: {Path.GetFullPath(Path.Combine(outputDirectory, AnalysisArtifactRoles.Manifest))}");

        if (nonAvailableEntries.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Next actions:");
            output.WriteLine("- Review 'Collection Coverage' in network-report.md for missing capabilities and reasons.");
            output.WriteLine("- Confirm Reader access at the requested scope, then repeat the collect command.");
        }
    }

    /// <summary>
    /// Reports the declared links and how much of each target could be confirmed. One line, because the links
    /// are context for the inventory above rather than a second inventory.
    /// </summary>
    private static void WriteLinkedResourceSummary(TextWriter output, NetworkSnapshot snapshot)
    {
        var links = snapshot.Resources.LinkedResources;

        if (links is null || links.Count == 0)
        {
            return;
        }

        var resolved = links.Count(link => link.Resolution == LinkedResourceInfo.ResolutionResolved);
        var derived = links.Count(link => link.Resolution == LinkedResourceInfo.ResolutionDerived);
        var unresolved = links.Count - resolved - derived;

        output.WriteLine(
            $"Links:    {links.Count} related resources ({resolved} resolved, {derived} named from id, {unresolved} not confirmed).");
    }

    private static void WriteCollectionCoverage(TextWriter output, NetworkSnapshot snapshot)
    {
        if (snapshot.CollectionMetadata.Count == 0)
        {
            return;
        }

        var normalizedCompleteness = snapshot.CollectionMetadata
            .Select(metadata => Math.Clamp(metadata.Completeness, 0d, 1d))
            .DefaultIfEmpty(0d)
            .Average();
        var coveragePercentage = (int)Math.Round(normalizedCompleteness * 100, MidpointRounding.AwayFromZero);

        output.WriteLine($"Collection coverage: {coveragePercentage}%");
        WriteCapabilityStatus(output, snapshot.CollectionMetadata, "Resources", "Configuration analysis");
        WriteCapabilityStatus(output, snapshot.CollectionMetadata, "EffectiveRoutes", "Effective routing");
        WriteCapabilityStatus(output, snapshot.CollectionMetadata, "EffectiveNSGs", "Effective NSG analysis");
    }

    private static void WriteCapabilityStatus(
        TextWriter output,
        IReadOnlyList<CollectionMetadata> metadata,
        string itemType,
        string label)
    {
        var entry = metadata.FirstOrDefault(value =>
            string.Equals(value.ItemType, itemType, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            output.WriteLine($"{label}: unavailable");
            output.WriteLine("Reason: no collection metadata entry available.");
            return;
        }

        var statusText = entry.Status switch
        {
            "Available" => "available",
            "Partial" => "partially available",
            _ => "unavailable"
        };

        output.WriteLine($"{label}: {statusText}");
        if (!string.Equals(entry.Status, "Available", StringComparison.OrdinalIgnoreCase) && entry.Errors.Count > 0)
        {
            output.WriteLine($"Reason: {entry.Errors[0]}");
        }
    }

    private static void WriteCollectionWarnings(TextWriter output, IReadOnlyList<CollectionMetadata> nonAvailableEntries)
    {
        output.WriteLine("Collection warnings:");
        foreach (var entry in nonAvailableEntries.Take(5))
        {
            var reason = entry.Errors.Count > 0
                ? $" — {entry.Errors[0]}"
                : string.Empty;
            output.WriteLine($"- {entry.ItemType}: {entry.Status}{reason}");
        }

        if (nonAvailableEntries.Count > 5)
        {
            output.WriteLine($"- ... {nonAvailableEntries.Count - 5} additional warning entries.");
        }
    }

    private static string Summary(IReadOnlyList<Finding> findings)
    {
        var parts = new List<string>();
        foreach (var severity in SeverityOrder)
        {
            var count = findings.Count(finding => finding.Severity == severity);
            if (count > 0)
            {
                parts.Add($"{count} {severity}");
            }
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }
}