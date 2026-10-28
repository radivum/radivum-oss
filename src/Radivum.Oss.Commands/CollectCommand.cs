namespace Radivum.Oss.Commands;

/// <summary>
/// Collects Azure network inventory for a scope and writes the snapshot and report artifacts.
/// </summary>
public sealed class CollectCommand(ICollectionService collectionService) : ICommand
{
    /// <inheritdoc />
    public string Name => "collect";

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var arguments = CliArguments.Parse(context.Arguments, "--scope", "--out", "--resolve-links");
        arguments.EnsureNoUnrecognizedArguments();

        var scopes = arguments.GetValues("--scope");
        var outputDirectory = arguments.GetValue("--out", "output");

        if (scopes.Count == 0)
        {
            context.Error.WriteLine("Missing --scope. Use --help.");
            return CliExitCodes.UsageError;
        }

        if (!TryReadResolveLinks(arguments.GetValue("--resolve-links"), out var resolveLinkedResources))
        {
            context.Error.WriteLine("Invalid --resolve-links value. Use 'true' or 'false'. Use --help.");
            return CliExitCodes.UsageError;
        }

        if (!ScopeParser.TryParseMany(scopes, out var selection, out var scopeError))
        {
            context.Error.WriteLine($"{scopeError} Use --help.");
            return CliExitCodes.UsageError;
        }

        var scopeDescription = ScopeParser.Describe(scopes, selection);
        context.Output.WriteLine($"Analyzing scope: {scopeDescription}");

        var outcome = await collectionService
            .CollectAsync(
                scopeDescription,
                selection.SubscriptionIds,
                selection.ResourceGroup,
                outputDirectory,
                resolveLinkedResources,
                context.CancellationToken)
            .ConfigureAwait(false);

        CollectionSummary.Write(context.Output, outputDirectory, outcome);
        return CliExitCodes.Success;
    }

    /// <summary>
    /// Reads the resolution switch. It takes a value rather than standing alone because every option this
    /// command surface accepts states one, and a bare flag would be parsed as an unknown argument.
    /// </summary>
    private static bool TryReadResolveLinks(string? value, out bool resolveLinkedResources)
    {
        resolveLinkedResources = false;

        return string.IsNullOrWhiteSpace(value) || bool.TryParse(value, out resolveLinkedResources);
    }
}