namespace Radivum.Oss.Commands;

/// <summary>
/// Everything a command needs that is not derived from its own arguments.
/// </summary>
public sealed class CommandContext
{
    /// <summary>
    /// Creates a command context.
    /// </summary>
    /// <param name="services">Service provider the host composed.</param>
    /// <param name="arguments">Arguments after the command name.</param>
    /// <param name="output">Writer for normal command output.</param>
    /// <param name="error">Writer for diagnostics and errors.</param>
    /// <param name="cancellationToken">Cancellation token signaled by Ctrl+C.</param>
    public CommandContext(
        IServiceProvider services,
        string[] arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        Services = services;
        Arguments = arguments;
        Output = output;
        Error = error;
        CancellationToken = cancellationToken;
    }

    /// <summary>Service provider the host composed.</summary>
    public IServiceProvider Services { get; }

    /// <summary>Arguments after the command name.</summary>
    public string[] Arguments { get; }

    /// <summary>Writer for normal command output.</summary>
    public TextWriter Output { get; }

    /// <summary>Writer for diagnostics and errors.</summary>
    public TextWriter Error { get; }

    /// <summary>Cancellation token signaled by Ctrl+C.</summary>
    public CancellationToken CancellationToken { get; }
}