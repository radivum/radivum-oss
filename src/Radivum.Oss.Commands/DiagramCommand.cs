using Radivum.Graph;
using Radivum.Model;
using Radivum.Oss.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Radivum.Oss.Commands;

/// <summary>
/// Builds a deterministic Mermaid topology document from a saved snapshot.
/// </summary>
public sealed class DiagramCommand : ICommand
{
    /// <inheritdoc />
    public string Name => "diagram";

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var arguments = CliArguments.Parse(context.Arguments, "--snapshot", "--out", "--format");
        arguments.EnsureNoUnrecognizedArguments();

        var snapshotFile = arguments.GetValue("--snapshot");
        var outputPath = arguments.GetValue("--out", "output");
        var format = arguments.GetValue("--format", "mermaid");

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

        if (!string.Equals(format, "mermaid", StringComparison.OrdinalIgnoreCase))
        {
            context.Error.WriteLine($"Unsupported --format '{format}'. Use 'mermaid'.");
            return CliExitCodes.UsageError;
        }

        var serializer = context.Services.GetRequiredService<INetworkSnapshotSerializer>();

        await using var stream = File.OpenRead(snapshotFile);
        var snapshot = await serializer.DeserializeAsync(stream, context.CancellationToken).ConfigureAwait(false);

        var graph = context.Services.GetRequiredService<IGraphGenerationService>().Generate(snapshot);
        var markdown = context.Services.GetRequiredService<IDiagramRenderer>().RenderMarkdown(graph);

        var diagramFilePath = ResolveOutputPath(outputPath);
        await context.Services.GetRequiredService<IArtifactWriter>()
            .WriteTextAsync(diagramFilePath, markdown, context.CancellationToken)
            .ConfigureAwait(false);

        context.Output.WriteLine($"Diagram:  {Path.GetFullPath(diagramFilePath)}");
        context.Output.WriteLine($"Format:   mermaid");
        context.Output.WriteLine($"Nodes:    {graph.Nodes.Count}");
        context.Output.WriteLine($"Edges:    {graph.Edges.Count}");

        return CliExitCodes.Success;
    }

    private static string ResolveOutputPath(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var trimmed = outputPath.Trim();
        if (trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return Path.Combine(trimmed, "network-topology.md");
    }
}