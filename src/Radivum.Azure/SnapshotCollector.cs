using Radivum.Model;
using System.Reflection;
using System.Text.Json;

namespace Radivum.Azure;

internal sealed class SnapshotCollector : ISnapshotCollector
{
    private const int MaxQueryPages = 20;

    private static readonly (string ResourceType, string ItemType, string Predicate)[] QueryDefinitions =
    [
        ("Microsoft.Network/virtualNetworks", "VNets", "type =~ 'Microsoft.Network/virtualNetworks'"),
        ("Microsoft.Network/networkSecurityGroups", "NSGs", "type =~ 'Microsoft.Network/networkSecurityGroups'"),
        ("Microsoft.Network/routeTables", "RouteTables", "type =~ 'Microsoft.Network/routeTables'"),
        ("Microsoft.Network/networkInterfaces", "NICs", "type =~ 'Microsoft.Network/networkInterfaces'"),
        ("Microsoft.Network/publicIPAddresses", "PublicIPs", "type =~ 'Microsoft.Network/publicIPAddresses'"),
        ("Microsoft.Network/privateEndpoints", "PrivateEndpoints", "type =~ 'Microsoft.Network/privateEndpoints'"),
        ("Microsoft.Network/privateDnsZones", "PrivateDnsZones", "type =~ 'Microsoft.Network/privateDnsZones'"),
        ("Microsoft.Network/azureFirewalls", "AzureFirewalls", "type =~ 'Microsoft.Network/azureFirewalls'"),
        ("Microsoft.Network/virtualHubs", "VirtualHubs", "type =~ 'Microsoft.Network/virtualHubs'"),
        ("Microsoft.Network/virtualWans", "VirtualWans", "type =~ 'Microsoft.Network/virtualWans'"),
        ("Microsoft.Network/expressRouteCircuits", "ExpressRoute", "type =~ 'Microsoft.Network/expressRouteCircuits'"),
        ("Microsoft.Network/virtualNetworkGateways", "VirtualNetworkGateways", "type =~ 'Microsoft.Network/virtualNetworkGateways'")
    ];

    private readonly IDiscovery _discovery;
    private readonly IAzureCredentialProvider _credentialProvider;
    private readonly IResourceGraphClient _resourceGraphClient;

    public SnapshotCollector(
        IDiscovery discovery,
        IAzureCredentialProvider credentialProvider,
        IResourceGraphClient resourceGraphClient)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(credentialProvider);
        ArgumentNullException.ThrowIfNull(resourceGraphClient);

        _discovery = discovery;
        _credentialProvider = credentialProvider;
        _resourceGraphClient = resourceGraphClient;
    }

    public Task<NetworkSnapshot> CollectAsync(
        SnapshotCollectionRequest request,
        CancellationToken cancellationToken = default)
        => CollectInternalAsync(request, cancellationToken);

    private async Task<NetworkSnapshot> CollectInternalAsync(
        SnapshotCollectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ScopeDescription))
        {
            throw new ArgumentException("Scope description is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            throw new ArgumentException("Subscription id is required.", nameof(request));
        }

        var subscriptionIds = ValidateScope(request);

        var token = await _discovery.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        // The credential's own tenant is read from the token; when it cannot be read the scope is not refused on a
        // guess, because a token need not be a JWT the client can decode. The snapshot keeps the established
        // placeholder in that case.
        var credentialTenantId = TryGetTenantIdFromJwt(token);
        var tenantId = credentialTenantId ?? "unknown-tenant";
        var collectedAt = DateTimeOffset.UtcNow;

        // Identity before inventory: a scope that does not belong to the signed-in tenant, or one that cannot be
        // read at all, is named here instead of surfacing later as resource queries that quietly omit it.
        var probes = await ProbeSubscriptionsAsync(subscriptionIds, credentialTenantId, cancellationToken)
            .ConfigureAwait(false);
        EnsureAReadableScope(request, probes);

        var queryOutcomes = new Dictionary<string, ResourceGraphCollectionOutcome>(StringComparer.OrdinalIgnoreCase);

        // The subscription array scopes the query, so the text carries the resource group alone. Reading a group
        // narrower than a subscription is only ever requested with exactly one subscription, which ValidateScope
        // enforces before any I/O.
        var scopeFilter = request.ResourceGroup is null
            ? string.Empty
            : $" | where resourceGroup =~ '{request.ResourceGroup}'";

        foreach (var (resourceType, itemType, predicate) in QueryDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query =
                $"resources | where {predicate}{scopeFilter} | project id, name, location, resourceGroup, subscriptionId, properties";
            var result = await QueryResourceGraphWithPagingAsync(
                    token,
                    resourceType,
                    itemType,
                    query,
                    subscriptionIds,
                    cancellationToken)
                .ConfigureAwait(false);

            queryOutcomes[resourceType] = result;
        }

        var toolVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.1.0";

        var snapshot = Parser.BuildSnapshot(
            queryOutcomes,
            request,
            tenantId,
            collectedAt,
            toolVersion,
            _credentialProvider.Credential.GetType().Name,
            probes);

        if (!request.ResolveLinkedResources)
        {
            return snapshot;
        }

        // Enrichment, not evidence: every link already exists, so resolution may degrade but never fail a run.
        return await LinkedResourceResolver
            .ResolveAsync(snapshot, _resourceGraphClient, token, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Validates the requested subscription set at the boundary where it enters a query.
    /// </summary>
    /// <remarks>
    /// The command line, the connected host and this collector each validate, because subscription ids are
    /// interpolated into a query and a host may be older than the value a newer caller assumes. The rules are the
    /// same everywhere: one to <see cref="CollectionScopeLimits.MaxSubscriptions"/> identifiers, each a GUID, no
    /// duplicates, and no resource group unless the scope is a single subscription.
    /// </remarks>
    /// <param name="request">Request whose scope is validated.</param>
    /// <returns>The subscriptions to collect, in the order the request names them.</returns>
    /// <exception cref="CollectionScopeException">The scope violates one of the rules above.</exception>
    private static IReadOnlyList<string> ValidateScope(SnapshotCollectionRequest request)
    {
        var subscriptionIds = request.SelectedSubscriptionIds;

        if (subscriptionIds.Count > CollectionScopeLimits.MaxSubscriptions)
        {
            throw new CollectionScopeException(
                $"At most {CollectionScopeLimits.MaxSubscriptions} subscriptions can be collected in one analysis; " +
                $"{subscriptionIds.Count} were requested.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subscriptionId in subscriptionIds)
        {
            if (!Guid.TryParse(subscriptionId, out _))
            {
                throw new CollectionScopeException($"'{subscriptionId}' is not a subscription id.");
            }

            if (!seen.Add(subscriptionId))
            {
                throw new CollectionScopeException($"Subscription '{subscriptionId}' was requested more than once.");
            }
        }

        if (subscriptionIds.Count > 1 && request.ResourceGroup is not null)
        {
            throw new CollectionScopeException("A resource group scope can only be collected for one subscription.");
        }

        return subscriptionIds;
    }

    /// <summary>
    /// Reads the metadata of every requested subscription, refusing a scope from another tenant.
    /// </summary>
    /// <remarks>
    /// The name is enrichment, not evidence: a denial, a transport failure, or an unreadable body leaves the probe
    /// without a name so the scope keeps the identifier it already carries, instead of failing a collection whose
    /// resources may still be read. Cancellation is not swallowed, so a cancelled run still stops.
    /// <para>
    /// A tenant mismatch is different from a denial. One analysis covers one tenant, so a subscription that answers
    /// with another tenant, or that ARM rejects with <c>InvalidAuthenticationTokenTenant</c>, is refused by name
    /// rather than degraded: collecting it would put two tenants in one snapshot. Only the sign-in can change that,
    /// so no second-tenant credential is acquired here.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<SubscriptionProbe>> ProbeSubscriptionsAsync(
        IReadOnlyList<string> subscriptionIds,
        string? credentialTenantId,
        CancellationToken cancellationToken)
    {
        var probes = new List<SubscriptionProbe>(subscriptionIds.Count);

        foreach (var subscriptionId in subscriptionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            probes.Add(await ReadSubscriptionProbeAsync(subscriptionId, credentialTenantId, cancellationToken)
                .ConfigureAwait(false));
        }

        return probes;
    }

    private async Task<SubscriptionProbe> ReadSubscriptionProbeAsync(
        string subscriptionId,
        string? credentialTenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await _discovery.GetSubscriptionAsync(subscriptionId, cancellationToken)
                .ConfigureAwait(false);

            if (credentialTenantId is not null &&
                !string.IsNullOrWhiteSpace(subscription.TenantId) &&
                !string.Equals(subscription.TenantId, credentialTenantId, StringComparison.OrdinalIgnoreCase))
            {
                throw TenantMismatch(
                    subscriptionId,
                    $"ARM reports it belongs to tenant '{subscription.TenantId}' while the signed-in credential is for " +
                    $"tenant '{credentialTenantId}'.");
            }

            return new SubscriptionProbe(
                subscriptionId,
                string.IsNullOrWhiteSpace(subscription.Name) ? null : subscription.Name,
                string.IsNullOrWhiteSpace(subscription.State) ? null : subscription.State,
                string.IsNullOrWhiteSpace(subscription.TenantId) ? null : subscription.TenantId,
                Error: null);
        }
        catch (Exception exception) when (
            exception is AzureRequestException { ErrorCode: "InvalidAuthenticationTokenTenant" })
        {
            throw TenantMismatch(
                subscriptionId,
                "ARM rejected the request with InvalidAuthenticationTokenTenant, which means the subscription is not " +
                "in the tenant of the signed-in credential.");
        }
        catch (Exception exception) when (exception is AzureRequestException or HttpRequestException or JsonException)
        {
            return new SubscriptionProbe(
                subscriptionId,
                Name: null,
                State: null,
                TenantId: null,
                Error: $"Subscription metadata could not be read: {exception.Message}");
        }
    }

    private static CollectionScopeException TenantMismatch(string subscriptionId, string detail)
        => new(
            $"Subscription '{subscriptionId}' is not in the tenant of the signed-in credential. {detail} " +
            "One analysis covers subscriptions of one tenant; sign in to that tenant to collect it.");

    /// <summary>
    /// Refuses to continue when a multi-subscription scope has nothing readable left.
    /// </summary>
    /// <remarks>
    /// A single-subscription scope keeps its established behaviour: metadata is enrichment there, and a resource
    /// query may still answer, so a failed metadata read does not end the run. Several subscriptions are different:
    /// Resource Graph answers a request for one readable and one unreadable subscription with the readable rows and
    /// no error, so proceeding after every scope failed would produce a confidently wrong analysis.
    /// </remarks>
    private static void EnsureAReadableScope(
        SnapshotCollectionRequest request,
        IReadOnlyList<SubscriptionProbe> probes)
    {
        if (probes.Count <= 1 || probes.Any(static probe => probe.IsReadable))
        {
            return;
        }

        var reasons = string.Join(
            "; ",
            probes.Select(static probe => $"'{probe.SubscriptionId}': {probe.Error}"));

        throw new CollectionScopeException(
            $"None of the requested subscriptions could be read ({reasons}). Nothing was collected.");
    }

    private async Task<ResourceGraphCollectionOutcome> QueryResourceGraphWithPagingAsync(
        string token,
        string resourceType,
        string itemType,
        string query,
        IReadOnlyList<string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<JsonElement>();
        var errors = new List<string>();
        var isTruncated = false;
        var reachedPageLimit = false;
        var pageCount = 0;
        int? totalRecords = null;
        string? skipToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageCount++;

            ResourceGraphQueryResult pageResult;
            try
            {
                pageResult = await _resourceGraphClient
                    .QueryAsync(token, query, subscriptionIds, skipToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AzureRequestException exception)
            {
                errors.Add($"Query failed for {resourceType}: {exception.StatusCode} ({exception.ErrorCode ?? "Unknown"}) - {exception.Message}");
                if (exception.IsAuthorization)
                {
                    errors.Add($"Missing read permission for {resourceType}. Inventory collection expects Reader access at the requested scope.");
                }

                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"Query failed for {resourceType}: {exception.Message}");
                break;
            }

            rows.AddRange(pageResult.Rows);
            isTruncated |= pageResult.IsTruncated;
            totalRecords = pageResult.TotalRecords ?? totalRecords;
            skipToken = pageResult.SkipToken;

            if (!string.IsNullOrWhiteSpace(skipToken) && pageCount >= MaxQueryPages)
            {
                reachedPageLimit = true;
                errors.Add($"Collection stopped after {MaxQueryPages} pages for {resourceType}; additional rows may exist.");
                break;
            }
        }
        while (!string.IsNullOrWhiteSpace(skipToken));

        if (isTruncated)
        {
            errors.Add($"Azure Resource Graph reported resultTruncated=true for {resourceType}.");
        }

        return new ResourceGraphCollectionOutcome(
            resourceType,
            itemType,
            rows,
            isTruncated,
            reachedPageLimit,
            pageCount,
            totalRecords,
            errors);
    }

    private static string? TryGetTenantIdFromJwt(string jwtToken)
    {
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            return null;
        }

        var parts = jwtToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        var payload = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');

        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        try
        {
            var bytes = Convert.FromBase64String(payload);
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.TryGetProperty("tid", out var tenantIdElement) &&
                   tenantIdElement.ValueKind == JsonValueKind.String
                ? tenantIdElement.GetString()
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
