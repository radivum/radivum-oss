using System.Text.Json;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

public sealed class NetworkGraphContractTests
{
    [Fact]
    public async Task SerializeAndDeserialize_RoundTripsGraph()
    {
        var graph = CreateGraph();

        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            stream,
            graph,
            NetworkSnapshotJsonContext.Default.NetworkGraph,
            CancellationToken.None);
        stream.Position = 0;

        var deserialized = await JsonSerializer.DeserializeAsync(
            stream,
            NetworkSnapshotJsonContext.Default.NetworkGraph,
            CancellationToken.None);

        Assert.NotNull(deserialized);
        Assert.Equal(graph.GraphSchemaVersion, deserialized.GraphSchemaVersion);
        Assert.Equal(graph.Nodes.Count, deserialized.Nodes.Count);
        Assert.Equal(graph.Edges.Count, deserialized.Edges.Count);
    }

    [Fact]
    public void CreateNodeIdFromResourceId_NormalizesToCanonicalLowerCase()
    {
        var resourceId = " /SUBSCRIPTIONS/Sub-1/ResourceGroups/RG/providers/Microsoft.Network/virtualNetworks/VNet-1 ";

        var id = NetworkGraphContract.CreateNodeIdFromResourceId(resourceId);

        Assert.Equal("/subscriptions/sub-1/resourcegroups/rg/providers/microsoft.network/virtualnetworks/vnet-1", id);
    }

    [Fact]
    public void CreateSyntheticNodeId_UsesDeterministicSyntheticPrefix()
    {
        var id = NetworkGraphContract.CreateSyntheticNodeId("Subscription", "Sub-1");

        Assert.Equal("synthetic:subscription:sub-1", id);
    }

    [Fact]
    public void CreateEdgeId_UsesResourceBackedIdentityWhenProvided()
    {
        var edgeId = NetworkGraphContract.CreateEdgeId(
            "PeeredWith",
            "/subscriptions/sub-1/.../vnet-a",
            "/subscriptions/sub-1/.../vnet-b",
            "/subscriptions/sub-1/.../virtualnetworkpeerings/a-to-b");

        Assert.Equal("edge:resource:/subscriptions/sub-1/.../virtualnetworkpeerings/a-to-b", edgeId);
    }

    private static NetworkGraph CreateGraph()
    {
        var scopeId = NetworkGraphContract.CreateSyntheticNodeId("Scope", "/subscriptions/sub-1");
        var subscriptionId = NetworkGraphContract.CreateSyntheticNodeId("Subscription", "sub-1");
        var vnetResourceId = "/subscriptions/sub-1/resourcegroups/rg/providers/microsoft.network/virtualnetworks/vnet-1";
        var subnetResourceId = "/subscriptions/sub-1/resourcegroups/rg/providers/microsoft.network/virtualnetworks/vnet-1/subnets/app";

        var nodes = new List<NetworkNode>
        {
            new(
                Id: scopeId,
                ResourceId: null,
                NodeType: "Scope",
                DisplayName: "Scope",
                AzureResourceType: null,
                SubscriptionId: null,
                ResourceGroup: null,
                Region: null,
                ParentNodeId: null,
                Metadata: new Dictionary<string, string>()),
            new(
                Id: subscriptionId,
                ResourceId: null,
                NodeType: "Subscription",
                DisplayName: "sub-1",
                AzureResourceType: null,
                SubscriptionId: "sub-1",
                ResourceGroup: null,
                Region: null,
                ParentNodeId: scopeId,
                Metadata: new Dictionary<string, string>()),
            new(
                Id: NetworkGraphContract.CreateNodeIdFromResourceId(vnetResourceId),
                ResourceId: vnetResourceId,
                NodeType: "VirtualNetwork",
                DisplayName: "vnet-1",
                AzureResourceType: "Microsoft.Network/virtualNetworks",
                SubscriptionId: "sub-1",
                ResourceGroup: "rg",
                Region: "eastus",
                ParentNodeId: subscriptionId,
                Metadata: new Dictionary<string, string>()),
            new(
                Id: NetworkGraphContract.CreateNodeIdFromResourceId(subnetResourceId),
                ResourceId: subnetResourceId,
                NodeType: "Subnet",
                DisplayName: "app",
                AzureResourceType: "Microsoft.Network/virtualNetworks/subnets",
                SubscriptionId: "sub-1",
                ResourceGroup: "rg",
                Region: "eastus",
                ParentNodeId: NetworkGraphContract.CreateNodeIdFromResourceId(vnetResourceId),
                Metadata: new Dictionary<string, string>())
        };

        var edges = new List<NetworkEdge>
        {
            new(
                Id: NetworkGraphContract.CreateEdgeId("Contains", scopeId, subscriptionId),
                SourceNodeId: scopeId,
                TargetNodeId: subscriptionId,
                RelationshipType: "Contains",
                DisplayName: "Scope contains subscription",
                AzureResourceId: null,
                Metadata: new Dictionary<string, string>()),
            new(
                Id: NetworkGraphContract.CreateEdgeId(
                    "Contains",
                    NetworkGraphContract.CreateNodeIdFromResourceId(vnetResourceId),
                    NetworkGraphContract.CreateNodeIdFromResourceId(subnetResourceId)),
                SourceNodeId: NetworkGraphContract.CreateNodeIdFromResourceId(vnetResourceId),
                TargetNodeId: NetworkGraphContract.CreateNodeIdFromResourceId(subnetResourceId),
                RelationshipType: "Contains",
                DisplayName: "VNet contains subnet",
                AzureResourceId: null,
                Metadata: new Dictionary<string, string>())
        };

        var groups = new List<NetworkGroup>
        {
            new(
                Id: scopeId,
                GroupType: "Scope",
                DisplayName: "Scope",
                ParentGroupId: null,
                NodeIds: [subscriptionId],
                Metadata: new Dictionary<string, string>())
        };

        return new NetworkGraph(
            GraphSchemaVersion: NetworkGraphContract.GraphSchemaVersionV1,
            SourceSnapshotSchemaVersion: NetworkSnapshotSchema.Version,
            SourceSnapshotToolVersion: "0.1.0",
            GeneratedAt: new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero),
            ScopeDescription: "/subscriptions/sub-1",
            Nodes: nodes,
            Edges: edges,
            Groups: groups,
            Metadata: new Dictionary<string, string> { ["generator"] = "test" });
    }
}
