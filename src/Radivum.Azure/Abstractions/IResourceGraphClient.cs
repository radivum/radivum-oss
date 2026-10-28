using System.Text.Json;

namespace Radivum.Azure;

/// <summary>
/// Executes Azure Resource Graph queries and returns raw row payloads.
/// </summary>
internal interface IResourceGraphClient
{
    /// <summary>
    /// Queries Azure Resource Graph for a single page of resource rows.
    /// </summary>
    /// <param name="accessToken">Bearer access token for ARM audience.</param>
    /// <param name="query">Resource Graph query text.</param>
    /// <param name="subscriptionIds">
    /// Subscriptions the query is scoped to. Azure reads the ones it can and silently omits the rest, so the caller
    /// establishes per-subscription coverage separately rather than inferring it from this answer.
    /// </param>
    /// <param name="skipToken">Optional pagination token returned by a prior call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw rows and paging metadata.</returns>
    Task<ResourceGraphQueryResult> QueryAsync(
        string accessToken,
        string query,
        IReadOnlyList<string> subscriptionIds,
        string? skipToken,
        CancellationToken cancellationToken = default);
}

internal sealed record ResourceGraphQueryResult(
    IReadOnlyList<JsonElement> Rows,
    bool IsTruncated,
    string? SkipToken,
    int? TotalRecords);
