using Radivum.Model;

namespace Radivum.Oss.Analysis;

/// <summary>
/// Deterministic Level-1 (configuration/topology) checks. Shared by every edition; the OSS CLI and
/// the Pro pipeline both run this same check set rather than maintaining a fork.
/// </summary>
public static class Checks
{
    private static readonly string[] ManagementPorts =
        ["22", "3389", "1433", "3306", "5432", "5900", "5985", "5986"];

    private static readonly HashSet<string> SpecialSubnets = new(StringComparer.OrdinalIgnoreCase)
    {
        "GatewaySubnet", "AzureFirewallSubnet", "AzureFirewallManagementSubnet", "RouteServerSubnet"
    };

    public static List<Finding> Run(NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var context = CheckContext.Create(snapshot.CollectionMetadata);
        var findings = new List<Finding>();
        var skippedChecks = new List<string>();

        CheckAddressSpaceOverlaps(snapshot, findings, context, skippedChecks);
        CheckSubnetsWithoutNsg(snapshot, findings, context, skippedChecks);
        CheckInternetManagementPorts(snapshot, findings, context, skippedChecks);
        CheckAnyAnyInboundAllows(snapshot, findings, context, skippedChecks);
        CheckDefaultInternetRoutes(snapshot, findings, context, skippedChecks);
        CheckRoutesToNone(snapshot, findings, context, skippedChecks);
        CheckAsymmetricPeerings(snapshot, findings, context, skippedChecks);
        CheckPrivateDnsCoverage(snapshot, findings, context, skippedChecks);
        CheckOrphanNsgs(snapshot, findings, context, skippedChecks);
        CheckOrphanRouteTables(snapshot, findings, context, skippedChecks);
        CheckOrphanPublicIps(snapshot, findings);
        CheckHubSpokeHeuristic(snapshot, findings, context, skippedChecks);
        AddSkippedChecksFinding(findings, skippedChecks);

        return FindingOrder.Sort(findings);
    }

    private static void CheckAddressSpaceOverlaps(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("VNets"))
        {
            skippedChecks.Add("NET-018 skipped: VNet inventory unavailable.");
            return;
        }

        var vnets = snapshot.Resources.VNets;
        if (vnets.Count < 2)
        {
            return;
        }

        var labels = BuildVNetLabels(vnets);
        var connectedPairs = BuildConnectedVNetPairs(snapshot);
        var connectedOverlaps = new List<string>();
        var unconnectedOverlaps = new List<string>();
        var connectedVnetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unconnectedVnetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < vnets.Count; i++)
        {
            for (var j = i + 1; j < vnets.Count; j++)
            {
                foreach (var leftPrefix in vnets[i].AddressSpace)
                {
                    foreach (var rightPrefix in vnets[j].AddressSpace)
                    {
                        if (!Cidr.Overlaps(leftPrefix, rightPrefix))
                        {
                            continue;
                        }

                        var description =
                            $"{labels[vnets[i].Id]} <-> {labels[vnets[j].Id]}: {leftPrefix} / {rightPrefix}";

                        if (connectedPairs.Contains(PairKey(vnets[i].Id, vnets[j].Id)))
                        {
                            connectedOverlaps.Add(description);
                            connectedVnetIds.Add(vnets[i].Id);
                            connectedVnetIds.Add(vnets[j].Id);
                        }
                        else
                        {
                            unconnectedOverlaps.Add(description);
                            unconnectedVnetIds.Add(vnets[i].Id);
                            unconnectedVnetIds.Add(vnets[j].Id);
                        }
                    }
                }
            }
        }

        // Only an overlap on a relationship that exists is reported as a defect. Reusing address space across
        // networks that nothing joins is an ordinary design choice, so it is stated as information instead: an
        // operator planning to connect them still sees it, and an operator who never will is not alarmed by it.
        if (connectedOverlaps.Count > 0)
        {
            findings.Add(CreateFinding(
                "NET-018",
                "High",
                "VNet address spaces overlap across connected networks",
                "These VNets overlap and are joined by a declared peering or by one virtual hub, so the overlap causes routing ambiguity on a path traffic can take.",
                connectedOverlaps,
                confidence: context.ConfidenceFor("VNets"),
                affectedResourceIds: OrderIds(connectedVnetIds)));
        }

        if (unconnectedOverlaps.Count > 0)
        {
            findings.Add(CreateFinding(
                "NET-019",
                "Info",
                "VNet address spaces overlap without an observed connection",
                "These VNets overlap and no peering or shared virtual hub between them was collected. Unconnected networks commonly reuse address space; if they are connected later, the overlap has to be resolved first.",
                unconnectedOverlaps,
                confidence: context.ConfidenceFor("VNets"),
                affectedResourceIds: OrderIds(unconnectedVnetIds)));
        }
    }

    /// <summary>
    /// Labels VNets for findings: the name alone, or the name plus its subscription when the name is not unique.
    /// </summary>
    /// <remarks>
    /// Several subscriptions make repeated VNet names ordinary. Qualifying only a duplicated name keeps every
    /// single-subscription finding text unchanged while leaving a collision unambiguous.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> BuildVNetLabels(IReadOnlyList<VNetResource> vnets)
    {
        var duplicatedNames = vnets
            .GroupBy(static vnet => vnet.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return vnets.ToDictionary(
            static vnet => vnet.Id,
            vnet => duplicatedNames.Contains(vnet.Name)
                ? $"{vnet.Name} ({ArmResourceId.ParseOrNull(vnet.Id)?.SubscriptionId ?? "unknown subscription"})"
                : vnet.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pairs of collected VNets joined by a declared peering or by membership of one virtual hub.
    /// </summary>
    /// <remarks>
    /// This classifies risk; it does not prove a forwarding path. Peering state is reported separately
    /// (<c>NET-025</c>), forwarded traffic and remote gateways may be disabled, and sharing a hub does not make a
    /// spoke reach another spoke. A remote peer that was not collected still forms the relationship, because the
    /// declaration exists even when the other side is outside the collected scope.
    /// </remarks>
    private static HashSet<string> BuildConnectedVNetPairs(NetworkSnapshot snapshot)
    {
        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collectedIds = snapshot.Resources.VNets
            .Select(static vnet => vnet.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var peersByRemoteId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var vnet in snapshot.Resources.VNets)
        {
            foreach (var peering in vnet.Peerings)
            {
                if (string.IsNullOrWhiteSpace(peering.RemoteVirtualNetworkId))
                {
                    continue;
                }

                var remoteId = peering.RemoteVirtualNetworkId;

                if (collectedIds.Contains(remoteId))
                {
                    pairs.Add(PairKey(vnet.Id, remoteId));
                }

                if (!peersByRemoteId.TryGetValue(remoteId, out var peers))
                {
                    peers = [];
                    peersByRemoteId[remoteId] = peers;
                }

                if (!peers.Contains(vnet.Id, StringComparer.OrdinalIgnoreCase))
                {
                    peers.Add(vnet.Id);
                }
            }
        }

        // A common remote peer joins the VNets that peer with it, whether or not that peer was collected. Azure
        // refuses to create a peering between overlapping address spaces, so this is where an overlap bites hardest.
        foreach (var peers in peersByRemoteId.Values)
        {
            for (var i = 0; i < peers.Count; i++)
            {
                for (var j = i + 1; j < peers.Count; j++)
                {
                    pairs.Add(PairKey(peers[i], peers[j]));
                }
            }
        }

        foreach (var hub in snapshot.Resources.VirtualHubs)
        {
            var connected = hub.ConnectedVirtualNetworkIds
                .Where(collectedIds.Contains)
                .ToList();

            for (var i = 0; i < connected.Count; i++)
            {
                for (var j = i + 1; j < connected.Count; j++)
                {
                    pairs.Add(PairKey(connected[i], connected[j]));
                }
            }
        }

        return pairs;
    }

    /// <summary>Order-independent key for a pair of VNet ids, so either direction finds the same pair.</summary>
    private static string PairKey(string leftId, string rightId)
        => string.Compare(leftId, rightId, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{leftId}\u001f{rightId}"
            : $"{rightId}\u001f{leftId}";

    private static IReadOnlyList<string> OrderIds(HashSet<string> ids)
        => ids.OrderBy(static id => id, StringComparer.Ordinal).ToList();

    private static void CheckSubnetsWithoutNsg(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("VNets"))
        {
            skippedChecks.Add("NET-004 skipped: VNet inventory unavailable.");
            return;
        }

        var labels = BuildVNetLabels(snapshot.Resources.VNets);
        var unsecuredSubnets = snapshot.Resources.VNets
            .SelectMany(vnet => vnet.Subnets.Select(subnet => (Label: labels[vnet.Id], subnet)))
            .Where(item => item.subnet.NetworkSecurityGroupId is null && !SpecialSubnets.Contains(item.subnet.Name))
            .Select(item => $"{item.Label}/{item.subnet.Name}")
            .ToList();

        if (unsecuredSubnets.Count > 0)
        {
            findings.Add(CreateFinding(
                "NET-004",
                "Medium",
                "Subnets without a network security group",
                "Traffic into these subnets is not filtered by an NSG. Subnets with service delegations may still enforce their own policies.",
                unsecuredSubnets,
                confidence: context.ConfidenceFor("VNets")));
        }
    }

    private static void CheckInternetManagementPorts(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("NSGs"))
        {
            skippedChecks.Add("NET-051 skipped: NSG inventory unavailable.");
            return;
        }

        var exposedRules = new List<string>();

        foreach (var nsg in snapshot.Resources.NetworkSecurityGroups)
        {
            foreach (var rule in nsg.Rules)
            {
                if (rule.Direction != "Inbound" || rule.Access != "Allow")
                {
                    continue;
                }

                if (!IsAnyOrInternet(rule.Source))
                {
                    continue;
                }

                if (rule.Protocol != "Tcp" && rule.Protocol != "*")
                {
                    continue;
                }

                if (ManagementPorts.Contains(rule.DestinationPort, StringComparer.Ordinal) || rule.DestinationPort == "*")
                {
                    exposedRules.Add($"{nsg.Name}/{rule.Name} (port {rule.DestinationPort})");
                }
            }
        }

        if (exposedRules.Count > 0)
        {
            findings.Add(CreateFinding(
                "NET-051",
                "High",
                "Management ports reachable from the internet",
                "NSG rules allow RDP/SSH/SQL or any port from 0.0.0.0/0 or the internet. Restrict to known source ranges or use Azure Bastion.",
                exposedRules,
                confidence: context.ConfidenceFor("NSGs")));
        }
    }

    private static void CheckDefaultInternetRoutes(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("RouteTables"))
        {
            skippedChecks.Add("NET-011 skipped: Route table inventory unavailable.");
            return;
        }

        var routeTables = snapshot.Resources.RouteTables
            .Where(table => table.Routes.Any(route => route.AddressPrefix is "0.0.0.0/0" && route.NextHopType is "Internet"))
            .Select(table => (Display: table.Name, ResourceId: table.Id))
            .ToList();

        if (routeTables.Count > 0)
        {
            findings.Add(CreateFinding(
                "NET-011",
                "Medium",
                "Default route via Internet in a route table",
                "0.0.0.0/0 with next hop Internet bypasses centralized egress (Azure Firewall/NVA). Verify this is intentional.",
                routeTables.Select(table => table.Display).ToList(),
                confidence: context.ConfidenceFor("RouteTables"),
                affectedResourceIds: routeTables.Select(table => table.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
        }
    }

    private static void CheckRoutesToNone(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("RouteTables"))
        {
            skippedChecks.Add("NET-012 skipped: Route table inventory unavailable.");
            return;
        }

        var impacted = snapshot.Resources.RouteTables
            .SelectMany(table => table.Routes
                .Where(route => string.Equals(route.NextHopType, "None", StringComparison.OrdinalIgnoreCase))
                .Select(route => (Display: $"{table.Name}/{route.Name}", ResourceId: table.Id)))
            .ToList();

        if (impacted.Count == 0)
        {
            return;
        }

        findings.Add(CreateFinding(
            "NET-012",
            "Medium",
            "Route table entries that drop traffic",
            "One or more route entries use next hop type None, which intentionally drops matching traffic.",
            impacted.Select(item => item.Display).ToList(),
            confidence: context.ConfidenceFor("RouteTables"),
            affectedResourceIds: impacted.Select(item => item.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
    }

    private static void CheckAsymmetricPeerings(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("VNets"))
        {
            skippedChecks.Add("NET-025 skipped: VNet peering inventory unavailable.");
            return;
        }

        var labels = BuildVNetLabels(snapshot.Resources.VNets);
        var disconnectedPeerings = snapshot.Resources.VNets
            .SelectMany(vnet => vnet.Peerings.Select(peering => (Label: labels[vnet.Id], peering)))
            .Where(item => item.peering.PeeringState is not null && item.peering.PeeringState != "Connected")
            .Select(item => $"{item.Label} -> {item.peering.Name} ({item.peering.PeeringState})")
            .ToList();

        if (disconnectedPeerings.Count > 0)
        {
            findings.Add(CreateFinding(
                "NET-025",
                "Warning",
                "VNet peering not connected",
                "A peering is in a non-Connected state (for example, one side not created or removed).",
                disconnectedPeerings,
                confidence: context.ConfidenceFor("VNets")));
        }
    }

    private static void CheckPrivateDnsCoverage(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("PrivateEndpoints") || context.IsUnavailable("PrivateDnsZones"))
        {
            skippedChecks.Add("NET-031 skipped: private endpoint or private DNS zone inventory unavailable.");
            return;
        }

        if (snapshot.Resources.PrivateEndpoints.Count > 0 && snapshot.Resources.PrivateDnsZones.Count == 0)
        {
            var endpointNames = snapshot.Resources.PrivateEndpoints.Select(endpoint => endpoint.Name).ToList();
            findings.Add(CreateFinding(
                "NET-031",
                "Warning",
                "Private endpoints but no private DNS zones in scope",
                "No private DNS zones found in the selected scope. Private endpoints typically need a linked private DNS zone to resolve via private IP.",
                endpointNames,
                confidence: context.ConfidenceFor("PrivateEndpoints", "PrivateDnsZones"),
                affectedResourceIds: snapshot.Resources.PrivateEndpoints.Select(endpoint => endpoint.Id).ToList()));
        }
    }

    private static void CheckOrphanNsgs(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("NSGs"))
        {
            skippedChecks.Add("NET-010 skipped: NSG inventory unavailable.");
            return;
        }

        var orphans = snapshot.Resources.NetworkSecurityGroups
            .Where(nsg => nsg.SubnetIds.Count == 0 && nsg.NetworkInterfaceIds.Count == 0)
            .Select(nsg => (Display: nsg.Name, ResourceId: nsg.Id))
            .ToList();

        if (orphans.Count > 0)
        {
            findings.Add(CreateFinding(
                "NET-010",
                "Info",
                "NSGs attached to nothing",
                "These NSGs are not associated with any subnet or NIC. Either attach or remove them to avoid confusion.",
                orphans.Select(item => item.Display).ToList(),
                confidence: context.ConfidenceFor("NSGs"),
                affectedResourceIds: orphans.Select(item => item.ResourceId).ToList()));
        }
    }

    private static void CheckOrphanRouteTables(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("RouteTables"))
        {
            skippedChecks.Add("NET-013 skipped: Route table inventory unavailable.");
            return;
        }

        var orphans = snapshot.Resources.RouteTables
            .Where(routeTable => routeTable.SubnetIds.Count == 0)
            .Select(routeTable => (Display: routeTable.Name, ResourceId: routeTable.Id))
            .ToList();

        if (orphans.Count == 0)
        {
            return;
        }

        findings.Add(CreateFinding(
            "NET-013",
            "Info",
            "Route tables attached to nothing",
            "These route tables are not associated with any subnet in the snapshot scope.",
            orphans.Select(item => item.Display).ToList(),
            confidence: context.ConfidenceFor("RouteTables"),
            affectedResourceIds: orphans.Select(item => item.ResourceId).ToList()));
    }

    private static void CheckOrphanPublicIps(NetworkSnapshot snapshot, List<Finding> findings)
    {
        var unassociated = snapshot.Resources.PublicIpAddresses
            .Where(publicIp => string.IsNullOrWhiteSpace(publicIp.AssociatedNetworkInterfaceId))
            .Select(publicIp => (Display: publicIp.Name, ResourceId: publicIp.Id))
            .ToList();

        if (unassociated.Count == 0)
        {
            return;
        }

        findings.Add(CreateFinding(
            "NET-014",
            "Info",
            "Public IPs not attached to NICs",
            "These public IP resources have no NIC association in scope and may be orphaned or reserved for future use.",
            unassociated.Select(item => item.Display).ToList(),
            affectedResourceIds: unassociated.Select(item => item.ResourceId).ToList()));
    }

    private static void CheckAnyAnyInboundAllows(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("NSGs"))
        {
            skippedChecks.Add("NET-052 skipped: NSG inventory unavailable.");
            return;
        }

        var broadRules = snapshot.Resources.NetworkSecurityGroups
            .SelectMany(nsg => nsg.Rules
                .Where(rule =>
                    string.Equals(rule.Direction, "Inbound", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rule.Access, "Allow", StringComparison.OrdinalIgnoreCase) &&
                    IsAnyOrInternet(rule.Source) &&
                    (string.Equals(rule.Destination, "*", StringComparison.OrdinalIgnoreCase) || string.Equals(rule.Destination, "Any", StringComparison.OrdinalIgnoreCase)) &&
                    (string.Equals(rule.DestinationPort, "*", StringComparison.OrdinalIgnoreCase) || string.Equals(rule.DestinationPort, "Any", StringComparison.OrdinalIgnoreCase)))
                .Select(rule => (Display: $"{nsg.Name}/{rule.Name}", ResourceId: nsg.Id)))
            .ToList();

        if (broadRules.Count == 0)
        {
            return;
        }

        findings.Add(CreateFinding(
            "NET-052",
            "High",
            "Broad inbound allow rules in NSGs",
            "One or more NSG rules allow broad inbound access from any or internet sources to any destination and port.",
            broadRules.Select(item => item.Display).ToList(),
            confidence: context.ConfidenceFor("NSGs"),
            affectedResourceIds: broadRules.Select(item => item.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
    }

    private static void CheckHubSpokeHeuristic(NetworkSnapshot snapshot, List<Finding> findings, CheckContext context, List<string> skippedChecks)
    {
        if (context.IsUnavailable("VNets"))
        {
            skippedChecks.Add("NET-090 skipped: VNet inventory unavailable.");
            return;
        }

        var labels = BuildVNetLabels(snapshot.Resources.VNets);
        var hubs = snapshot.Resources.VNets
            .Where(vnet => vnet.Peerings.Count >= 2)
            .Select(vnet => (Display: $"{labels[vnet.Id]} ({vnet.Peerings.Count} peerings)", ResourceId: vnet.Id))
            .ToList();

        if (hubs.Count > 0)
        {
            findings.Add(CreateFinding(
                "NET-090",
                "Info",
                "Potential hub VNets (heuristic)",
                "VNets peered with two or more others often act as hubs. Shown to help orientation; verify against intended design.",
                hubs.Select(item => item.Display).ToList(),
                confidence: context.ConfidenceFor("VNets"),
                affectedResourceIds: hubs.Select(item => item.ResourceId).ToList()));
        }
    }

    private static void AddSkippedChecksFinding(List<Finding> findings, IReadOnlyList<string> skippedChecks)
    {
        if (skippedChecks.Count == 0)
        {
            return;
        }

        findings.Add(CreateFinding(
            "NET-001",
            "Info",
            "Some deterministic checks were skipped due to incomplete collection",
            "One or more Level-1 checks were skipped because required inventory sources were unavailable for this snapshot scope.",
            skippedChecks,
            confidence: "High"));
    }

    private static Finding CreateFinding(
        string code,
        string severity,
        string title,
        string description,
        IReadOnlyList<string> resources,
        string confidence = "High",
        IReadOnlyList<string>? affectedResourceIds = null)
    {
        var canonicalAffectedResourceIds = (affectedResourceIds ?? resources.Where(IsAzureResourceId).ToList())
            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var affectedGraphNodeIds = canonicalAffectedResourceIds
            .Select(NetworkGraphContract.CreateNodeIdFromResourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var evidenceSourceIds = canonicalAffectedResourceIds.Count > 0
            ? canonicalAffectedResourceIds
            : resources;

        var evidence = evidenceSourceIds
            .Select(resource => new FindingEvidence(
                SourceType: "ConfiguredResource",
                SourceId: resource,
                Description: "Derived from configured network inventory.",
                CollectedAt: null))
            .ToList();

        return new Finding(code, severity, title, description, confidence, evidence, resources)
        {
            EvidenceLevel = "Configured",
            Source = "Rule",
            AffectedResourceIds = canonicalAffectedResourceIds,
            AffectedGraphNodeIds = affectedGraphNodeIds
        };
    }

    private static bool IsAzureResourceId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith("/", StringComparison.Ordinal);

    private static bool IsAnyOrInternet(string source)
        => source is "*" or "0.0.0.0/0" or "Internet" or "Any";
}
