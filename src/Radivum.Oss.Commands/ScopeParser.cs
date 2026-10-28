using System.Diagnostics.CodeAnalysis;
using Radivum.Model;

namespace Radivum.Oss.Commands;

/// <summary>
/// Parses the scope forms accepted by the collect command.
/// </summary>
public static class ScopeParser
{
    private const string SubscriptionPrefix = "/subscriptions/";

    /// <summary>
    /// Parses a subscription id, <c>/subscriptions/&lt;id&gt;</c>, or
    /// <c>/subscriptions/&lt;id&gt;/resourceGroups/&lt;rg&gt;</c> scope.
    /// </summary>
    /// <param name="scope">Scope text supplied by the operator.</param>
    /// <param name="subscriptionId">Parsed subscription id when the scope is valid.</param>
    /// <param name="resourceGroup">Parsed resource group, or <see langword="null"/> for a subscription scope.</param>
    /// <returns><see langword="true"/> when the scope is valid.</returns>
    public static bool TryParse(string scope, [NotNullWhen(true)] out string? subscriptionId, out string? resourceGroup)
    {
        ArgumentNullException.ThrowIfNull(scope);

        subscriptionId = null;
        resourceGroup = null;

        var normalizedScope = scope.Trim().TrimEnd('/');
        if (Guid.TryParse(normalizedScope, out _))
        {
            subscriptionId = normalizedScope;
            return true;
        }

        if (!normalizedScope.StartsWith(SubscriptionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = normalizedScope[SubscriptionPrefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Guid.TryParse(parts[0], out _))
        {
            return false;
        }

        if (parts.Length >= 3 && parts[1].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
        {
            subscriptionId = parts[0];
            resourceGroup = string.Join('/', parts.Skip(2));
            return true;
        }

        if (parts.Length == 1)
        {
            subscriptionId = parts[0];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses the scopes of one collection into the subscriptions it selects.
    /// </summary>
    /// <remarks>
    /// Every scope form <see cref="TryParse"/> accepts stays valid, repeated as needed. The result is normalized so
    /// that the same selection always produces the same request: duplicates collapse, ids are ordered ordinally, and
    /// a resource group is only allowed when exactly one subscription remains, because a snapshot describes one
    /// resource group scope.
    /// </remarks>
    /// <param name="scopes">Scope arguments, in the order they appear on the command line.</param>
    /// <param name="selection">Parsed selection when the scopes are valid.</param>
    /// <param name="error">Why the scopes are invalid, suitable for the console.</param>
    /// <returns><see langword="true"/> when the scopes select a collectable set.</returns>
    public static bool TryParseMany(
        IReadOnlyList<string> scopes,
        [NotNullWhen(true)] out ScopeSelection? selection,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        selection = null;

        if (scopes.Count == 0)
        {
            error = "Missing --scope.";
            return false;
        }

        var subscriptionIds = new List<string>();
        string? resourceGroup = null;

        foreach (var scope in scopes)
        {
            if (!TryParse(scope, out var subscriptionId, out var scopeResourceGroup))
            {
                error =
                    $"Invalid scope '{scope}'. Use a subscription id, /subscriptions/<id>, or /subscriptions/<id>/resourceGroups/<rg>.";
                return false;
            }

            if (scopeResourceGroup is not null)
            {
                if (resourceGroup is not null &&
                    !string.Equals(resourceGroup, scopeResourceGroup, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Scopes name different resource groups. A collection describes one resource group scope.";
                    return false;
                }

                resourceGroup = scopeResourceGroup;
            }

            if (!subscriptionIds.Contains(subscriptionId, StringComparer.OrdinalIgnoreCase))
            {
                subscriptionIds.Add(subscriptionId);
            }
        }

        if (subscriptionIds.Count > CollectionScopeLimits.MaxSubscriptions)
        {
            error =
                $"At most {CollectionScopeLimits.MaxSubscriptions} subscriptions can be collected together; " +
                $"{subscriptionIds.Count} were given.";
            return false;
        }

        if (resourceGroup is not null && subscriptionIds.Count > 1)
        {
            error = "A resource group scope requires exactly one subscription.";
            return false;
        }

        subscriptionIds.Sort(StringComparer.OrdinalIgnoreCase);
        selection = new ScopeSelection(subscriptionIds, resourceGroup);
        error = null;
        return true;
    }

    /// <summary>
    /// Composes the scope text a selection records.
    /// </summary>
    /// <remarks>
    /// A single scope keeps the text the operator supplied, so an existing analysis describes itself exactly as it
    /// did before. Several scopes become a comma-separated list of ARM scopes. The text is provenance for the report;
    /// the structured subscription ids are what the collector reads.
    /// </remarks>
    /// <param name="scopes">Scope arguments as supplied.</param>
    /// <param name="selection">Parsed selection.</param>
    /// <returns>Scope text to record on the analysis.</returns>
    public static string Describe(IReadOnlyList<string> scopes, ScopeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.SubscriptionIds.Count == 1 && scopes.Count == 1)
        {
            return scopes[0];
        }

        return string.Join(", ", selection.SubscriptionIds.Select(id => SubscriptionPrefix + id));
    }
}

/// <summary>
/// Subscriptions one collection selects, with the optional resource group that narrows them.
/// </summary>
/// <param name="SubscriptionIds">Selected subscriptions, ordered ordinally and free of duplicates.</param>
/// <param name="ResourceGroup">Resource group when the scope is narrower than its subscription.</param>
public sealed record ScopeSelection(IReadOnlyList<string> SubscriptionIds, string? ResourceGroup)
{
    /// <summary>Whether the selection names more than one subscription.</summary>
    public bool IsMultiSubscription => SubscriptionIds.Count > 1;
}