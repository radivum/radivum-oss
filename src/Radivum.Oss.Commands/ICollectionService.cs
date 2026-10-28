namespace Radivum.Oss.Commands;

/// <summary>
/// Encapsulates the collect/report sequence so every host runs the same steps in the same order.
/// </summary>
public interface ICollectionService
{
    /// <summary>
    /// Collects a snapshot for a scope, evaluates OSS checks, and writes the snapshot and report.
    /// </summary>
    /// <param name="scope">Scope text as supplied by the operator, echoed to the report.</param>
    /// <param name="subscriptionIds">
    /// Subscriptions the scope selects, ordered ordinally. One analysis covers at most
    /// <c>CollectionScopeLimits.MaxSubscriptions</c> subscriptions of one tenant.
    /// </param>
    /// <param name="resourceGroup">
    /// Parsed resource group, or <see langword="null"/> for a subscription scope. Only valid with exactly one
    /// subscription.
    /// </param>
    /// <param name="outputDirectory">Directory that receives the snapshot and report artifacts.</param>
    /// <param name="resolveLinkedResources">
    /// Whether to confirm the targets of declared links with one batched Resource Graph read. Off by default.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The collected snapshot and its findings.</returns>
    Task<CollectionOutcome> CollectAsync(
        string scope,
        IReadOnlyList<string> subscriptionIds,
        string? resourceGroup,
        string outputDirectory,
        bool resolveLinkedResources = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Collects a snapshot for one subscription, evaluates OSS checks, and writes the snapshot and report.
    /// </summary>
    /// <param name="scope">Scope text as supplied by the operator, echoed to the report.</param>
    /// <param name="subscriptionId">Parsed subscription id.</param>
    /// <param name="resourceGroup">Parsed resource group, or <see langword="null"/> for a subscription scope.</param>
    /// <param name="outputDirectory">Directory that receives the snapshot and report artifacts.</param>
    /// <param name="resolveLinkedResources">
    /// Whether to confirm the targets of declared links with one batched Resource Graph read. Off by default.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The collected snapshot and its findings.</returns>
    Task<CollectionOutcome> CollectAsync(
        string scope,
        string subscriptionId,
        string? resourceGroup,
        string outputDirectory,
        bool resolveLinkedResources = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing snapshot, evaluates OSS checks, and rewrites the report.
    /// </summary>
    /// <param name="snapshotPath">Path to a saved network snapshot.</param>
    /// <param name="outputDirectory">Directory that receives the regenerated artifacts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded snapshot and its findings.</returns>
    Task<CollectionOutcome> ReportAsync(
        string snapshotPath,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}