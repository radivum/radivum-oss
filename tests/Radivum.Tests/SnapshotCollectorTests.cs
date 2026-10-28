using System.Net;
using System.Text.Json;
using Azure.Core;
using Radivum.Azure;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Covers the subscription name the collector resolves for a collection scope. The name is enrichment, so a
/// read that ARM refuses or a transport that fails must degrade to the identifier the scope already carries
/// rather than failing a collection whose resources were read successfully.
/// </summary>
public sealed class SnapshotCollectorTests
{
    private const string Sub1 = "11111111-1111-1111-1111-111111111111";
    private const string Sub2 = "22222222-2222-2222-2222-222222222222";
    private const string Sub3 = "33333333-3333-3333-3333-333333333333";
    private const string Sub4 = "44444444-4444-4444-4444-444444444444";

    [Fact]
    public async Task CollectAsync_GivenASubscriptionName_RecordsItOnTheScope()
    {
        var collector = CreateCollector(new StubDiscovery { Name = "Contoso Production" });

        var snapshot = await collector.CollectAsync(Request());

        Assert.Equal("Contoso Production", snapshot.Scope.SubscriptionName);
        Assert.Equal(Sub1, snapshot.Scope.SubscriptionId);
    }

    [Fact]
    public async Task CollectAsync_WhenTheNameCannotBeRead_KeepsTheIdentifierAndStillCollects()
    {
        var collector = CreateCollector(
            new StubDiscovery { Failure = new HttpRequestException("The subscription read failed.") });

        var snapshot = await collector.CollectAsync(Request());

        Assert.Null(snapshot.Scope.SubscriptionName);
        Assert.Equal(Sub1, snapshot.Scope.SubscriptionId);
    }

    [Fact]
    public async Task CollectAsync_WhenTheNameIsBlank_RecordsNothing()
    {
        var collector = CreateCollector(new StubDiscovery { Name = "   " });

        var snapshot = await collector.CollectAsync(Request());

        Assert.Null(snapshot.Scope.SubscriptionName);
    }

    [Fact]
    public async Task CollectAsync_GivenResolveLinkedResources_ConfirmsTheRelatedResource()
    {
        var collector = CreateCollector(new StubDiscovery(), new ScriptedResourceGraphClient());

        var snapshot = await collector.CollectAsync(Request(resolveLinkedResources: true));

        var link = Assert.Single(snapshot.Resources.LinkedResources!);
        Assert.Equal(LinkedResourceInfo.ResolutionResolved, link.Resolution);
        Assert.Equal("vm-app-01", link.DisplayName);
        Assert.Equal("Microsoft.Compute/virtualMachines", link.AzureResourceType);
    }

    [Fact]
    public async Task CollectAsync_WithoutResolveLinkedResources_KeepsTheTargetNamedFromItsId()
    {
        var client = new ScriptedResourceGraphClient();
        var collector = CreateCollector(new StubDiscovery(), client);

        var snapshot = await collector.CollectAsync(Request());

        var link = Assert.Single(snapshot.Resources.LinkedResources!);
        Assert.Equal(LinkedResourceInfo.ResolutionDerived, link.Resolution);
        Assert.Equal("vm-app-01", link.DisplayName);
        Assert.DoesNotContain(client.Queries, query => query.Contains("id in~", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CollectAsync_GivenTwoSubscriptions_QueriesBothAndRecordsACoverageRowEach()
    {
        var client = new ScriptedResourceGraphClient();
        var collector = CreateCollector(new StubDiscovery { Name = "Contoso" }, client);

        var snapshot = await collector.CollectAsync(Request(Sub1, Sub2));

        Assert.All(client.Subscriptions, subscriptions => Assert.Equal([Sub1, Sub2], subscriptions));
        Assert.Equal([Sub1, Sub2], snapshot.SubscriptionIds);
        Assert.Equal([Sub1, Sub2], snapshot.Scope.SubscriptionIds);

        var first = Assert.Single(
            snapshot.CollectionMetadata,
            item => item.ItemType == "Subscription" && item.ScopeId == Sub1);
        var second = Assert.Single(
            snapshot.CollectionMetadata,
            item => item.ItemType == "Subscription" && item.ScopeId == Sub2);

        Assert.Equal("Available", first.Status);
        Assert.Equal("Available", second.Status);
        Assert.Equal(1d, first.Completeness);
    }

    [Fact]
    public async Task CollectAsync_GivenTwoSubscriptions_RecordsNoSingularSubscription()
    {
        var collector = CreateCollector(new StubDiscovery());

        var snapshot = await collector.CollectAsync(Request(Sub1, Sub2));

        Assert.Null(snapshot.Scope.SubscriptionId);
        Assert.Null(snapshot.Scope.SubscriptionName);
        Assert.Equal([Sub1, Sub2], snapshot.Scope.Subscriptions!.Select(entry => entry.Id));
        Assert.All(snapshot.Scope.Subscriptions!, entry => Assert.Equal("sub-name", entry.Name));
    }

    [Fact]
    public async Task CollectAsync_WhenOneScopeCannotBeRead_CompletesWithNamedCoverage()
    {
        var discovery = new StubDiscovery { Name = "sub-name" };
        discovery.UnreadableSubscriptions.Add(Sub2);
        var collector = CreateCollector(discovery);

        var snapshot = await collector.CollectAsync(Request(Sub1, Sub2));

        var unreadable = Assert.Single(
            snapshot.CollectionMetadata,
            item => item.ItemType == "Subscription" && item.ScopeId == Sub2);
        Assert.Equal("Unavailable", unreadable.Status);
        Assert.Equal(0d, unreadable.Completeness);
        Assert.NotEmpty(unreadable.Errors);

        Assert.Equal("Available", Assert.Single(
            snapshot.CollectionMetadata,
            item => item.ItemType == "Subscription" && item.ScopeId == Sub1).Status);
    }

    [Fact]
    public async Task CollectAsync_WhenEveryScopeCannotBeRead_RefusesTheRun()
    {
        var discovery = new StubDiscovery();
        discovery.UnreadableSubscriptions.Add(Sub1);
        discovery.UnreadableSubscriptions.Add(Sub2);
        var collector = CreateCollector(discovery);

        var exception = await Assert.ThrowsAsync<CollectionScopeException>(
            () => collector.CollectAsync(Request(Sub1, Sub2)));

        Assert.Contains("None of the requested subscriptions", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectAsync_WhenASubscriptionBelongsToAnotherTenant_RefusesTheRun()
    {
        var discovery = new StubDiscovery { Name = "sub-name", Token = JwtWithTenant("tenant-a"), TenantId = "tenant-b" };
        var collector = CreateCollector(discovery);

        var exception = await Assert.ThrowsAsync<CollectionScopeException>(
            () => collector.CollectAsync(Request(Sub1, Sub2)));

        Assert.Contains("not in the tenant", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectAsync_WhenArmRejectsTheTokenTenant_RefusesTheRun()
    {
        var discovery = new StubDiscovery { Name = "sub-name", Token = JwtWithTenant("tenant-a") };
        discovery.TenantRejectedSubscriptions.Add(Sub1);
        var collector = CreateCollector(discovery);

        var exception = await Assert.ThrowsAsync<CollectionScopeException>(
            () => collector.CollectAsync(Request(Sub1)));

        Assert.Contains("InvalidAuthenticationTokenTenant", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectAsync_GivenMoreSubscriptionsThanTheCap_RejectsTheScope()
    {
        var collector = CreateCollector(new StubDiscovery());

        await Assert.ThrowsAsync<CollectionScopeException>(
            () => collector.CollectAsync(Request(Sub1, Sub2, Sub3, Sub4)));
    }

    [Fact]
    public async Task CollectAsync_GivenAResourceGroupWithTwoSubscriptions_RejectsTheScope()
    {
        var collector = CreateCollector(new StubDiscovery());
        var request = new SnapshotCollectionRequest(
            $"subscriptions {Sub1}, {Sub2}",
            Sub1,
            "rg-one",
            SubscriptionIds: [Sub1, Sub2]);

        await Assert.ThrowsAsync<CollectionScopeException>(() => collector.CollectAsync(request));
    }

    [Fact]
    public async Task CollectAsync_GivenADuplicateSubscription_RejectsTheScope()
    {
        var collector = CreateCollector(new StubDiscovery());
        var request = new SnapshotCollectionRequest(
            $"subscriptions {Sub1}",
            Sub1,
            ResourceGroup: null,
            SubscriptionIds: [Sub1, Sub1]);

        await Assert.ThrowsAsync<CollectionScopeException>(() => collector.CollectAsync(request));
    }

    private static SnapshotCollectionRequest Request(params string[] subscriptionIds)
        => Request(resolveLinkedResources: false, subscriptionIds);

    private static SnapshotCollectionRequest Request(bool resolveLinkedResources, params string[] subscriptionIds)
    {
        var ids = subscriptionIds.Length == 0 ? [Sub1] : subscriptionIds;

        return new SnapshotCollectionRequest(
            ids.Length == 1 ? $"/subscriptions/{ids[0]}" : $"subscriptions {string.Join(", ", ids)}",
            ids[0],
            ResourceGroup: null,
            resolveLinkedResources,
            ids);
    }

    /// <summary>Builds a token whose payload carries a tenant, as a real JWT does.</summary>
    private static string JwtWithTenant(string tenantId)
    {
        static string Encode(object value)
            => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        return $"{Encode(new { alg = "none" })}.{Encode(new { tid = tenantId })}.signature";
    }

    private static SnapshotCollector CreateCollector(IDiscovery discovery, IResourceGraphClient? resourceGraphClient = null)
        => new(discovery, new StubCredentialProvider(), resourceGraphClient ?? new EmptyResourceGraphClient());

    /// <summary>A discovery service whose subscription answers a test decides.</summary>
    private sealed class StubDiscovery : IDiscovery
    {
        public string Name { get; set; } = "sub-name";

        public string? TenantId { get; set; }

        public Exception? Failure { get; set; }

        public string Token { get; set; } = "test-token";

        /// <summary>Subscriptions whose metadata read is denied without naming a tenant.</summary>
        public HashSet<string> UnreadableSubscriptions { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Subscriptions ARM rejects because the token belongs to another tenant.</summary>
        public HashSet<string> TenantRejectedSubscriptions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Token);

        public Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionInfo>>([new SubscriptionInfo(Sub1, Name, "Enabled", TenantId)]);

        public Task<SubscriptionInfo> GetSubscriptionAsync(
            string subscriptionId,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException<SubscriptionInfo>(Failure);
            }

            if (TenantRejectedSubscriptions.Contains(subscriptionId))
            {
                return Task.FromException<SubscriptionInfo>(new AzureRequestException(
                    "read subscription",
                    HttpStatusCode.Unauthorized,
                    "InvalidAuthenticationTokenTenant",
                    "The access token is from the wrong issuer."));
            }

            if (UnreadableSubscriptions.Contains(subscriptionId))
            {
                return Task.FromException<SubscriptionInfo>(new AzureRequestException(
                    "read subscription",
                    HttpStatusCode.Forbidden,
                    "AuthorizationFailed",
                    $"The client does not have authorization on subscription {subscriptionId}."));
            }

            return Task.FromResult(new SubscriptionInfo(subscriptionId, Name, "Enabled", TenantId));
        }

        public Task<IReadOnlyList<ResourceGroupInfo>> ListResourceGroupsAsync(
            string subscriptionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceGroupInfo>>([]);
    }

    /// <summary>A Resource Graph client that returns no rows, so the collection only exercises the scope.</summary>
    private sealed class EmptyResourceGraphClient : IResourceGraphClient
    {
        public Task<ResourceGraphQueryResult> QueryAsync(
            string accessToken,
            string query,
            IReadOnlyList<string> subscriptionIds,
            string? skipToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceGraphQueryResult([], IsTruncated: false, SkipToken: null, TotalRecords: null));
    }

    /// <summary>
    /// Answers the two queries this test needs — the network-interface read and the link-resolution read — so the
    /// flag's whole path is exercised rather than each half separately.
    /// </summary>
    private sealed class ScriptedResourceGraphClient : IResourceGraphClient
    {
        private const string NicId = "/subscriptions/" + Sub1 + "/resourceGroups/rg-app-prod/providers/Microsoft.Network/networkInterfaces/nic-vm01";
        private const string VirtualMachineId = "/subscriptions/" + Sub1 + "/resourceGroups/rg-app-prod/providers/Microsoft.Compute/virtualMachines/vm-app-01";

        public List<string> Queries { get; } = [];

        /// <summary>Subscription sets every query was scoped to.</summary>
        public List<IReadOnlyList<string>> Subscriptions { get; } = [];

        public Task<ResourceGraphQueryResult> QueryAsync(
            string accessToken,
            string query,
            IReadOnlyList<string> subscriptionIds,
            string? skipToken,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            Subscriptions.Add(subscriptionIds);

            if (query.Contains("Microsoft.Network/networkInterfaces", StringComparison.Ordinal))
            {
                return Task.FromResult(Result(JsonSerializer.SerializeToElement(new
                {
                    id = NicId,
                    name = "nic-vm01",
                    location = "westeurope",
                    resourceGroup = "rg-app-prod",
                    subscriptionId = Sub1,
                    properties = new
                    {
                        ipConfigurations = Array.Empty<object>(),
                        virtualMachine = new { id = VirtualMachineId },
                    },
                })));
            }

            if (query.Contains("id in~", StringComparison.Ordinal))
            {
                return Task.FromResult(Result(JsonSerializer.SerializeToElement(new
                {
                    id = VirtualMachineId,
                    name = "vm-app-01",
                    type = "Microsoft.Compute/virtualMachines",
                    location = "westeurope",
                    resourceGroup = "rg-app-prod",
                })));
            }

            return Task.FromResult(new ResourceGraphQueryResult([], IsTruncated: false, SkipToken: null, TotalRecords: null));
        }

        private static ResourceGraphQueryResult Result(JsonElement row) =>
            new([row], IsTruncated: false, SkipToken: null, TotalRecords: 1);
    }

    private sealed class StubCredentialProvider : IAzureCredentialProvider
    {
        public TokenCredential Credential { get; } = new StubTokenCredential();

        private sealed class StubTokenCredential : TokenCredential
        {
            public override AccessToken GetToken(
                TokenRequestContext requestContext,
                CancellationToken cancellationToken)
                => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

            public override ValueTask<AccessToken> GetTokenAsync(
                TokenRequestContext requestContext,
                CancellationToken cancellationToken)
                => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }
}
