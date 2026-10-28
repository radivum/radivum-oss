using Radivum.Graph;
using Radivum.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Radivum.Oss.Commands;

/// <summary>
/// Builds a deterministic network graph artifact from a saved snapshot.
/// </summary>
public sealed class GraphCommand : ICommand
{
    /// <inheritdoc />
    public string Name => "graph";

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var arguments = CliArguments.Parse(context.Arguments, "--snapshot", "--out");
        arguments.EnsureNoUnrecognizedArguments();

        var snapshotFile = arguments.GetValue("--snapshot");
        var outputPath = arguments.GetValue("--out", "output");

        if (string.IsNullOrWhiteSpace(snapshotFile))
        {
            context.Error.WriteLine("Missing --snapshot. Use --help.");
            return CliExitCodes.UsageError;
        }

        if (!File.Exists(snapshotFile))
        {
            context.Error.WriteLine($"Snapshot file not found: {snapshotFile}");
            return CliExitCodes.UsageError;
        }

        var serializer = context.Services.GetRequiredService<INetworkSnapshotSerializer>();

        await using var stream = File.OpenRead(snapshotFile);
        var snapshot = await serializer.DeserializeAsync(stream, context.CancellationToken).ConfigureAwait(false);

        var graph = context.Services.GetRequiredService<IGraphGenerationService>().Generate(snapshot);

        var graphFilePath = ResolveOutputPath(outputPath);
        await context.Services.GetRequiredService<IGraphArtifactWriter>()
            .WriteAsync(graph, graphFilePath, context.CancellationToken)
            .ConfigureAwait(false);

        context.Output.WriteLine($"Graph:    {Path.GetFullPath(graphFilePath)}");
        context.Output.WriteLine($"Nodes:    {graph.Nodes.Count}");
        context.Output.WriteLine($"Edges:    {graph.Edges.Count}");
        context.Output.WriteLine($"Groups:   {graph.Groups.Count}");

        return CliExitCodes.Success;
    }

    private static string ResolveOutputPath(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var trimmed = outputPath.Trim();
        if (trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return Path.Combine(trimmed, "network-graph.json");
    }
}