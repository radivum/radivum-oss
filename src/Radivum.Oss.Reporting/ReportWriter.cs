using System.Text;
using System.Text.Json;
using Radivum.Model;

namespace Radivum.Oss.Reporting;

/// <summary>
/// Writes `network-snapshot.json`, `findings.json`, and `network-report.md` in the local output directory.
/// </summary>
public sealed class ReportWriter(INetworkSnapshotSerializer snapshotSerializer) : IReportWriter
{
    /// <inheritdoc />
    public async Task WriteAsync(
        string outputDirectory,
        NetworkSnapshot snapshot,
        IReadOnlyList<Finding> findings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(findings);

        Directory.CreateDirectory(outputDirectory);

        var snapshotPath = Path.Combine(outputDirectory, AnalysisArtifactRoles.NetworkSnapshot);
        await using (var snapshotStream = File.Create(snapshotPath))
        {
            await snapshotSerializer.SerializeAsync(snapshotStream, snapshot, cancellationToken).ConfigureAwait(false);
        }

        // The findings artifact carries the machine-readable result of the analysis; the report renders it.
        var findingsArtifact = findings as List<Finding> ?? [.. findings];
        var findingsPath = Path.Combine(outputDirectory, AnalysisArtifactRoles.Findings);
        await using (var findingsStream = File.Create(findingsPath))
        {
            await JsonSerializer
                .SerializeAsync(findingsStream, findingsArtifact, NetworkSnapshotJsonContext.Default.ListFinding, cancellationToken)
                .ConfigureAwait(false);
        }

        var report = BuildReport(snapshot, findings);
        var reportPath = Path.Combine(outputDirectory, "network-report.md");
        await File.WriteAllTextAsync(reportPath, report, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildReport(NetworkSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Radivum — Network Report");
        sb.AppendLine();
        sb.AppendLine($"- **Schema Version:** {snapshot.SchemaVersion}");
        sb.AppendLine($"- **Scope:** {snapshot.Scope.ScopeDescription}");
        sb.AppendLine($"- **Tenant:** {snapshot.TenantId}");
        sb.AppendLine($"- **Collected (UTC):** {snapshot.CollectedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        AppendInventorySummary(sb, snapshot);

        AppendCollectionCoverage(sb, snapshot.CollectionMetadata);

        sb.AppendLine("## Inventory");
        sb.AppendLine();
        sb.AppendLine("| Type | Count |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Virtual networks | {snapshot.Resources.VNets.Count} |");
        sb.AppendLine($"| Subnets | {snapshot.Resources.VNets.Sum(vnet => vnet.Subnets.Count)} |");
        sb.AppendLine($"| Peerings | {snapshot.Resources.VNets.Sum(vnet => vnet.Peerings.Count)} |");
        sb.AppendLine($"| Network security groups | {snapshot.Resources.NetworkSecurityGroups.Count} |");
        sb.AppendLine($"| Route tables | {snapshot.Resources.RouteTables.Count} |");
        sb.AppendLine($"| Network interfaces | {snapshot.Resources.NetworkInterfaces.Count} |");
        sb.AppendLine($"| Public IPs | {snapshot.Resources.PublicIpAddresses.Count} |");
        sb.AppendLine($"| Private endpoints | {snapshot.Resources.PrivateEndpoints.Count} |");
        sb.AppendLine($"| Private DNS zones | {snapshot.Resources.PrivateDnsZones.Count} |");
        sb.AppendLine($"| Azure Firewalls | {snapshot.Resources.AzureFirewalls.Count} |");
        sb.AppendLine($"| Virtual hubs (vWAN) | {snapshot.Resources.VirtualHubs.Count} |");
        sb.AppendLine($"| Virtual WANs | {snapshot.Resources.VirtualWans.Count} |");
        sb.AppendLine($"| ExpressRoute circuits | {snapshot.Resources.ExpressRouteCircuits.Count} |");
        sb.AppendLine($"| Virtual network gateways | {snapshot.Resources.VirtualNetworkGateways.Count} |");
        sb.AppendLine();

        sb.AppendLine("## Findings");
        sb.AppendLine();

        if (findings.Count == 0)
        {
            sb.AppendLine("No findings — configured Level-1 checks passed in this scope.");
            sb.AppendLine();
        }
        else
        {
            foreach (var severity in new[] { "High", "Medium", "Warning", "Info" })
            {
                var bucket = findings.Where(finding => finding.Severity == severity).ToList();
                if (bucket.Count == 0)
                {
                    continue;
                }

                sb.AppendLine($"### {severity}");
                sb.AppendLine();
                foreach (var finding in bucket)
                {
                    sb.AppendLine($"**{finding.Code} — {finding.Title}**");
                    sb.AppendLine();
                    sb.AppendLine(finding.Description);
                    sb.AppendLine();
                    sb.AppendLine($"Confidence: **{finding.Confidence}**");
                    sb.AppendLine($"Evidence level: **{finding.EvidenceLevel}**");
                    sb.AppendLine($"Source: **{finding.Source}**");

                    if (finding.AffectedResourceIds.Count > 0)
                    {
                        sb.AppendLine($"Affected resource IDs: **{finding.AffectedResourceIds.Count}**");
                    }

                    if (finding.AffectedGraphNodeIds.Count > 0)
                    {
                        sb.AppendLine($"Affected graph node IDs: **{finding.AffectedGraphNodeIds.Count}**");
                    }

                    sb.AppendLine();
                    foreach (var resource in finding.Resources)
                    {
                        sb.AppendLine($"- {Escape(resource)}");
                    }

                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Generated by radivum-oss (read-only local agent using the operator's identity)." );

        return sb.ToString();
    }

    private static void AppendCollectionCoverage(StringBuilder sb, IReadOnlyList<CollectionMetadata> metadata)
    {
        if (metadata.Count == 0)
        {
            return;
        }

        var coverage = metadata
            .Select(item => Math.Clamp(item.Completeness, 0d, 1d))
            .DefaultIfEmpty(0d)
            .Average();

        sb.AppendLine("## Collection Coverage");
        sb.AppendLine();
        sb.AppendLine($"- **Collection coverage:** {(int)Math.Round(coverage * 100, MidpointRounding.AwayFromZero)}%");
        AppendCapabilityLine(sb, metadata, "Resources", "Configuration analysis");
        AppendCapabilityLine(sb, metadata, "EffectiveRoutes", "Effective routing");
        AppendCapabilityLine(sb, metadata, "EffectiveNSGs", "Effective NSG analysis");
        sb.AppendLine();

        sb.AppendLine("### Source Status");
        sb.AppendLine();
        sb.AppendLine("| Source | Item Type | Status | Completeness | Permission |");
        sb.AppendLine("|---|---|---|---:|---|");
        foreach (var entry in metadata
                     .OrderBy(item => item.Source, StringComparer.Ordinal)
                     .ThenBy(item => item.ItemType, StringComparer.Ordinal))
        {
            sb.AppendLine($"| {Escape(entry.Source)} | {Escape(DescribeItemType(entry))} | {Escape(entry.Status)} | {(int)Math.Round(Math.Clamp(entry.Completeness, 0d, 1d) * 100, MidpointRounding.AwayFromZero)}% | {Escape(entry.Permission)} |");
        }

        var unavailableSources = metadata
            .Where(entry => !string.Equals(entry.Status, "Available", StringComparison.OrdinalIgnoreCase))
            .ToList();

        sb.AppendLine();
        sb.AppendLine("### Unavailable or Partial Evidence Sources");
        sb.AppendLine();

        if (unavailableSources.Count == 0)
        {
            sb.AppendLine("All modeled evidence sources are available.");
        }
        else
        {
            foreach (var entry in unavailableSources)
            {
                sb.AppendLine($"- **{Escape(DescribeItemType(entry))}** ({Escape(entry.Status)}, {Escape(entry.Source)})");
                if (entry.Errors.Count == 0)
                {
                    continue;
                }

                foreach (var error in entry.Errors.Distinct(StringComparer.Ordinal))
                {
                    sb.AppendLine($"  - {Escape(error)}");
                }
            }
        }

        var collectionErrors = metadata
            .SelectMany(entry => entry.Errors)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        sb.AppendLine();
        sb.AppendLine("### Encountered Collection Errors");
        sb.AppendLine();

        if (collectionErrors.Count == 0)
        {
            sb.AppendLine("No collection errors were reported.");
        }
        else
        {
            foreach (var error in collectionErrors)
            {
                sb.AppendLine($"- {Escape(error)}");
            }
        }

        sb.AppendLine();
    }

    private static void AppendInventorySummary(StringBuilder sb, NetworkSnapshot snapshot)
    {
        sb.AppendLine("## Inventory Summary");
        sb.AppendLine();
        sb.AppendLine($"- **VNets:** {snapshot.Resources.VNets.Count}");
        sb.AppendLine($"- **Subnets:** {snapshot.Resources.VNets.Sum(vnet => vnet.Subnets.Count)}");
        sb.AppendLine($"- **NSGs:** {snapshot.Resources.NetworkSecurityGroups.Count}");
        sb.AppendLine($"- **Route tables:** {snapshot.Resources.RouteTables.Count}");
        sb.AppendLine($"- **Private endpoints:** {snapshot.Resources.PrivateEndpoints.Count}");
        sb.AppendLine($"- **ExpressRoute circuits:** {snapshot.Resources.ExpressRouteCircuits.Count}");
        sb.AppendLine();
    }

    private static void AppendCapabilityLine(
        StringBuilder sb,
        IReadOnlyList<CollectionMetadata> metadata,
        string itemType,
        string label)
    {
        var entry = metadata.FirstOrDefault(value =>
            string.Equals(value.ItemType, itemType, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            sb.AppendLine($"- **{label}:** unavailable (no metadata)");
            return;
        }

        var statusText = entry.Status switch
        {
            "Available" => "available",
            "Partial" => "partially available",
            _ => "unavailable"
        };

        var reason = !string.Equals(entry.Status, "Available", StringComparison.OrdinalIgnoreCase) && entry.Errors.Count > 0
            ? $" — {entry.Errors[0]}"
            : string.Empty;

        sb.AppendLine($"- **{label}:** {statusText}{reason}");
    }

    /// <summary>
    /// Names a metadata row's source, including the scope it describes when one row covers one scope.
    /// </summary>
    /// <remarks>
    /// A multi-subscription collection writes one row per subscription, so the rows would otherwise be
    /// indistinguishable in the table. The scope id is present only on those rows, which keeps a single-subscription
    /// report byte-for-byte what it was.
    /// </remarks>
    private static string DescribeItemType(CollectionMetadata entry)
        => entry.ScopeId is null ? entry.ItemType : $"{entry.ItemType} ({entry.ScopeId})";

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
