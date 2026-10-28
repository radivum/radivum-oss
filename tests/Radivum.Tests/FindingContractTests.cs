using System.Text.Json;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

public sealed class FindingContractTests
{
    [Fact]
    public void Constructor_UsesBackwardCompatibleDefaultsForNewFields()
    {
        var finding = new Finding(
            Code: "NET-001",
            Severity: "High",
            Title: "Test finding",
            Description: "Description",
            Confidence: "High",
            Evidence: [],
            Resources: ["/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1"]);

        Assert.Equal("Configured", finding.EvidenceLevel);
        Assert.Equal("Rule", finding.Source);
        Assert.Equal(finding.Resources, finding.AffectedResourceIds);
        Assert.Empty(finding.AffectedGraphNodeIds);
        Assert.Equal(FindingOrigin.Oss, finding.Origin);
    }

    [Fact]
    public void InitOnlyFields_CanOverrideDefaultsWithoutBreakingConstructorPattern()
    {
        var finding = new Finding(
            Code: "NET-002",
            Severity: "Medium",
            Title: "Test finding",
            Description: "Description",
            Confidence: "Medium",
            Evidence: [],
            Resources: ["resource-1"])
        {
            EvidenceLevel = "Effective",
            Source = "Graph",
            AffectedResourceIds = ["resource-1", "resource-2"],
            AffectedGraphNodeIds = ["node-1"],
            Origin = FindingOrigin.Pro
        };

        Assert.Equal("Effective", finding.EvidenceLevel);
        Assert.Equal("Graph", finding.Source);
        Assert.Equal(2, finding.AffectedResourceIds.Count);
        Assert.Single(finding.AffectedGraphNodeIds);
        Assert.Equal(FindingOrigin.Pro, finding.Origin);
    }

    [Fact]
    public async Task SerializeAndDeserialize_PreservesAdditiveFields()
    {
        var original = new Finding(
            Code: "NET-003",
            Severity: "Info",
            Title: "Test finding",
            Description: "Description",
            Confidence: "Low",
            Evidence: [],
            Resources: ["resource-1"])
        {
            EvidenceLevel = "Configured",
            Source = "Snapshot",
            AffectedResourceIds = ["resource-1"],
            AffectedGraphNodeIds = ["node-1", "node-2"],
            Origin = FindingOrigin.Pro
        };

        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            stream,
            original,
            NetworkSnapshotJsonContext.Default.Finding,
            CancellationToken.None);
        stream.Position = 0;

        var deserialized = await JsonSerializer.DeserializeAsync(
            stream,
            NetworkSnapshotJsonContext.Default.Finding,
            CancellationToken.None);

        Assert.NotNull(deserialized);
        Assert.Equal(original.EvidenceLevel, deserialized.EvidenceLevel);
        Assert.Equal(original.Source, deserialized.Source);
        Assert.Equal(original.AffectedResourceIds, deserialized.AffectedResourceIds);
        Assert.Equal(original.AffectedGraphNodeIds, deserialized.AffectedGraphNodeIds);
        Assert.Equal(FindingOrigin.Pro, deserialized.Origin);
    }
}
