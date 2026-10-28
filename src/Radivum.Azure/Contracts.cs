namespace Radivum.Azure;

/// <summary>
/// Subscription summary visible to the current credential.
/// </summary>
/// <param name="Id">Subscription identifier.</param>
/// <param name="Name">Display name, falling back to the identifier when ARM declares none.</param>
/// <param name="State">Subscription state as ARM reports it, for example <c>Enabled</c>.</param>
/// <param name="TenantId">
/// Tenant the subscription belongs to, or <see langword="null"/> when the read could not establish it. One
/// analysis covers one tenant, so this is what turns a cross-tenant scope into a named refusal instead of a series
/// of authorization errors.
/// </param>
public sealed record SubscriptionInfo(string Id, string Name, string State, string? TenantId = null);

/// <summary>
/// Resource group summary visible to the current credential within one subscription.
/// </summary>
/// <param name="Id">Fully qualified Azure resource id of the group.</param>
/// <param name="Name">Resource group name, which is what a collection scope carries.</param>
/// <param name="Location">Azure region the group metadata lives in.</param>
public sealed record ResourceGroupInfo(string Id, string Name, string Location);

/// <summary>
/// Snapshot collection scope request.
/// </summary>
/// <param name="ScopeDescription">Scope as the operator supplied it, echoed to the report.</param>
/// <param name="SubscriptionId">
/// First selected subscription. It stays populated for a single-subscription request, which is what keeps the
/// existing scope contract unchanged; <paramref name="SubscriptionIds"/> is what a reader uses when the scope may
/// name more than one.
/// </param>
/// <param name="ResourceGroup">Resource group when the scope is narrower than the subscription.</param>
/// <param name="ResolveLinkedResources">
/// Whether to look the targets of declared links up in Resource Graph. Off by default: the references are
/// always collected, but confirming a target's own name and type costs one extra query and reads other resource
/// types, so the operator opts in.
/// </param>
/// <param name="SubscriptionIds">
/// Every subscription the scope names, ordered ordinally, or <see langword="null"/> to collect only
/// <paramref name="SubscriptionId"/>. One analysis covers at most
/// <see cref="Radivum.Model.CollectionScopeLimits.MaxSubscriptions"/> subscriptions of one tenant.
/// </param>
public sealed record SnapshotCollectionRequest(
    string ScopeDescription,
    string SubscriptionId,
    string? ResourceGroup,
    bool ResolveLinkedResources = false,
    IReadOnlyList<string>? SubscriptionIds = null)
{
    /// <summary>The subscriptions to collect, ordered ordinally, with the single-subscription form as the fallback.</summary>
    public IReadOnlyList<string> SelectedSubscriptionIds =>
        SubscriptionIds is { Count: > 0 } ids ? ids : [SubscriptionId];

    /// <summary>Whether the request names more than one subscription.</summary>
    public bool IsMultiSubscription => SelectedSubscriptionIds.Count > 1;
}

/// <summary>
/// Configures which Azure.Identity credential is used.
/// </summary>
public sealed class AzureCredentialOptions
{
    /// <summary>
    /// When true, forces AzureCliCredential. Otherwise DefaultAzureCredential is used.
    /// </summary>
    public bool UseAzureCliCredential { get; set; }
}
