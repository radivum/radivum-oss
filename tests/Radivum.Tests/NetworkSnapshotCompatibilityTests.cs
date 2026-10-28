using System.Text;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Pins the compatibility rule the related-resource evidence depends on: <c>linkedResources</c> is an optional
/// additive member of schema 0.1, so a snapshot written before it existed still loads, and a snapshot that found
/// no links keeps the bytes it already produced.
/// </summary>
public sealed class NetworkSnapshotCompatibilityTests
{
    private const string NetworkInterfaceId =
        "/subscriptions/sub-1/resourceGroups/rg-app-prod/providers/Microsoft.Network/networkInterfaces/nic-vm01";

    private const string VirtualMachineId =
        "/subscriptions/sub-1/resourceGroups/rg-app-prod/providers/Microsoft.Compute/virtualMachines/vm-app-01";

    /// <summary>A snapshot exactly as schema 0.1 produced it before the linked-resource member existed.</summary>
    private const string SnapshotWithoutLinkedResources = """
        {
          "schemaVersion": "0.1",
          "toolVersion": "0.1.0",
          "tenantId": "tenant-1",
          "subscriptionIds": ["sub-1"],
          "accountContext": {
            "accountId": null,
            "accountName": null,
            "environmentName": "AzureCloud",
            "credentialSource": "DefaultAzureCredential"
          },
          "collectedAt": "2026-10-20T00:00:00+00:00",
          "scope": {
            "scopeDescription": "/subscriptions/sub-1",
            "subscriptionId": "sub-1",
            "resourceGroup": null,
            "subscriptionIds": ["sub-1"],
            "subscriptionName": "Contoso Production"
          },
          "resources": {
            "vnets": [],
            "networkSecurityGroups": [],
            "routeTables": [],
            "networkInterfaces": [],
            "publicIpAddresses": [],
            "privateEndpoints": [],
            "privateDnsZones": [],
            "azureFirewalls": [],
            "virtualHubs": [],
            "virtualWans": [],
            "expressRouteCircuits": [],
            "virtualNetworkGateways": []
          },
          "effectiveEvidence": {
            "effectiveRoutes": [],
            "effectiveNetworkSecurityGroups": [],
            "nextHopResults": []
          },
          "relationships": [],
          "collectionMetadata": []
        }
        """;

    [Fact]
    public async Task DeserializeAsync_GivenASnapshotWrittenBeforeLinkedResources_LoadsWithNoLinks()
    {
        var serializer = new NetworkSnapshotSerializer();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SnapshotWithoutLinkedResources));

        var snapshot = await serializer.DeserializeAsync(stream, CancellationToken.None);

        Assert.Equal("0.1", snapshot.SchemaVersion);
        Assert.Null(snapshot.Resources.LinkedResources);
    }

    [Fact]
    public async Task SerializeAsync_GivenASnapshotThatPredatesTheScopeSet_AddsNoMemberAndStaysStable()
    {
        var serializer = new NetworkSnapshotSerializer();
        NetworkSnapshot snapshot;

        await using (var input = new MemoryStream(Encoding.UTF8.GetBytes(SnapshotWithoutLinkedResources)))
        {
            snapshot = await serializer.DeserializeAsync(input, CancellationToken.None);
        }

        var first = await SerializeAsync(serializer, snapshot);

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(first)))
        {
            snapshot = await serializer.DeserializeAsync(stream, CancellationToken.None);
        }

        var second = await SerializeAsync(serializer, snapshot);

        // The optional scope members a multi-subscription collection writes are omitted for a single
        // subscription, so re-serializing an existing snapshot neither adds them nor moves any byte.
        Assert.DoesNotContain("\"subscriptions\"", first, StringComparison.Ordinal);
        Assert.DoesNotContain("scopeId", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    private static async Task<string> SerializeAsync(NetworkSnapshotSerializer serializer, NetworkSnapshot snapshot)
    {
        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, snapshot, CancellationToken.None);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task SerializeAsync_GivenASnapshotWithNoLinks_OmitsTheMember()
    {
        var serializer = new NetworkSnapshotSerializer();
        var snapshot = TestSnapshotFactory.Create();

        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, snapshot, CancellationToken.None);

        var written = Encoding.UTF8.GetString(stream.ToArray());

        // Absent data leaves the contract and the digest a manifest records untouched, which is what keeps the
        // member compatible without a schema-version change.
        Assert.DoesNotContain("linkedResources", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SerializeAndDeserialize_RoundTripsALinkAndItsResolutionState()
    {
        var serializer = new NetworkSnapshotSerializer();
        var original = TestSnapshotFactory.Create();
        var withLink = original with
        {
            Resources = original.Resources with
            {
                LinkedResources =
                [
                    new LinkedResourceInfo(
                        SourceResourceId: NetworkInterfaceId,
                        TargetResourceId: VirtualMachineId,
                        LinkKind: LinkedResourceKinds.HostedOn,
                        Resolution: LinkedResourceInfo.ResolutionDerived,
                        Metadata: Evidence(),
                        DisplayName: "vm-app-01",
                        AzureResourceType: "Microsoft.Compute/virtualMachines",
                        ResourceGroup: "rg-app-prod"),
                ],
            },
        };

        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, withLink, CancellationToken.None);
        stream.Position = 0;

        var deserialized = await serializer.DeserializeAsync(stream, CancellationToken.None);
        var link = Assert.Single(deserialized.Resources.LinkedResources!);

        Assert.Equal(LinkedResourceKinds.HostedOn, link.LinkKind);
        Assert.Equal(LinkedResourceInfo.ResolutionDerived, link.Resolution);
        Assert.Equal(VirtualMachineId, link.TargetResourceId);
        Assert.Equal("vm-app-01", link.DisplayName);
    }

    private static CollectionMetadata Evidence() => new(
        ItemType: "LinkedResources",
        Source: "AzureResourceGraph",
        Timestamp: new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
        Permission: "Reader",
        Status: "Available",
        Completeness: 1d,
        Errors: []);
}
