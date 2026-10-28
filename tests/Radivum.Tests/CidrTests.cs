using Radivum.Oss.Analysis;
using Xunit;

namespace Radivum.Tests;

public sealed class CidrTests
{
    [Fact]
    public void TryParse_GivenValidBlock_ReturnsMaskedNetworkAndMask()
    {
        var parsed = Cidr.TryParse("10.0.5.7/16", out var network, out var mask);

        Assert.True(parsed);
        Assert.Equal(0x0A000000u, network);
        Assert.Equal(0xFFFF0000u, mask);
    }

    [Fact]
    public void TryParse_GivenSlashZero_ProducesEmptyMask()
    {
        var parsed = Cidr.TryParse("0.0.0.0/0", out var network, out var mask);

        Assert.True(parsed);
        Assert.Equal(0u, network);
        Assert.Equal(0u, mask);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("10.0.0/16")]
    [InlineData("10.0.0.0.0/16")]
    [InlineData("ten.0.0.0/16")]
    [InlineData("10.0.0.0/sixteen")]
    public void TryParse_GivenMalformedBlock_ReturnsFalse(string candidate)
    {
        var parsed = Cidr.TryParse(candidate, out _, out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData("10.0.0.0/16", "10.0.1.0/24", true)]
    [InlineData("10.0.0.0/8", "10.1.0.0/16", true)]
    [InlineData("10.0.0.0/24", "10.0.1.0/24", false)]
    [InlineData("10.0.0.0/24", "192.168.0.0/16", false)]
    public void Overlaps_GivenTextualBlocks_ReportsWhetherTheyShareAddresses(
        string left,
        string right,
        bool expected)
    {
        Assert.Equal(expected, Cidr.Overlaps(left, right));
        Assert.Equal(expected, Cidr.Overlaps(right, left));
    }

    [Theory]
    [InlineData("not-a-cidr", "10.0.0.0/16")]
    [InlineData("10.0.0.0/16", "not-a-cidr")]
    [InlineData("not-a-cidr", "also-not-a-cidr")]
    public void Overlaps_GivenUnparseableInput_ReturnsFalse(string left, string right)
    {
        Assert.False(Cidr.Overlaps(left, right));
    }
}
