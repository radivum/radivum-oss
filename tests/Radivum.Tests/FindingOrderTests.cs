using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

public sealed class FindingOrderTests
{
    [Theory]
    [InlineData("High", 4)]
    [InlineData("Medium", 3)]
    [InlineData("Warning", 2)]
    [InlineData("Info", 1)]
    [InlineData("Unknown", 1)]
    public void SeverityRank_GivenSeverity_ReturnsItsSortRank(string severity, int expected)
        => Assert.Equal(expected, FindingOrder.SeverityRank(severity));

    [Fact]
    public void Sort_GivenMixedSeverities_OrdersByDescendingSeverity()
    {
        var findings = new[]
        {
            CreateFinding("NET-003", "Warning"),
            CreateFinding("NET-001", "High"),
            CreateFinding("NET-002", "Medium")
        };

        var sorted = FindingOrder.Sort(findings);

        Assert.Equal(["NET-001", "NET-002", "NET-003"], sorted.Select(finding => finding.Code));
    }

    [Fact]
    public void Sort_GivenEqualSeverities_OrdersByCodeOrdinal()
    {
        var findings = new[]
        {
            CreateFinding("NET-010", "High"),
            CreateFinding("NET-002", "High")
        };

        var sorted = FindingOrder.Sort(findings);

        Assert.Equal(["NET-002", "NET-010"], sorted.Select(finding => finding.Code));
    }

    private static Finding CreateFinding(string code, string severity) => new(
        Code: code,
        Severity: severity,
        Title: $"{code} title",
        Description: $"{code} description",
        Confidence: "High",
        Evidence: [],
        Resources: []);
}
