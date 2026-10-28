namespace Radivum.Azure;

/// <summary>
/// Reads Azure account and scope discovery data used by the collector.
/// </summary>
public interface IDiscovery
{
    /// <summary>
    /// Gets an ARM access token for the configured credential.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bearer token string.</returns>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists subscriptions visible to the current credential, across every page ARM returns.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Subscription summaries, ordered by display name then id.</returns>
    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one subscription by its identifier, which is what names a collection scope beyond the id.
    /// </summary>
    /// <param name="subscriptionId">Subscription to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The subscription summary; its name falls back to the identifier when ARM declares no display name.
    /// </returns>
    Task<SubscriptionInfo> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the resource groups visible in one subscription for the current credential, across every page
    /// ARM returns.
    /// </summary>
    /// <param name="subscriptionId">Subscription to enumerate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resource group summaries, ordered by name.</returns>
    Task<IReadOnlyList<ResourceGroupInfo>> ListResourceGroupsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);
}
