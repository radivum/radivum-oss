using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Covers the one parser every consumer uses to read an Azure resource id. The leaf name, the type trail and the
/// parent are what turn a private endpoint's target or a NIC's virtual machine into a node the reader recognizes,
/// so each is pinned here rather than re-derived per host.
/// </summary>
public sealed class ArmResourceIdTests
{
    private const string SubscriptionId = "11111111-1111-1111-1111-111111111111";

    private const string ProviderScope =
        "/subscriptions/" + SubscriptionId + "/resourceGroups/rg-app-prod/providers";

    [Fact]
    public void TryParse_GivenAVirtualMachineId_ExposesTheResourceIdentity()
    {
        var parsed = ParseRequired($"{ProviderScope}/Microsoft.Compute/virtualMachines/vm-app-01");

        Assert.Equal(SubscriptionId, parsed.SubscriptionId);
        Assert.Equal("rg-app-prod", parsed.ResourceGroup);
        Assert.Equal("Microsoft.Compute", parsed.ProviderNamespace);
        Assert.Equal("Microsoft.Compute/virtualMachines", parsed.AzureResourceType);
        Assert.Equal("vm-app-01", parsed.Name);
    }

    [Fact]
    public void TryParse_GivenATopLevelResource_ReportsNoParent()
    {
        var parsed = ParseRequired($"{ProviderScope}/Microsoft.Compute/virtualMachines/vm-app-01");

        Assert.Null(parsed.ParentName);
        Assert.Null(parsed.ParentAzureResourceType);
    }

    [Fact]
    public void TryParse_GivenASubnetId_KeepsTheVirtualNetworkAsItsParent()
    {
        var parsed = ParseRequired(
            $"{ProviderScope}/Microsoft.Network/virtualNetworks/vnet-hub/subnets/snet-app");

        Assert.Equal("Microsoft.Network/virtualNetworks/subnets", parsed.AzureResourceType);
        Assert.Equal(["virtualNetworks", "subnets"], parsed.TypeTrail);
        Assert.Equal("snet-app", parsed.Name);
        Assert.Equal("vnet-hub", parsed.ParentName);
        Assert.Equal("Microsoft.Network/virtualNetworks", parsed.ParentAzureResourceType);
    }

    [Fact]
    public void TryParse_GivenAScaleSetInstanceId_ExposesTheInstanceAndTheScaleSet()
    {
        var parsed = ParseRequired(
            $"{ProviderScope}/Microsoft.Compute/virtualMachineScaleSets/vmss-web/virtualMachines/0");

        Assert.Equal(["virtualMachineScaleSets", "virtualMachines"], parsed.TypeTrail);
        Assert.Equal("0", parsed.Name);
        Assert.Equal("vmss-web", parsed.ParentName);
        Assert.Equal("Microsoft.Compute/virtualMachineScaleSets", parsed.ParentAzureResourceType);
    }

    [Fact]
    public void TryParse_GivenANestedSqlDatabaseId_ExposesTheDatabase()
    {
        var parsed = ParseRequired($"{ProviderScope}/Microsoft.Sql/servers/sql-prod/databases/db-orders");

        Assert.Equal("Microsoft.Sql/servers/databases", parsed.AzureResourceType);
        Assert.Equal("db-orders", parsed.Name);
        Assert.Equal("sql-prod", parsed.ParentName);
    }

    [Fact]
    public void TryParse_GivenUpperCasedNameSegments_PreservesTheirCasing()
    {
        var parsed = ParseRequired($"{ProviderScope}/Microsoft.Compute/virtualMachines/VM-App-01");

        Assert.Equal("VM-App-01", parsed.Name);
    }

    [Fact]
    public void TryParse_GivenMixedCaseScopeSegments_ReadsThemCaseInsensitively()
    {
        var parsed = ParseRequired(
            $"/Subscriptions/{SubscriptionId}/RESOURCEGROUPS/rg-app-prod/Providers/Microsoft.Compute/virtualMachines/vm-app-01");

        Assert.Equal(SubscriptionId, parsed.SubscriptionId);
        Assert.Equal("rg-app-prod", parsed.ResourceGroup);
    }

    [Fact]
    public void TryParse_GivenATrailingSlash_IgnoresIt()
    {
        var parsed = ParseRequired($"{ProviderScope}/Microsoft.Compute/virtualMachines/vm-app-01/");

        Assert.Equal("vm-app-01", parsed.Name);
        Assert.Equal("Microsoft.Compute/virtualMachines", parsed.AzureResourceType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_GivenNoResourceId_ReturnsFalse(string? resourceId)
    {
        Assert.False(ArmResourceId.TryParse(resourceId, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_GivenPlainText_ReturnsFalse()
    {
        Assert.False(ArmResourceId.TryParse("vm-app-01", out _));
    }

    [Fact]
    public void TryParse_GivenAnIdWithoutProviders_ReturnsFalse()
    {
        Assert.False(ArmResourceId.TryParse($"/subscriptions/{SubscriptionId}/resourceGroups/rg-app-prod", out _));
    }

    [Fact]
    public void TryParse_GivenATypeWithoutAName_ReturnsFalse()
    {
        Assert.False(
            ArmResourceId.TryParse($"{ProviderScope}/Microsoft.Network/virtualNetworks", out _));
    }

    [Fact]
    public void TryParse_GivenASubscriptionScopedResourceWithoutAResourceGroup_ReturnsFalse()
    {
        Assert.False(
            ArmResourceId.TryParse(
                $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/policyDefinitions/policy-1",
                out _));
    }

    [Fact]
    public void TryParse_GivenATrailingTypeWithoutAName_ReturnsFalse()
    {
        Assert.False(
            ArmResourceId.TryParse(
                $"{ProviderScope}/Microsoft.Network/virtualNetworks/vnet-hub/subnets",
                out _));
    }

    [Fact]
    public void ParseOrNull_GivenAnUnsupportedId_ReturnsNull()
    {
        Assert.Null(ArmResourceId.ParseOrNull("not-a-resource-id"));
    }

    /// <summary>
    /// Parses an id a test expects to be valid. A failure names the input, because the assertion that matters is
    /// which shape was rejected, not merely that something was.
    /// </summary>
    private static ArmResourceId ParseRequired(string resourceId)
    {
        if (!ArmResourceId.TryParse(resourceId, out var parsed))
        {
            throw new Xunit.Sdk.XunitException($"Expected '{resourceId}' to parse as an Azure resource id.");
        }

        return parsed;
    }
}
