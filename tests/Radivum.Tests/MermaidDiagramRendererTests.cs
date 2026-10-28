using Radivum.Graph;
using Radivum.Model;
using Radivum.Oss.Reporting;
using Xunit;

namespace Radivum.Tests;

public sealed class MermaidDiagramRendererTests
{
    [Fact]
    public void RenderMarkdown_EmitsMermaidFlowchartWithNodesAndEdges()
    {
        var graph = CreateGraph();
        var renderer = new MermaidDiagramRenderer();

        var markdown = renderer.RenderMarkdown(graph);

        Assert.Contains("```mermaid", markdown, StringComparison.Ordinal);
        Assert.Contains("flowchart TB", markdown, StringComparison.Ordinal);
        Assert.Contains("Contains", markdown, StringComparison.Ordinal);
        Assert.Contains("PeeredWith", markdown, StringComparison.Ordinal);
        Assert.Contains("vnet-a", markdown, StringComparison.Ordinal);
        Assert.Contains("vnet-b", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMarkdown_GivenSameGraph_ProducesDeterministicOutput()
    {
        var graph = CreateGraph();
        var renderer = new MermaidDiagramRenderer();

        var first = renderer.RenderMarkdown(graph);
        var second = renderer.RenderMarkdown(graph);

        Assert.Equal(first, second);
    }

    [Fact]
    public void RenderMarkdown_GivenTheBaselineSnapshot_MatchesTheGoldenDocument()
    {
        // The golden document is what a user reads in GitHub, VS Code or a wiki, so a rendering change is a
        // change to the published topology rather than to an internal detail.
        var graph = new NetworkGraphBuilder().Build(
            TestSnapshotFactory.Create(),
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));

        var actual = new MermaidDiagramRenderer().RenderMarkdown(graph);
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "baseline-mermaid.md"));

        Assert.Equal(NormalizeLineEndings(expected), NormalizeLineEndings(actual));
    }

    private static string NormalizeLineEndings(string text)
        => text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\t", "    ", StringComparison.Ordinal)
            .Trim();

    private static NetworkGraph CreateGraph()
    {
        var nodeAResource = "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-a";
        var nodeBResource = "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-b";

        var nodeA = new NetworkNode(
            Id: NetworkGraphContract.CreateNodeIdFromResourceId(nodeAResource),
            ResourceId: nodeAResource,
            NodeType: "VirtualNetwork",
            DisplayName: "vnet-a",
            AzureResourceType: "Microsoft.Network/virtualNetworks",
            SubscriptionId: "sub-1",
            ResourceGroup: "rg",
            Region: "eastus",
            ParentNodeId: null,
            Metadata: null);

        var nodeB = new NetworkNode(
            Id: NetworkGraphContract.CreateNodeIdFromResourceId(nodeBResource),
            ResourceId: nodeBResource,
            NodeType: "VirtualNetwork",
            DisplayName: "vnet-b",
            AzureResourceType: "Microsoft.Network/virtualNetworks",
            SubscriptionId: "sub-1",
            ResourceGroup: "rg",
            Region: "eastus",
            ParentNodeId: null,
            Metadata: null);

        var containsEdge = new NetworkEdge(
            Id: NetworkGraphContract.CreateEdgeId("Contains", nodeA.Id, nodeB.Id),
            SourceNodeId: nodeA.Id,
            TargetNodeId: nodeB.Id,
            RelationshipType: "Contains",
            DisplayName: "vnet-a contains vnet-b",
            AzureResourceId: null,
            Metadata: null);

        var peeringEdge = new NetworkEdge(
            Id: NetworkGraphContract.CreateEdgeId("PeeredWith", nodeA.Id, nodeB.Id, $"{nodeAResource}/virtualNetworkPeerings/a-to-b"),
            SourceNodeId: nodeA.Id,
            TargetNodeId: nodeB.Id,
            RelationshipType: "PeeredWith",
            DisplayName: "VNet peering a-to-b",
            AzureResourceId: $"{nodeAResource}/virtualNetworkPeerings/a-to-b",
            Metadata: null);

        return new NetworkGraph(
            GraphSchemaVersion: NetworkGraphContract.GraphSchemaVersionV1,
            SourceSnapshotSchemaVersion: NetworkSnapshotSchema.Version,
            SourceSnapshotToolVersion: "0.1.0",
            GeneratedAt: new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            ScopeDescription: "/subscriptions/sub-1",
            Nodes: [nodeA, nodeB],
            Edges: [containsEdge, peeringEdge],
            Groups: [],
            Metadata: null);
    }
}
