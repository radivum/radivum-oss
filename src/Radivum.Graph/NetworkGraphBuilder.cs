using Radivum.Model;

namespace Radivum.Graph;

/// <summary>
/// Builds a deterministic, renderer-neutral <see cref="NetworkGraph"/> from a <see cref="NetworkSnapshot"/>.
/// </summary>
public sealed class NetworkGraphBuilder
{
    /// <summary>
    /// Metadata key marking a node the graph created from a declared link rather than from collected inventory.
    /// Both the network-view projector and the browser key off this value, so it is deliberately metadata and
    /// not a node type: a node's type still says what the resource is.
    /// </summary>
    private const string LinkedOriginMetadataKey = "origin";

    private const string LinkedOriginValue = "linked";

    /// <summary>
    /// Builds a graph from a snapshot while tolerating partial data.
    /// </summary>
    /// <param name="snapshot">Source snapshot.</param>
    /// <param name="generatedAt">Optional graph generation timestamp; UTC now when omitted.</param>
    /// <returns>Deterministic graph projection.</returns>
    public NetworkGraph Build(NetworkSnapshot snapshot, DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var nodes = new Dictionary<string, NetworkNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, NetworkEdge>(StringComparer.OrdinalIgnoreCase);

        var scopeNodeId = NetworkGraphContract.CreateSyntheticNodeId("Scope", snapshot.Scope.ScopeDescription);
        AddNode(nodes, new NetworkNode(
            Id: scopeNodeId,
            ResourceId: null,
            NodeType: "Scope",
            DisplayName: snapshot.Scope.ScopeDescription,
            AzureResourceType: null,
            SubscriptionId: null,
            ResourceGroup: null,
            Region: null,
            ParentNodeId: null,
            Metadata: null));

        var subscriptionNodeIds = EnsureSubscriptionNodes(snapshot, nodes, scopeNodeId, edges);
        var resourceGroupNodeIds = EnsureResourceGroupNodes(snapshot, nodes, subscriptionNodeIds, edges);

        // Targets a declared link already names. The private-endpoint pass keeps its legacy placeholder for every
        // target no link covers, so a snapshot written before links existed looks exactly as it did.
        var linkedTargets = CollectLinkedTargets(snapshot);

        AddNsgNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddRouteTableNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddVnetNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddNetworkInterfaceNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddPublicIpNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddPrivateEndpointNodes(snapshot, nodes, resourceGroupNodeIds, edges, linkedTargets);
        AddPrivateDnsZoneNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddAzureFirewallNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddVirtualWanNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddVirtualHubNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddExpressRouteCircuitNodes(snapshot, nodes, resourceGroupNodeIds, edges);
        AddVirtualNetworkGatewayNodes(snapshot, nodes, resourceGroupNodeIds, edges);

        // Last, so every collected source node exists before a link becomes an edge, and so a target that is
        // itself a collected resource keeps the identity the collection gave it.
        AddLinkedResourceNodes(snapshot, nodes, edges);

        var groups = BuildGroups(nodes);

        return new NetworkGraph(
            GraphSchemaVersion: NetworkGraphContract.GraphSchemaVersionV1,
            SourceSnapshotSchemaVersion: snapshot.SchemaVersion,
            SourceSnapshotToolVersion: snapshot.ToolVersion,
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ScopeDescription: snapshot.Scope.ScopeDescription,
            Nodes: nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray(),
            Edges: edges.Values.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
            Groups: groups,
            Metadata: new Dictionary<string, string>
            {
                ["sourceTenantId"] = snapshot.TenantId,
                ["sourceSubscriptions"] = string.Join(',', snapshot.SubscriptionIds.OrderBy(id => id, StringComparer.Ordinal))
            });
    }

    private static Dictionary<string, string> EnsureSubscriptionNodes(
        NetworkSnapshot snapshot,
        IDictionary<string, NetworkNode> nodes,
        string scopeNodeId,
        IDictionary<string, NetworkEdge> edges)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subscriptionId in snapshot.SubscriptionIds.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                continue;
            }

            var subscriptionNodeId = NetworkGraphContract.CreateSyntheticNodeId("Subscription", subscriptionId);
            result[subscriptionId] = subscriptionNodeId;

            AddNode(nodes, new NetworkNode(
                Id: subscriptionNodeId,
                ResourceId: null,
                NodeType: "Subscription",
                DisplayName: subscriptionId,
                AzureResourceType: null,
                SubscriptionId: subscriptionId,
                ResourceGroup: null,
                Region: null,
                ParentNodeId: scopeNodeId,
                Metadata: null));

            AddEdge(edges, new NetworkEdge(
                Id: NetworkGraphContract.CreateEdgeId("Contains", scopeNodeId, subscriptionNodeId),
                SourceNodeId: scopeNodeId,
                TargetNodeId: subscriptionNodeId,
                RelationshipType: "Contains",
                DisplayName: "Scope contains subscription",
                AzureResourceId: null,
                Metadata: null));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Scope.SubscriptionId) && !result.ContainsKey(snapshot.Scope.SubscriptionId))
        {
            var scopeSubscription = snapshot.Scope.SubscriptionId;
            var scopeSubscriptionNodeId = NetworkGraphContract.CreateSyntheticNodeId("Subscription", scopeSubscription);
            result[scopeSubscription] = scopeSubscriptionNodeId;

            AddNode(nodes, new NetworkNode(
                Id: scopeSubscriptionNodeId,
                ResourceId: null,
                NodeType: "Subscription",
                DisplayName: scopeSubscription,
                AzureResourceType: null,
                SubscriptionId: scopeSubscription,
                ResourceGroup: null,
                Region: null,
                ParentNodeId: scopeNodeId,
                Metadata: null));

            AddEdge(edges, new NetworkEdge(
                Id: NetworkGraphContract.CreateEdgeId("Contains", scopeNodeId, scopeSubscriptionNodeId),
                SourceNodeId: scopeNodeId,
                TargetNodeId: scopeSubscriptionNodeId,
                RelationshipType: "Contains",
                DisplayName: "Scope contains subscription",
                AzureResourceId: null,
                Metadata: null));
        }

        return result;
    }

    private static Dictionary<string, string> EnsureResourceGroupNodes(
        NetworkSnapshot snapshot,
        IDictionary<string, NetworkNode> nodes,
        IReadOnlyDictionary<string, string> subscriptionNodeIds,
        IDictionary<string, NetworkEdge> edges)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in EnumerateResourceDescriptors(snapshot))
        {
            if (string.IsNullOrWhiteSpace(descriptor.SubscriptionId) || string.IsNullOrWhiteSpace(descriptor.ResourceGroup))
            {
                continue;
            }

            var key = $"{descriptor.SubscriptionId}/{descriptor.ResourceGroup}";
            if (map.ContainsKey(key))
            {
                continue;
            }

            if (!subscriptionNodeIds.TryGetValue(descriptor.SubscriptionId, out var subscriptionNodeId))
            {
                continue;
            }

            var resourceGroupNodeId = NetworkGraphContract.CreateSyntheticNodeId("ResourceGroup", key);
            map[key] = resourceGroupNodeId;

            AddNode(nodes, new NetworkNode(
                Id: resourceGroupNodeId,
                ResourceId: null,
                NodeType: "ResourceGroup",
                DisplayName: descriptor.ResourceGroup,
                AzureResourceType: null,
                SubscriptionId: descriptor.SubscriptionId,
                ResourceGroup: descriptor.ResourceGroup,
                Region: null,
                ParentNodeId: subscriptionNodeId,
                Metadata: null));

            AddEdge(edges, new NetworkEdge(
                Id: NetworkGraphContract.CreateEdgeId("Contains", subscriptionNodeId, resourceGroupNodeId),
                SourceNodeId: subscriptionNodeId,
                TargetNodeId: resourceGroupNodeId,
                RelationshipType: "Contains",
                DisplayName: "Subscription contains resource group",
                AzureResourceId: null,
                Metadata: null));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Scope.SubscriptionId) && !string.IsNullOrWhiteSpace(snapshot.Scope.ResourceGroup))
        {
            var scopeKey = $"{snapshot.Scope.SubscriptionId}/{snapshot.Scope.ResourceGroup}";
            if (!map.ContainsKey(scopeKey) && subscriptionNodeIds.TryGetValue(snapshot.Scope.SubscriptionId, out var scopeSubscriptionNodeId))
            {
                var scopeRgNodeId = NetworkGraphContract.CreateSyntheticNodeId("ResourceGroup", scopeKey);
                map[scopeKey] = scopeRgNodeId;

                AddNode(nodes, new NetworkNode(
                    Id: scopeRgNodeId,
                    ResourceId: null,
                    NodeType: "ResourceGroup",
                    DisplayName: snapshot.Scope.ResourceGroup,
                    AzureResourceType: null,
                    SubscriptionId: snapshot.Scope.SubscriptionId,
                    ResourceGroup: snapshot.Scope.ResourceGroup,
                    Region: null,
                    ParentNodeId: scopeSubscriptionNodeId,
                    Metadata: null));

                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("Contains", scopeSubscriptionNodeId, scopeRgNodeId),
                    SourceNodeId: scopeSubscriptionNodeId,
                    TargetNodeId: scopeRgNodeId,
                    RelationshipType: "Contains",
                    DisplayName: "Subscription contains resource group",
                    AzureResourceId: null,
                    Metadata: null));
            }
        }

        return map;
    }

    private static void AddVnetNodes(
        NetworkSnapshot snapshot,
        IDictionary<string, NetworkNode> nodes,
        IReadOnlyDictionary<string, string> resourceGroupNodeIds,
        IDictionary<string, NetworkEdge> edges)
    {
        foreach (var vnet in snapshot.Resources.VNets.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var vnetNodeId = AddResourceNode(nodes, vnet.Id, "VirtualNetwork", vnet.Name, "Microsoft.Network/virtualNetworks", vnet.ResourceGroup, vnet.Location);
            AttachToResourceGroup(nodes, edges, vnetNodeId, vnet.Id, vnet.ResourceGroup, resourceGroupNodeIds);

            foreach (var subnet in vnet.Subnets.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                var subnetNodeId = AddResourceNode(nodes, subnet.Id, "Subnet", subnet.Name, "Microsoft.Network/virtualNetworks/subnets", vnet.ResourceGroup, vnet.Location, parentNodeId: vnetNodeId);

                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("Contains", vnetNodeId, subnetNodeId),
                    SourceNodeId: vnetNodeId,
                    TargetNodeId: subnetNodeId,
                    RelationshipType: "Contains",
                    DisplayName: "VNet contains subnet",
                    AzureResourceId: null,
                    Metadata: null));

                if (!string.IsNullOrWhiteSpace(subnet.NetworkSecurityGroupId) && TryGetNodeId(nodes, subnet.NetworkSecurityGroupId, out var nsgNodeId))
                {
                    AddEdge(edges, new NetworkEdge(
                        Id: NetworkGraphContract.CreateEdgeId("ProtectedBy", subnetNodeId, nsgNodeId),
                        SourceNodeId: subnetNodeId,
                        TargetNodeId: nsgNodeId,
                        RelationshipType: "ProtectedBy",
                        DisplayName: "Subnet protected by NSG",
                        AzureResourceId: subnet.NetworkSecurityGroupId,
                        Metadata: null));
                }

                if (!string.IsNullOrWhiteSpace(subnet.RouteTableId) && TryGetNodeId(nodes, subnet.RouteTableId, out var routeTableNodeId))
                {
                    AddEdge(edges, new NetworkEdge(
                        Id: NetworkGraphContract.CreateEdgeId("RoutesVia", subnetNodeId, routeTableNodeId),
                        SourceNodeId: subnetNodeId,
                        TargetNodeId: routeTableNodeId,
                        RelationshipType: "RoutesVia",
                        DisplayName: "Subnet routes via route table",
                        AzureResourceId: subnet.RouteTableId,
                        Metadata: null));
                }
            }
        }

        foreach (var vnet in snapshot.Resources.VNets.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryGetNodeId(nodes, vnet.Id, out var vnetNodeId))
            {
                continue;
            }

            foreach (var peering in vnet.Peerings.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(peering.RemoteVirtualNetworkId))
                {
                    continue;
                }

                if (!TryGetNodeId(nodes, peering.RemoteVirtualNetworkId, out var remoteNodeId))
                {
                    continue;
                }

                var peeringResourceId = $"{vnet.Id}/virtualNetworkPeerings/{peering.Name}";
                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("PeeredWith", vnetNodeId, remoteNodeId, peeringResourceId),
                    SourceNodeId: vnetNodeId,
                    TargetNodeId: remoteNodeId,
                    RelationshipType: "PeeredWith",
                    DisplayName: $"VNet peering {vnet.Name} -> {peering.Name}",
                    AzureResourceId: peeringResourceId,
                    Metadata: new Dictionary<string, string>
                    {
                        ["peeringState"] = peering.PeeringState ?? string.Empty,
                        ["allowVirtualNetworkAccess"] = peering.AllowVirtualNetworkAccess.ToString(),
                        ["allowForwardedTraffic"] = peering.AllowForwardedTraffic.ToString(),
                        ["allowGatewayTransit"] = peering.AllowGatewayTransit.ToString(),
                        ["useRemoteGateways"] = peering.UseRemoteGateways.ToString()
                    }));
            }
        }
    }

    private static void AddNsgNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var nsg in snapshot.Resources.NetworkSecurityGroups.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var nsgNodeId = AddResourceNode(nodes, nsg.Id, "NetworkSecurityGroup", nsg.Name, "Microsoft.Network/networkSecurityGroups", nsg.ResourceGroup, nsg.Location);
            AttachToResourceGroup(nodes, edges, nsgNodeId, nsg.Id, nsg.ResourceGroup, resourceGroupNodeIds);
        }
    }

    private static void AddRouteTableNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var routeTable in snapshot.Resources.RouteTables.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var routeNodeId = AddResourceNode(nodes, routeTable.Id, "RouteTable", routeTable.Name, "Microsoft.Network/routeTables", routeTable.ResourceGroup, routeTable.Location);
            AttachToResourceGroup(nodes, edges, routeNodeId, routeTable.Id, routeTable.ResourceGroup, resourceGroupNodeIds);
        }
    }

    private static void AddNetworkInterfaceNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var nic in snapshot.Resources.NetworkInterfaces.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var nicNodeId = AddResourceNode(nodes, nic.Id, "NetworkInterface", nic.Name, "Microsoft.Network/networkInterfaces", nic.ResourceGroup, nic.Location);
            AttachToResourceGroup(nodes, edges, nicNodeId, nic.Id, nic.ResourceGroup, resourceGroupNodeIds);

            if (!string.IsNullOrWhiteSpace(nic.SubnetId) && TryGetNodeId(nodes, nic.SubnetId, out var subnetNodeId))
            {
                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("ConnectedTo", nicNodeId, subnetNodeId),
                    SourceNodeId: nicNodeId,
                    TargetNodeId: subnetNodeId,
                    RelationshipType: "ConnectedTo",
                    DisplayName: "NIC connected to subnet",
                    AzureResourceId: null,
                    Metadata: null));
            }

            if (!string.IsNullOrWhiteSpace(nic.NetworkSecurityGroupId) && TryGetNodeId(nodes, nic.NetworkSecurityGroupId, out var nsgNodeId))
            {
                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("ProtectedBy", nicNodeId, nsgNodeId),
                    SourceNodeId: nicNodeId,
                    TargetNodeId: nsgNodeId,
                    RelationshipType: "ProtectedBy",
                    DisplayName: "NIC protected by NSG",
                    AzureResourceId: nic.NetworkSecurityGroupId,
                    Metadata: null));
            }
        }
    }

    private static void AddPublicIpNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var publicIp in snapshot.Resources.PublicIpAddresses.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var pipNodeId = AddResourceNode(nodes, publicIp.Id, "PublicIp", publicIp.Name, "Microsoft.Network/publicIPAddresses", publicIp.ResourceGroup, publicIp.Location);
            AttachToResourceGroup(nodes, edges, pipNodeId, publicIp.Id, publicIp.ResourceGroup, resourceGroupNodeIds);

            if (!string.IsNullOrWhiteSpace(publicIp.AssociatedNetworkInterfaceId) && TryGetNodeId(nodes, publicIp.AssociatedNetworkInterfaceId, out var nicNodeId))
            {
                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("UsesPublicIp", nicNodeId, pipNodeId),
                    SourceNodeId: nicNodeId,
                    TargetNodeId: pipNodeId,
                    RelationshipType: "UsesPublicIp",
                    DisplayName: "NIC uses public IP",
                    AzureResourceId: publicIp.Id,
                    Metadata: null));
            }
        }

        foreach (var nic in snapshot.Resources.NetworkInterfaces.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(nic.PublicIpAddressId))
            {
                continue;
            }

            if (TryGetNodeId(nodes, nic.Id, out var nicNodeId) && TryGetNodeId(nodes, nic.PublicIpAddressId, out var publicIpNodeId))
            {
                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("UsesPublicIp", nicNodeId, publicIpNodeId),
                    SourceNodeId: nicNodeId,
                    TargetNodeId: publicIpNodeId,
                    RelationshipType: "UsesPublicIp",
                    DisplayName: "NIC uses public IP",
                    AzureResourceId: nic.PublicIpAddressId,
                    Metadata: null));
            }
        }
    }

    private static void AddPrivateEndpointNodes(
        NetworkSnapshot snapshot,
        IDictionary<string, NetworkNode> nodes,
        IReadOnlyDictionary<string, string> resourceGroupNodeIds,
        IDictionary<string, NetworkEdge> edges,
        IReadOnlySet<string> linkedTargets)
    {
        foreach (var privateEndpoint in snapshot.Resources.PrivateEndpoints.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var peNodeId = AddResourceNode(nodes, privateEndpoint.Id, "PrivateEndpoint", privateEndpoint.Name, "Microsoft.Network/privateEndpoints", privateEndpoint.ResourceGroup, privateEndpoint.Location);
            AttachToResourceGroup(nodes, edges, peNodeId, privateEndpoint.Id, privateEndpoint.ResourceGroup, resourceGroupNodeIds);

            if (!string.IsNullOrWhiteSpace(privateEndpoint.SubnetId) && TryGetNodeId(nodes, privateEndpoint.SubnetId, out var subnetNodeId))
            {
                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("ConnectedTo", peNodeId, subnetNodeId),
                    SourceNodeId: peNodeId,
                    TargetNodeId: subnetNodeId,
                    RelationshipType: "ConnectedTo",
                    DisplayName: "Private endpoint connected to subnet",
                    AzureResourceId: privateEndpoint.SubnetId,
                    Metadata: null));
            }

            foreach (var serviceId in privateEndpoint.PrivateLinkServiceIds.Where(id => !string.IsNullOrWhiteSpace(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (linkedTargets.Contains(LinkPairKey(privateEndpoint.Id, serviceId)))
                {
                    // A declared link names this target and AddLinkedResourceNodes draws it. Creating the generic
                    // placeholder here as well would leave two identities for one resource.
                    continue;
                }

                var serviceNodeId = EnsureExternalResourceNode(nodes, serviceId, "PrivateLinkService", "Private link target");

                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("PrivateEndpointFor", peNodeId, serviceNodeId, serviceId),
                    SourceNodeId: peNodeId,
                    TargetNodeId: serviceNodeId,
                    RelationshipType: "PrivateEndpointFor",
                    DisplayName: "Private endpoint targets resource",
                    AzureResourceId: serviceId,
                    Metadata: null));
            }
        }
    }

    /// <summary>
    /// Draws every declared link as a node and an edge.
    /// </summary>
    /// <remarks>
    /// A related node is not an owned resource: it keeps <c>ParentNodeId</c> null so the ownership axis keeps
    /// stating only what the collection established, and it carries <c>origin=linked</c> so both the projector and
    /// the browser can tell it apart from inventory without knowing this vocabulary.
    /// </remarks>
    private static void AddLinkedResourceNodes(
        NetworkSnapshot snapshot,
        IDictionary<string, NetworkNode> nodes,
        IDictionary<string, NetworkEdge> edges)
    {
        var links = snapshot.Resources.LinkedResources;
        if (links is null || links.Count == 0)
        {
            return;
        }

        foreach (var link in links
            .Where(link => !string.IsNullOrWhiteSpace(link.SourceResourceId) && !string.IsNullOrWhiteSpace(link.TargetResourceId))
            .OrderBy(link => link.SourceResourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(link => link.TargetResourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(link => link.LinkKind, StringComparer.Ordinal))
        {
            // A link whose source was not collected would describe a resource the graph does not draw, so it is
            // dropped rather than left as a dangling node.
            if (!TryGetNodeId(nodes, link.SourceResourceId, out var sourceNodeId))
            {
                continue;
            }

            var targetNodeId = EnsureLinkedResourceNode(nodes, link);

            AddEdge(edges, new NetworkEdge(
                // The edge id is keyed by the pair, not by the target resource: two endpoints may front the same
                // target, and an id keyed on the resource would collapse them into one.
                Id: NetworkGraphContract.CreateEdgeId(link.LinkKind, sourceNodeId, targetNodeId),
                SourceNodeId: sourceNodeId,
                TargetNodeId: targetNodeId,
                RelationshipType: link.LinkKind,
                DisplayName: DescribeLink(link.LinkKind),
                AzureResourceId: link.TargetResourceId,
                Metadata: LinkEdgeMetadata(link)));
        }
    }

    private static string EnsureLinkedResourceNode(IDictionary<string, NetworkNode> nodes, LinkedResourceInfo link)
    {
        var nodeId = NetworkGraphContract.CreateNodeIdFromResourceId(link.TargetResourceId);
        if (nodes.ContainsKey(nodeId))
        {
            return nodeId;
        }

        var parsed = ArmResourceId.ParseOrNull(link.TargetResourceId);
        var azureResourceType = string.IsNullOrWhiteSpace(link.AzureResourceType)
            ? parsed?.AzureResourceType
            : link.AzureResourceType;

        TryParseSubscriptionId(link.TargetResourceId, out var subscriptionId);

        AddNode(nodes, new NetworkNode(
            Id: nodeId,
            ResourceId: link.TargetResourceId,
            NodeType: LinkedNodeType(azureResourceType),
            DisplayName: LinkedDisplayName(link, parsed),
            AzureResourceType: azureResourceType,
            SubscriptionId: string.IsNullOrWhiteSpace(subscriptionId) ? parsed?.SubscriptionId : subscriptionId,
            ResourceGroup: string.IsNullOrWhiteSpace(link.ResourceGroup) ? parsed?.ResourceGroup : link.ResourceGroup,
            Region: string.IsNullOrWhiteSpace(link.Location) ? null : link.Location,
            ParentNodeId: null,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [LinkedOriginMetadataKey] = LinkedOriginValue,
                ["resolution"] = link.Resolution,
            }));

        return nodeId;
    }

    /// <summary>Pairs a link's source and target so the two passes agree on what is already covered.</summary>
    private static string LinkPairKey(string sourceResourceId, string targetResourceId) =>
        $"{sourceResourceId.Trim().ToLowerInvariant()}\u001f{targetResourceId.Trim().ToLowerInvariant()}";

    private static HashSet<string> CollectLinkedTargets(NetworkSnapshot snapshot)
    {
        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links = snapshot.Resources.LinkedResources;
        if (links is null)
        {
            return pairs;
        }

        foreach (var link in links)
        {
            if (!string.IsNullOrWhiteSpace(link.SourceResourceId) && !string.IsNullOrWhiteSpace(link.TargetResourceId))
            {
                pairs.Add(LinkPairKey(link.SourceResourceId, link.TargetResourceId));
            }
        }

        return pairs;
    }

    private static string LinkedDisplayName(LinkedResourceInfo link, ArmResourceId? parsed)
    {
        if (!string.IsNullOrWhiteSpace(link.DisplayName))
        {
            return link.DisplayName;
        }

        return parsed?.Name ?? link.TargetResourceId;
    }

    private static string DescribeLink(string linkKind) => linkKind switch
    {
        LinkedResourceKinds.HostedOn => "Network interface attached to its host",
        LinkedResourceKinds.PrivateEndpointFor => "Private endpoint targets resource",
        LinkedResourceKinds.AttachedTo => "Network interface attached to private endpoint",
        _ => linkKind,
    };

    private static IReadOnlyDictionary<string, string> LinkEdgeMetadata(LinkedResourceInfo link)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resolution"] = link.Resolution,
        };

        if (!string.IsNullOrWhiteSpace(link.SubResource))
        {
            metadata["subResource"] = link.SubResource;
        }

        return metadata;
    }

    /// <summary>
    /// Maps a target's Azure resource type to the small vocabulary the canvas styles. The raw type stays on the
    /// node, so a category this list does not know is still displayed honestly as <c>RelatedResource</c>.
    /// </summary>
    private static string LinkedNodeType(string? azureResourceType)
    {
        if (string.IsNullOrWhiteSpace(azureResourceType))
        {
            return "RelatedResource";
        }

        var normalized = azureResourceType.Trim().ToLowerInvariant();

        // A scale-set instance is a child of its scale set, so naming it a virtual machine would be wrong.
        if (normalized.EndsWith("/virtualmachines", StringComparison.Ordinal)
            && normalized.Contains("/virtualmachinescalesets/", StringComparison.Ordinal))
        {
            return "VirtualMachineScaleSetInstance";
        }

        return normalized switch
        {
            "microsoft.compute/virtualmachines" => "VirtualMachine",
            "microsoft.compute/virtualmachinescalesets" => "VirtualMachineScaleSet",
            "microsoft.sql/servers" => "SqlServer",
            "microsoft.sql/servers/databases" => "SqlDatabase",
            "microsoft.sql/managedinstances" => "SqlManagedInstance",
            "microsoft.storage/storageaccounts" => "StorageAccount",
            "microsoft.keyvault/vaults" => "KeyVault",
            "microsoft.documentdb/databaseaccounts" => "CosmosDbAccount",
            "microsoft.web/sites" => "AppService",
            "microsoft.web/serverfarms" => "AppServicePlan",
            "microsoft.containerregistry/registries" => "ContainerRegistry",
            "microsoft.servicebus/namespaces" => "ServiceBusNamespace",
            "microsoft.eventhub/namespaces" => "EventHubNamespace",
            "microsoft.dbforpostgresql/flexibleservers" => "PostgreSqlServer",
            "microsoft.dbformysql/flexibleservers" => "MySqlServer",
            _ => "RelatedResource",
        };
    }

    private static void AddPrivateDnsZoneNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var zone in snapshot.Resources.PrivateDnsZones.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var zoneNodeId = AddResourceNode(nodes, zone.Id, "PrivateDnsZone", zone.Name, "Microsoft.Network/privateDnsZones", zone.ResourceGroup, null);
            AttachToResourceGroup(nodes, edges, zoneNodeId, zone.Id, zone.ResourceGroup, resourceGroupNodeIds);
        }
    }

    private static void AddAzureFirewallNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var firewall in snapshot.Resources.AzureFirewalls.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var firewallNodeId = AddResourceNode(nodes, firewall.Id, "AzureFirewall", firewall.Name, "Microsoft.Network/azureFirewalls", firewall.ResourceGroup, firewall.Location);
            AttachToResourceGroup(nodes, edges, firewallNodeId, firewall.Id, firewall.ResourceGroup, resourceGroupNodeIds);

            foreach (var subnetId in firewall.IpConfigurationSubnetIds.Where(id => !string.IsNullOrWhiteSpace(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (TryGetNodeId(nodes, subnetId, out var subnetNodeId))
                {
                    AddEdge(edges, new NetworkEdge(
                        Id: NetworkGraphContract.CreateEdgeId("ConnectedTo", firewallNodeId, subnetNodeId),
                        SourceNodeId: firewallNodeId,
                        TargetNodeId: subnetNodeId,
                        RelationshipType: "ConnectedTo",
                        DisplayName: "Azure Firewall connected to subnet",
                        AzureResourceId: subnetId,
                        Metadata: null));
                }
            }
        }
    }

    private static void AddVirtualHubNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var virtualHub in snapshot.Resources.VirtualHubs.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var hubNodeId = AddResourceNode(nodes, virtualHub.Id, "VirtualHub", virtualHub.Name, "Microsoft.Network/virtualHubs", virtualHub.ResourceGroup, virtualHub.Location);
            AttachToResourceGroup(nodes, edges, hubNodeId, virtualHub.Id, virtualHub.ResourceGroup, resourceGroupNodeIds);

            if (!string.IsNullOrWhiteSpace(virtualHub.VirtualWanId) && TryGetNodeId(nodes, virtualHub.VirtualWanId, out var wanNodeId))
            {
                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("UsesGateway", hubNodeId, wanNodeId, virtualHub.VirtualWanId),
                    SourceNodeId: hubNodeId,
                    TargetNodeId: wanNodeId,
                    RelationshipType: "UsesGateway",
                    DisplayName: "Virtual hub uses virtual WAN",
                    AzureResourceId: virtualHub.VirtualWanId,
                    Metadata: null));
            }

            foreach (var connectedVnetId in virtualHub.ConnectedVirtualNetworkIds.Where(id => !string.IsNullOrWhiteSpace(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (TryGetNodeId(nodes, connectedVnetId, out var vnetNodeId))
                {
                    AddEdge(edges, new NetworkEdge(
                        Id: NetworkGraphContract.CreateEdgeId("ConnectedTo", hubNodeId, vnetNodeId, connectedVnetId),
                        SourceNodeId: hubNodeId,
                        TargetNodeId: vnetNodeId,
                        RelationshipType: "ConnectedTo",
                        DisplayName: "Virtual hub connected to VNet",
                        AzureResourceId: connectedVnetId,
                        Metadata: null));
                }
            }
        }
    }

    private static void AddVirtualWanNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var virtualWan in snapshot.Resources.VirtualWans.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var wanNodeId = AddResourceNode(nodes, virtualWan.Id, "VirtualWan", virtualWan.Name, "Microsoft.Network/virtualWans", virtualWan.ResourceGroup, virtualWan.Location);
            AttachToResourceGroup(nodes, edges, wanNodeId, virtualWan.Id, virtualWan.ResourceGroup, resourceGroupNodeIds);
        }
    }

    private static void AddExpressRouteCircuitNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var circuit in snapshot.Resources.ExpressRouteCircuits.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var circuitNodeId = AddResourceNode(nodes, circuit.Id, "ExpressRouteCircuit", circuit.Name, "Microsoft.Network/expressRouteCircuits", circuit.ResourceGroup, circuit.Location);
            AttachToResourceGroup(nodes, edges, circuitNodeId, circuit.Id, circuit.ResourceGroup, resourceGroupNodeIds);
        }
    }

    private static void AddVirtualNetworkGatewayNodes(NetworkSnapshot snapshot, IDictionary<string, NetworkNode> nodes, IReadOnlyDictionary<string, string> resourceGroupNodeIds, IDictionary<string, NetworkEdge> edges)
    {
        foreach (var gateway in snapshot.Resources.VirtualNetworkGateways.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var gatewayNodeId = AddResourceNode(nodes, gateway.Id, "VirtualNetworkGateway", gateway.Name, "Microsoft.Network/virtualNetworkGateways", gateway.ResourceGroup, gateway.Location);
            AttachToResourceGroup(nodes, edges, gatewayNodeId, gateway.Id, gateway.ResourceGroup, resourceGroupNodeIds);

            if (!string.IsNullOrWhiteSpace(gateway.VirtualNetworkId) && TryGetNodeId(nodes, gateway.VirtualNetworkId, out var vnetNodeId))
            {
                AddEdge(edges, new NetworkEdge(
                    Id: NetworkGraphContract.CreateEdgeId("UsesGateway", vnetNodeId, gatewayNodeId, gateway.Id),
                    SourceNodeId: vnetNodeId,
                    TargetNodeId: gatewayNodeId,
                    RelationshipType: "UsesGateway",
                    DisplayName: "VNet uses virtual network gateway",
                    AzureResourceId: gateway.Id,
                    Metadata: null));
            }
        }
    }

    private static IReadOnlyList<NetworkGroup> BuildGroups(IDictionary<string, NetworkNode> nodes)
    {
        // Group membership is derived from ParentNodeId so that groups cannot diverge from the
        // authoritative containment hierarchy carried on each node.
        var childrenByParent = nodes.Values
            .Where(node => !string.IsNullOrWhiteSpace(node.ParentNodeId))
            .GroupBy(node => node.ParentNodeId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var groups = new List<NetworkGroup>();
        foreach (var node in nodes.Values.Where(node => node.NodeType is "Scope" or "Subscription" or "ResourceGroup" or "VirtualNetwork").OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            groups.Add(new NetworkGroup(
                Id: node.Id,
                GroupType: node.NodeType,
                DisplayName: node.DisplayName,
                ParentGroupId: node.ParentNodeId,
                NodeIds: childrenByParent.GetValueOrDefault(node.Id) ?? [],
                Metadata: null));
        }

        return groups;
    }

    private static void AttachToResourceGroup(
        IDictionary<string, NetworkNode> nodes,
        IDictionary<string, NetworkEdge> edges,
        string resourceNodeId,
        string resourceId,
        string resourceGroup,
        IReadOnlyDictionary<string, string> resourceGroupNodeIds)
    {
        if (!TryParseSubscriptionId(resourceId, out var subscriptionId) || string.IsNullOrWhiteSpace(resourceGroup))
        {
            return;
        }

        var key = $"{subscriptionId}/{resourceGroup}";
        if (!resourceGroupNodeIds.TryGetValue(key, out var resourceGroupNodeId))
        {
            return;
        }

        if (!nodes.ContainsKey(resourceGroupNodeId))
        {
            return;
        }

        AddEdge(edges, new NetworkEdge(
            Id: NetworkGraphContract.CreateEdgeId("Contains", resourceGroupNodeId, resourceNodeId),
            SourceNodeId: resourceGroupNodeId,
            TargetNodeId: resourceNodeId,
            RelationshipType: "Contains",
            DisplayName: "Resource group contains resource",
            AzureResourceId: null,
            Metadata: null));

        SetParentNode(nodes, resourceNodeId, resourceGroupNodeId);
    }

    private static void SetParentNode(IDictionary<string, NetworkNode> nodes, string nodeId, string parentNodeId)
    {
        if (nodes.TryGetValue(nodeId, out var node) && !string.Equals(node.ParentNodeId, parentNodeId, StringComparison.OrdinalIgnoreCase))
        {
            nodes[nodeId] = node with { ParentNodeId = parentNodeId };
        }
    }

    private static string AddResourceNode(
        IDictionary<string, NetworkNode> nodes,
        string resourceId,
        string nodeType,
        string displayName,
        string azureResourceType,
        string? resourceGroup,
        string? region,
        string? parentNodeId = null)
    {
        var nodeId = NetworkGraphContract.CreateNodeIdFromResourceId(resourceId);
        TryParseSubscriptionId(resourceId, out var subscriptionId);

        AddNode(nodes, new NetworkNode(
            Id: nodeId,
            ResourceId: resourceId,
            NodeType: nodeType,
            DisplayName: displayName,
            AzureResourceType: azureResourceType,
            SubscriptionId: subscriptionId,
            ResourceGroup: resourceGroup,
            Region: region,
            ParentNodeId: parentNodeId,
            Metadata: null));

        return nodeId;
    }

    private static string EnsureExternalResourceNode(IDictionary<string, NetworkNode> nodes, string resourceId, string nodeType, string displayName)
    {
        var nodeId = NetworkGraphContract.CreateNodeIdFromResourceId(resourceId);
        if (!nodes.ContainsKey(nodeId))
        {
            TryParseSubscriptionId(resourceId, out var subscriptionId);
            TryParseResourceGroup(resourceId, out var resourceGroup);

            AddNode(nodes, new NetworkNode(
                Id: nodeId,
                ResourceId: resourceId,
                NodeType: nodeType,
                DisplayName: displayName,
                AzureResourceType: null,
                SubscriptionId: subscriptionId,
                ResourceGroup: resourceGroup,
                Region: null,
                ParentNodeId: null,
                Metadata: new Dictionary<string, string> { ["origin"] = "inferred" }));
        }

        return nodeId;
    }

    private static bool TryGetNodeId(IDictionary<string, NetworkNode> nodes, string resourceId, out string nodeId)
    {
        nodeId = NetworkGraphContract.CreateNodeIdFromResourceId(resourceId);
        return nodes.ContainsKey(nodeId);
    }

    private static IEnumerable<(string Id, string? SubscriptionId, string? ResourceGroup)> EnumerateResourceDescriptors(NetworkSnapshot snapshot)
    {
        foreach (var vnet in snapshot.Resources.VNets)
        {
            yield return (vnet.Id, GetSubscriptionId(vnet.Id), vnet.ResourceGroup);
            foreach (var subnet in vnet.Subnets)
            {
                yield return (subnet.Id, GetSubscriptionId(subnet.Id), vnet.ResourceGroup);
            }
        }

        foreach (var nsg in snapshot.Resources.NetworkSecurityGroups)
        {
            yield return (nsg.Id, GetSubscriptionId(nsg.Id), nsg.ResourceGroup);
        }

        foreach (var routeTable in snapshot.Resources.RouteTables)
        {
            yield return (routeTable.Id, GetSubscriptionId(routeTable.Id), routeTable.ResourceGroup);
        }

        foreach (var nic in snapshot.Resources.NetworkInterfaces)
        {
            yield return (nic.Id, GetSubscriptionId(nic.Id), nic.ResourceGroup);
        }

        foreach (var publicIp in snapshot.Resources.PublicIpAddresses)
        {
            yield return (publicIp.Id, GetSubscriptionId(publicIp.Id), publicIp.ResourceGroup);
        }

        foreach (var privateEndpoint in snapshot.Resources.PrivateEndpoints)
        {
            yield return (privateEndpoint.Id, GetSubscriptionId(privateEndpoint.Id), privateEndpoint.ResourceGroup);
        }

        foreach (var privateDnsZone in snapshot.Resources.PrivateDnsZones)
        {
            yield return (privateDnsZone.Id, GetSubscriptionId(privateDnsZone.Id), privateDnsZone.ResourceGroup);
        }

        foreach (var firewall in snapshot.Resources.AzureFirewalls)
        {
            yield return (firewall.Id, GetSubscriptionId(firewall.Id), firewall.ResourceGroup);
        }

        foreach (var virtualHub in snapshot.Resources.VirtualHubs)
        {
            yield return (virtualHub.Id, GetSubscriptionId(virtualHub.Id), virtualHub.ResourceGroup);
        }

        foreach (var virtualWan in snapshot.Resources.VirtualWans)
        {
            yield return (virtualWan.Id, GetSubscriptionId(virtualWan.Id), virtualWan.ResourceGroup);
        }

        foreach (var expressRoute in snapshot.Resources.ExpressRouteCircuits)
        {
            yield return (expressRoute.Id, GetSubscriptionId(expressRoute.Id), expressRoute.ResourceGroup);
        }

        foreach (var gateway in snapshot.Resources.VirtualNetworkGateways)
        {
            yield return (gateway.Id, GetSubscriptionId(gateway.Id), gateway.ResourceGroup);
        }
    }

    private static string? GetSubscriptionId(string resourceId)
        => TryParseSubscriptionId(resourceId, out var subscriptionId) ? subscriptionId : null;

    private static bool TryParseSubscriptionId(string resourceId, out string subscriptionId)
    {
        subscriptionId = string.Empty;
        var segments = resourceId.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                subscriptionId = segments[i + 1];
                return !string.IsNullOrWhiteSpace(subscriptionId);
            }
        }

        return false;
    }

    private static bool TryParseResourceGroup(string resourceId, out string resourceGroup)
    {
        resourceGroup = string.Empty;
        var segments = resourceId.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
            {
                resourceGroup = segments[i + 1];
                return !string.IsNullOrWhiteSpace(resourceGroup);
            }
        }

        return false;
    }

    private static void AddNode(IDictionary<string, NetworkNode> nodes, NetworkNode node)
    {
        if (!nodes.TryGetValue(node.Id, out var existing))
        {
            nodes[node.Id] = node;
            return;
        }

        var mergedNode = existing with
        {
            ResourceId = existing.ResourceId ?? node.ResourceId,
            ParentNodeId = existing.ParentNodeId ?? node.ParentNodeId,
            Region = existing.Region ?? node.Region,
            ResourceGroup = existing.ResourceGroup ?? node.ResourceGroup,
            SubscriptionId = existing.SubscriptionId ?? node.SubscriptionId,
            AzureResourceType = existing.AzureResourceType ?? node.AzureResourceType,
            Metadata = existing.Metadata ?? node.Metadata
        };

        nodes[node.Id] = mergedNode;
    }

    private static void AddEdge(IDictionary<string, NetworkEdge> edges, NetworkEdge edge)
    {
        if (!edges.ContainsKey(edge.Id))
        {
            edges[edge.Id] = edge;
        }
    }
}
