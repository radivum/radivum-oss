using Azure.Identity;
using Radivum.Azure;

namespace Radivum.Oss.Commands;

/// <summary>
/// Selects a command from the command line and maps failures to the shared exit codes.
/// </summary>
public sealed class CliCommandDispatcher
{
    private readonly IReadOnlyDictionary<string, ICommand> _commands;
    private readonly Action<TextWriter> _printUsage;

    /// <summary>
    /// Creates a dispatcher.
    /// </summary>
    /// <param name="commands">Commands the host exposes.</param>
    /// <param name="printUsage">Writes the host's usage text.</param>
    public CliCommandDispatcher(IEnumerable<ICommand> commands, Action<TextWriter> printUsage)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(printUsage);

        _commands = commands.ToDictionary(command => command.Name, StringComparer.Ordinal);
        _printUsage = printUsage;
    }

    /// <summary>
    /// Runs the command named by <paramref name="args"/>, or prints usage for an empty or help-only
    /// command line.
    /// </summary>
    /// <param name="args">Raw process arguments.</param>
    /// <param name="services">Service provider the host composed.</param>
    /// <param name="output">Writer for normal command output.</param>
    /// <param name="error">Writer for diagnostics and errors.</param>
    /// <param name="cancellationToken">Cancellation token signaled by Ctrl+C.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> DispatchAsync(
        string[] args,
        IServiceProvider services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            _printUsage(output);
            return CliExitCodes.Success;
        }

        try
        {
            if (!_commands.TryGetValue(args[0], out var command))
            {
                error.WriteLine($"Unknown command '{args[0]}'. Use --help.");
                return CliExitCodes.UsageError;
            }

            var context = new CommandContext(services, args[1..], output, error, cancellationToken);
            return await command.ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Operation canceled.");
            return CliExitCodes.Canceled;
        }
        catch (Exception exception) when (exception is AuthenticationFailedException or CredentialUnavailableException)
        {
            error.WriteLine("Authentication failed. Run 'az login' first (the agent uses your existing Azure CLI session).");
            error.WriteLine($"Details: {exception.Message}");
            return CliExitCodes.AuthenticationFailed;
        }
        catch (AzureRequestException exception) when (exception.IsAuthorization)
        {
            error.WriteLine("Access denied. Your account lacks the required permissions for this scope.");
            error.WriteLine("Read-only access (Reader) is expected for inventory collection at the requested scope.");
            error.WriteLine("If some providers are still denied, rerun collect and review partial-coverage metadata in the generated report.");
            error.WriteLine($"Details: {exception.Message}");
            return CliExitCodes.AzureRequestFailed;
        }
        catch (AzureRequestException exception)
        {
            error.WriteLine($"Azure request failed: {exception.Message}");
            return CliExitCodes.AzureRequestFailed;
        }
        catch (FileNotFoundException exception)
        {
            error.WriteLine(exception.Message);
            return CliExitCodes.UsageError;
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            return CliExitCodes.UsageError;
        }
        catch (AnalysisStageFailedException exception)
        {
            error.WriteLine($"Analysis failed at the '{exception.Stage}' stage.");
            error.WriteLine("The artifacts the earlier stages wrote were kept; the manifest describes what the run produced.");
            error.WriteLine($"Details: {exception.Message}");
            return CliExitCodes.AnalysisStageFailed;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Error: {exception.Message}");
            return CliExitCodes.UsageError;
        }
    }
}