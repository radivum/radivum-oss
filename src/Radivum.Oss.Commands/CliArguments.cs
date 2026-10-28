namespace Radivum.Oss.Commands;

/// <summary>
/// Parses a command's argument list into named options.
/// </summary>
/// <remarks>
/// An option is recognized only when its name is declared by the command <em>and</em> a value follows
/// it. A declared option in the last position is therefore reported as unknown, which is the behavior
/// both hosts had before the parser was shared.
/// </remarks>
public sealed class CliArguments
{
    private readonly IReadOnlyList<string> _arguments;
    private readonly HashSet<string> _optionNames;

    private CliArguments(IReadOnlyList<string> arguments, HashSet<string> optionNames)
    {
        _arguments = arguments;
        _optionNames = optionNames;
    }

    /// <summary>
    /// Parses <paramref name="arguments"/> and declares the option names the command accepts.
    /// </summary>
    /// <param name="arguments">Command arguments, without the command name.</param>
    /// <param name="optionNames">Declared option names, for example <c>--out</c>.</param>
    /// <returns>The parsed argument list.</returns>
    public static CliArguments Parse(IReadOnlyList<string> arguments, params string[] optionNames)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(optionNames);

        return new CliArguments(arguments, new HashSet<string>(optionNames, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets an option value, or <see langword="null"/> when the option is absent.
    /// The last occurrence wins when an option is repeated.
    /// </summary>
    /// <param name="name">Declared option name, for example <c>--out</c>.</param>
    /// <returns>The option value, or <see langword="null"/>.</returns>
    public string? GetValue(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string? value = null;
        for (var i = 0; i + 1 < _arguments.Count; i++)
        {
            if (!_optionNames.Contains(_arguments[i]))
            {
                continue;
            }

            if (string.Equals(_arguments[i], name, StringComparison.Ordinal))
            {
                value = _arguments[i + 1];
            }

            i++;
        }

        return value;
    }

    /// <summary>
    /// Gets every value of an option, in the order the values appear, or an empty list when the option is absent.
    /// </summary>
    /// <remarks>
    /// Repeatable options such as <c>--scope</c> need every occurrence; <see cref="GetValue"/> keeps last-wins
    /// semantics for the options that take one value, so the two behaviours stay explicit rather than inferred.
    /// </remarks>
    /// <param name="name">Declared option name, for example <c>--scope</c>.</param>
    /// <returns>The option values, in command-line order.</returns>
    public IReadOnlyList<string> GetValues(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var values = new List<string>();
        for (var i = 0; i + 1 < _arguments.Count; i++)
        {
            if (!_optionNames.Contains(_arguments[i]))
            {
                continue;
            }

            if (string.Equals(_arguments[i], name, StringComparison.Ordinal))
            {
                values.Add(_arguments[i + 1]);
            }

            i++;
        }

        return values;
    }

    /// <summary>
    /// Gets an option value, falling back to <paramref name="defaultValue"/> when the option is absent.
    /// </summary>
    /// <param name="name">Declared option name, for example <c>--out</c>.</param>
    /// <param name="defaultValue">Value to use when the option is absent.</param>
    /// <returns>The option value, or <paramref name="defaultValue"/>.</returns>
    public string GetValue(string name, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);

        return GetValue(name) ?? defaultValue;
    }

    /// <summary>
    /// Throws when the command line contains an argument the command does not recognize.
    /// </summary>
    /// <exception cref="ArgumentException">An argument is not a declared option, or has no value.</exception>
    public void EnsureNoUnrecognizedArguments()
    {
        var unrecognized = FirstUnrecognizedArgument();
        if (unrecognized is not null)
        {
            throw new ArgumentException($"Unknown argument '{unrecognized}'. Use --help.");
        }
    }

    private string? FirstUnrecognizedArgument()
    {
        for (var i = 0; i < _arguments.Count; i++)
        {
            if (!_optionNames.Contains(_arguments[i]) || i + 1 >= _arguments.Count)
            {
                return _arguments[i];
            }

            i++;
        }

        return null;
    }
}