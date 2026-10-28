using Radivum.Graph;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Covers how declared links become graph nodes and edges: the target is named, it is not folded into the
/// ownership hierarchy, and a snapshot written before links existed keeps exactly the placeholder it had.
/// </summary>
public sealed class NetworkGraphRelatedResourceTests
{
    private const string SubscriptionId = "sub-1";
    private const string ProviderScope = "/subscriptions/" + SubscriptionId + "/resourceGroups/rg-app-prod/providers";
    private const string InterfaceId = ProviderScope + "/Microsoft.Network/networkInterfaces/nic-vm01";
    private const string VirtualMachineId = ProviderScope + "/Microsoft.Compute/virtualMachines/vm-app-01";
    private const string EndpointId = ProviderScope + "/Microsoft.Network/privateEndpoints/pe-sql";
    private const string SecondEndpointId = ProviderScope + "/Microsoft.Network/privateEndpoints/pe-sql-2";
    private const string SqlServerId = ProviderScope + "/Microsoft.Sql/servers/sql-prod";

    private static readonly DateTimeOffset GeneratedAt = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly CollectionMetadata Evidence = new(
        ItemType: "Resources",
        Source: "AzureResourceGraph",
        Timestamp: GeneratedAt,
        Permission: "Reader",
        Status: "Available",
        Completeness: 1d,
        Errors: []);

    [Fact]
    public void Build_GivenAHostedOnLink_DrawsANamedRelatedNodeAndAnEdge()
    {
        var graph = Build(SnapshotWith(
            interfaces: [Interface(InterfaceId)],
            links: [Link(InterfaceId, VirtualMachineId, LinkedResourceKinds.HostedOn)]));

        var node = Assert.Single(graph.Nodes, node => SameId(node.Id, VirtualMachineId));
        Assert.Equal("VirtualMachine", node.NodeType);
        Assert.Equal("vm-app-01", node.DisplayName);
        Assert.Equal("Microsoft.Compute/virtualMachines", node.AzureResourceType);
        Assert.Equal("rg-app-prod", node.ResourceGroup);
        Assert.Equal(LinkedResourceInfo.ResolutionDerived, node.Metadata?["resolution"]);

        var edge = Assert.Single(graph.Edges, edge => edge.RelationshipType == LinkedResourceKinds.HostedOn);
        Assert.Equal(InterfaceId.ToLowerInvariant(), edge.SourceNodeId);
        Assert.Equal(VirtualMachineId.ToLowerInvariant(), edge.TargetNodeId);
        Assert.Equal(VirtualMachineId, edge.AzureResourceId);
    }

    [Fact]
    public void Build_GivenARelatedNode_DoesNotAttachItToTheOwnershipHierarchy()
    {
        var graph = Build(SnapshotWith(
            interfaces: [Interface(InterfaceId)],
            links: [Link(InterfaceId, VirtualMachineId, LinkedResourceKinds.HostedOn)]));

        var node = Assert.Single(graph.Nodes, node => SameId(node.Id, VirtualMachineId));

        // A related resource was not collected, so the ownership axis must not claim it (ADR-0001).
        Assert.Null(node.ParentNodeId);
        Assert.DoesNotContain(graph.Groups, group => group.NodeIds.Any(nodeId => SameId(nodeId, VirtualMachineId)));
    }

    [Fact]
    public void Build_GivenAPrivateEndpointLink_ReplacesTheGenericPlaceholder()
    {
        var graph = Build(SnapshotWith(
            endpoints: [Endpoint(EndpointId, SqlServerId)],
            links: [Link(EndpointId, SqlServerId, LinkedResourceKinds.PrivateEndpointFor, subResource: "sqlServer")]));

        Assert.DoesNotContain(graph.Nodes, node => node.DisplayName == "Private link target");

        var node = Assert.Single(graph.Nodes, node => SameId(node.Id, SqlServerId));
        Assert.Equal("SqlServer", node.NodeType);
        Assert.Equal("sql-prod", node.DisplayName);

        var edge = Assert.Single(graph.Edges, edge => edge.RelationshipType == LinkedResourceKinds.PrivateEndpointFor);
        Assert.Equal("sqlServer", edge.Metadata?["subResource"]);
    }

    [Fact]
    public void Build_GivenAnEndpointInterfaceLink_ConnectsTwoCollectedResourcesAndChipsNothing()
    {
        var endpointId = ProviderScope + "/Microsoft.Network/privateEndpoints/pe-blob";
        var interfaceId = ProviderScope + "/Microsoft.Network/networkInterfaces/pe-blob.nic.0f2c";

        var graph = Build(SnapshotWith(
            interfaces: [Interface(interfaceId)],
            endpoints: [Endpoint(endpointId, SqlServerId)],
            links: [Link(interfaceId, endpointId, LinkedResourceKinds.AttachedTo)]));

        var edge = Assert.Single(graph.Edges, edge => edge.RelationshipType == LinkedResourceKinds.AttachedTo);
        Assert.Equal(interfaceId.ToLowerInvariant(), edge.SourceNodeId);
        Assert.Equal(endpointId.ToLowerInvariant(), edge.TargetNodeId);
        Assert.Equal("Network interface attached to private endpoint", edge.DisplayName);

        // Both ends were collected, so the endpoint keeps the identity, the parent and the absent link origin
        // collection gave it: this link joins inventory, it does not add a related node.
        var node = Assert.Single(graph.Nodes, node => SameId(node.Id, endpointId));
        Assert.Equal("PrivateEndpoint", node.NodeType);
        Assert.Equal("pe-blob", node.DisplayName);
        Assert.NotNull(node.ParentNodeId);
        Assert.Null(node.Metadata);
    }

    [Fact]
    public void Build_GivenTwoEndpointsFrontingTheSameTarget_KeepsOneEdgePerEndpoint()
    {
        var graph = Build(SnapshotWith(
            endpoints: [Endpoint(EndpointId, SqlServerId), Endpoint(SecondEndpointId, SqlServerId)],
            links:
            [
                Link(EndpointId, SqlServerId, LinkedResourceKinds.PrivateEndpointFor),
                Link(SecondEndpointId, SqlServerId, LinkedResourceKinds.PrivateEndpointFor),
            ]));

        var edges = graph.Edges
            .Where(edge => edge.RelationshipType == LinkedResourceKinds.PrivateEndpointFor)
            .ToArray();

        Assert.Equal(2, edges.Length);
        Assert.Equal(2, edges.Select(edge => edge.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Single(graph.Nodes, node => SameId(node.Id, SqlServerId));
    }

    [Fact]
    public void Build_GivenASnapshotWithoutLinks_KeepsTheLegacyPlaceholder()
    {
        var graph = Build(SnapshotWith(endpoints: [Endpoint(EndpointId, SqlServerId)]));

        var node = Assert.Single(graph.Nodes, node => SameId(node.Id, SqlServerId));
        Assert.Equal("PrivateLinkService", node.NodeType);
        Assert.Equal("Private link target", node.DisplayName);
        Assert.Equal("inferred", node.Metadata?["origin"]);
    }

    [Fact]
    public void Build_GivenALinkWhoseSourceWasNotCollected_DropsTheLink()
    {
        var missingInterfaceId = ProviderScope + "/Microsoft.Network/networkInterfaces/nic-missing";

        var graph = Build(SnapshotWith(
            links: [Link(missingInterfaceId, VirtualMachineId, LinkedResourceKinds.HostedOn)]));

        Assert.DoesNotContain(graph.Nodes, node => SameId(node.Id, VirtualMachineId));
        Assert.DoesNotContain(graph.Edges, edge => edge.RelationshipType == LinkedResourceKinds.HostedOn);
    }

    private static NetworkGraph Build(NetworkSnapshot snapshot) =>
        new NetworkGraphBuilder().Build(snapshot, GeneratedAt);

    private static NetworkSnapshot SnapshotWith(
        IReadOnlyList<NetworkInterfaceResource>? interfaces = null,
        IReadOnlyList<PrivateEndpointResource>? endpoints = null,
        IReadOnlyList<LinkedResourceInfo>? links = null)
    {
        var snapshot = TestSnapshotFactory.Create();

        return snapshot with
        {
            Resources = snapshot.Resources with
            {
                NetworkInterfaces = interfaces ?? [],
                PrivateEndpoints = endpoints ?? [],
                LinkedResources = links,
            },
        };
    }

    private static NetworkInterfaceResource Interface(string id) => new(
        Id: id,
        Name: "nic-vm01",
        ResourceGroup: "rg-app-prod",
        Location: "westeurope",
        VirtualNetworkId: null,
        SubnetId: null,
        NetworkSecurityGroupId: null,
        PublicIpAddressId: null,
        PrivateIpAddress: "10.0.1.4",
        Metadata: Evidence);

    private static PrivateEndpointResource Endpoint(string id, string targetResourceId) => new(
        Id: id,
        Name: id[(id.LastIndexOf('/') + 1)..],
        ResourceGroup: "rg-app-prod",
        Location: "westeurope",
        SubnetId: null,
        PrivateLinkServiceIds: [targetResourceId],
        Metadata: Evidence);

    private static LinkedResourceInfo Link(
        string sourceResourceId,
        string targetResourceId,
        string linkKind,
        string? subResource = null)
    {
        var target = ArmResourceId.ParseOrNull(targetResourceId);

        return new LinkedResourceInfo(
            SourceResourceId: sourceResourceId,
            TargetResourceId: targetResourceId,
            LinkKind: linkKind,
            Resolution: LinkedResourceInfo.ResolutionDerived,
            Metadata: Evidence,
            SubResource: subResource,
            DisplayName: target?.Name,
            AzureResourceType: target?.AzureResourceType,
            ResourceGroup: target?.ResourceGroup);
    }

    private static bool SameId(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
