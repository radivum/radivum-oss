using System.Text.Json;
using Radivum.Azure;
using Xunit;

namespace Radivum.Tests;

public sealed class ParserCollectionMetadataTests
{
    [Fact]
    public void BuildSnapshot_EmitsPerResourceStatusAndScopeValidation()
    {
        const string requestedSubscriptionId = "11111111-1111-1111-1111-111111111111";
        const string discoveredSubscriptionId = "22222222-2222-2222-2222-222222222222";
        var request = new SnapshotCollectionRequest($"/subscriptions/{requestedSubscriptionId}", requestedSubscriptionId, null);
        var collectedAt = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

        var vnetRow = JsonSerializer.SerializeToElement(new
        {
            id = $"/subscriptions/{discoveredSubscriptionId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1",
            name = "vnet-1",
            location = "eastus",
            resourceGroup = "rg",
            subscriptionId = discoveredSubscriptionId,
            properties = new
            {
                addressSpace = new
                {
                    addressPrefixes = new[] { "10.0.0.0/16" }
                },
                subnets = Array.Empty<object>(),
                virtualNetworkPeerings = Array.Empty<object>()
            }
        });

        var outcomes = new Dictionary<string, ResourceGraphCollectionOutcome>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.Network/virtualNetworks"] = new ResourceGraphCollectionOutcome(
                ResourceType: "Microsoft.Network/virtualNetworks",
                ItemType: "VNets",
                Rows: new[] { vnetRow },
                IsTruncated: false,
                ReachedPageLimit: false,
                PageCount: 1,
                TotalRecords: 1,
                Errors: []),
            ["Microsoft.Network/networkSecurityGroups"] = new ResourceGraphCollectionOutcome(
                ResourceType: "Microsoft.Network/networkSecurityGroups",
                ItemType: "NSGs",
                Rows: Array.Empty<JsonElement>(),
                IsTruncated: false,
                ReachedPageLimit: false,
                PageCount: 1,
                TotalRecords: null,
                Errors: new[] { "Authorization failed for NSGs." })
        };

        var snapshot = Parser.BuildSnapshot(outcomes, request, "tenant-1", collectedAt, "0.1.0", "DefaultAzureCredential");

        var nsgMetadata = snapshot.CollectionMetadata.Single(item => item.ItemType == "NSGs");
        var scopeValidation = snapshot.CollectionMetadata.Single(item => item.ItemType == "ScopeValidation");
        var resourcesMetadata = snapshot.CollectionMetadata.Single(item => item.ItemType == "Resources");

        Assert.Equal("Unavailable", nsgMetadata.Status);
        Assert.Equal(0d, nsgMetadata.Completeness);
        Assert.Equal("Partial", scopeValidation.Status);
        Assert.Contains(scopeValidation.Errors, error => error.Contains(requestedSubscriptionId, StringComparison.Ordinal));
        Assert.Equal("Partial", resourcesMetadata.Status);

        // The scope is what was requested. A row from another subscription is a scope violation, not an implicit
        // widening of the analysis.
        Assert.Equal([requestedSubscriptionId], snapshot.SubscriptionIds);
        Assert.DoesNotContain(discoveredSubscriptionId, snapshot.SubscriptionIds);
        Assert.Equal(requestedSubscriptionId, snapshot.Scope.SubscriptionId);
    }

    [Fact]
    public void BuildSnapshot_GivenTwoSubscriptions_RecordsScopeAndCoverageWithoutASingularSubscription()
    {
        const string firstSubscriptionId = "11111111-1111-1111-1111-111111111111";
        const string secondSubscriptionId = "22222222-2222-2222-2222-222222222222";
        var collectedAt = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
        var request = new SnapshotCollectionRequest(
            $"subscriptions {firstSubscriptionId}, {secondSubscriptionId}",
            firstSubscriptionId,
            ResourceGroup: null,
            SubscriptionIds: [firstSubscriptionId, secondSubscriptionId]);

        var outcomes = new Dictionary<string, ResourceGraphCollectionOutcome>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.Network/virtualNetworks"] = new ResourceGraphCollectionOutcome(
                ResourceType: "Microsoft.Network/virtualNetworks",
                ItemType: "VNets",
                Rows: [],
                IsTruncated: false,
                ReachedPageLimit: false,
                PageCount: 1,
                TotalRecords: 0,
                Errors: []),
        };

        var probes = new[]
        {
            new SubscriptionProbe(firstSubscriptionId, "First", "Enabled", "tenant-1", Error: null),
            new SubscriptionProbe(secondSubscriptionId, "Second", "Enabled", "tenant-1", Error: null),
        };

        var snapshot = Parser.BuildSnapshot(
            outcomes,
            request,
            "tenant-1",
            collectedAt,
            "0.1.0",
            "DefaultAzureCredential",
            probes);

        Assert.Null(snapshot.Scope.SubscriptionId);
        Assert.Null(snapshot.Scope.SubscriptionName);
        Assert.Equal([firstSubscriptionId, secondSubscriptionId], snapshot.Scope.SubscriptionIds);
        Assert.Equal(
            [firstSubscriptionId, secondSubscriptionId],
            snapshot.Scope.Subscriptions!.Select(entry => entry.Id));
        Assert.Equal(["First", "Second"], snapshot.Scope.Subscriptions!.Select(entry => entry.Name));

        var coverage = snapshot.CollectionMetadata
            .Where(item => item.ItemType == "Subscription")
            .ToList();

        Assert.Equal(2, coverage.Count);
        Assert.All(coverage, item => Assert.Equal("Available", item.Status));
        Assert.Equal(
            [firstSubscriptionId, secondSubscriptionId],
            coverage.Select(item => item.ScopeId!));
    }

    [Fact]
    public void BuildSnapshot_GivenASingleSubscription_WritesNoSubscriptionCoverageRow()
    {
        const string subscriptionId = "11111111-1111-1111-1111-111111111111";
        var request = new SnapshotCollectionRequest($"/subscriptions/{subscriptionId}", subscriptionId, null);
        var outcomes = new Dictionary<string, ResourceGraphCollectionOutcome>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.Network/virtualNetworks"] = new ResourceGraphCollectionOutcome(
                ResourceType: "Microsoft.Network/virtualNetworks",
                ItemType: "VNets",
                Rows: [],
                IsTruncated: false,
                ReachedPageLimit: false,
                PageCount: 1,
                TotalRecords: 0,
                Errors: []),
        };

        var snapshot = Parser.BuildSnapshot(
            outcomes,
            request,
            "tenant-1",
            new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero),
            "0.1.0",
            "DefaultAzureCredential");

        Assert.DoesNotContain(snapshot.CollectionMetadata, item => item.ItemType == "Subscription");
        Assert.Null(snapshot.Scope.Subscriptions);
    }
}
