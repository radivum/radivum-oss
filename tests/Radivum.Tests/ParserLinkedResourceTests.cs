using System.Text.Json;
using Radivum.Azure;
using Radivum.Graph;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Covers the references the collector reads out of payloads it already has. Each one is declared evidence: the
/// target's identity is derived from its id, so naming a host or a private-link target costs no extra query.
/// </summary>
public sealed class ParserLinkedResourceTests
{
    private const string SubscriptionId = "11111111-1111-1111-1111-111111111111";
    private const string NetworkInterfaceType = "Microsoft.Network/networkInterfaces";
    private const string PrivateEndpointType = "Microsoft.Network/privateEndpoints";
    private const string VirtualNetworkType = "Microsoft.Network/virtualNetworks";
    private const string ProviderScope = "/subscriptions/" + SubscriptionId + "/resourceGroups/rg-app-prod/providers";

    private static readonly DateTimeOffset CollectedAt = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> ItemTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [NetworkInterfaceType] = "NICs",
        [PrivateEndpointType] = "PrivateEndpoints",
        [VirtualNetworkType] = "VNets",
    };

    [Fact]
    public void BuildSnapshot_GivenAPeeringOutOfTheCollectedScope_RecordsItAsEvidence()
    {
        var vnetId = $"{ProviderScope}/Microsoft.Network/virtualNetworks/vnet-app";
        var remoteId = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-hub/providers/Microsoft.Network/virtualNetworks/vnet-hub";

        var snapshot = BuildSnapshot((VirtualNetworkType, VNetRow(vnetId, "vnet-app", remoteId)));

        // The remote network is named from its id and never looked up: the collection still reads exactly the
        // scope the operator chose, and the topology no longer looks closed when it is not.
        var link = Assert.Single(snapshot.Resources.LinkedResources!);
        Assert.Equal(vnetId, link.SourceResourceId);
        Assert.Equal(remoteId, link.TargetResourceId);
        Assert.Equal(LinkedResourceKinds.PeeredWith, link.LinkKind);
        Assert.Equal(LinkedResourceInfo.ResolutionDerived, link.Resolution);
        Assert.Equal("vnet-hub", link.DisplayName);
        Assert.Equal("rg-hub", link.ResourceGroup);
    }

    [Fact]
    public void BuildSnapshot_GivenAPeeringInsideTheCollectedScope_RecordsNoLink()
    {
        var vnetId = $"{ProviderScope}/Microsoft.Network/virtualNetworks/vnet-app";
        var peerId = $"{ProviderScope}/Microsoft.Network/virtualNetworks/vnet-data";

        var snapshot = BuildSnapshot(
            (VirtualNetworkType, VNetRow(vnetId, "vnet-app", peerId)),
            (VirtualNetworkType, VNetRow(peerId, "vnet-data", vnetId)));

        // A peer the collection read is drawn as a peering between two collected networks, which is evidence the
        // graph already had; it is not an external reference.
        Assert.Empty(snapshot.Resources.LinkedResources!);
    }

    private static object VNetRow(string id, string name, string remoteVirtualNetworkId) => new
    {
        id,
        name,
        location = "eastus",
        resourceGroup = "rg-app-prod",
        subscriptionId = SubscriptionId,
        properties = new
        {
            addressSpace = new { addressPrefixes = new[] { "10.0.0.0/16" } },
            subnets = Array.Empty<object>(),
            virtualNetworkPeerings = new[]
            {
                new
                {
                    name = $"to-{LastSegment(remoteVirtualNetworkId)}",
                    properties = new
                    {
                        remoteVirtualNetwork = new { id = remoteVirtualNetworkId },
                        peeringState = "Connected",
                    },
                },
            },
        },
    };

    [Fact]
    public void BuildSnapshot_GivenAnInterfaceAttachedToAVirtualMachine_RecordsAHostedOnLink()
    {
        var interfaceId = $"{ProviderScope}/Microsoft.Network/networkInterfaces/nic-vm01";
        var virtualMachineId = $"{ProviderScope}/Microsoft.Compute/virtualMachines/vm-app-01";

        var snapshot = BuildSnapshot((NetworkInterfaceType, InterfaceRow(interfaceId, virtualMachineId)));

        var link = Assert.Single(snapshot.Resources.LinkedResources!);
        Assert.Equal(interfaceId, link.SourceResourceId);
        Assert.Equal(virtualMachineId, link.TargetResourceId);
        Assert.Equal(LinkedResourceKinds.HostedOn, link.LinkKind);
        Assert.Equal(LinkedResourceInfo.ResolutionDerived, link.Resolution);
        Assert.Equal("vm-app-01", link.DisplayName);
        Assert.Equal("Microsoft.Compute/virtualMachines", link.AzureResourceType);
        Assert.Equal("rg-app-prod", link.ResourceGroup);
    }

    [Fact]
    public void BuildSnapshot_GivenAnInterfaceWithNoHost_RecordsNoLink()
    {
        var snapshot = BuildSnapshot(
            (NetworkInterfaceType, InterfaceRow($"{ProviderScope}/Microsoft.Network/networkInterfaces/nic-loose", virtualMachineId: null)));

        Assert.Empty(snapshot.Resources.LinkedResources!);
    }

    [Fact]
    public void BuildSnapshot_GivenAPrivateEndpointInterface_RecordsAnAttachedToLink()
    {
        var interfaceId = $"{ProviderScope}/Microsoft.Network/networkInterfaces/pe-blob.nic.0f2c";
        var endpointId = $"{ProviderScope}/Microsoft.Network/privateEndpoints/pe-blob";
        var storageAccountId = $"{ProviderScope}/Microsoft.Storage/storageAccounts/stgblob";

        var snapshot = BuildSnapshot(
            (NetworkInterfaceType, InterfaceRow(interfaceId, virtualMachineId: null, privateEndpointId: endpointId)),
            (PrivateEndpointType, EndpointRow(endpointId, storageAccountId, groupIds: ["blob"], manuallyApproved: false)));

        // An endpoint's interface has no host, so without this reference it would read as an unreferenced NIC
        // sharing a subnet with an unrelated endpoint.
        var link = Assert.Single(
            snapshot.Resources.LinkedResources!,
            candidate => candidate.LinkKind == LinkedResourceKinds.AttachedTo);
        Assert.Equal(interfaceId, link.SourceResourceId);
        Assert.Equal(endpointId, link.TargetResourceId);
        Assert.Equal("pe-blob", link.DisplayName);
        Assert.Equal("Microsoft.Network/privateEndpoints", link.AzureResourceType);
        Assert.Equal(LinkedResourceInfo.ResolutionDerived, link.Resolution);
        Assert.Null(link.SubResource);
        Assert.DoesNotContain(snapshot.Resources.LinkedResources!, candidate => candidate.LinkKind == LinkedResourceKinds.HostedOn);
    }

    [Fact]
    public void BuildSnapshot_GivenAnAutomaticallyApprovedEndpoint_RecordsTheTargetAndItsSubResource()
    {
        var endpointId = $"{ProviderScope}/Microsoft.Network/privateEndpoints/pe-sql";
        var sqlServerId = $"{ProviderScope}/Microsoft.Sql/servers/sql-prod";

        var snapshot = BuildSnapshot(
            (PrivateEndpointType, EndpointRow(endpointId, sqlServerId, groupIds: ["sqlServer"], manuallyApproved: false)));

        var link = Assert.Single(snapshot.Resources.LinkedResources!);
        Assert.Equal(LinkedResourceKinds.PrivateEndpointFor, link.LinkKind);
        Assert.Equal(sqlServerId, link.TargetResourceId);
        Assert.Equal("sqlServer", link.SubResource);
        Assert.Equal("sql-prod", link.DisplayName);
        Assert.Equal("Microsoft.Sql/servers", link.AzureResourceType);
    }

    [Fact]
    public void BuildSnapshot_GivenAManuallyApprovedEndpoint_RecordsTheTargetAndTheCollectedServiceId()
    {
        var endpointId = $"{ProviderScope}/Microsoft.Network/privateEndpoints/pe-vault";
        var keyVaultId = $"{ProviderScope}/Microsoft.KeyVault/vaults/kv-prod";

        var snapshot = BuildSnapshot(
            (PrivateEndpointType, EndpointRow(endpointId, keyVaultId, groupIds: ["vault"], manuallyApproved: true)));

        Assert.Equal(keyVaultId, Assert.Single(snapshot.Resources.PrivateEndpoints).PrivateLinkServiceIds.Single());
        Assert.Equal(keyVaultId, Assert.Single(snapshot.Resources.LinkedResources!).TargetResourceId);
    }

    [Fact]
    public void BuildSnapshot_GivenAnEndpointOnADatabase_NamesTheDatabaseRatherThanItsServer()
    {
        var endpointId = $"{ProviderScope}/Microsoft.Network/privateEndpoints/pe-db";
        var databaseId = $"{ProviderScope}/Microsoft.Sql/servers/sql-prod/databases/db-orders";

        var snapshot = BuildSnapshot(
            (PrivateEndpointType, EndpointRow(endpointId, databaseId, groupIds: ["sqlServer"], manuallyApproved: false)));

        var link = Assert.Single(snapshot.Resources.LinkedResources!);
        Assert.Equal("db-orders", link.DisplayName);
        Assert.Equal("Microsoft.Sql/servers/databases", link.AzureResourceType);
    }

    [Fact]
    public void BuildSnapshot_GivenTheSameRowsInAnotherOrder_ProducesTheSameLinks()
    {
        var firstInterface = $"{ProviderScope}/Microsoft.Network/networkInterfaces/nic-b";
        var secondInterface = $"{ProviderScope}/Microsoft.Network/networkInterfaces/nic-a";
        var firstMachine = $"{ProviderScope}/Microsoft.Compute/virtualMachines/vm-b";
        var secondMachine = $"{ProviderScope}/Microsoft.Compute/virtualMachines/vm-a";

        var forward = BuildSnapshot(
            (NetworkInterfaceType, InterfaceRow(firstInterface, firstMachine)),
            (NetworkInterfaceType, InterfaceRow(secondInterface, secondMachine)));
        var reversed = BuildSnapshot(
            (NetworkInterfaceType, InterfaceRow(secondInterface, secondMachine)),
            (NetworkInterfaceType, InterfaceRow(firstInterface, firstMachine)));

        Assert.Equal(
            forward.Resources.LinkedResources!.Select(link => link.SourceResourceId),
            reversed.Resources.LinkedResources!.Select(link => link.SourceResourceId));
    }

    [Fact]
    public void BuildSnapshot_GivenATargetThatIsNotAResourceId_KeepsTheLinkWithoutADerivedName()
    {
        var endpointId = $"{ProviderScope}/Microsoft.Network/privateEndpoints/pe-odd";

        var snapshot = BuildSnapshot(
            (PrivateEndpointType, EndpointRow(endpointId, "not-a-resource-id", groupIds: [], manuallyApproved: false)));

        var link = Assert.Single(snapshot.Resources.LinkedResources!);
        Assert.Equal("not-a-resource-id", link.TargetResourceId);
        Assert.Null(link.DisplayName);
        Assert.Null(link.AzureResourceType);
        Assert.Equal(LinkedResourceInfo.ResolutionDerived, link.Resolution);
    }

    [Fact]
    public void BuildSnapshot_ThenGraph_DrawsTheHostAsANamedRelatedNode()
    {
        var interfaceId = $"{ProviderScope}/Microsoft.Network/networkInterfaces/nic-vm01";
        var virtualMachineId = $"{ProviderScope}/Microsoft.Compute/virtualMachines/vm-app-01";

        var snapshot = BuildSnapshot((NetworkInterfaceType, InterfaceRow(interfaceId, virtualMachineId)));
        var graph = new NetworkGraphBuilder().Build(snapshot, CollectedAt);

        // The seam that matters: what the collection parsed is what the graph draws, with no second derivation.
        var node = Assert.Single(
            graph.Nodes,
            node => string.Equals(node.Id, virtualMachineId, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("vm-app-01", node.DisplayName);
        Assert.Equal("VirtualMachine", node.NodeType);
        Assert.Contains(graph.Edges, edge => edge.RelationshipType == LinkedResourceKinds.HostedOn);
    }

    private static NetworkSnapshot BuildSnapshot(params (string ResourceType, object Row)[] rows)
    {
        var outcomes = new Dictionary<string, ResourceGraphCollectionOutcome>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in rows.GroupBy(row => row.ResourceType, StringComparer.OrdinalIgnoreCase))
        {
            outcomes[group.Key] = new ResourceGraphCollectionOutcome(
                ResourceType: group.Key,
                ItemType: ItemTypes[group.Key],
                Rows: group.Select(row => JsonSerializer.SerializeToElement(row.Row)).ToArray(),
                IsTruncated: false,
                ReachedPageLimit: false,
                PageCount: 1,
                TotalRecords: group.Count(),
                Errors: []);
        }

        var request = new SnapshotCollectionRequest($"/subscriptions/{SubscriptionId}", SubscriptionId, null);
        return Parser.BuildSnapshot(outcomes, request, "tenant-1", CollectedAt, "0.1.0", "DefaultAzureCredential");
    }

    private static object InterfaceRow(string id, string? virtualMachineId, string? privateEndpointId = null) => new
    {
        id,
        name = LastSegment(id),
        location = "westeurope",
        resourceGroup = "rg-app-prod",
        subscriptionId = SubscriptionId,
        properties = new
        {
            ipConfigurations = Array.Empty<object>(),
            virtualMachine = virtualMachineId is null ? null : new { id = virtualMachineId },
            privateEndpoint = privateEndpointId is null ? null : new { id = privateEndpointId },
        },
    };

    private static object EndpointRow(string id, string targetResourceId, string[] groupIds, bool manuallyApproved) => new
    {
        id,
        name = LastSegment(id),
        location = "westeurope",
        resourceGroup = "rg-app-prod",
        subscriptionId = SubscriptionId,
        properties = new
        {
            subnet = new { id = $"{ProviderScope}/Microsoft.Network/virtualNetworks/vnet-app/subnets/snet-private" },
            privateLinkServiceConnections = manuallyApproved
                ? Array.Empty<object>()
                : new object[] { new { properties = new { privateLinkServiceId = targetResourceId, groupIds } } },
            manualPrivateLinkServiceConnections = manuallyApproved
                ? new object[] { new { properties = new { privateLinkServiceId = targetResourceId, groupIds } } }
                : Array.Empty<object>(),
        },
    };

    private static string LastSegment(string id) => id[(id.LastIndexOf('/') + 1)..];
}
