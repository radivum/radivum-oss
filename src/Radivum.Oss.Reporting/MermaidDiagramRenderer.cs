using System.Text;
using Radivum.Model;

namespace Radivum.Oss.Reporting;

/// <summary>
/// Renders a renderer-neutral <see cref="NetworkGraph"/> into Mermaid markdown.
/// </summary>
public sealed class MermaidDiagramRenderer : IDiagramRenderer
{
    /// <inheritdoc />
    public string RenderMarkdown(NetworkGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var orderedNodes = graph.Nodes
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var nodeAliases = orderedNodes
            .Select((node, index) => (node.Id, Alias: $"N{index + 1:D4}"))
            .ToDictionary(item => item.Id, item => item.Alias, StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("# Radivum — Mermaid Topology");
        sb.AppendLine();
        sb.AppendLine($"- **Graph schema:** {graph.GraphSchemaVersion}");
        sb.AppendLine($"- **Source snapshot schema:** {graph.SourceSnapshotSchemaVersion}");
        sb.AppendLine($"- **Scope:** {graph.ScopeDescription}");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart TB");

        foreach (var node in orderedNodes)
        {
            var alias = nodeAliases[node.Id];
            var label = EscapeMermaidLabel($"{node.DisplayName}\\n({node.NodeType})");
            sb.AppendLine($"    {alias}[\"{label}\"]");
        }

        foreach (var edge in graph.Edges.OrderBy(edge => edge.Id, StringComparer.Ordinal))
        {
            if (!nodeAliases.TryGetValue(edge.SourceNodeId, out var sourceAlias) ||
                !nodeAliases.TryGetValue(edge.TargetNodeId, out var targetAlias))
            {
                continue;
            }

            var relation = EscapeMermaidLabel(edge.RelationshipType);
            sb.AppendLine($"    {sourceAlias} -->|{relation}| {targetAlias}");
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    private static string EscapeMermaidLabel(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("\"", "'", StringComparison.Ordinal);
    }
}
