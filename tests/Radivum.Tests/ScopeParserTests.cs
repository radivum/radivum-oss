using Radivum.Oss.Commands;
using Xunit;

namespace Radivum.Tests;

public sealed class ScopeParserTests
{
    private const string SubscriptionId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void TryParse_AcceptsBareSubscriptionId()
    {
        var parsed = ScopeParser.TryParse(SubscriptionId, out var subscriptionId, out var resourceGroup);

        Assert.True(parsed);
        Assert.Equal(SubscriptionId, subscriptionId);
        Assert.Null(resourceGroup);
    }

    [Fact]
    public void TryParse_AcceptsSubscriptionPath()
    {
        var parsed = ScopeParser.TryParse($"/subscriptions/{SubscriptionId}", out var subscriptionId, out var resourceGroup);

        Assert.True(parsed);
        Assert.Equal(SubscriptionId, subscriptionId);
        Assert.Null(resourceGroup);
    }

    [Fact]
    public void TryParse_AcceptsResourceGroupPath()
    {
        var parsed = ScopeParser.TryParse(
            $"/subscriptions/{SubscriptionId}/resourceGroups/rg-network",
            out var subscriptionId,
            out var resourceGroup);

        Assert.True(parsed);
        Assert.Equal(SubscriptionId, subscriptionId);
        Assert.Equal("rg-network", resourceGroup);
    }

    [Fact]
    public void TryParse_IgnoresSurroundingWhitespaceAndTrailingSeparators()
    {
        var parsed = ScopeParser.TryParse(
            $"  /subscriptions/{SubscriptionId}/resourceGroups/rg/  ",
            out var subscriptionId,
            out var resourceGroup);

        Assert.True(parsed);
        Assert.Equal(SubscriptionId, subscriptionId);
        Assert.Equal("rg", resourceGroup);
    }

    [Fact]
    public void TryParse_MatchesResourceGroupsSegmentCaseInsensitively()
    {
        var parsed = ScopeParser.TryParse(
            $"/subscriptions/{SubscriptionId}/RESOURCEGROUPS/rg",
            out var subscriptionId,
            out var resourceGroup);

        Assert.True(parsed);
        Assert.Equal(SubscriptionId, subscriptionId);
        Assert.Equal("rg", resourceGroup);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("/subscriptions")]
    [InlineData("/subscriptions/not-a-guid")]
    [InlineData("/subscriptions/{id}/resourceGroups")]
    [InlineData("/subscriptions/{id}/locations/eastus")]
    [InlineData("")]
    public void TryParse_RejectsMalformedScope(string scope)
    {
        var parsed = ScopeParser.TryParse(scope.Replace("{id}", SubscriptionId), out _, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParse_ThrowsWhenScopeIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ScopeParser.TryParse(null!, out _, out _));
    }

    [Fact]
    public void TryParseMany_GivenOneScope_SelectsOneSubscription()
    {
        var parsed = ScopeParser.TryParseMany([$"/subscriptions/{SubscriptionId}"], out var selection, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal([SubscriptionId], selection!.SubscriptionIds);
        Assert.Null(selection.ResourceGroup);
        Assert.False(selection.IsMultiSubscription);
    }

    [Fact]
    public void TryParseMany_GivenRepeatedScopes_CollapsesDuplicatesAndOrdersThem()
    {
        var parsed = ScopeParser.TryParseMany(
            ["/subscriptions/22222222-2222-2222-2222-222222222222", SubscriptionId, $"  {SubscriptionId}  ",
             "/subscriptions/22222222-2222-2222-2222-222222222222"],
            out var selection,
            out _);

        Assert.True(parsed);
        Assert.Equal(
            [SubscriptionId, "22222222-2222-2222-2222-222222222222"],
            selection!.SubscriptionIds);
        Assert.True(selection.IsMultiSubscription);
    }

    [Fact]
    public void TryParseMany_GivenMoreThanTheCap_RejectsTheSelection()
    {
        string[] scopes =
        [
            "/subscriptions/11111111-1111-1111-1111-111111111111",
            "/subscriptions/22222222-2222-2222-2222-222222222222",
            "/subscriptions/33333333-3333-3333-3333-333333333333",
            "/subscriptions/44444444-4444-4444-4444-444444444444",
        ];

        var parsed = ScopeParser.TryParseMany(scopes, out var selection, out var error);

        Assert.False(parsed);
        Assert.Null(selection);
        Assert.Contains("At most", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseMany_GivenAResourceGroupWithSeveralSubscriptions_RejectsTheSelection()
    {
        string[] scopes =
        [
            "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg",
            "/subscriptions/22222222-2222-2222-2222-222222222222",
        ];

        var parsed = ScopeParser.TryParseMany(scopes, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("exactly one subscription", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseMany_GivenAnInvalidScope_NamesIt()
    {
        var parsed = ScopeParser.TryParseMany([SubscriptionId, "nonsense"], out _, out var error);

        Assert.False(parsed);
        Assert.Contains("nonsense", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseMany_GivenNoScope_ReportsIt()
    {
        var parsed = ScopeParser.TryParseMany([], out _, out var error);

        Assert.False(parsed);
        Assert.Contains("Missing --scope", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_GivenOneScope_KeepsTheTextTheOperatorSupplied()
    {
        var scopes = new[] { $"  {SubscriptionId}  " };
        Assert.True(ScopeParser.TryParseMany(scopes, out var selection, out _));

        Assert.Equal(scopes[0], ScopeParser.Describe(scopes, selection!));
    }

    [Fact]
    public void Describe_GivenSeveralScopes_ComposesArmScopesInOrder()
    {
        string[] scopes =
        [
            "/subscriptions/22222222-2222-2222-2222-222222222222",
            "/subscriptions/11111111-1111-1111-1111-111111111111",
        ];
        Assert.True(ScopeParser.TryParseMany(scopes, out var selection, out _));

        Assert.Equal(
            "/subscriptions/11111111-1111-1111-1111-111111111111, /subscriptions/22222222-2222-2222-2222-222222222222",
            ScopeParser.Describe(scopes, selection!));
    }
}