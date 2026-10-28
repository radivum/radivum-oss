using System.Text.Json;
using Radivum.Model;

namespace Radivum.Azure;

internal static class Parser
{
    public static NetworkSnapshot BuildSnapshot(
        IReadOnlyDictionary<string, ResourceGraphCollectionOutcome> queryOutcomes,
        SnapshotCollectionRequest request,
        string tenantId,
        DateTimeOffset collectedAt,
        string toolVersion,
        string credentialSource,
        IReadOnlyList<SubscriptionProbe>? subscriptionProbes = null)
    {
        ArgumentNullException.ThrowIfNull(queryOutcomes);
        ArgumentNullException.ThrowIfNull(request);

        var rowsByType = queryOutcomes.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Rows,
            StringComparer.OrdinalIgnoreCase);
        var discoveredSubscriptionIds = ExtractSubscriptionIds(rowsByType);

        // The scope is what the operator asked for, not what the rows happened to contain. A subscription that
        // answered no rows is still part of the analysis, and one whose rows arrived although it was not requested is
        // a scope violation rather than a silent widening of the scope.
        var requestedSubscriptionIds = request.SelectedSubscriptionIds;
        var isMultiSubscription = request.IsMultiSubscription;

        var hasPartialResourceGraphOutcome = queryOutcomes.Values.Any(static outcome =>
            outcome.IsTruncated || outcome.ReachedPageLimit || outcome.Errors.Count > 0);
        var hasAnyResourceRows = queryOutcomes.Values.Any(static outcome => outcome.Rows.Count > 0);
        var aggregateCompleteness = ComputeAggregateCompleteness(queryOutcomes);
        var scopeValidationErrors = BuildScopeValidationErrors(discoveredSubscriptionIds, requestedSubscriptionIds);

        var resourceMetadata = CreateCollectionMetadata(
            "Resources",
            source: "AzureResourceGraph",
            timestamp: collectedAt,
            permission: "Reader",
            status: DetermineAggregateStatus(hasPartialResourceGraphOutcome, hasAnyResourceRows),
            completeness: ComputeAggregateCompleteness(queryOutcomes),
            errors: queryOutcomes.Values
                .SelectMany(static outcome => outcome.Errors)
                .Distinct(StringComparer.Ordinal)
                .ToList());

        var vnets = ParseVirtualNetworks(GetRows(rowsByType, "Microsoft.Network/virtualNetworks"), resourceMetadata);
        var nsgs = ParseNetworkSecurityGroups(GetRows(rowsByType, "Microsoft.Network/networkSecurityGroups"), resourceMetadata);
        var routeTables = ParseRouteTables(GetRows(rowsByType, "Microsoft.Network/routeTables"), resourceMetadata);

        // References read from the payloads already collected. They describe the network elements, so they are
        // gathered while those elements are parsed rather than in a second pass over the same rows.
        var linkedResources = new List<LinkedResourceInfo>();

        AddUncollectedPeeringLinks(vnets, linkedResources, resourceMetadata);

        var networkInterfaces = ParseNetworkInterfaces(GetRows(rowsByType, "Microsoft.Network/networkInterfaces"), resourceMetadata, linkedResources);
        var publicIpAddresses = ParsePublicIpAddresses(GetRows(rowsByType, "Microsoft.Network/publicIPAddresses"), resourceMetadata);
        var privateEndpoints = ParsePrivateEndpoints(GetRows(rowsByType, "Microsoft.Network/privateEndpoints"), resourceMetadata, linkedResources);
        var privateDnsZones = ParsePrivateDnsZones(GetRows(rowsByType, "Microsoft.Network/privateDnsZones"), resourceMetadata);
        var azureFirewalls = ParseAzureFirewalls(GetRows(rowsByType, "Microsoft.Network/azureFirewalls"), resourceMetadata);
        var virtualHubs = ParseVirtualHubs(GetRows(rowsByType, "Microsoft.Network/virtualHubs"), resourceMetadata);
        var virtualWans = ParseVirtualWans(GetRows(rowsByType, "Microsoft.Network/virtualWans"), resourceMetadata);
        var expressRouteCircuits = ParseExpressRouteCircuits(GetRows(rowsByType, "Microsoft.Network/expressRouteCircuits"), resourceMetadata);
        var virtualNetworkGateways = ParseVirtualNetworkGateways(GetRows(rowsByType, "Microsoft.Network/virtualNetworkGateways"), resourceMetadata);

        var relationships = BuildRelationships(vnets, nsgs, routeTables, networkInterfaces, privateEndpoints, virtualHubs, resourceMetadata);

        var collectionMetadata = new List<CollectionMetadata>
        {
            CreateCollectionMetadata(
                "Resources",
                "AzureResourceGraph",
                collectedAt,
                "Reader",
                DetermineAggregateStatus(hasPartialResourceGraphOutcome, hasAnyResourceRows),
                ComputeAggregateCompleteness(queryOutcomes),
                queryOutcomes.Values
                    .SelectMany(static outcome => outcome.Errors)
                    .Distinct(StringComparer.Ordinal)
                    .ToList())
        };

        var resourceCollectionMetadata = queryOutcomes.Values
            .OrderBy(static outcome => outcome.ItemType, StringComparer.Ordinal)
            .Select(outcome => CreateCollectionMetadata(
                outcome.ItemType,
                "AzureResourceGraph",
                collectedAt,
                "Reader",
                DetermineOutcomeStatus(outcome),
                ComputeOutcomeCompleteness(outcome),
                outcome.Errors))
            .ToList();

        collectionMetadata.AddRange(resourceCollectionMetadata);

        // Per-subscription coverage exists only for a multi-subscription scope: a single-subscription snapshot keeps
        // exactly the metadata rows it had, which is what keeps its bytes and its coverage numbers unchanged.
        collectionMetadata.AddRange(BuildSubscriptionCoverage(
            requestedSubscriptionIds,
            subscriptionProbes,
            collectedAt,
            hasPartialResourceGraphOutcome,
            aggregateCompleteness));

        collectionMetadata.Add(CreateCollectionMetadata(
            "Relationships",
            "Derived",
            collectedAt,
            "Reader",
            DetermineAggregateStatus(hasPartialResourceGraphOutcome, hasAnyResourceRows),
            ComputeAggregateCompleteness(queryOutcomes),
            []));

        collectionMetadata.Add(CreateCollectionMetadata(
            "ScopeValidation",
            "Derived",
            collectedAt,
            "Reader",
            scopeValidationErrors.Count == 0 ? "Available" : "Partial",
            scopeValidationErrors.Count == 0 ? 1d : 0.5d,
            scopeValidationErrors));

        collectionMetadata.AddRange(
        [
            CreateCollectionMetadata(
                "EffectiveRoutes",
                "ARM",
                collectedAt,
                "Microsoft.Network/networkInterfaces/effectiveRouteTable/action",
                "Unavailable",
                0d,
                ["Level 2 effective route collection is not implemented yet."]),
            CreateCollectionMetadata(
                "EffectiveNSGs",
                "ARM",
                collectedAt,
                "Microsoft.Network/networkInterfaces/effectiveNetworkSecurityGroups/action",
                "Unavailable",
                0d,
                ["Level 2 effective NSG collection is not implemented yet."]),
            CreateCollectionMetadata(
                "NextHop",
                "NetworkWatcher",
                collectedAt,
                "Microsoft.Network/networkWatchers/nextHop/action",
                "Unavailable",
                0d,
                ["Level 3 active diagnostics are not implemented yet."])
        ]);

        var resources = new NetworkResourceSet(
            vnets,
            nsgs,
            routeTables,
            networkInterfaces,
            publicIpAddresses,
            privateEndpoints,
            privateDnsZones,
            azureFirewalls,
            virtualHubs,
            virtualWans,
            expressRouteCircuits,
            virtualNetworkGateways,
            // Sorted so the artifact does not depend on the order Resource Graph returned rows in. An empty list
            // is still a statement ("this collection looked for links and found none"), unlike a null member.
            linkedResources
                .OrderBy(link => link.SourceResourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(link => link.TargetResourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(link => link.LinkKind, StringComparer.Ordinal)
                .ToList());

        var scope = new SnapshotScope(
            request.ScopeDescription,
            isMultiSubscription ? null : request.SubscriptionId,
            request.ResourceGroup,
            requestedSubscriptionIds,
            isMultiSubscription ? null : SingleSubscriptionName(request, subscriptionProbes),
            isMultiSubscription ? BuildSubscriptionEntries(requestedSubscriptionIds, subscriptionProbes) : null);

        var accountContext = new AccountContextInfo(
            AccountId: null,
            AccountName: null,
            EnvironmentName: "AzureCloud",
            CredentialSource: credentialSource);

        return new NetworkSnapshot(
            NetworkSnapshotSchema.Version,
            toolVersion,
            tenantId,
            requestedSubscriptionIds,
            accountContext,
            collectedAt,
            scope,
            resources,
            new EffectiveEvidenceSet([], [], []),
            relationships,
            collectionMetadata);
    }

    private static IReadOnlyList<JsonElement> GetRows(
        IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> rowsByType,
        string resourceType)
        => rowsByType.TryGetValue(resourceType, out var rows) ? rows : [];

    private static double ComputeAggregateCompleteness(IReadOnlyDictionary<string, ResourceGraphCollectionOutcome> queryOutcomes)
    {
        if (queryOutcomes.Count == 0)
        {
            return 1d;
        }

        return queryOutcomes.Values
            .Select(ComputeOutcomeCompleteness)
            .Average();
    }

    private static double ComputeOutcomeCompleteness(ResourceGraphCollectionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Errors.Count > 0 && outcome.Rows.Count == 0)
        {
            return 0d;
        }

        if (!outcome.IsTruncated && !outcome.ReachedPageLimit && outcome.Errors.Count == 0)
        {
            return 1d;
        }

        if (outcome.TotalRecords is > 0)
        {
            var estimated = (double)outcome.Rows.Count / outcome.TotalRecords.Value;
            return Math.Clamp(estimated, 0d, 0.99d);
        }

        return outcome.ReachedPageLimit ? 0.9d : 0.95d;
    }

    private static string DetermineAggregateStatus(bool hasPartialResourceGraphOutcome, bool hasAnyResourceRows)
    {
        if (!hasAnyResourceRows && hasPartialResourceGraphOutcome)
        {
            return "Unavailable";
        }

        return hasPartialResourceGraphOutcome ? "Partial" : "Available";
    }

    private static string DetermineOutcomeStatus(ResourceGraphCollectionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Errors.Count > 0 && outcome.Rows.Count == 0)
        {
            return "Unavailable";
        }

        return outcome.IsTruncated || outcome.ReachedPageLimit || outcome.Errors.Count > 0
            ? "Partial"
            : "Available";
    }

    private static IReadOnlyList<string> ExtractSubscriptionIds(
        IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> rowsByType)
    {
        return rowsByType
            .Values
            .SelectMany(static rows => rows)
            .Select(static row => GetStringProp(row, "subscriptionId"))
            .Where(static subscriptionId => !string.IsNullOrWhiteSpace(subscriptionId))
            .Select(static subscriptionId => subscriptionId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Reports rows that arrived from a subscription the request did not select.
    /// </summary>
    /// <remarks>
    /// A missing subscription is not an error: an empty or unreadable one is described by its own coverage row. Rows
    /// from an unrequested subscription are different, because they mean the query widened beyond the scope the
    /// operator chose.
    /// </remarks>
    private static IReadOnlyList<string> BuildScopeValidationErrors(
        IReadOnlyList<string> discoveredSubscriptionIds,
        IReadOnlyList<string> requestedSubscriptionIds)
    {
        var errors = new List<string>();
        var requested = string.Join(", ", requestedSubscriptionIds);

        foreach (var subscriptionId in discoveredSubscriptionIds)
        {
            if (!requestedSubscriptionIds.Contains(subscriptionId, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Collected rows included subscription {subscriptionId} while request scope targeted {requested}.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Builds one coverage row per requested subscription for a multi-subscription scope.
    /// </summary>
    /// <remarks>
    /// The status is deliberately conservative. Metadata that could not be read is <c>Unavailable</c>. Otherwise the
    /// row follows the resource queries as a whole, because an aggregate Resource Graph answer cannot attribute a
    /// page-limit stop or a failed query to one subscription; claiming per-subscription precision beyond this would
    /// be invented. Metadata readability is not proof of subscription-wide Reader either, so <c>Available</c> here
    /// means "no gap was detected", not "everything was certainly visible".
    /// </remarks>
    private static IEnumerable<CollectionMetadata> BuildSubscriptionCoverage(
        IReadOnlyList<string> requestedSubscriptionIds,
        IReadOnlyList<SubscriptionProbe>? subscriptionProbes,
        DateTimeOffset collectedAt,
        bool hasPartialResourceGraphOutcome,
        double aggregateCompleteness)
    {
        if (requestedSubscriptionIds.Count <= 1 || subscriptionProbes is not { Count: > 0 } probes)
        {
            yield break;
        }

        foreach (var subscriptionId in requestedSubscriptionIds)
        {
            var probe = probes.FirstOrDefault(candidate =>
                string.Equals(candidate.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));

            if (probe is null || !probe.IsReadable)
            {
                yield return CreateCollectionMetadata(
                    "Subscription",
                    "ARM",
                    collectedAt,
                    "Reader",
                    "Unavailable",
                    0d,
                    [probe?.Error ?? "Subscription metadata was not read."],
                    subscriptionId);
                continue;
            }

            var status = hasPartialResourceGraphOutcome ? "Partial" : "Available";
            yield return CreateCollectionMetadata(
                "Subscription",
                "ARM",
                collectedAt,
                "Reader",
                status,
                hasPartialResourceGraphOutcome ? aggregateCompleteness : 1d,
                [],
                subscriptionId);
        }
    }

    private static string? SingleSubscriptionName(
        SnapshotCollectionRequest request,
        IReadOnlyList<SubscriptionProbe>? subscriptionProbes)
        => subscriptionProbes is { Count: > 0 } probes
            ? probes
                .FirstOrDefault(probe => string.Equals(
                    probe.SubscriptionId,
                    request.SubscriptionId,
                    StringComparison.OrdinalIgnoreCase))
                ?.Name
            : null;

    private static IReadOnlyList<SubscriptionScopeEntry> BuildSubscriptionEntries(
        IReadOnlyList<string> requestedSubscriptionIds,
        IReadOnlyList<SubscriptionProbe>? subscriptionProbes)
        => requestedSubscriptionIds
            .Select(subscriptionId => new SubscriptionScopeEntry(
                subscriptionId,
                subscriptionProbes?
                    .FirstOrDefault(probe => string.Equals(
                        probe.SubscriptionId,
                        subscriptionId,
                        StringComparison.OrdinalIgnoreCase))
                    ?.Name))
            .ToList();

    private static List<VNetResource> ParseVirtualNetworks(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<VNetResource>();

        foreach (var row in rows)
        {
            var props = GetProps(row);
            var subnets = new List<SubnetResource>();
            if (props.TryGetProperty("subnets", out var subnetsElement) && subnetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var subnet in subnetsElement.EnumerateArray())
                {
                    var subnetProps = GetProps(subnet);
                    subnets.Add(new SubnetResource(
                        GetStringProp(subnet, "id") ?? string.Empty,
                        GetStringProp(subnet, "name") ?? string.Empty,
                        NestedStringProp(subnetProps, "addressPrefix"),
                        NestedStringProp(subnetProps, "networkSecurityGroup", "id"),
                        NestedStringProp(subnetProps, "routeTable", "id"),
                        metadata));
                }
            }

            var peerings = new List<VNetPeeringResource>();
            if (props.TryGetProperty("virtualNetworkPeerings", out var peeringsElement) && peeringsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var peering in peeringsElement.EnumerateArray())
                {
                    var peeringProps = GetProps(peering);
                    peerings.Add(new VNetPeeringResource(
                        GetStringProp(peering, "name") ?? string.Empty,
                        NestedStringProp(peeringProps, "remoteVirtualNetwork", "id"),
                        NestedStringProp(peeringProps, "peeringState"),
                        NestedBoolProp(peeringProps, "allowGatewayTransit") ?? false,
                        NestedBoolProp(peeringProps, "useRemoteGateways") ?? false,
                        NestedBoolProp(peeringProps, "allowVirtualNetworkAccess") ?? true,
                        NestedBoolProp(peeringProps, "allowForwardedTraffic") ?? false,
                        metadata));
                }
            }

            result.Add(new VNetResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                StringArray(props, "addressSpace").Count > 0 ? StringArray(props, "addressSpace") : AddressSpace(props),
                subnets,
                peerings,
                metadata));
        }

        return result;
    }

    private static List<NetworkSecurityGroupResource> ParseNetworkSecurityGroups(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<NetworkSecurityGroupResource>();

        foreach (var row in rows)
        {
            var props = GetProps(row);
            var rules = new List<NetworkSecurityRuleResource>();

            if (props.TryGetProperty("securityRules", out var rulesElement) && rulesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in rulesElement.EnumerateArray())
                {
                    var ruleProps = GetProps(rule);
                    rules.Add(new NetworkSecurityRuleResource(
                        GetStringProp(rule, "name") ?? string.Empty,
                        NestedStringProp(ruleProps, "direction") ?? string.Empty,
                        NestedStringProp(ruleProps, "access") ?? string.Empty,
                        NestedIntProp(ruleProps, "priority") ?? 0,
                        NestedStringProp(ruleProps, "protocol") ?? string.Empty,
                        FirstString(NestedElement(ruleProps, "sourceAddressPrefix"), ruleProps, "sourceAddressPrefixes") ?? "*",
                        FirstString(NestedElement(ruleProps, "destinationAddressPrefix"), ruleProps, "destinationAddressPrefixes") ?? "*",
                        FirstString(NestedElement(ruleProps, "sourcePortRange"), ruleProps, "sourcePortRanges") ?? "*",
                        FirstString(NestedElement(ruleProps, "destinationPortRange"), ruleProps, "destinationPortRanges") ?? "*",
                        metadata));
                }
            }

            result.Add(new NetworkSecurityGroupResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                rules,
                IdArray(props, "subnets"),
                IdArray(props, "networkInterfaces"),
                metadata));
        }

        return result;
    }

    private static List<RouteTableResource> ParseRouteTables(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<RouteTableResource>();

        foreach (var row in rows)
        {
            var props = GetProps(row);
            var routes = new List<RouteResource>();

            if (props.TryGetProperty("routes", out var routesElement) && routesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var route in routesElement.EnumerateArray())
                {
                    var routeProps = GetProps(route);
                    routes.Add(new RouteResource(
                        GetStringProp(route, "name") ?? string.Empty,
                        NestedStringProp(routeProps, "addressPrefix"),
                        NestedStringProp(routeProps, "nextHopType"),
                        NestedStringProp(routeProps, "nextHopIpAddress"),
                        metadata));
                }
            }

            result.Add(new RouteTableResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                GetBool(props, "disableBgpRoutePropagation") ?? false,
                routes,
                IdArray(props, "subnets"),
                metadata));
        }

        return result;
    }

    private static List<NetworkInterfaceResource> ParseNetworkInterfaces(
        IReadOnlyList<JsonElement> rows,
        CollectionMetadata metadata,
        ICollection<LinkedResourceInfo> links)
    {
        var result = new List<NetworkInterfaceResource>();

        foreach (var row in rows)
        {
            var resourceId = GetRowString(row, "id");
            var props = GetProps(row);
            string? subnetId = null;
            string? publicIpId = null;
            string? privateIpAddress = null;

            if (props.TryGetProperty("ipConfigurations", out var ipConfigurations) && ipConfigurations.ValueKind == JsonValueKind.Array)
            {
                foreach (var config in ipConfigurations.EnumerateArray())
                {
                    var configProps = GetProps(config);
                    subnetId ??= NestedStringProp(configProps, "subnet", "id");
                    publicIpId ??= NestedStringProp(configProps, "publicIPAddress", "id");
                    privateIpAddress ??= NestedStringProp(configProps, "privateIPAddress");
                }
            }

            // The interface's host is in the payload already, so naming it costs no extra query. A scale-set
            // instance appears here as its own resource id; ArmResourceId keeps both parts of that identity.
            var virtualMachineId = NestedStringProp(props, "virtualMachine", "id");
            if (!string.IsNullOrWhiteSpace(virtualMachineId))
            {
                links.Add(CreateDerivedLink(resourceId, virtualMachineId, LinkedResourceKinds.HostedOn, subResource: null, metadata));
            }

            // A private endpoint's interface is collected as a network interface of its own, and the payload says
            // which endpoint owns it. Reading it here is what keeps an endpoint and the interface it created
            // connected, instead of leaving two resources sharing a subnet with no relationship between them.
            var privateEndpointId = NestedStringProp(props, "privateEndpoint", "id");
            if (!string.IsNullOrWhiteSpace(privateEndpointId))
            {
                links.Add(CreateDerivedLink(resourceId, privateEndpointId, LinkedResourceKinds.AttachedTo, subResource: null, metadata));
            }

            result.Add(new NetworkInterfaceResource(
                resourceId,
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                VNetOfSubnet(subnetId),
                subnetId,
                NestedStringProp(props, "networkSecurityGroup", "id"),
                publicIpId,
                privateIpAddress,
                metadata));
        }

        return result;
    }

    private static List<PublicIpAddressResource> ParsePublicIpAddresses(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<PublicIpAddressResource>();

        foreach (var row in rows)
        {
            var props = GetProps(row);
            var ipConfigurationId = NestedStringProp(props, "ipConfiguration", "id");
            var networkInterfaceId = ipConfigurationId is not null && ipConfigurationId.Contains("/networkInterfaces/", StringComparison.Ordinal)
                ? ipConfigurationId[..ipConfigurationId.IndexOf("/ipConfigurations/", StringComparison.Ordinal)]
                : null;

            result.Add(new PublicIpAddressResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                NestedStringProp(props, "ipAddress"),
                networkInterfaceId,
                metadata));
        }

        return result;
    }

    private static List<PrivateEndpointResource> ParsePrivateEndpoints(
        IReadOnlyList<JsonElement> rows,
        CollectionMetadata metadata,
        ICollection<LinkedResourceInfo> links)
    {
        var result = new List<PrivateEndpointResource>();

        foreach (var row in rows)
        {
            var resourceId = GetRowString(row, "id");
            var props = GetProps(row);
            var serviceIds = new List<string>();
            var seenServiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Both collections carry targets: one holds the connections the service approved automatically, the
            // other the ones an owner approved by hand. Reading only the first hid every manually approved
            // endpoint's target, which is why both feed the collected ids and the link evidence alike.
            foreach (var connection in EnumeratePrivateLinkConnections(props))
            {
                var connectionProps = GetProps(connection);
                var targetResourceId = NestedStringProp(connectionProps, "privateLinkServiceId");
                if (string.IsNullOrWhiteSpace(targetResourceId) || !seenServiceIds.Add(targetResourceId))
                {
                    continue;
                }

                serviceIds.Add(targetResourceId);
                links.Add(CreateDerivedLink(
                    resourceId,
                    targetResourceId,
                    LinkedResourceKinds.PrivateEndpointFor,
                    ReadGroupIds(connectionProps),
                    metadata));
            }

            result.Add(new PrivateEndpointResource(
                resourceId,
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                NestedStringProp(props, "subnet", "id"),
                serviceIds,
                metadata));
        }

        return result;
    }

    /// <summary>
    /// The private-link connection collections a private endpoint can carry. Both have the same shape; only the
    /// approval path differs, and a target in either is a resource the endpoint fronts.
    /// </summary>
    private static readonly string[] PrivateLinkConnectionCollections =
    [
        "privateLinkServiceConnections",
        "manualPrivateLinkServiceConnections"
    ];

    private static IEnumerable<JsonElement> EnumeratePrivateLinkConnections(JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var collectionName in PrivateLinkConnectionCollections)
        {
            if (!properties.TryGetProperty(collectionName, out var connections) || connections.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var connection in connections.EnumerateArray())
            {
                yield return connection;
            }
        }
    }

    /// <summary>
    /// Reads a connection's group ids — the target service's sub-resource, such as <c>sqlServer</c> or
    /// <c>blob</c> — as one comma-separated value, because the link contract states a single sub-resource.
    /// </summary>
    private static string? ReadGroupIds(JsonElement connectionProperties)
    {
        if (!connectionProperties.TryGetProperty("groupIds", out var groupIds) || groupIds.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = groupIds
            .EnumerateArray()
            .Where(groupId => groupId.ValueKind == JsonValueKind.String)
            .Select(groupId => groupId.GetString()!)
            .Where(groupId => !string.IsNullOrWhiteSpace(groupId))
            .ToList();

        return values.Count == 0 ? null : string.Join(',', values);
    }

    /// <summary>
    /// Records every declared peering whose remote network was not collected.
    /// </summary>
    /// <remarks>
    /// This is evidence about a collected network, not a request to read another one: the target is derived from
    /// its id and never looked up, so the collection still reads exactly the scope the operator chose. It exists
    /// because dropping the peering makes a topology look closed when it is not, and a conclusion drawn over the
    /// collected networks must not imply that nothing lies beyond them (decision D3).
    /// </remarks>
    private static void AddUncollectedPeeringLinks(
        IReadOnlyList<VNetResource> vnets,
        ICollection<LinkedResourceInfo> links,
        CollectionMetadata metadata)
    {
        var collectedVNetIds = vnets
            .Select(static vnet => vnet.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vnet in vnets)
        {
            foreach (var peering in vnet.Peerings)
            {
                var remoteId = peering.RemoteVirtualNetworkId;

                if (string.IsNullOrWhiteSpace(remoteId) || collectedVNetIds.Contains(remoteId))
                {
                    continue;
                }

                // Two peerings from one network to the same remote network describe one relationship.
                if (!recorded.Add($"{vnet.Id}\u001f{remoteId}"))
                {
                    continue;
                }

                links.Add(CreateDerivedLink(
                    vnet.Id,
                    remoteId,
                    LinkedResourceKinds.PeeredWith,
                    subResource: null,
                    metadata));
            }
        }
    }

    /// <summary>
    /// Builds the link a payload states, deriving the target's identity from its id. Derivation is why a
    /// collection can name a target without a lookup; a later resolution replaces these values with what the
    /// target itself says.
    /// </summary>
    private static LinkedResourceInfo CreateDerivedLink(
        string sourceResourceId,
        string targetResourceId,
        string linkKind,
        string? subResource,
        CollectionMetadata metadata)
    {
        var target = ArmResourceId.ParseOrNull(targetResourceId);

        return new LinkedResourceInfo(
            SourceResourceId: sourceResourceId,
            TargetResourceId: targetResourceId,
            LinkKind: linkKind,
            Resolution: LinkedResourceInfo.ResolutionDerived,
            Metadata: metadata,
            SubResource: subResource,
            DisplayName: target?.Name,
            AzureResourceType: target?.AzureResourceType,
            ResourceGroup: target?.ResourceGroup);
    }

    private static List<PrivateDnsZoneResource> ParsePrivateDnsZones(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<PrivateDnsZoneResource>();

        foreach (var row in rows)
        {
            result.Add(new PrivateDnsZoneResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                metadata));
        }

        return result;
    }

    private static List<AzureFirewallResource> ParseAzureFirewalls(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<AzureFirewallResource>();

        foreach (var row in rows)
        {
            var props = GetProps(row);
            var ipConfigSubnetIds = new List<string>();
            var managementSubnetIds = new List<string>();

            if (props.TryGetProperty("ipConfigurations", out var ipConfigurations) && ipConfigurations.ValueKind == JsonValueKind.Array)
            {
                foreach (var config in ipConfigurations.EnumerateArray())
                {
                    var subnetId = NestedStringProp(GetProps(config), "subnet", "id");
                    if (!string.IsNullOrWhiteSpace(subnetId))
                    {
                        ipConfigSubnetIds.Add(subnetId);
                    }
                }
            }

            if (props.TryGetProperty("managementIpConfiguration", out var managementConfiguration) && managementConfiguration.ValueKind == JsonValueKind.Object)
            {
                var managementSubnetId = NestedStringProp(GetProps(managementConfiguration), "subnet", "id");
                if (!string.IsNullOrWhiteSpace(managementSubnetId))
                {
                    managementSubnetIds.Add(managementSubnetId);
                }
            }

            result.Add(new AzureFirewallResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                NestedStringProp(props, "firewallPolicy", "id"),
                ipConfigSubnetIds,
                managementSubnetIds,
                metadata));
        }

        return result;
    }

    private static List<VirtualHubResource> ParseVirtualHubs(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<VirtualHubResource>();

        foreach (var row in rows)
        {
            var props = GetProps(row);
            var connectedVnets = new List<string>();

            if (props.TryGetProperty("virtualNetworkConnections", out var connections) && connections.ValueKind == JsonValueKind.Array)
            {
                foreach (var connection in connections.EnumerateArray())
                {
                    var vnetId = NestedStringProp(GetProps(connection), "remoteVirtualNetwork", "id");
                    if (!string.IsNullOrWhiteSpace(vnetId))
                    {
                        connectedVnets.Add(vnetId);
                    }
                }
            }

            result.Add(new VirtualHubResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                NestedStringProp(props, "virtualWan", "id"),
                connectedVnets,
                metadata));
        }

        return result;
    }

    private static List<VirtualWanResource> ParseVirtualWans(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<VirtualWanResource>();

        foreach (var row in rows)
        {
            result.Add(new VirtualWanResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                metadata));
        }

        return result;
    }

    private static List<ExpressRouteCircuitResource> ParseExpressRouteCircuits(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<ExpressRouteCircuitResource>();

        foreach (var row in rows)
        {
            var props = GetProps(row);
            var serviceProvider = props.TryGetProperty("serviceProviderProperties", out var serviceProviderProperties)
                ? serviceProviderProperties
                : default;

            result.Add(new ExpressRouteCircuitResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                NestedStringProp(serviceProvider, "serviceProviderName"),
                NestedIntProp(serviceProvider, "bandwidthInMbps"),
                NestedStringProp(serviceProvider, "peeringLocation"),
                metadata));
        }

        return result;
    }

    private static List<VirtualNetworkGatewayResource> ParseVirtualNetworkGateways(IReadOnlyList<JsonElement> rows, CollectionMetadata metadata)
    {
        var result = new List<VirtualNetworkGatewayResource>();

        foreach (var row in rows)
        {
            var props = GetProps(row);
            string? subnetId = null;

            if (props.TryGetProperty("ipConfigurations", out var ipConfigurations) && ipConfigurations.ValueKind == JsonValueKind.Array)
            {
                foreach (var configuration in ipConfigurations.EnumerateArray())
                {
                    subnetId ??= NestedStringProp(GetProps(configuration), "subnet", "id");
                }
            }

            result.Add(new VirtualNetworkGatewayResource(
                GetRowString(row, "id"),
                GetRowString(row, "name"),
                GetRowString(row, "resourceGroup"),
                GetRowString(row, "location"),
                NestedStringProp(props, "gatewayType"),
                VNetOfSubnet(subnetId),
                metadata));
        }

        return result;
    }

    private static List<RelationshipInfo> BuildRelationships(
        IReadOnlyList<VNetResource> vnets,
        IReadOnlyList<NetworkSecurityGroupResource> nsgs,
        IReadOnlyList<RouteTableResource> routeTables,
        IReadOnlyList<NetworkInterfaceResource> nics,
        IReadOnlyList<PrivateEndpointResource> privateEndpoints,
        IReadOnlyList<VirtualHubResource> virtualHubs,
        CollectionMetadata metadata)
    {
        var relationships = new List<RelationshipInfo>();

        foreach (var vnet in vnets)
        {
            foreach (var subnet in vnet.Subnets)
            {
                relationships.Add(new RelationshipInfo(
                    "Contains",
                    vnet.Id,
                    subnet.Id,
                    "VNet contains subnet",
                    metadata));

                if (!string.IsNullOrWhiteSpace(subnet.NetworkSecurityGroupId))
                {
                    relationships.Add(new RelationshipInfo(
                        "SecuredBy",
                        subnet.Id,
                        subnet.NetworkSecurityGroupId,
                        "Subnet associated with NSG",
                        metadata));
                }

                if (!string.IsNullOrWhiteSpace(subnet.RouteTableId))
                {
                    relationships.Add(new RelationshipInfo(
                        "RoutedBy",
                        subnet.Id,
                        subnet.RouteTableId,
                        "Subnet associated with route table",
                        metadata));
                }
            }

            foreach (var peering in vnet.Peerings)
            {
                if (!string.IsNullOrWhiteSpace(peering.RemoteVirtualNetworkId))
                {
                    relationships.Add(new RelationshipInfo(
                        "PeersWith",
                        vnet.Id,
                        peering.RemoteVirtualNetworkId,
                        peering.Name,
                        metadata));
                }
            }
        }

        foreach (var nsg in nsgs)
        {
            foreach (var subnetId in nsg.SubnetIds)
            {
                relationships.Add(new RelationshipInfo(
                    "AttachedTo",
                    nsg.Id,
                    subnetId,
                    "NSG attached to subnet",
                    metadata));
            }

            foreach (var nicId in nsg.NetworkInterfaceIds)
            {
                relationships.Add(new RelationshipInfo(
                    "AttachedTo",
                    nsg.Id,
                    nicId,
                    "NSG attached to NIC",
                    metadata));
            }
        }

        foreach (var routeTable in routeTables)
        {
            foreach (var subnetId in routeTable.SubnetIds)
            {
                relationships.Add(new RelationshipInfo(
                    "AttachedTo",
                    routeTable.Id,
                    subnetId,
                    "Route table attached to subnet",
                    metadata));
            }
        }

        foreach (var nic in nics)
        {
            if (!string.IsNullOrWhiteSpace(nic.SubnetId))
            {
                relationships.Add(new RelationshipInfo(
                    "ConnectedTo",
                    nic.Id,
                    nic.SubnetId,
                    "NIC connected to subnet",
                    metadata));
            }
        }

        foreach (var endpoint in privateEndpoints)
        {
            if (!string.IsNullOrWhiteSpace(endpoint.SubnetId))
            {
                relationships.Add(new RelationshipInfo(
                    "ConnectedTo",
                    endpoint.Id,
                    endpoint.SubnetId,
                    "Private endpoint connected to subnet",
                    metadata));
            }

            foreach (var serviceId in endpoint.PrivateLinkServiceIds)
            {
                relationships.Add(new RelationshipInfo(
                    "Targets",
                    endpoint.Id,
                    serviceId,
                    "Private endpoint service connection",
                    metadata));
            }
        }

        foreach (var hub in virtualHubs)
        {
            foreach (var vnetId in hub.ConnectedVirtualNetworkIds)
            {
                relationships.Add(new RelationshipInfo(
                    "ConnectedTo",
                    hub.Id,
                    vnetId,
                    "Virtual hub network connection",
                    metadata));
            }
        }

        return relationships;
    }

    private static CollectionMetadata CreateCollectionMetadata(
        string itemType,
        string source,
        DateTimeOffset timestamp,
        string permission,
        string status,
        double completeness,
        IReadOnlyList<string> errors,
        string? scopeId = null)
        => new(itemType, source, timestamp, permission, status, completeness, errors, scopeId);

    private static JsonElement GetProps(JsonElement element)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty("properties", out var properties)
            ? properties
            : default;

    private static string GetRowString(JsonElement row, string propertyName)
        => row.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string? GetStringProp(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? NestedStringProp(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static bool? NestedBoolProp(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind is JsonValueKind.True or JsonValueKind.False ? current.GetBoolean() : null;
    }

    private static int? NestedIntProp(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.Number ? current.GetInt32() : null;
    }

    private static JsonElement NestedElement(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return default;
            }
        }

        return current;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static List<string> IdArray(JsonElement props, string propertyName)
    {
        var result = new List<string>();
        if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String)
            {
                var id = idProperty.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    private static string? FirstString(JsonElement single, JsonElement parent, string arrayPropertyName)
    {
        if (single.ValueKind == JsonValueKind.String)
        {
            return single.GetString();
        }

        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(arrayPropertyName, out var arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    return item.GetString();
                }
            }
        }

        return null;
    }

    private static List<string> StringArray(JsonElement element, string propertyName)
    {
        var result = new List<string>();
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value);
                    }
                }
            }
        }

        return result;
    }

    private static List<string> AddressSpace(JsonElement props)
    {
        var result = new List<string>();
        if (props.ValueKind == JsonValueKind.Object &&
            props.TryGetProperty("addressSpace", out var addressSpace) &&
            addressSpace.ValueKind == JsonValueKind.Object &&
            addressSpace.TryGetProperty("addressPrefixes", out var prefixes) &&
            prefixes.ValueKind == JsonValueKind.Array)
        {
            foreach (var prefix in prefixes.EnumerateArray())
            {
                if (prefix.ValueKind == JsonValueKind.String)
                {
                    var value = prefix.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value);
                    }
                }
            }
        }

        return result;
    }

    private static string? VNetOfSubnet(string? subnetId)
    {
        if (string.IsNullOrWhiteSpace(subnetId))
        {
            return null;
        }

        var marker = "/subnets/";
        var index = subnetId.IndexOf(marker, StringComparison.Ordinal);
        return index > 0 ? subnetId[..index] : null;
    }
}
