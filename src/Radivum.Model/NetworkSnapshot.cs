using System.Text.Json.Serialization;

namespace Radivum.Model;

/// <summary>
/// Versioned, evidence-aware snapshot of an Azure network landscape.
/// This is the stable contract between collection, analysis, reporting, and future SaaS ingestion.
/// </summary>
public sealed record NetworkSnapshot(
    string SchemaVersion,
    string ToolVersion,
    string TenantId,
    IReadOnlyList<string> SubscriptionIds,
    AccountContextInfo AccountContext,
    DateTimeOffset CollectedAt,
    SnapshotScope Scope,
    NetworkResourceSet Resources,
    EffectiveEvidenceSet EffectiveEvidence,
    IReadOnlyList<RelationshipInfo> Relationships,
    IReadOnlyList<CollectionMetadata> CollectionMetadata);

public sealed record AccountContextInfo(
    string? AccountId,
    string? AccountName,
    string? EnvironmentName,
    string? CredentialSource);

/// <summary>
/// Scope a snapshot was collected for.
/// </summary>
/// <param name="ScopeDescription">Scope as the operator supplied it, echoed to the report.</param>
/// <param name="SubscriptionId">
/// The single scoped subscription, or <see langword="null"/> when the scope names several. A reader that needs
/// the whole selection uses <paramref name="SubscriptionIds"/>.
/// </param>
/// <param name="ResourceGroup">Resource group when the scope is narrower than a subscription.</param>
/// <param name="SubscriptionIds">Every subscription the scope names, ordered ordinally.</param>
/// <param name="SubscriptionName">
/// Display name of the single scoped subscription, or <see langword="null"/> when it could not be read or the scope
/// names several. Omitted from the artifact when null, so a single-subscription snapshot keeps the bytes it had.
/// </param>
/// <param name="Subscriptions">
/// Identity and display name of each scoped subscription. Written only for a multi-subscription scope: a
/// single-subscription snapshot keeps <paramref name="SubscriptionName"/> as its only name, so its bytes are
/// unchanged.
/// </param>
public sealed record SnapshotScope(
    string ScopeDescription,
    string? SubscriptionId,
    string? ResourceGroup,
    IReadOnlyList<string> SubscriptionIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubscriptionName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SubscriptionScopeEntry>? Subscriptions = null);

/// <summary>
/// One subscription of a collection scope, with the display name ARM answered for it.
/// </summary>
/// <param name="Id">Subscription identifier.</param>
/// <param name="Name">Display name, or <see langword="null"/> when ARM did not answer.</param>
public sealed record SubscriptionScopeEntry(
    string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);

/// <param name="ItemType">Evidence source this row describes, for example <c>VNets</c>.</param>
/// <param name="Source">Reader that produced the evidence.</param>
/// <param name="Timestamp">Time the evidence was read.</param>
/// <param name="Permission">Permission the read needs.</param>
/// <param name="Status">One of <c>Available</c>, <c>Partial</c>, or <c>Unavailable</c>.</param>
/// <param name="Completeness">Estimated share of the source that was read, in the range 0..1.</param>
/// <param name="Errors">Why the source is not fully available.</param>
/// <param name="ScopeId">
/// Scope this row describes when one row covers one scope, such as a subscription. Optional and additive: rows that
/// describe the whole analysis omit it, so existing snapshots keep their bytes and a reader never has to parse an
/// identifier out of error text.
/// </param>
public sealed record CollectionMetadata(
    string ItemType,
    string Source,
    DateTimeOffset Timestamp,
    string Permission,
    string Status,
    double Completeness,
    IReadOnlyList<string> Errors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ScopeId = null);

public sealed record RelationshipInfo(
    string RelationshipType,
    string SourceResourceId,
    string TargetResourceId,
    string? Description,
    CollectionMetadata Metadata);

public sealed record NetworkResourceSet(
    IReadOnlyList<VNetResource> VNets,
    IReadOnlyList<NetworkSecurityGroupResource> NetworkSecurityGroups,
    IReadOnlyList<RouteTableResource> RouteTables,
    IReadOnlyList<NetworkInterfaceResource> NetworkInterfaces,
    IReadOnlyList<PublicIpAddressResource> PublicIpAddresses,
    IReadOnlyList<PrivateEndpointResource> PrivateEndpoints,
    IReadOnlyList<PrivateDnsZoneResource> PrivateDnsZones,
    IReadOnlyList<AzureFirewallResource> AzureFirewalls,
    IReadOnlyList<VirtualHubResource> VirtualHubs,
    IReadOnlyList<VirtualWanResource> VirtualWans,
    IReadOnlyList<ExpressRouteCircuitResource> ExpressRouteCircuits,
    IReadOnlyList<VirtualNetworkGatewayResource> VirtualNetworkGateways,
    // Optional and additive: a snapshot whose producer parses no references omits the member entirely, so its
    // serialized contract stays byte-for-byte what 0.1 already produced (the same rule the scope's optional
    // subscription name follows). A null value therefore means "not collected", not "no links found".
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<LinkedResourceInfo>? LinkedResources = null);

/// <summary>
/// Declared evidence that a collected network element belongs to, hosts, or targets another resource.
/// </summary>
/// <remarks>
/// This is evidence, not presentation: the reference is read from the payload the source resource was collected
/// from, and the target's name and type are either looked up or derived from its id. The graph turns every link
/// into a node and an edge, and the network view treats the target as an external node. Nothing here places the
/// target in the network, because a related resource is not an owned one.
/// </remarks>
/// <param name="SourceResourceId">Collected network element the link starts from.</param>
/// <param name="TargetResourceId">Referenced resource, exactly as the source's payload stated it.</param>
/// <param name="LinkKind">What the reference means; one of <see cref="LinkedResourceKinds"/>.</param>
/// <param name="Resolution">
/// How the target's identity was established: <see cref="ResolutionResolved"/> when a lookup answered,
/// <see cref="ResolutionDerived"/> when no lookup was requested and name and type come from the id, and
/// <see cref="ResolutionUnresolved"/> when a lookup was requested and did not answer.
/// </param>
/// <param name="Metadata">Collection evidence for this link.</param>
/// <param name="SubResource">Child resource the source targets, such as a private endpoint's group id.</param>
/// <param name="DisplayName">Target name from the lookup, or derived from the id.</param>
/// <param name="AzureResourceType">Target type from the lookup, or derived from the id.</param>
/// <param name="ResourceGroup">Target resource group from the lookup, or derived from the id.</param>
/// <param name="Location">Target region, present only when a lookup answered.</param>
public sealed record LinkedResourceInfo(
    string SourceResourceId,
    string TargetResourceId,
    string LinkKind,
    string Resolution,
    CollectionMetadata Metadata,
    string? SubResource = null,
    string? DisplayName = null,
    string? AzureResourceType = null,
    string? ResourceGroup = null,
    string? Location = null)
{
    /// <summary>A lookup answered with the target's own name, type and resource group.</summary>
    public const string ResolutionResolved = "resolved";

    /// <summary>No lookup was requested; name and type are derived from the resource id.</summary>
    public const string ResolutionDerived = "derived";

    /// <summary>A lookup was requested and the target was not returned.</summary>
    public const string ResolutionUnresolved = "unresolved";
}

/// <summary>
/// Vocabulary for <see cref="LinkedResourceInfo.LinkKind"/>.
/// </summary>
/// <remarks>
/// The kind becomes the graph edge's relationship type, so these names are contract rather than internal
/// labels.
/// </remarks>
public static class LinkedResourceKinds
{
    /// <summary>A network interface is attached to a virtual machine, or to a scale-set instance.</summary>
    public const string HostedOn = "HostedOn";

    /// <summary>A private endpoint fronts the target resource, optionally a named sub-resource.</summary>
    public const string PrivateEndpointFor = "PrivateEndpointFor";

    /// <summary>
    /// A network interface is the interface a private endpoint created, so it belongs to that endpoint.
    /// </summary>
    /// <remarks>
    /// A private endpoint's interface is collected as an ordinary network interface, and it names its endpoint on
    /// its own payload. Without this link the two collected elements sit in the same subnet with no edge between
    /// them, which reads as a broken topology rather than as an unreferenced interface. The interface is the
    /// source because it is the element the reference is read from, and because a link whose source was not
    /// collected is dropped rather than drawn.
    /// </remarks>
    public const string AttachedTo = "AttachedTo";

    /// <summary>
    /// A collected virtual network declares a peering to a virtual network this collection did not read.
    /// </summary>
    /// <remarks>
    /// Evidence, never a finding, and never a reason to widen the query: the remote network is named from its id
    /// and not looked up. It is recorded because dropping it makes a topology look closed when it is not, which is
    /// the one thing a conclusion drawn over the collected networks must not imply (decision D3).
    /// </remarks>
    public const string PeeredWith = "PeeredWith";
}

public sealed record VNetResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    IReadOnlyList<string> AddressSpace,
    IReadOnlyList<SubnetResource> Subnets,
    IReadOnlyList<VNetPeeringResource> Peerings,
    CollectionMetadata Metadata);

public sealed record SubnetResource(
    string Id,
    string Name,
    string? AddressPrefix,
    string? NetworkSecurityGroupId,
    string? RouteTableId,
    CollectionMetadata Metadata);

public sealed record VNetPeeringResource(
    string Name,
    string? RemoteVirtualNetworkId,
    string? PeeringState,
    bool AllowGatewayTransit,
    bool UseRemoteGateways,
    bool AllowVirtualNetworkAccess,
    bool AllowForwardedTraffic,
    CollectionMetadata Metadata);

public sealed record NetworkSecurityGroupResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    IReadOnlyList<NetworkSecurityRuleResource> Rules,
    IReadOnlyList<string> SubnetIds,
    IReadOnlyList<string> NetworkInterfaceIds,
    CollectionMetadata Metadata);

public sealed record NetworkSecurityRuleResource(
    string Name,
    string Direction,
    string Access,
    int Priority,
    string Protocol,
    string Source,
    string Destination,
    string SourcePort,
    string DestinationPort,
    CollectionMetadata Metadata);

public sealed record RouteTableResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    bool DisableBgpRoutePropagation,
    IReadOnlyList<RouteResource> Routes,
    IReadOnlyList<string> SubnetIds,
    CollectionMetadata Metadata);

public sealed record RouteResource(
    string Name,
    string? AddressPrefix,
    string? NextHopType,
    string? NextHopIpAddress,
    CollectionMetadata Metadata);

public sealed record NetworkInterfaceResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    string? VirtualNetworkId,
    string? SubnetId,
    string? NetworkSecurityGroupId,
    string? PublicIpAddressId,
    string? PrivateIpAddress,
    CollectionMetadata Metadata);

public sealed record PublicIpAddressResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    string? IpAddress,
    string? AssociatedNetworkInterfaceId,
    CollectionMetadata Metadata);

public sealed record PrivateEndpointResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    string? SubnetId,
    IReadOnlyList<string> PrivateLinkServiceIds,
    CollectionMetadata Metadata);

public sealed record PrivateDnsZoneResource(
    string Id,
    string Name,
    string ResourceGroup,
    CollectionMetadata Metadata);

public sealed record AzureFirewallResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    string? FirewallPolicyId,
    IReadOnlyList<string> IpConfigurationSubnetIds,
    IReadOnlyList<string> ManagementSubnetIds,
    CollectionMetadata Metadata);

public sealed record VirtualHubResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    string? VirtualWanId,
    IReadOnlyList<string> ConnectedVirtualNetworkIds,
    CollectionMetadata Metadata);

public sealed record VirtualWanResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    CollectionMetadata Metadata);

public sealed record ExpressRouteCircuitResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    string? ServiceProviderName,
    int? BandwidthInMbps,
    string? PeeringLocation,
    CollectionMetadata Metadata);

public sealed record VirtualNetworkGatewayResource(
    string Id,
    string Name,
    string ResourceGroup,
    string Location,
    string? GatewayType,
    string? VirtualNetworkId,
    CollectionMetadata Metadata);

public sealed record EffectiveEvidenceSet(
    IReadOnlyList<EffectiveRouteTableEvidence> EffectiveRoutes,
    IReadOnlyList<EffectiveNetworkSecurityGroupEvidence> EffectiveNetworkSecurityGroups,
    IReadOnlyList<NextHopEvidence> NextHopResults);

public sealed record EffectiveRouteTableEvidence(
    string NetworkInterfaceId,
    string? NetworkInterfaceName,
    IReadOnlyList<EffectiveRouteEvidence> Routes,
    CollectionMetadata Metadata);

public sealed record EffectiveRouteEvidence(
    string? AddressPrefix,
    string? NextHopType,
    string? NextHopIpAddress,
    string? Source,
    string? State,
    CollectionMetadata Metadata);

public sealed record EffectiveNetworkSecurityGroupEvidence(
    string NetworkInterfaceId,
    string? NetworkInterfaceName,
    IReadOnlyList<EffectiveSecurityRuleEvidence> Rules,
    CollectionMetadata Metadata);

public sealed record EffectiveSecurityRuleEvidence(
    string Name,
    string Direction,
    string Access,
    int Priority,
    string Protocol,
    string Source,
    string Destination,
    string SourcePort,
    string DestinationPort,
    CollectionMetadata Metadata);

public sealed record NextHopEvidence(
    string SourceResourceId,
    string DestinationIpAddress,
    string? NextHopType,
    string? NextHopIpAddress,
    CollectionMetadata Metadata);
