namespace Radivum.Oss.Commands;

/// <summary>
/// A single command a host can expose on its command line.
/// </summary>
public interface ICommand
{
    /// <summary>Command name as typed on the command line, for example <c>collect</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="context">Services, output writers, arguments, and the cancellation token.</param>
    /// <returns>The process exit code.</returns>
    Task<int> ExecuteAsync(CommandContext context);
}