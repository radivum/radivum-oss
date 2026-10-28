using Radivum.Model;

namespace Radivum.Tests;

internal static class TestSnapshotFactory
{
    public static NetworkSnapshot Create(
        string? tenantId = null,
        IReadOnlyList<CollectionMetadata>? collectionMetadata = null,
        string? subscriptionName = null)
    {
        var timestamp = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
        var metadata = new CollectionMetadata(
            ItemType: "Resources",
            Source: "AzureResourceGraph",
            Timestamp: timestamp,
            Permission: "Reader",
            Status: "Available",
            Completeness: 1d,
            Errors: []);

        var vnet = new VNetResource(
            Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1",
            Name: "vnet-1",
            ResourceGroup: "rg",
            Location: "eastus",
            AddressSpace: ["10.0.0.0/16"],
            Subnets:
            [
                new SubnetResource(
                    Id: "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1/subnets/app",
                    Name: "app",
                    AddressPrefix: "10.0.1.0/24",
                    NetworkSecurityGroupId: null,
                    RouteTableId: null,
                    Metadata: metadata)
            ],
            Peerings: [],
            Metadata: metadata);

        return new NetworkSnapshot(
            SchemaVersion: NetworkSnapshotSchema.Version,
            ToolVersion: "0.1.0",
            TenantId: tenantId ?? "tenant-1",
            SubscriptionIds: ["sub-1"],
            AccountContext: new AccountContextInfo("account", "operator", "AzureCloud", "DefaultAzureCredential"),
            CollectedAt: timestamp,
            Scope: new SnapshotScope("/subscriptions/sub-1", "sub-1", null, ["sub-1"], subscriptionName),
            Resources: new NetworkResourceSet(
                VNets: [vnet],
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
            CollectionMetadata: collectionMetadata ?? [metadata]);
    }
}
