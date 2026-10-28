using System.Diagnostics.CodeAnalysis;

namespace Radivum.Model;

/// <summary>
/// A parsed Azure resource id in the classic
/// <c>/subscriptions/{subscription}/resourceGroups/{group}/providers/{namespace}/{type}/{name}[/{type}/{name}…]</c>
/// shape.
/// </summary>
/// <remarks>
/// The type trail is kept because a child resource's identity includes its parent:
/// <c>…/virtualMachineScaleSets/{set}/virtualMachines/{instance}</c> and
/// <c>…/servers/{server}/databases/{database}</c> are not their parent, and neither is a subnet its virtual
/// network. A consumer that needs the parent asks for it by name instead of splitting the id again, so the rule
/// lives in exactly one place.
/// <para>
/// Parsing is strict. An id without that shape is rejected rather than guessed at, because "this is not a
/// resource id" and "this is a resource id in a scope this parser does not model" are different answers, and
/// only the first is a reason for a caller to fall back to displaying the raw text.
/// </para>
/// </remarks>
public sealed record ArmResourceId
{
    private const string SubscriptionsSegment = "subscriptions";
    private const string ResourceGroupsSegment = "resourceGroups";
    private const string ProvidersSegment = "providers";

    /// <summary>Index of the first type segment, which follows <c>…/providers/{namespace}</c>.</summary>
    private const int FirstTypeIndex = 6;

    private ArmResourceId(
        string id,
        string subscriptionId,
        string resourceGroup,
        string providerNamespace,
        IReadOnlyList<string> typeTrail,
        IReadOnlyList<string> names)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        ResourceGroup = resourceGroup;
        ProviderNamespace = providerNamespace;
        TypeTrail = typeTrail;
        Names = names;
    }

    /// <summary>The id as parsed, trimmed of surrounding whitespace.</summary>
    public string Id { get; }

    /// <summary>Subscription the resource belongs to.</summary>
    public string SubscriptionId { get; }

    /// <summary>Resource group the resource belongs to.</summary>
    public string ResourceGroup { get; }

    /// <summary>Resource provider namespace, for example <c>Microsoft.Compute</c>.</summary>
    public string ProviderNamespace { get; }

    /// <summary>
    /// Type segments, outermost first: <c>["virtualMachineScaleSets", "virtualMachines"]</c> for a scale-set
    /// instance, <c>["virtualMachines"]</c> for a virtual machine.
    /// </summary>
    public IReadOnlyList<string> TypeTrail { get; }

    /// <summary>Names aligned one-to-one with <see cref="TypeTrail"/>; the last one is <see cref="Name"/>.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Name of the resource itself, with the casing the id carries.</summary>
    public string Name => Names[^1];

    /// <summary>Name of the enclosing resource, or <see langword="null"/> for a top-level resource.</summary>
    public string? ParentName => Names.Count > 1 ? Names[^2] : null;

    /// <summary>Azure resource type, for example <c>Microsoft.Network/virtualNetworks/subnets</c>.</summary>
    public string AzureResourceType => ProviderNamespace + "/" + string.Join('/', TypeTrail);

    /// <summary>
    /// Azure resource type of the enclosing resource, or <see langword="null"/> for a top-level resource.
    /// </summary>
    public string? ParentAzureResourceType => TypeTrail.Count > 1
        ? ProviderNamespace + "/" + string.Join('/', TypeTrail.Take(TypeTrail.Count - 1))
        : null;

    /// <summary>Parses an Azure resource id.</summary>
    /// <param name="resourceId">Candidate id; surrounding whitespace and a trailing slash are ignored.</param>
    /// <param name="parsed">The parsed id when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the id has the supported shape.</returns>
    public static bool TryParse([NotNullWhen(true)] string? resourceId, [NotNullWhen(true)] out ArmResourceId? parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return false;
        }

        var trimmed = resourceId.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return false;
        }

        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // subscriptions/{id}/resourceGroups/{group}/providers/{namespace}/{type}/{name}
        if (segments.Length < FirstTypeIndex + 2)
        {
            return false;
        }

        if (!IsSegment(segments[0], SubscriptionsSegment)
            || !IsSegment(segments[2], ResourceGroupsSegment)
            || !IsSegment(segments[4], ProvidersSegment))
        {
            return false;
        }

        // Everything after the provider namespace alternates type and name, so an odd count is a truncated id.
        var remainder = segments.Length - FirstTypeIndex;
        if (remainder % 2 != 0)
        {
            return false;
        }

        var typeTrail = new List<string>(remainder / 2);
        var names = new List<string>(remainder / 2);
        for (var index = FirstTypeIndex; index < segments.Length; index += 2)
        {
            typeTrail.Add(segments[index]);
            names.Add(segments[index + 1]);
        }

        parsed = new ArmResourceId(
            trimmed,
            segments[1],
            segments[3],
            segments[5],
            typeTrail,
            names);

        return true;
    }

    /// <summary>Parses an Azure resource id, or returns <see langword="null"/> when it has another shape.</summary>
    /// <param name="resourceId">Candidate id.</param>
    /// <returns>The parsed id, or <see langword="null"/>.</returns>
    public static ArmResourceId? ParseOrNull(string? resourceId) =>
        TryParse(resourceId, out var parsed) ? parsed : null;

    private static bool IsSegment(string segment, string expected) =>
        string.Equals(segment, expected, StringComparison.OrdinalIgnoreCase);
}
