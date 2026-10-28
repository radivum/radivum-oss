using Radivum.Oss.Analysis;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

public sealed class AnalysisCapabilityEvaluatorTests
{
    [Fact]
    public void EvaluateCoverage_GivenFullyAvailableEvidence_ReportsAvailable()
    {
        var snapshot = TestSnapshotFactory.Create(collectionMetadata: [CreateMetadata("Resources", "Available", 1d)]);

        var coverage = AnalysisCapabilityEvaluator.EvaluateCoverage(snapshot);

        Assert.Equal(1d, coverage.OverallCompleteness);
        Assert.Equal(AnalysisCapabilityStatus.Available, coverage.Status);
        Assert.Equal(0, coverage.UnavailableSourceCount);
        Assert.Equal(1, coverage.SnapshotResourceCount);
        Assert.Equal(0, coverage.EffectiveEvidenceCount);
    }

    [Fact]
    public void EvaluateCoverage_GivenPartiallyAvailableEvidence_ReportsLimited()
    {
        var snapshot = TestSnapshotFactory.Create(collectionMetadata:
        [
            CreateMetadata("Resources", "Available", 1d),
            CreateMetadata("EffectiveRoutes", "Partial", 0.5d)
        ]);

        var coverage = AnalysisCapabilityEvaluator.EvaluateCoverage(snapshot);

        Assert.Equal(0.75d, coverage.OverallCompleteness);
        Assert.Equal(AnalysisCapabilityStatus.Limited, coverage.Status);
        Assert.Equal(1, coverage.UnavailableSourceCount);
    }

    [Fact]
    public void EvaluateCoverage_GivenNoMetadata_ReportsUnavailable()
    {
        var snapshot = TestSnapshotFactory.Create(collectionMetadata: []);

        var coverage = AnalysisCapabilityEvaluator.EvaluateCoverage(snapshot);

        Assert.Equal(0d, coverage.OverallCompleteness);
        Assert.Equal(AnalysisCapabilityStatus.Unavailable, coverage.Status);
    }

    [Fact]
    public void EvaluateCapabilities_GivenSnapshotWithoutArtifacts_ReportsInventoryOnly()
    {
        var snapshot = TestSnapshotFactory.Create();

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(snapshot, [], [], AnalysisEdition.Oss);

        Assert.Equal(AnalysisCapabilityStatus.Available, StatusOf(capabilities, AnalysisCapabilityIds.Overview));
        Assert.Equal(AnalysisCapabilityStatus.Available, StatusOf(capabilities, AnalysisCapabilityIds.Inventory));
        Assert.Equal(AnalysisCapabilityStatus.Unavailable, StatusOf(capabilities, AnalysisCapabilityIds.Topology));
        Assert.Equal(AnalysisCapabilityStatus.Unavailable, StatusOf(capabilities, AnalysisCapabilityIds.Findings));
        Assert.Equal(AnalysisCapabilityStatus.Unavailable, StatusOf(capabilities, AnalysisCapabilityIds.Routing));
        Assert.Equal(AnalysisCapabilityStatus.Unavailable, StatusOf(capabilities, AnalysisCapabilityIds.Security));
        Assert.Equal(AnalysisCapabilityStatus.Unavailable, StatusOf(capabilities, AnalysisCapabilityIds.Trace));
    }

    [Fact]
    public void EvaluateCapabilities_GivenGraphArtifact_ReportsTopologyAvailable()
    {
        var snapshot = TestSnapshotFactory.Create();

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.NetworkGraph],
            [],
            AnalysisEdition.Oss);

        Assert.Equal(AnalysisCapabilityStatus.Available, StatusOf(capabilities, AnalysisCapabilityIds.Topology));
    }

    [Fact]
    public void EvaluateCapabilities_GivenBothFindingOrigins_ReportsFindingsAvailable()
    {
        var snapshot = TestSnapshotFactory.Create();

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.Findings],
            [FindingOrigin.Oss, FindingOrigin.Pro],
            AnalysisEdition.Pro);

        Assert.Equal(AnalysisCapabilityStatus.Available, StatusOf(capabilities, AnalysisCapabilityIds.Findings));
    }

    [Fact]
    public void EvaluateCapabilities_GivenOssFindingsOnly_ReportsFindingsLimited()
    {
        var snapshot = TestSnapshotFactory.Create();

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.Findings],
            [FindingOrigin.Oss],
            AnalysisEdition.Oss);

        var findings = Assert.Single(capabilities, capability => capability.Id == AnalysisCapabilityIds.Findings);
        Assert.Equal(AnalysisCapabilityStatus.Limited, findings.Status);
        Assert.Equal("findings produced by oss only", findings.Detail);
    }

    [Fact]
    public void EvaluateCapabilities_GivenRouteTablesWithoutEffectiveRoutes_ReportsRoutingLimited()
    {
        var snapshot = TestSnapshotFactory.Create() with
        {
            Resources = TestSnapshotFactory.Create().Resources with
            {
                RouteTables =
                [
                    new RouteTableResource(
                        Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/routeTables/rt-1",
                        Name: "rt-1",
                        ResourceGroup: "rg",
                        Location: "eastus",
                        DisableBgpRoutePropagation: false,
                        Routes: [],
                        SubnetIds: [],
                        Metadata: CreateMetadata("Resources", "Available", 1d))
                ]
            }
        };

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(snapshot, [], [], AnalysisEdition.Pro);

        var routing = Assert.Single(capabilities, capability => capability.Id == AnalysisCapabilityIds.Routing);
        Assert.Equal(AnalysisCapabilityStatus.Limited, routing.Status);
        Assert.Equal("missing routing-evidence.json", routing.Detail);
    }

    [Fact]
    public void EvaluateCapabilities_GivenRoutingEvidenceAndEffectiveRoutes_ReportsRoutingAvailable()
    {
        var snapshot = TestSnapshotFactory.Create(collectionMetadata:
        [
            CreateMetadata("Resources", "Available", 1d),
            CreateMetadata("EffectiveRoutes", "Available", 1d)
        ]);

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.RoutingEvidence],
            [],
            AnalysisEdition.Pro);

        Assert.Equal(AnalysisCapabilityStatus.Available, StatusOf(capabilities, AnalysisCapabilityIds.Routing));
    }

    [Fact]
    public void EvaluateCapabilities_GivenArtifactNamesInAnyCasing_MatchesCaseInsensitively()
    {
        var snapshot = TestSnapshotFactory.Create();

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.NetworkGraph.ToUpperInvariant()],
            [],
            AnalysisEdition.Oss);

        Assert.Equal(AnalysisCapabilityStatus.Available, StatusOf(capabilities, AnalysisCapabilityIds.Topology));
    }

    [Fact]
    public void EvaluateCapabilities_GivenOssEditionWithRoutingEvidence_ReportsRoutingUnavailable()
    {
        var snapshot = TestSnapshotFactory.Create(collectionMetadata:
        [
            CreateMetadata("Resources", "Available", 1d),
            CreateMetadata("EffectiveRoutes", "Available", 1d)
        ]);

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.RoutingEvidence],
            [],
            AnalysisEdition.Oss);

        var routing = Assert.Single(capabilities, capability => capability.Id == AnalysisCapabilityIds.Routing);
        Assert.Equal(AnalysisCapabilityStatus.Unavailable, routing.Status);
        Assert.Equal("requires the pro edition", routing.Detail);
    }

    [Fact]
    public void EvaluateCapabilities_GivenOssEditionWithSecurityEvidence_ReportsSecurityUnavailable()
    {
        var snapshot = TestSnapshotFactory.Create(collectionMetadata:
        [
            CreateMetadata("Resources", "Available", 1d),
            CreateMetadata("EffectiveNSGs", "Available", 1d)
        ]);

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.SecurityEvidence],
            [],
            AnalysisEdition.Oss);

        var security = Assert.Single(capabilities, capability => capability.Id == AnalysisCapabilityIds.Security);
        Assert.Equal(AnalysisCapabilityStatus.Unavailable, security.Status);
        Assert.Equal("requires the pro edition", security.Detail);
    }

    [Fact]
    public void EvaluateCapabilities_GivenProEditionWithOssFindingsOnly_ReportsPremiumRulesMissing()
    {
        var snapshot = TestSnapshotFactory.Create();

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.Findings],
            [FindingOrigin.Oss],
            AnalysisEdition.Pro);

        var findings = Assert.Single(capabilities, capability => capability.Id == AnalysisCapabilityIds.Findings);
        Assert.Equal(AnalysisCapabilityStatus.Limited, findings.Status);
        Assert.Equal("premium finding rules did not contribute findings", findings.Detail);
    }

    [Fact]
    public void EvaluateCapabilities_GivenFindingsArtifactWithoutFindings_ReportsNoFindingsRecorded()
    {
        var snapshot = TestSnapshotFactory.Create();

        var capabilities = AnalysisCapabilityEvaluator.EvaluateCapabilities(
            snapshot,
            [AnalysisArtifactRoles.Findings],
            [],
            AnalysisEdition.Oss);

        var findings = Assert.Single(capabilities, capability => capability.Id == AnalysisCapabilityIds.Findings);
        Assert.Equal(AnalysisCapabilityStatus.Limited, findings.Status);
        Assert.Equal($"no findings recorded in {AnalysisArtifactRoles.Findings}", findings.Detail);
    }

    private static string StatusOf(IReadOnlyList<AnalysisCapability> capabilities, string capabilityId)
        => Assert.Single(capabilities, capability => capability.Id == capabilityId).Status;

    private static CollectionMetadata CreateMetadata(string itemType, string status, double completeness) => new(
        ItemType: itemType,
        Source: "AzureResourceGraph",
        Timestamp: new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero),
        Permission: "Reader",
        Status: status,
        Completeness: completeness,
        Errors: []);
}
