using System.Text.Json.Serialization;

namespace Radivum.Model;

/// <summary>
/// Scope and provenance of the snapshot an analysis bundle was produced from.
/// </summary>
/// <param name="ScopeDescription">Scope the snapshot was collected for, as the operator supplied it.</param>
/// <param name="SubscriptionId">Subscription the scope resolves to, or <see langword="null"/> for a multi-subscription scope.</param>
/// <param name="CollectedAt">Time the snapshot evidence was collected.</param>
/// <param name="SourceKind">Transport the analysis came from, one of <see cref="AnalysisSourceKind"/>.</param>
/// <param name="SubscriptionName">
/// Human-readable name of the scoped subscription, or <see langword="null"/> when the producer could not
/// resolve one. It is optional and additive, so a manifest that omits it reads exactly as before
/// (manifest schema 0.1 is unchanged) and a reader falls back to the identifier.
/// </param>
/// <param name="SubscriptionIds">
/// Every subscription the analysis covers, ordered ordinally. Omitted for a single-subscription analysis, which
/// keeps <paramref name="SubscriptionId"/> as its only identifier, so an existing manifest reads and hashes
/// exactly as before.
/// </param>
/// <param name="Subscriptions">
/// Identity and display name of each covered subscription. Omitted for a single-subscription analysis for the same
/// reason as <paramref name="SubscriptionIds"/>.
/// </param>
public sealed record AnalysisMetadata(
    string ScopeDescription,
    string? SubscriptionId,
    DateTimeOffset CollectedAt,
    string SourceKind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubscriptionName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? SubscriptionIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SubscriptionScopeEntry>? Subscriptions = null);
