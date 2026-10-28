namespace Radivum.Model;

/// <summary>
/// Versioned, renderer-neutral graph derived from a <see cref="NetworkSnapshot"/>.
/// </summary>
public sealed record NetworkGraph(
    string GraphSchemaVersion,
    string SourceSnapshotSchemaVersion,
    string SourceSnapshotToolVersion,
    DateTimeOffset GeneratedAt,
    string ScopeDescription,
    IReadOnlyList<NetworkNode> Nodes,
    IReadOnlyList<NetworkEdge> Edges,
    IReadOnlyList<NetworkGroup> Groups,
    IReadOnlyDictionary<string, string>? Metadata);

/// <summary>
/// Graph node representing one architecture object.
/// </summary>
public sealed record NetworkNode(
    string Id,
    string? ResourceId,
    string NodeType,
    string DisplayName,
    string? AzureResourceType,
    string? SubscriptionId,
    string? ResourceGroup,
    string? Region,
    string? ParentNodeId,
    IReadOnlyDictionary<string, string>? Metadata);

/// <summary>
/// Semantic relationship between two graph nodes.
/// </summary>
public sealed record NetworkEdge(
    string Id,
    string SourceNodeId,
    string TargetNodeId,
    string RelationshipType,
    string DisplayName,
    string? AzureResourceId,
    IReadOnlyDictionary<string, string>? Metadata);

/// <summary>
/// Optional logical grouping used to provide a deterministic hierarchy for renderers.
/// </summary>
public sealed record NetworkGroup(
    string Id,
    string GroupType,
    string DisplayName,
    string? ParentGroupId,
    IReadOnlyList<string> NodeIds,
    IReadOnlyDictionary<string, string>? Metadata);

/// <summary>
/// Shared schema and identifier conventions for <see cref="NetworkGraph"/>.
/// </summary>
public static class NetworkGraphContract
{
    /// <summary>
    /// Current graph schema version for Version 1.
    /// </summary>
    public const string GraphSchemaVersionV1 = "1.0";

    /// <summary>
    /// Prefix used for deterministic synthetic node identifiers.
    /// </summary>
    public const string SyntheticNodePrefix = "synthetic:";

    /// <summary>
    /// Builds a canonical node identifier from an Azure resource ID.
    /// </summary>
    /// <param name="resourceId">Azure resource ID.</param>
    /// <returns>Canonical lower-case node identifier.</returns>
    public static string CreateNodeIdFromResourceId(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return resourceId.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Builds a deterministic synthetic node identifier.
    /// </summary>
    /// <param name="nodeType">Logical node type.</param>
    /// <param name="scopeKey">Deterministic scope key for the node.</param>
    /// <returns>Canonical synthetic node identifier.</returns>
    public static string CreateSyntheticNodeId(string nodeType, string scopeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);

        return $"{SyntheticNodePrefix}{Normalize(nodeType)}:{Normalize(scopeKey)}";
    }

    /// <summary>
    /// Builds a deterministic edge identifier.
    /// </summary>
    /// <param name="relationshipType">Semantic relationship type.</param>
    /// <param name="sourceNodeId">Source node identifier.</param>
    /// <param name="targetNodeId">Target node identifier.</param>
    /// <param name="azureResourceId">Optional Azure resource ID backing the relationship.</param>
    /// <returns>Canonical edge identifier.</returns>
    public static string CreateEdgeId(
        string relationshipType,
        string sourceNodeId,
        string targetNodeId,
        string? azureResourceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);

        if (!string.IsNullOrWhiteSpace(azureResourceId))
        {
            return $"edge:resource:{Normalize(azureResourceId)}";
        }

        return $"edge:{Normalize(relationshipType)}:{Normalize(sourceNodeId)}->{Normalize(targetNodeId)}";
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
