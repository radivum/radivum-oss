namespace Radivum.Oss.Commands;

/// <summary>
/// Regenerates the Markdown report and snapshot artifacts from a saved snapshot.
/// </summary>
public sealed class ReportCommand(ICollectionService collectionService) : ICommand
{
    /// <inheritdoc />
    public string Name => "report";

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var arguments = CliArguments.Parse(context.Arguments, "--snapshot", "--out");
        arguments.EnsureNoUnrecognizedArguments();

        var snapshotFile = arguments.GetValue("--snapshot");
        var outputDirectory = arguments.GetValue("--out", "output");

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

        var outcome = await collectionService
            .ReportAsync(snapshotFile, outputDirectory, context.CancellationToken)
            .ConfigureAwait(false);

        CollectionSummary.Write(context.Output, outputDirectory, outcome);
        return CliExitCodes.Success;
    }
}