using System.Reflection;
using System.Text.Json;
using Radivum.Azure;
using Radivum.Graph;
using Radivum.Model;
using Radivum.Oss.Analysis;
using Radivum.Oss.Commands;
using Radivum.Oss.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Enforces plan section 5.4 T18 and requirements section 28: the released command surface, exit codes,
/// and artifact formats keep working while the editions are restructured and repackaged.
///
/// The published values are asserted as literals rather than through the constants, because the
/// constants are exactly what a refactor could change without noticing.
/// </summary>
public sealed class BackwardCompatibilityTests
{
    private static readonly string[] OssCommandNames =
    [
        "collect",
        "diagram",
        "graph",
        "login-status",
        "report",
    ];

    [Theory]
    [InlineData(nameof(CliExitCodes.Success), 0)]
    [InlineData(nameof(CliExitCodes.UsageError), 1)]
    [InlineData(nameof(CliExitCodes.AuthenticationFailed), 2)]
    [InlineData(nameof(CliExitCodes.AzureRequestFailed), 3)]
    [InlineData(nameof(CliExitCodes.NotReachable), 4)]
    [InlineData(nameof(CliExitCodes.AnalysisStageFailed), 5)]
    [InlineData(nameof(CliExitCodes.ConnectionFailed), 6)]
    [InlineData(nameof(CliExitCodes.Canceled), 130)]
    public void CliExitCodes_KeepTheirPublishedValue(string name, int expected)
    {
        var field = typeof(CliExitCodes).GetField(name, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(expected, field.GetRawConstantValue());
    }

    [Fact]
    public void OssHost_StillExposesTheDocumentedCommandSurface()
    {
        using var provider = new ServiceCollection()
            .AddRadivumAzure()
            .AddRadivumAnalysis()
            .AddRadivumGraph()
            .AddRadivumReporting()
            .AddRadivumCollection()
            .BuildServiceProvider();

        var names = provider
            .GetServices<ICommand>()
            .Select(command => command.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(OssCommandNames, names);
    }

    [Theory]
    [InlineData("radivum-oss login-status")]
    [InlineData("radivum-oss collect --scope <scope> [--out <dir>]")]
    [InlineData("radivum-oss report --snapshot <file> [--out <dir>]")]
    [InlineData("radivum-oss graph --snapshot <file> [--out <dir-or-file>]")]
    [InlineData("radivum-oss diagram --snapshot <file> [--out <dir-or-file>] [--format mermaid]")]
    public void OssUsage_StillDocumentsAnExistingWorkflow(string documented)
    {
        using var usage = new StringWriter();

        CliUsage.WriteBaseCommands(usage, "radivum-oss");

        Assert.Contains(documented, usage.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyNetworkGraphArtifact_StillLoadsThroughTheSharedContract()
    {
        // The sample that ships with the tool predates the analysis bundle contract: a bare graph with no
        // manifest beside it, which is what existing OSS users have on disk (requirements section 28). It is
        // copied to the test output rather than read from the repository, so this test holds in the exported
        // tree where the sample lives at a different path.
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "network-graph.sample.v1.json");

        await using var stream = File.OpenRead(path);
        var graph = await JsonSerializer.DeserializeAsync(
            stream,
            NetworkSnapshotJsonContext.Default.NetworkGraph,
            CancellationToken.None);

        Assert.NotNull(graph);
        Assert.Equal(NetworkGraphContract.GraphSchemaVersionV1, graph.GraphSchemaVersion);
    }

    [Fact]
    public async Task LegacyFindingWithoutAnOrigin_StillDeserializesAsOpenSource()
    {
        // A findings.json written before origin tagging carries no origin, and the contract defaults it
        // to the open-source edition rather than failing the read (requirements section 28, DEC-10).
        const string legacyFindingsJson =
            """
            [
              {
                "code": "NET-004",
                "severity": "Medium",
                "title": "Subnet without a network security group",
                "description": "The subnet is not associated with a network security group.",
                "confidence": "High",
                "evidence": [],
                "resources": [
                  "/subscriptions/s1/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-1/subnets/app"
                ]
              }
            ]
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(legacyFindingsJson));
        var findings = await JsonSerializer.DeserializeAsync(
            stream,
            NetworkSnapshotJsonContext.Default.ListFinding,
            CancellationToken.None);

        Assert.NotNull(findings);
        Assert.Equal(FindingOrigin.Oss, Assert.Single(findings).Origin);
    }
}
