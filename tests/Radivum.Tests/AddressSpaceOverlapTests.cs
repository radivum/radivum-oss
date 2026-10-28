using Radivum.Model;
using Radivum.Oss.Analysis;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Covers the split between an overlap on a connection that exists (<c>NET-018</c>) and one between networks nothing
/// joins (<c>NET-019</c>). The distinction classifies risk; it does not assert that a path is usable.
/// </summary>
public sealed class AddressSpaceOverlapTests
{
    private static readonly DateTimeOffset CollectedAt = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Run_GivenOverlappingVNetsNothingJoins_ReportsInformationOnly()
    {
        var snapshot = Snapshot(
            VNet("sub-1", "vnet-a", "10.0.0.0/16"),
            VNet("sub-2", "vnet-b", "10.0.0.0/16"));

        var findings = Checks.Run(snapshot);

        Assert.DoesNotContain(findings, finding => finding.Code == "NET-018");
        var informational = Assert.Single(findings, finding => finding.Code == "NET-019");
        Assert.Equal("Info", informational.Severity);
    }

    [Fact]
    public void Run_GivenDirectlyPeeredOverlappingVNets_ReportsTheHighOverlap()
    {
        var hubId = VNetId("sub-2", "vnet-b");
        var snapshot = Snapshot(
            VNet("sub-1", "vnet-a", "10.0.0.0/16", ("to-b", hubId)),
            VNet("sub-2", "vnet-b", "10.0.0.0/16"));

        var findings = Checks.Run(snapshot);

        Assert.DoesNotContain(findings, finding => finding.Code == "NET-019");
        var actionable = Assert.Single(findings, finding => finding.Code == "NET-018");
        Assert.Equal("High", actionable.Severity);
        Assert.Contains(hubId, actionable.AffectedResourceIds);
    }

    [Fact]
    public void Run_GivenPeeringDeclaredInOneDirectionOnly_StillReportsTheHighOverlap()
    {
        var snapshot = Snapshot(
            VNet("sub-1", "vnet-a", "10.0.0.0/16"),
            VNet("sub-2", "vnet-b", "10.0.0.0/16", ("to-a", VNetId("sub-1", "vnet-a"))));

        var findings = Checks.Run(snapshot);

        Assert.Single(findings, finding => finding.Code == "NET-018");
        Assert.DoesNotContain(findings, finding => finding.Code == "NET-019");
    }

    [Fact]
    public void Run_GivenTwoVNetsPeeredWithOneUncollectedHub_ReportsTheHighOverlap()
    {
        const string hubId = "/subscriptions/sub-3/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-hub";
        var snapshot = Snapshot(
            VNet("sub-1", "vnet-spoke-a", "10.0.0.0/16", ("to-hub", hubId)),
            VNet("sub-2", "vnet-spoke-b", "10.0.0.0/16", ("to-hub", hubId)));

        var findings = Checks.Run(snapshot);

        var actionable = Assert.Single(findings, finding => finding.Code == "NET-018");
        Assert.Equal(2, actionable.AffectedResourceIds.Count);
    }

    [Fact]
    public void Run_GivenTwoVNetsOfOneVirtualHub_ReportsTheHighOverlap()
    {
        var snapshot = Snapshot(
            [VNet("sub-1", "vnet-spoke-a", "10.0.0.0/16"), VNet("sub-2", "vnet-spoke-b", "10.0.0.0/16")],
            [VirtualHub([VNetId("sub-1", "vnet-spoke-a"), VNetId("sub-2", "vnet-spoke-b")])]);

        var findings = Checks.Run(snapshot);

        Assert.Single(findings, finding => finding.Code == "NET-018");
        Assert.DoesNotContain(findings, finding => finding.Code == "NET-019");
    }

    [Fact]
    public void Run_GivenConnectedAndUnrelatedOverlaps_ReportsBothFindings()
    {
        var snapshot = Snapshot(
            VNet("sub-1", "vnet-a", "10.0.0.0/16", ("to-b", VNetId("sub-2", "vnet-b"))),
            VNet("sub-2", "vnet-b", "10.0.0.0/16"),
            VNet("sub-3", "vnet-c", "10.0.0.0/16"));

        var findings = Checks.Run(snapshot);

        var actionable = Assert.Single(findings, finding => finding.Code == "NET-018");
        var informational = Assert.Single(findings, finding => finding.Code == "NET-019");
        Assert.Single(actionable.Resources);
        Assert.Equal(2, informational.Resources.Count);
    }

    [Fact]
    public void Run_GivenDuplicateVNetNames_QualifiesThemWithTheirSubscription()
    {
        var snapshot = Snapshot(
            VNet("sub-1", "vnet", "10.0.0.0/16"),
            VNet("sub-2", "vnet", "10.0.0.0/16"));

        var findings = Checks.Run(snapshot);

        var informational = Assert.Single(findings, finding => finding.Code == "NET-019");
        Assert.Contains(informational.Resources, text => text.Contains("vnet (sub-1)", StringComparison.Ordinal));
        Assert.Contains(informational.Resources, text => text.Contains("vnet (sub-2)", StringComparison.Ordinal));
    }

    private static string VNetId(string subscriptionId, string name)
        => $"/subscriptions/{subscriptionId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static VNetResource VNet(
        string subscriptionId,
        string name,
        string addressPrefix,
        params (string Name, string RemoteId)[] peerings)
        => new(
            Id: VNetId(subscriptionId, name),
            Name: name,
            ResourceGroup: "rg",
            Location: "eastus",
            AddressSpace: [addressPrefix],
            Subnets: [],
            Peerings: peerings
                .Select(peering => new VNetPeeringResource(
                    peering.Name,
                    peering.RemoteId,
                    "Connected",
                    AllowGatewayTransit: false,
                    UseRemoteGateways: false,
                    AllowVirtualNetworkAccess: true,
                    AllowForwardedTraffic: false,
                    Metadata()))
                .ToList(),
            Metadata: Metadata());

    private static VirtualHubResource VirtualHub(IReadOnlyList<string> connectedVNetIds)
        => new(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualHubs/hub-1",
            Name: "hub-1",
            ResourceGroup: "rg",
            Location: "eastus",
            VirtualWanId: null,
            ConnectedVirtualNetworkIds: connectedVNetIds,
            Metadata: Metadata());

    private static NetworkSnapshot Snapshot(params VNetResource[] vnets)
        => Snapshot(vnets, []);

    private static NetworkSnapshot Snapshot(
        IReadOnlyList<VNetResource> vnets,
        IReadOnlyList<VirtualHubResource> hubs)
        => new(
            SchemaVersion: NetworkSnapshotSchema.Version,
            ToolVersion: "0.1.0",
            TenantId: "tenant-1",
            SubscriptionIds: vnets
                .Select(vnet => ArmResourceId.ParseOrNull(vnet.Id)!.SubscriptionId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList(),
            AccountContext: new AccountContextInfo(null, null, "AzureCloud", "DefaultAzureCredential"),
            CollectedAt: CollectedAt,
            Scope: new SnapshotScope("scope", null, null, []),
            Resources: new NetworkResourceSet(
                VNets: vnets,
                NetworkSecurityGroups: [],
                RouteTables: [],
                NetworkInterfaces: [],
                PublicIpAddresses: [],
                PrivateEndpoints: [],
                PrivateDnsZones: [],
                AzureFirewalls: [],
                VirtualHubs: hubs,
                VirtualWans: [],
                ExpressRouteCircuits: [],
                VirtualNetworkGateways: []),
            EffectiveEvidence: new EffectiveEvidenceSet([], [], []),
            Relationships: [],
            CollectionMetadata: [Metadata("Resources"), Metadata("VNets")]);

    private static CollectionMetadata Metadata(string itemType = "VNets")
        => new(
            ItemType: itemType,
            Source: "AzureResourceGraph",
            Timestamp: CollectedAt,
            Permission: "Reader",
            Status: "Available",
            Completeness: 1d,
            Errors: []);
}
