using Radivum.Oss.Commands;
using Xunit;

namespace Radivum.Tests;

public sealed class CliArgumentsTests
{
    [Fact]
    public void GetValue_ReturnsOptionValue()
    {
        var arguments = CliArguments.Parse(["--out", "output"], "--out");

        Assert.Equal("output", arguments.GetValue("--out"));
    }

    [Fact]
    public void GetValue_ReturnsNullWhenOptionIsAbsent()
    {
        var arguments = CliArguments.Parse(["--scope", "sub-1"], "--scope", "--out");

        Assert.Null(arguments.GetValue("--out"));
    }

    [Fact]
    public void GetValue_ReturnsLastOccurrenceWhenOptionRepeats()
    {
        var arguments = CliArguments.Parse(["--out", "first", "--out", "second"], "--out");

        Assert.Equal("second", arguments.GetValue("--out"));
    }

    [Fact]
    public void GetValue_WithDefault_ReturnsDefaultWhenOptionIsAbsent()
    {
        var arguments = CliArguments.Parse([], "--out");

        Assert.Equal("output", arguments.GetValue("--out", "output"));
    }

    [Fact]
    public void GetValue_WithDefault_ReturnsOptionValueWhenPresent()
    {
        var arguments = CliArguments.Parse(["--out", "custom"], "--out");

        Assert.Equal("custom", arguments.GetValue("--out", "output"));
    }

    [Fact]
    public void GetValues_ReturnsEveryOccurrenceInCommandLineOrder()
    {
        var arguments = CliArguments.Parse(
            ["--scope", "first", "--out", "output", "--scope", "second"],
            "--scope",
            "--out");

        Assert.Equal(["first", "second"], arguments.GetValues("--scope"));
    }

    [Fact]
    public void GetValues_ReturnsEmptyWhenOptionIsAbsent()
    {
        var arguments = CliArguments.Parse(["--out", "output"], "--scope", "--out");

        Assert.Empty(arguments.GetValues("--scope"));
    }

    [Fact]
    public void GetValue_TreatsUndeclaredOptionNameAsAPositionalValue()
    {
        var arguments = CliArguments.Parse(["--out", "--scope", "sub-1"], "--out", "--scope");

        Assert.Equal("--scope", arguments.GetValue("--out"));
        Assert.Null(arguments.GetValue("--scope"));
    }

    [Fact]
    public void EnsureNoUnrecognizedArguments_AllowsDeclaredOptionPairs()
    {
        var arguments = CliArguments.Parse(["--out", "output", "--scope", "sub-1"], "--out", "--scope");

        arguments.EnsureNoUnrecognizedArguments();
    }

    [Fact]
    public void EnsureNoUnrecognizedArguments_ThrowsWhenArgumentIsUndeclared()
    {
        var arguments = CliArguments.Parse(["--out", "output", "extra"], "--out");

        var exception = Assert.Throws<ArgumentException>(arguments.EnsureNoUnrecognizedArguments);

        Assert.Equal("Unknown argument 'extra'. Use --help.", exception.Message);
    }

    [Fact]
    public void EnsureNoUnrecognizedArguments_ThrowsWhenDeclaredOptionHasNoValue()
    {
        var arguments = CliArguments.Parse(["--out"], "--out");

        var exception = Assert.Throws<ArgumentException>(arguments.EnsureNoUnrecognizedArguments);

        Assert.Equal("Unknown argument '--out'. Use --help.", exception.Message);
    }

    [Fact]
    public void Parse_ThrowsWhenArgumentsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => CliArguments.Parse(null!, "--out"));
    }

    [Fact]
    public void GetValue_ThrowsWhenNameIsBlank()
    {
        var arguments = CliArguments.Parse(["--out", "output"], "--out");

        Assert.Throws<ArgumentException>(() => arguments.GetValue(" "));
    }
}