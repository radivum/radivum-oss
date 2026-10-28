using System.Security.Cryptography;
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
/// Pins what a report run leaves on disk (decision O10). The graph and its topology are bundle outputs, so one
/// command produces a bundle a Workbench surface opens with Topology available instead of declaring it missing.
/// </summary>
public sealed class OssCollectionArtifactTests
{
    private static readonly DateTimeOffset WrittenAt = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

    // Asserted as a literal: the file name is what a user sees on disk, not the constant that produces it.
    private const string TopologyDiagramFileName = "network-topology.md";

    [Fact]
    public async Task ReportAsync_GivenASnapshot_WritesTheGraphAndTopologyArtifacts()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var outcome = await WriteReportAsync(directory);

            var graphBytes = await File.ReadAllBytesAsync(Path.Combine(directory, AnalysisArtifactRoles.NetworkGraph));
            var topology = await File.ReadAllTextAsync(Path.Combine(directory, TopologyDiagramFileName));

            Assert.NotEmpty(graphBytes);
            Assert.NotEmpty(topology);

            var graph = Assert.Single(
                outcome.Manifest.Artifacts,
                reference => reference.Role == AnalysisArtifactRoles.NetworkGraph);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(graphBytes)), graph.Sha256);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ReportAsync_GivenASnapshot_DeclaresTopologyAvailable()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var outcome = await WriteReportAsync(directory);

            var topology = Assert.Single(
                outcome.Manifest.Capabilities,
                capability => capability.Id == AnalysisCapabilityIds.Topology);

            Assert.Equal(AnalysisCapabilityStatus.Available, topology.Status);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ReportAsync_GivenASnapshot_WritesTheSameGraphTheGraphCommandWrites()
    {
        var reportDirectory = CreateBundleDirectory();
        var graphDirectory = CreateBundleDirectory();

        try
        {
            await WriteReportAsync(reportDirectory);

            using var provider = CreateProvider();
            var graph = provider.GetRequiredService<IGraphGenerationService>().Generate(TestSnapshotFactory.Create());
            await provider.GetRequiredService<IGraphArtifactWriter>()
                .WriteAsync(graph, Path.Combine(graphDirectory, AnalysisArtifactRoles.NetworkGraph));

            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(graphDirectory, AnalysisArtifactRoles.NetworkGraph)),
                await File.ReadAllBytesAsync(Path.Combine(reportDirectory, AnalysisArtifactRoles.NetworkGraph)));
        }
        finally
        {
            DeleteBundleDirectory(reportDirectory);
            DeleteBundleDirectory(graphDirectory);
        }
    }

    private static ServiceProvider CreateProvider() =>
        new ServiceCollection()
            // Registered first, so the graph carries the fixed instant the assertions compare against.
            .AddSingleton<TimeProvider>(new FixedTimeProvider(WrittenAt))
            .AddRadivumAzure()
            .AddRadivumAnalysis()
            .AddRadivumGraph()
            .AddRadivumReporting()
            .AddRadivumCollection()
            .BuildServiceProvider();

    private static async Task<CollectionOutcome> WriteReportAsync(string directory)
    {
        using var provider = CreateProvider();
        var snapshotPath = Path.Combine(directory, AnalysisArtifactRoles.NetworkSnapshot);

        await using (var stream = File.Create(snapshotPath))
        {
            await provider.GetRequiredService<INetworkSnapshotSerializer>()
                .SerializeAsync(stream, TestSnapshotFactory.Create(), CancellationToken.None);
        }

        return await provider.GetRequiredService<ICollectionService>()
            .ReportAsync(snapshotPath, directory, CancellationToken.None);
    }

    private static string CreateBundleDirectory()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"radivum-tests-{Guid.NewGuid():N}")).FullName;

    private static void DeleteBundleDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
