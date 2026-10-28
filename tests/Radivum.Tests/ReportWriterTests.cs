using System.Text.Json;
using Radivum.Model;
using Radivum.Oss.Reporting;
using Xunit;

namespace Radivum.Tests;

public sealed class ReportWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesCoverageAndCollectionErrorsSections()
    {
        var serializer = new NetworkSnapshotSerializer();
        var writer = new ReportWriter(serializer);
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"radivum-tests-{Guid.NewGuid():N}");

        var metadata = new List<CollectionMetadata>
        {
            new(
                ItemType: "Resources",
                Source: "AzureResourceGraph",
                Timestamp: DateTimeOffset.UtcNow,
                Permission: "Reader",
                Status: "Partial",
                Completeness: 0.8d,
                Errors: ["Azure Resource Graph returned partial results."]),
            new(
                ItemType: "EffectiveRoutes",
                Source: "ARM",
                Timestamp: DateTimeOffset.UtcNow,
                Permission: "Microsoft.Network/networkInterfaces/effectiveRouteTable/action",
                Status: "Unavailable",
                Completeness: 0d,
                Errors: ["Effective route collection is not implemented yet."])
        };

        var snapshot = TestSnapshotFactory.Create(collectionMetadata: metadata);
        var findings = new List<Finding>();

        try
        {
            await writer.WriteAsync(outputDirectory, snapshot, findings, CancellationToken.None);

            var reportPath = Path.Combine(outputDirectory, "network-report.md");
            var report = await File.ReadAllTextAsync(reportPath);

            Assert.Contains("## Inventory Summary", report, StringComparison.Ordinal);
            Assert.Contains("### Source Status", report, StringComparison.Ordinal);
            Assert.Contains("### Unavailable or Partial Evidence Sources", report, StringComparison.Ordinal);
            Assert.Contains("### Encountered Collection Errors", report, StringComparison.Ordinal);
            Assert.Contains("Azure Resource Graph returned partial results.", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteAsync_WritesFindingEvidenceLevelSourceAndAffectedCounts()
    {
        var serializer = new NetworkSnapshotSerializer();
        var writer = new ReportWriter(serializer);
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"radivum-tests-{Guid.NewGuid():N}");

        var snapshot = TestSnapshotFactory.Create();
        var findings = new List<Finding>
        {
            new(
                Code: "NET-999",
                Severity: "High",
                Title: "Synthetic test finding",
                Description: "Used to validate report rendering of additive finding fields.",
                Confidence: "High",
                Evidence: [],
                Resources: ["vnet-1/app"])
            {
                EvidenceLevel = "Configured",
                Source = "Rule",
                AffectedResourceIds =
                [
                    "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1",
                    "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/networkSecurityGroups/nsg-1"
                ],
                AffectedGraphNodeIds =
                [
                    "/subscriptions/sub-1/resourcegroups/rg/providers/microsoft.network/virtualnetworks/vnet-1"
                ]
            }
        };

        try
        {
            await writer.WriteAsync(outputDirectory, snapshot, findings, CancellationToken.None);

            var reportPath = Path.Combine(outputDirectory, "network-report.md");
            var report = await File.ReadAllTextAsync(reportPath);

            Assert.Contains("Evidence level: **Configured**", report, StringComparison.Ordinal);
            Assert.Contains("Source: **Rule**", report, StringComparison.Ordinal);
            Assert.Contains("Affected resource IDs: **2**", report, StringComparison.Ordinal);
            Assert.Contains("Affected graph node IDs: **1**", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteAsync_WritesFindingsArtifactWithEveryFinding()
    {
        var serializer = new NetworkSnapshotSerializer();
        var writer = new ReportWriter(serializer);
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"radivum-tests-{Guid.NewGuid():N}");

        var snapshot = TestSnapshotFactory.Create();
        var findings = new List<Finding> { TestAnalysisBundle.CreateOssFinding(), TestAnalysisBundle.CreateProFinding() };

        try
        {
            await writer.WriteAsync(outputDirectory, snapshot, findings, CancellationToken.None);

            var findingsPath = Path.Combine(outputDirectory, AnalysisArtifactRoles.Findings);
            var json = await File.ReadAllTextAsync(findingsPath);
            var written = JsonSerializer.Deserialize(
                json,
                NetworkSnapshotJsonContext.Default.ListFinding);

            Assert.DoesNotContain('\r', json);
            Assert.NotNull(written);
            Assert.Equal(findings.Count, written.Count);
            Assert.Equal(
                findings.Select(finding => (finding.Code, finding.Origin)),
                written.Select(finding => (finding.Code, finding.Origin)));
            Assert.Equal(findings[0].Resources, written[0].Resources);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}
