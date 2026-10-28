using Radivum.Graph;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

public sealed class NetworkGraphBuilderRegressionTests
{
    [Fact]
    public void Build_SameSnapshotTwice_ProducesDeterministicNodeAndEdgeSets()
    {
        var snapshot = CreateHubSpokeLikeSnapshot();
        var builder = new NetworkGraphBuilder();
        var generatedAt = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        var first = builder.Build(snapshot, generatedAt);
        var second = builder.Build(snapshot, generatedAt);

        Assert.Equal(first.GraphSchemaVersion, second.GraphSchemaVersion);
        Assert.Equal(first.Nodes.Select(node => node.Id), second.Nodes.Select(node => node.Id));
        Assert.Equal(first.Edges.Select(edge => edge.Id), second.Edges.Select(edge => edge.Id));
        Assert.Equal(first.Groups.Select(group => group.Id), second.Groups.Select(group => group.Id));
    }

    [Fact]
    public void Build_SnapshotResourceOrderChanges_ProducesEquivalentGraphIdentity()
    {
        var ordered = CreateOrderedMultiResourceSnapshot();
        var reordered = CreateReorderedMultiResourceSnapshot();
        var builder = new NetworkGraphBuilder();
        var generatedAt = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        var first = builder.Build(ordered, generatedAt);
        var second = builder.Build(reordered, generatedAt);

        Assert.Equal(first.Nodes.Select(node => node.Id), second.Nodes.Select(node => node.Id));
        Assert.Equal(first.Edges.Select(edge => edge.Id), second.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public void Build_S4HubSpokeLikeSnapshot_ProducesExpectedPeeringAndContainsEdges()
    {
        var snapshot = CreateHubSpokeLikeSnapshot();
        var builder = new NetworkGraphBuilder();

        var graph = builder.Build(snapshot, new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(4, graph.Edges.Count(edge => edge.RelationshipType == "PeeredWith"));
        Assert.Contains(graph.Edges, edge => edge.DisplayName.Contains("hub-to-spoke1", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge => edge.DisplayName.Contains("spoke1-to-hub", StringComparison.Ordinal));
        Assert.Contains(graph.Nodes, node => node.NodeType == "VirtualNetwork" && node.DisplayName == "aznw-s4-hub");
        Assert.Contains(graph.Nodes, node => node.NodeType == "Subnet" && node.DisplayName == "shared");
    }

    [Fact]
    public void Build_S3UdrLikeSnapshot_ProducesRoutesViaEdge()
    {
        var snapshot = CreateUdrLikeSnapshot();
        var builder = new NetworkGraphBuilder();

        var graph = builder.Build(snapshot, new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains(graph.Edges, edge => edge.RelationshipType == "RoutesVia");
    }

    [Fact]
    public void Build_S6PartialPermissionLikeSnapshot_ToleratesMissingRelationshipTargets()
    {
        var snapshot = CreatePartialDataSnapshot();
        var builder = new NetworkGraphBuilder();

        var graph = builder.Build(snapshot, new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));

        Assert.NotEmpty(graph.Nodes);
        Assert.DoesNotContain(graph.Edges, edge => edge.RelationshipType == "ProtectedBy");
        Assert.DoesNotContain(graph.Edges, edge => edge.RelationshipType == "RoutesVia");
    }

    [Theory]
    [InlineData("VirtualNetwork")]
    [InlineData("NetworkSecurityGroup")]
    [InlineData("RouteTable")]
    public void Build_MultiResourceSnapshot_NestsResourceGroupResourceUnderResourceGroupNode(string nodeType)
    {
        var graph = new NetworkGraphBuilder().Build(
            CreateOrderedMultiResourceSnapshot(),
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));

        var resourceGroupNodeId = Assert.Single(graph.Nodes, node => node.NodeType == "ResourceGroup").Id;
        var resourceNode = Assert.Single(graph.Nodes, node => node.NodeType == nodeType);

        Assert.Equal(resourceGroupNodeId, resourceNode.ParentNodeId);
    }

    [Fact]
    public void Build_HubSpokeLikeSnapshot_NestsSubnetUnderParentVirtualNetwork()
    {
        var graph = BuildHubSpokeLikeGraph();

        var hub = Assert.Single(graph.Nodes, node => node.NodeType == "VirtualNetwork" && node.DisplayName == "aznw-s4-hub");
        var subnet = Assert.Single(graph.Nodes, node => node.NodeType == "Subnet" && node.DisplayName == "shared");

        Assert.Equal(hub.Id, subnet.ParentNodeId);
    }

    [Fact]
    public void Build_HubSpokeLikeSnapshot_GroupMembershipMatchesParentNodeId()
    {
        var graph = BuildHubSpokeLikeGraph();

        var memberships = graph.Groups
            .SelectMany(group => group.NodeIds.Select(nodeId => (GroupId: group.Id, NodeId: nodeId)))
            .ToArray();

        Assert.NotEmpty(memberships);
        Assert.All(memberships, membership => Assert.Equal(
            membership.GroupId,
            graph.Nodes.Single(node => node.Id == membership.NodeId).ParentNodeId));
    }

    [Fact]
    public void Build_HubSpokeLikeSnapshot_EveryParentedNodeAppearsInItsParentGroup()
    {
        var graph = BuildHubSpokeLikeGraph();

        var nodeIdsByGroupId = graph.Groups.ToDictionary(
            group => group.Id,
            group => group.NodeIds.ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var parentedNodes = graph.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.ParentNodeId)).ToArray();

        Assert.NotEmpty(parentedNodes);
        Assert.All(parentedNodes, node => Assert.Contains(node.Id, nodeIdsByGroupId[node.ParentNodeId!]));
    }

    private static NetworkGraph BuildHubSpokeLikeGraph()
        => new NetworkGraphBuilder().Build(
            CreateHubSpokeLikeSnapshot(),
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));

    private static NetworkSnapshot CreateOrderedMultiResourceSnapshot()
    {
        var metadata = CreateMetadata();
        var subscriptionId = "sub-1";
        var resourceGroup = "rg";

        var nsg = new NetworkSecurityGroupResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/networkSecurityGroups/nsg-1",
            Name: "nsg-1",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            Rules: [],
            SubnetIds: ["/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1/subnets/app"],
            NetworkInterfaceIds: [],
            Metadata: metadata);

        var routeTable = new RouteTableResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/routeTables/rt-1",
            Name: "rt-1",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            DisableBgpRoutePropagation: false,
            Routes: [],
            SubnetIds: ["/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1/subnets/app"],
            Metadata: metadata);

        var vnet = new VNetResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1",
            Name: "vnet-1",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            AddressSpace: ["10.0.0.0/16"],
            Subnets:
            [
                new SubnetResource(
                    Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1/subnets/app",
                    Name: "app",
                    AddressPrefix: "10.0.1.0/24",
                    NetworkSecurityGroupId: nsg.Id,
                    RouteTableId: routeTable.Id,
                    Metadata: metadata)
            ],
            Peerings: [],
            Metadata: metadata);

        return new NetworkSnapshot(
            SchemaVersion: NetworkSnapshotSchema.Version,
            ToolVersion: "0.1.0",
            TenantId: "tenant-1",
            SubscriptionIds: [subscriptionId],
            AccountContext: new AccountContextInfo(null, null, "AzureCloud", "DefaultAzureCredential"),
            CollectedAt: new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            Scope: new SnapshotScope($"/subscriptions/{subscriptionId}", subscriptionId, null, [subscriptionId]),
            Resources: new NetworkResourceSet(
                VNets: [vnet],
                NetworkSecurityGroups: [nsg],
                RouteTables: [routeTable],
                NetworkInterfaces: [],
                PublicIpAddresses: [],
                PrivateEndpoints: [],
                PrivateDnsZones: [],
                AzureFirewalls: [],
                VirtualHubs: [],
                VirtualWans: [],
                ExpressRouteCircuits: [],
                VirtualNetworkGateways: []),
            EffectiveEvidence: new EffectiveEvidenceSet([], [], []),
            Relationships: [],
            CollectionMetadata: [metadata]);
    }

    private static NetworkSnapshot CreateReorderedMultiResourceSnapshot()
    {
        var ordered = CreateOrderedMultiResourceSnapshot();
        var resources = ordered.Resources;

        return ordered with
        {
            Resources = resources with
            {
                VNets = resources.VNets.Reverse().ToArray(),
                NetworkSecurityGroups = resources.NetworkSecurityGroups.Reverse().ToArray(),
                RouteTables = resources.RouteTables.Reverse().ToArray()
            }
        };
    }

    private static NetworkSnapshot CreateHubSpokeLikeSnapshot()
    {
        var metadata = CreateMetadata();
        const string subscriptionId = "sub-1";
        const string resourceGroup = "rg";

        var hub = new VNetResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/aznw-s4-hub",
            Name: "aznw-s4-hub",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            AddressSpace: ["10.40.0.0/16"],
            Subnets:
            [
                new SubnetResource(
                    Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/aznw-s4-hub/subnets/shared",
                    Name: "shared",
                    AddressPrefix: "10.40.1.0/24",
                    NetworkSecurityGroupId: null,
                    RouteTableId: null,
                    Metadata: metadata)
            ],
            Peerings:
            [
                new VNetPeeringResource("hub-to-spoke1", "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/aznw-s4-spoke1", "Connected", false, false, true, true, metadata),
                new VNetPeeringResource("hub-to-spoke2", "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/aznw-s4-spoke2", "Connected", false, false, true, true, metadata)
            ],
            Metadata: metadata);

        var spoke1 = new VNetResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/aznw-s4-spoke1",
            Name: "aznw-s4-spoke1",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            AddressSpace: ["10.41.0.0/16"],
            Subnets:
            [
                new SubnetResource(
                    Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/aznw-s4-spoke1/subnets/workload",
                    Name: "workload",
                    AddressPrefix: "10.41.1.0/24",
                    NetworkSecurityGroupId: null,
                    RouteTableId: null,
                    Metadata: metadata)
            ],
            Peerings:
            [
                new VNetPeeringResource("spoke1-to-hub", hub.Id, "Connected", false, false, true, true, metadata)
            ],
            Metadata: metadata);

        var spoke2 = new VNetResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/aznw-s4-spoke2",
            Name: "aznw-s4-spoke2",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            AddressSpace: ["10.42.0.0/16"],
            Subnets:
            [
                new SubnetResource(
                    Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/aznw-s4-spoke2/subnets/workload",
                    Name: "workload",
                    AddressPrefix: "10.42.1.0/24",
                    NetworkSecurityGroupId: null,
                    RouteTableId: null,
                    Metadata: metadata)
            ],
            Peerings:
            [
                new VNetPeeringResource("spoke2-to-hub", hub.Id, "Connected", false, false, true, true, metadata)
            ],
            Metadata: metadata);

        return new NetworkSnapshot(
            SchemaVersion: NetworkSnapshotSchema.Version,
            ToolVersion: "0.1.0",
            TenantId: "tenant-1",
            SubscriptionIds: [subscriptionId],
            AccountContext: new AccountContextInfo(null, null, "AzureCloud", "DefaultAzureCredential"),
            CollectedAt: new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            Scope: new SnapshotScope($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}", subscriptionId, resourceGroup, [subscriptionId]),
            Resources: new NetworkResourceSet(
                VNets: [hub, spoke1, spoke2],
                NetworkSecurityGroups: [],
                RouteTables: [],
                NetworkInterfaces: [],
                PublicIpAddresses: [],
                PrivateEndpoints: [],
                PrivateDnsZones: [],
                AzureFirewalls: [],
                VirtualHubs: [],
                VirtualWans: [],
                ExpressRouteCircuits: [],
                VirtualNetworkGateways: []),
            EffectiveEvidence: new EffectiveEvidenceSet([], [], []),
            Relationships: [],
            CollectionMetadata: [metadata]);
    }

    private static NetworkSnapshot CreateUdrLikeSnapshot()
    {
        var metadata = CreateMetadata();
        const string subscriptionId = "sub-1";
        const string resourceGroup = "rg";
        var routeTableId = "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/routeTables/rt-default";

        var vnet = new VNetResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-udr",
            Name: "vnet-udr",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            AddressSpace: ["10.10.0.0/16"],
            Subnets:
            [
                new SubnetResource(
                    Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-udr/subnets/app",
                    Name: "app",
                    AddressPrefix: "10.10.1.0/24",
                    NetworkSecurityGroupId: null,
                    RouteTableId: routeTableId,
                    Metadata: metadata)
            ],
            Peerings: [],
            Metadata: metadata);

        var routeTable = new RouteTableResource(
            Id: routeTableId,
            Name: "rt-default",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            DisableBgpRoutePropagation: false,
            Routes:
            [
                new RouteResource("default", "0.0.0.0/0", "VirtualAppliance", "10.10.2.4", metadata)
            ],
            SubnetIds: [vnet.Subnets[0].Id],
            Metadata: metadata);

        return new NetworkSnapshot(
            SchemaVersion: NetworkSnapshotSchema.Version,
            ToolVersion: "0.1.0",
            TenantId: "tenant-1",
            SubscriptionIds: [subscriptionId],
            AccountContext: new AccountContextInfo(null, null, "AzureCloud", "DefaultAzureCredential"),
            CollectedAt: new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            Scope: new SnapshotScope($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}", subscriptionId, resourceGroup, [subscriptionId]),
            Resources: new NetworkResourceSet(
                VNets: [vnet],
                NetworkSecurityGroups: [],
                RouteTables: [routeTable],
                NetworkInterfaces: [],
                PublicIpAddresses: [],
                PrivateEndpoints: [],
                PrivateDnsZones: [],
                AzureFirewalls: [],
                VirtualHubs: [],
                VirtualWans: [],
                ExpressRouteCircuits: [],
                VirtualNetworkGateways: []),
            EffectiveEvidence: new EffectiveEvidenceSet([], [], []),
            Relationships: [],
            CollectionMetadata: [metadata]);
    }

    private static NetworkSnapshot CreatePartialDataSnapshot()
    {
        var metadata = CreateMetadata();
        const string subscriptionId = "sub-1";
        const string resourceGroup = "rg";

        var vnet = new VNetResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-partial",
            Name: "vnet-partial",
            ResourceGroup: resourceGroup,
            Location: "eastus",
            AddressSpace: ["10.20.0.0/16"],
            Subnets:
            [
                new SubnetResource(
                    Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-partial/subnets/workload",
                    Name: "workload",
                    AddressPrefix: "10.20.1.0/24",
                    NetworkSecurityGroupId: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/networkSecurityGroups/missing",
                    RouteTableId: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/routeTables/missing",
                    Metadata: metadata)
            ],
            Peerings: [],
            Metadata: metadata);

        return new NetworkSnapshot(
            SchemaVersion: NetworkSnapshotSchema.Version,
            ToolVersion: "0.1.0",
            TenantId: "tenant-1",
            SubscriptionIds: [subscriptionId],
            AccountContext: new AccountContextInfo(null, null, "AzureCloud", "DefaultAzureCredential"),
            CollectedAt: new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            Scope: new SnapshotScope($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}", subscriptionId, resourceGroup, [subscriptionId]),
            Resources: new NetworkResourceSet(
                VNets: [vnet],
                NetworkSecurityGroups: [],
                RouteTables: [],
                NetworkInterfaces:
                [
                    new NetworkInterfaceResource(
                        Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/networkInterfaces/nic-missing-links",
                        Name: "nic-missing-links",
                        ResourceGroup: resourceGroup,
                        Location: "eastus",
                        VirtualNetworkId: vnet.Id,
                        SubnetId: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-partial/subnets/not-found",
                        NetworkSecurityGroupId: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/networkSecurityGroups/not-found",
                        PublicIpAddressId: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/publicIPAddresses/not-found",
                        PrivateIpAddress: "10.20.1.5",
                        Metadata: metadata)
                ],
                PublicIpAddresses: [],
                PrivateEndpoints: [],
                PrivateDnsZones: [],
                AzureFirewalls: [],
                VirtualHubs: [],
                VirtualWans: [],
                ExpressRouteCircuits: [],
                VirtualNetworkGateways: []),
            EffectiveEvidence: new EffectiveEvidenceSet([], [], []),
            Relationships: [],
            CollectionMetadata:
            [
                metadata,
                metadata with
                {
                    ItemType = "NSGs",
                    Status = "Unavailable",
                    Completeness = 0d,
                    Errors = ["Insufficient privileges for NSG read in this scope."]
                }
            ]);
    }

    private static CollectionMetadata CreateMetadata()
        => new(
            ItemType: "Resources",
            Source: "AzureResourceGraph",
            Timestamp: new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            Permission: "Reader",
            Status: "Available",
            Completeness: 1d,
            Errors: []);
}
