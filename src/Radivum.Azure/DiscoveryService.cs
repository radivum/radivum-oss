using Azure.Core;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Radivum.Azure;

internal sealed class DiscoveryService : IDiscovery
{
    private const string SubscriptionsApiVersion = "2022-12-01";

    private const string SubscriptionsEndpoint = $"subscriptions?api-version={SubscriptionsApiVersion}";

    /// <summary>
    /// Ceiling on the pages a single listing call will read. ARM pages both the subscription and the
    /// resource-group list with a <c>nextLink</c>, and a malformed or self-referencing link would otherwise
    /// spin forever; the ceiling turns that into a clear failure rather than an endless loop, and it is high
    /// enough that no real tenant reaches it.
    /// </summary>
    private const int MaxListPages = 50;

    private readonly TokenCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public DiscoveryService(IAzureCredentialProvider credentialProvider, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(credentialProvider);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _credential = credentialProvider.Credential;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await _credential
            .GetTokenAsync(new TokenRequestContext([AzureConstants.ArmAudience]), cancellationToken)
            .ConfigureAwait(false);

        return token.Token;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var values = await ReadAllPagesAsync(SubscriptionsEndpoint, token, "list subscriptions", cancellationToken)
            .ConfigureAwait(false);

        return values
            .Select(element =>
            {
                var id = HttpResponse.GetString(element, "subscriptionId") ?? string.Empty;
                var name = HttpResponse.GetString(element, "displayName");
                var state = HttpResponse.GetString(element, "state") ?? string.Empty;
                var tenantId = HttpResponse.GetString(element, "tenantId");

                // A display name is optional in ARM. The id is what the operator needs, so it stands in
                // rather than leaving an unnamed option in the picker.
                return new SubscriptionInfo(
                    id,
                    string.IsNullOrWhiteSpace(name) ? id : name,
                    state,
                    string.IsNullOrWhiteSpace(tenantId) ? null : tenantId);
            })
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.Id))
            .OrderBy(subscription => subscription.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(subscription => subscription.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SubscriptionInfo> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);

        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var endpoint = $"subscriptions/{Uri.EscapeDataString(subscriptionId)}?api-version={SubscriptionsApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClientFactory.CreateClient("AzureArm")
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var content = await HttpResponse.ReadOrThrowAsync(response, "read subscription", cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        var id = HttpResponse.GetString(root, "subscriptionId") ?? subscriptionId;
        var name = HttpResponse.GetString(root, "displayName");
        var state = HttpResponse.GetString(root, "state") ?? string.Empty;
        var tenantId = HttpResponse.GetString(root, "tenantId");

        // A display name is optional in ARM. The id is what the operator needs, so it stands in rather
        // than leaving the scope unnamed, exactly as the listing does.
        return new SubscriptionInfo(
            id,
            string.IsNullOrWhiteSpace(name) ? id : name,
            state,
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResourceGroupInfo>> ListResourceGroupsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);

        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var endpoint =
            $"subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourceGroups?api-version=2021-04-01";
        var values = await ReadAllPagesAsync(endpoint, token, "list resource groups", cancellationToken)
            .ConfigureAwait(false);

        return values
            .Select(element =>
            {
                var id = HttpResponse.GetString(element, "id") ?? string.Empty;
                var name = HttpResponse.GetString(element, "name");
                var location = HttpResponse.GetString(element, "location") ?? string.Empty;

                return new ResourceGroupInfo(
                    id,
                    string.IsNullOrWhiteSpace(name) ? LastSegment(id) : name,
                    location);
            })
            .Where(group => !string.IsNullOrWhiteSpace(group.Id))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Reads every page of an ARM listing, following <c>nextLink</c> until it is absent.
    /// </summary>
    /// <param name="requestUri">First request, relative to the ARM base address or absolute.</param>
    /// <param name="accessToken">Bearer token to authorize every page.</param>
    /// <param name="operation">Operation name used in a failure message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every element of the pages' <c>value</c> arrays, in the order ARM returned them.</returns>
    private async Task<List<JsonElement>> ReadAllPagesAsync(
        string requestUri,
        string accessToken,
        string operation,
        CancellationToken cancellationToken)
    {
        var values = new List<JsonElement>();
        string? nextUri = requestUri;
        var pages = 0;

        while (!string.IsNullOrWhiteSpace(nextUri))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++pages > MaxListPages)
            {
                throw new InvalidOperationException(
                    $"Failed to {operation}: the listing did not finish within {MaxListPages} pages. " +
                    "Azure returned a next link that did not terminate.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, nextUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClientFactory.CreateClient("AzureArm")
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            var content = await HttpResponse.ReadOrThrowAsync(response, operation, cancellationToken)
                .ConfigureAwait(false);

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("value", out var valueElement) &&
                valueElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in valueElement.EnumerateArray())
                {
                    values.Add(item.Clone());
                }
            }

            nextUri = root.ValueKind == JsonValueKind.Object
                ? HttpResponse.GetString(root, "nextLink")
                : null;
        }

        return values;
    }

    private static string LastSegment(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        var separator = id.LastIndexOf('/');
        return separator >= 0 && separator < id.Length - 1 ? id[(separator + 1)..] : id;
    }
}

