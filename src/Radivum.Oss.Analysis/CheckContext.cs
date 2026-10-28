using Radivum.Model;

namespace Radivum.Oss.Analysis;

/// <summary>
/// Availability of the evidence sources a snapshot was collected from. Checks use it to decide
/// whether to run at all and how much confidence their findings may claim.
/// </summary>
public sealed class CheckContext
{
    private readonly IReadOnlyDictionary<string, string> statusByItemType;

    private CheckContext(IReadOnlyDictionary<string, string> statusByItemType)
        => this.statusByItemType = statusByItemType;

    /// <summary>
    /// Builds a context from a snapshot's collection metadata.
    /// </summary>
    /// <param name="metadata">Collection metadata entries.</param>
    /// <returns>Context describing which evidence sources were available.</returns>
    public static CheckContext Create(IReadOnlyList<CollectionMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var statusByItemType = metadata
            .GroupBy(static item => item.ItemType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().Status,
                StringComparer.OrdinalIgnoreCase);

        return new CheckContext(statusByItemType);
    }

    /// <summary>
    /// Determines whether an evidence source was collected as unavailable. Sources that were never
    /// reported are treated as unavailable.
    /// </summary>
    /// <param name="itemType">Collection metadata item type, for example <c>VNets</c>.</param>
    /// <returns><see langword="true"/> when the source is unavailable.</returns>
    public bool IsUnavailable(string itemType)
        => string.Equals(GetStatus(itemType), "Unavailable", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether an evidence source was fully collected. Sources that were never reported are
    /// treated as unavailable.
    /// </summary>
    /// <param name="itemType">Collection metadata item type, for example <c>VNets</c>.</param>
    /// <returns><see langword="true"/> when the source is available.</returns>
    public bool IsAvailable(string itemType)
        => string.Equals(GetStatus(itemType), "Available", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the confidence a finding may claim given the sources it relies on.
    /// </summary>
    /// <param name="itemTypes">Collection metadata item types the finding depends on.</param>
    /// <returns><c>High</c> when every source is available, otherwise <c>Medium</c>.</returns>
    public string ConfidenceFor(params string[] itemTypes)
        => itemTypes.Any(type => !string.Equals(GetStatus(type), "Available", StringComparison.OrdinalIgnoreCase))
            ? "Medium"
            : "High";

    /// <summary>
    /// Reads the recorded status of an evidence source.
    /// </summary>
    /// <param name="itemType">Collection metadata item type, for example <c>VNets</c>.</param>
    /// <returns>The recorded status, or <c>Unavailable</c> when the source was never reported.</returns>
    public string GetStatus(string itemType)
        => statusByItemType.TryGetValue(itemType, out var status)
            ? status
            : "Unavailable";
}
