using Radivum.Oss.Analysis;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

public sealed class CheckEngineTests
{
    [Fact]
    public void Check_GivenSnapshot_ReturnsTheDeterministicCheckSet()
    {
        var snapshot = TestSnapshotFactory.Create();
        var engine = new CheckEngine();

        var findings = engine.Check(snapshot);

        Assert.Equal(Describe(Checks.Run(snapshot)), Describe(findings));
    }

    [Fact]
    public void Check_GivenNullSnapshot_Throws()
    {
        var engine = new CheckEngine();

        Assert.Throws<ArgumentNullException>(() => engine.Check(null!));
    }

    [Fact]
    public void Check_TagsEveryFindingWithTheOssOrigin()
    {
        var snapshot = TestSnapshotFactory.Create();
        var engine = new CheckEngine();

        var findings = engine.Check(snapshot);

        Assert.NotEmpty(findings);
        Assert.All(findings, finding => Assert.Equal(FindingOrigin.Oss, finding.Origin));
    }

    [Fact]
    public void Check_OrdersFindingsBySeverityThenCode()
    {
        var snapshot = TestSnapshotFactory.Create();
        var engine = new CheckEngine();

        var findings = engine.Check(snapshot);

        var expectedOrder = findings
            .OrderByDescending(finding => Rank(finding.Severity))
            .ThenBy(finding => finding.Code, StringComparer.Ordinal);

        Assert.Equal(Describe(expectedOrder), Describe(findings));
    }

    // Findings carry list members that compare by reference, so the tests compare value projections.
    private static IEnumerable<string> Describe(IEnumerable<Finding> findings)
        => findings.Select(finding =>
            $"{finding.Severity}|{finding.Code}|{finding.Title}|{finding.Confidence}|{finding.Resources.Count}");

    private static int Rank(string severity) => severity switch
    {
        "High" => 4,
        "Medium" => 3,
        "Warning" => 2,
        _ => 1
    };
}
