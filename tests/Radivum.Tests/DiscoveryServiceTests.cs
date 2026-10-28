using Azure.Core;
using Radivum.Azure;
using System.Net;
using System.Text;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Covers the shared discovery service that lists the subscriptions and resource groups an agent's
/// credential can see: paging, deterministic ordering, name fallbacks, cancellation, and ARM failures.
/// </summary>
public sealed class DiscoveryServiceTests
{
    private const string SubscriptionId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task ListSubscriptionsAsync_FollowsNextLinkAndReturnsEveryPageSortedByName()
    {
        var handler = new StubHandler()
            .Enqueue(
                HttpStatusCode.OK,
                WithNextLink(
                    SubscriptionsPage(
                        ("22222222-2222-2222-2222-222222222222", "Zulu", "Enabled"),
                        ("33333333-3333-3333-3333-333333333333", "Alpha", "Enabled")),
                    "https://management.azure.com/subscriptions?api-version=2022-12-01&$skipToken=page2"))
            .Enqueue(
                HttpStatusCode.OK,
                SubscriptionsPage((SubscriptionId, "Mike", "Enabled")));
        var discovery = CreateDiscovery(handler);

        var subscriptions = await discovery.ListSubscriptionsAsync();

        Assert.Equal(["Alpha", "Mike", "Zulu"], subscriptions.Select(subscription => subscription.Name));
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains("$skipToken=page2", handler.RequestUris[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_SendsTheCredentialBearerToken()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, SubscriptionsPage());
        var discovery = CreateDiscovery(handler);

        await discovery.ListSubscriptionsAsync();

        Assert.Equal("Bearer test-token", handler.AuthorizationHeaders[0]);
        Assert.Contains("subscriptions?api-version=2022-12-01", handler.RequestUris[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenThereAreNoSubscriptions()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, SubscriptionsPage());
        var discovery = CreateDiscovery(handler);

        var subscriptions = await discovery.ListSubscriptionsAsync();

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_UsesTheIdWhenTheDisplayNameIsMissing()
    {
        var handler = new StubHandler().Enqueue(
            HttpStatusCode.OK,
            """{ "value": [ { "subscriptionId": "11111111-1111-1111-1111-111111111111", "state": "Enabled" } ] }""");
        var discovery = CreateDiscovery(handler);

        var subscriptions = await discovery.ListSubscriptionsAsync();

        Assert.Equal(SubscriptionId, Assert.Single(subscriptions).Name);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ThrowsAuthorizationFailureForForbidden()
    {
        var handler = new StubHandler().Enqueue(
            HttpStatusCode.Forbidden,
            """{ "error": { "code": "AuthorizationFailed", "message": "Denied" } }""");
        var discovery = CreateDiscovery(handler);

        var exception = await Assert.ThrowsAsync<AzureRequestException>(() => discovery.ListSubscriptionsAsync());

        Assert.True(exception.IsAuthorization);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ThrowsOnServerError()
    {
        var handler = new StubHandler().Enqueue(
            HttpStatusCode.InternalServerError,
            """{ "error": { "code": "InternalServerError", "message": "Boom" } }""");
        var discovery = CreateDiscovery(handler);

        var exception = await Assert.ThrowsAsync<AzureRequestException>(() => discovery.ListSubscriptionsAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.False(exception.IsAuthorization);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_PropagatesCancellation()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, SubscriptionsPage());
        var discovery = CreateDiscovery(handler);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.ListSubscriptionsAsync(source.Token));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_FailsWhenTheNextLinkDoesNotTerminate()
    {
        // A link that keeps pointing onward is the case the page ceiling exists for: without it the call
        // would never return, so the ceiling must turn the cycle into a failure rather than a hang.
        var handler = new StubHandler
        {
            Fallback = _ => JsonResponse(WithNextLink(
                SubscriptionsPage(),
                "https://management.azure.com/subscriptions?api-version=2022-12-01&$skipToken=again")),
        };
        var discovery = CreateDiscovery(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => discovery.ListSubscriptionsAsync());

        Assert.Equal(50, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReadsTheNameOfOneSubscription()
    {
        var handler = new StubHandler().Enqueue(
            HttpStatusCode.OK,
            """{ "subscriptionId": "11111111-1111-1111-1111-111111111111", "displayName": "Contoso Production", "state": "Enabled" }""");
        var discovery = CreateDiscovery(handler);

        var subscription = await discovery.GetSubscriptionAsync(SubscriptionId);

        Assert.Equal(SubscriptionId, subscription.Id);
        Assert.Equal("Contoso Production", subscription.Name);
        Assert.Equal("Enabled", subscription.State);
        Assert.Equal("Bearer test-token", handler.AuthorizationHeaders[0]);
        Assert.Contains(
            $"subscriptions/{SubscriptionId}?api-version=2022-12-01",
            handler.RequestUris[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSubscriptionAsync_UsesTheIdWhenTheDisplayNameIsMissing()
    {
        var handler = new StubHandler().Enqueue(
            HttpStatusCode.OK,
            """{ "subscriptionId": "11111111-1111-1111-1111-111111111111", "state": "Enabled" }""");
        var discovery = CreateDiscovery(handler);

        var subscription = await discovery.GetSubscriptionAsync(SubscriptionId);

        Assert.Equal(SubscriptionId, subscription.Name);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ThrowsAuthorizationFailureForForbidden()
    {
        var handler = new StubHandler().Enqueue(
            HttpStatusCode.Forbidden,
            """{ "error": { "code": "AuthorizationFailed", "message": "Denied" } }""");
        var discovery = CreateDiscovery(handler);

        var exception = await Assert.ThrowsAsync<AzureRequestException>(
            () => discovery.GetSubscriptionAsync(SubscriptionId));

        Assert.True(exception.IsAuthorization);
    }

    [Fact]
    public async Task GetSubscriptionAsync_RejectsAnEmptySubscriptionId()
    {
        var discovery = CreateDiscovery(new StubHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => discovery.GetSubscriptionAsync("  "));
    }

    [Fact]
    public async Task ListResourceGroupsAsync_ReturnsGroupsSortedByNameForTheSubscription()
    {
        var handler = new StubHandler().Enqueue(
            HttpStatusCode.OK,
            ResourceGroupsPage(
                ($"/subscriptions/{SubscriptionId}/resourceGroups/rg-zulu", "rg-zulu", "eastus"),
                ($"/subscriptions/{SubscriptionId}/resourceGroups/rg-alpha", "rg-alpha", "westus")));
        var discovery = CreateDiscovery(handler);

        var groups = await discovery.ListResourceGroupsAsync(SubscriptionId);

        Assert.Equal(["rg-alpha", "rg-zulu"], groups.Select(group => group.Name));
        Assert.Equal("eastus", groups[1].Location);
        Assert.Contains(
            $"/subscriptions/{SubscriptionId}/resourceGroups?api-version=2021-04-01",
            handler.RequestUris[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListResourceGroupsAsync_FollowsNextLink()
    {
        var handler = new StubHandler()
            .Enqueue(
                HttpStatusCode.OK,
                WithNextLink(
                    ResourceGroupsPage(($"/subscriptions/{SubscriptionId}/resourceGroups/rg-a", "rg-a", "eastus")),
                    $"https://management.azure.com/subscriptions/{SubscriptionId}/resourceGroups?api-version=2021-04-01&$skipToken=2"))
            .Enqueue(
                HttpStatusCode.OK,
                ResourceGroupsPage(($"/subscriptions/{SubscriptionId}/resourceGroups/rg-b", "rg-b", "eastus")));
        var discovery = CreateDiscovery(handler);

        var groups = await discovery.ListResourceGroupsAsync(SubscriptionId);

        Assert.Equal(["rg-a", "rg-b"], groups.Select(group => group.Name));
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task ListResourceGroupsAsync_DerivesTheNameFromTheIdWhenMissing()
    {
        var handler = new StubHandler().Enqueue(
            HttpStatusCode.OK,
            $"{{\"value\":[{{\"id\":\"/subscriptions/{SubscriptionId}/resourceGroups/rg-from-id\",\"location\":\"eastus\"}}]}}");
        var discovery = CreateDiscovery(handler);

        var groups = await discovery.ListResourceGroupsAsync(SubscriptionId);

        Assert.Equal("rg-from-id", Assert.Single(groups).Name);
    }

    [Fact]
    public async Task ListResourceGroupsAsync_RejectsAnEmptySubscriptionId()
    {
        var discovery = CreateDiscovery(new StubHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => discovery.ListResourceGroupsAsync("  "));
    }

    private static DiscoveryService CreateDiscovery(StubHandler handler)
        => new(new StubCredentialProvider(), new StubHttpClientFactory(handler));

    private static string SubscriptionsPage(params (string Id, string Name, string State)[] subscriptions)
    {
        var items = string.Join(",", subscriptions.Select(subscription =>
            $"{{\"subscriptionId\":\"{subscription.Id}\",\"displayName\":\"{subscription.Name}\",\"state\":\"{subscription.State}\"}}"));
        return $"{{\"value\":[{items}]}}";
    }

    private static string ResourceGroupsPage(params (string Id, string Name, string Location)[] groups)
    {
        var items = string.Join(",", groups.Select(group =>
            $"{{\"id\":\"{group.Id}\",\"name\":\"{group.Name}\",\"location\":\"{group.Location}\"}}"));
        return $"{{\"value\":[{items}]}}";
    }

    private static string WithNextLink(string pageJson, string nextLink)
        => pageJson[..^1] + $",\"nextLink\":\"{nextLink}\"}}";

    private static HttpResponseMessage JsonResponse(string content)
        => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

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

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        // Mirrors the "AzureArm" registration, so the service's relative paths resolve the same way they do
        // in the running agent.
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://management.azure.com/"),
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public Func<HttpRequestMessage, HttpResponseMessage>? Fallback { get; set; }

        public List<string> RequestUris { get; } = [];

        public List<string?> AuthorizationHeaders { get; } = [];

        public StubHandler Enqueue(HttpStatusCode statusCode, string content)
        {
            var response = JsonResponse(content);
            response.StatusCode = statusCode;
            _responses.Enqueue(response);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            cancellationToken.ThrowIfCancellationRequested();

            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : Fallback?.Invoke(request) ?? throw new InvalidOperationException("No stub response is queued.");

            return Task.FromResult(response);
        }
    }
}
