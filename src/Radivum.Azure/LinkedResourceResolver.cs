using System.Text.Json;
using Radivum.Model;

namespace Radivum.Azure;

/// <summary>
/// Confirms the targets of the snapshot's declared links with one batched Resource Graph read, so a target can be
/// named from its own record instead of only from its id.
/// </summary>
/// <remarks>
/// Resolution is enrichment, never evidence about the network. Every link exists before this runs, and every
/// failure leaves it in place with a degraded resolution state, so a run with no access to the targets still
/// produces a complete topology with honest labels.
/// <para>
/// A target outside the collected subscriptions is not queried at all: the Resource Graph client is scoped to the
/// subscriptions the operator chose, and widening that set would read scopes they did not select. Such a target
/// therefore degrades to <see cref="LinkedResourceInfo.ResolutionUnresolved"/> and the evidence says why.
/// </para>
/// </remarks>
internal static class LinkedResourceResolver
{
    /// <summary>Targets per Resource Graph query. Kept small enough that the query text stays modest.</summary>
    internal const int MaxTargetsPerBatch = 100;

    /// <summary>Batches one collection may spend. Beyond it, links keep the name derived from their id.</summary>
    internal const int MaxBatches = 10;

    /// <summary>Item type of the collection-evidence row this resolver writes.</summary>
    internal const string EvidenceItemType = "LinkedResources";

    private const int MaxRecordedErrors = 10;

    /// <summary>
    /// Resolves every link target it is allowed to read, and returns the snapshot that carries the result.
    /// </summary>
    /// <param name="snapshot">Snapshot whose links are to be resolved. It is never mutated.</param>
    /// <param name="resourceGraphClient">Resource Graph client scoped to the collected subscription.</param>
    /// <param name="accessToken">Bearer token for the ARM audience.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The same snapshot when it has no links, otherwise a snapshot whose links name their resolution state and
    /// whose collection metadata carries one <c>LinkedResources</c> row.
    /// </returns>
    public static async Task<NetworkSnapshot> ResolveAsync(
        NetworkSnapshot snapshot,
        IResourceGraphClient resourceGraphClient,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(resourceGraphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var links = snapshot.Resources.LinkedResources;
        if (links is null || links.Count == 0)
        {
            return snapshot;
        }

        var subscriptionIds = snapshot.SubscriptionIds;
        var collectedSubscriptions = new HashSet<string>(subscriptionIds, StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();

        // A target is decided once, however many links name it: one query per distinct id, one outcome per id.
        var unreadable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        foreach (var link in links)
        {
            if (unreadable.ContainsKey(link.TargetResourceId) || Contains(candidates, link.TargetResourceId))
            {
                continue;
            }

            if (collectedSubscriptions.Count == 0)
            {
                unreadable[link.TargetResourceId] =
                    $"'{link.TargetResourceId}' cannot be looked up because the snapshot names no subscription";
                continue;
            }

            var parsed = ArmResourceId.ParseOrNull(link.TargetResourceId);

            if (parsed is null)
            {
                unreadable[link.TargetResourceId] = $"'{link.TargetResourceId}' is not an Azure resource id";
                continue;
            }

            if (!collectedSubscriptions.Contains(parsed.SubscriptionId))
            {
                unreadable[link.TargetResourceId] =
                    $"'{link.TargetResourceId}' is outside the collected subscriptions ({string.Join(", ", subscriptionIds)})";
                continue;
            }

            candidates.Add(link.TargetResourceId);
        }

        // Deterministic order before chunking, so the same snapshot always produces the same queries.
        candidates.Sort(StringComparer.Ordinal);

        var attemptable = candidates.Take(MaxBatches * MaxTargetsPerBatch).ToList();
        var notAttempted = candidates.Count - attemptable.Count;

        if (unreadable.Count > 0)
        {
            notes.Add($"{unreadable.Count} target(s) were not looked up: {string.Join("; ", unreadable.Values.Take(MaxRecordedErrors))}");
        }

        if (notAttempted > 0)
        {
            notes.Add(
                $"{notAttempted} target(s) were not looked up because the batch limit ({MaxBatches} x {MaxTargetsPerBatch}) was reached");
        }

        var readable = new Dictionary<string, ResolvedTarget>(StringComparer.OrdinalIgnoreCase);
        var failedBatches = 0;
        var attemptedBatches = 0;

        foreach (var batch in Batch(attemptable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            attemptedBatches++;

            try
            {
                var result = await resourceGraphClient
                    .QueryAsync(accessToken, BuildQuery(batch), subscriptionIds, skipToken: null, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var row in result.Rows)
                {
                    var id = ReadString(row, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    readable[id] = new ResolvedTarget(
                        Name: ReadString(row, "name"),
                        Type: ReadString(row, "type"),
                        Location: ReadString(row, "location"),
                        ResourceGroup: ReadString(row, "resourceGroup"));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedBatches++;
                if (notes.Count < MaxRecordedErrors)
                {
                    notes.Add($"a resolution query failed: {exception.Message}");
                }
            }
        }

        var attempted = new HashSet<string>(attemptable, StringComparer.OrdinalIgnoreCase);
        var updated = new List<LinkedResourceInfo>(links.Count);
        var resolvedCount = 0;
        var unresolvedCount = 0;

        // Every link states the same evidence, so the row is built once and shared.
        var provisional = new CollectionMetadata(
            ItemType: EvidenceItemType,
            Source: "AzureResourceGraph",
            Timestamp: snapshot.CollectedAt,
            Permission: "Reader",
            Status: "Partial",
            Completeness: 0d,
            Errors: []);

        foreach (var link in links)
        {
            if (unreadable.ContainsKey(link.TargetResourceId))
            {
                updated.Add(link with
                {
                    Resolution = LinkedResourceInfo.ResolutionUnresolved,
                    Metadata = provisional,
                });
                unresolvedCount++;
                continue;
            }

            if (!attempted.Contains(link.TargetResourceId))
            {
                // The batch ceiling was reached: nothing was learned, so the id-derived name stands.
                updated.Add(link with { Metadata = provisional });
                continue;
            }

            if (!readable.TryGetValue(link.TargetResourceId, out var target))
            {
                updated.Add(link with
                {
                    Resolution = LinkedResourceInfo.ResolutionUnresolved,
                    Metadata = provisional,
                });
                unresolvedCount++;
                continue;
            }

            updated.Add(link with
            {
                Resolution = LinkedResourceInfo.ResolutionResolved,
                DisplayName = string.IsNullOrWhiteSpace(target.Name) ? link.DisplayName : target.Name,
                AzureResourceType = string.IsNullOrWhiteSpace(target.Type) ? link.AzureResourceType : target.Type,
                ResourceGroup = string.IsNullOrWhiteSpace(target.ResourceGroup) ? link.ResourceGroup : target.ResourceGroup,
                Location = string.IsNullOrWhiteSpace(target.Location) ? link.Location : target.Location,
                Metadata = provisional,
            });
            resolvedCount++;
        }

        var status = unresolvedCount == 0 && notAttempted == 0
            ? "Available"
            : attemptedBatches > 0 && failedBatches == attemptedBatches && resolvedCount == 0
                ? "Unavailable"
                : "Partial";

        var evidence = provisional with
        {
            Status = status,
            Completeness = links.Count == 0 ? 1d : (double)resolvedCount / links.Count,
            Errors = notes.Take(MaxRecordedErrors).ToList(),
        };

        return snapshot with
        {
            Resources = snapshot.Resources with
            {
                LinkedResources = updated
                    .Select(link => link with { Metadata = evidence })
                    .ToList(),
            },
            CollectionMetadata = WithEvidence(snapshot.CollectionMetadata, evidence),
        };
    }

    private static string BuildQuery(IReadOnlyList<string> targetResourceIds)
    {
        var quoted = string.Join(
            ", ",
            targetResourceIds.Select(id => $"'{id.Replace("'", "''", StringComparison.Ordinal)}'"));

        return $"resources | where id in~ ({quoted}) | project id, name, type, location, resourceGroup";
    }

    private static IEnumerable<IReadOnlyList<string>> Batch(IReadOnlyList<string> values)
    {
        for (var index = 0; index < values.Count; index += MaxTargetsPerBatch)
        {
            yield return values.Skip(index).Take(MaxTargetsPerBatch).ToList();
        }
    }

    private static IReadOnlyList<CollectionMetadata> WithEvidence(
        IReadOnlyList<CollectionMetadata> metadata,
        CollectionMetadata evidence) =>
        metadata
            .Where(item => !string.Equals(item.ItemType, evidence.ItemType, StringComparison.OrdinalIgnoreCase))
            .Append(evidence)
            .ToList();

    /// <summary>Case-insensitive membership without materialising a set for a list this small.</summary>
    private static bool Contains(IReadOnlyList<string> values, string candidate) =>
        values.Any(value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

    private static string? ReadString(JsonElement row, string propertyName) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record ResolvedTarget(
        string? Name,
        string? Type,
        string? Location,
        string? ResourceGroup);
}
