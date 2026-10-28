using System.Text;
using Radivum.Graph;
using Radivum.Oss.Reporting;
using Xunit;

namespace Radivum.Tests;

public sealed class GraphGenerationServiceTests
{
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_GivenInjectedClock_StampsGraphWithClockTime()
    {
        var service = new GraphGenerationService(new FixedTimeProvider(FixedTimestamp));

        var graph = service.Generate(TestSnapshotFactory.Create());

        Assert.Equal(FixedTimestamp, graph.GeneratedAt);
    }

    [Fact]
    public void Generate_GivenTestSnapshotAndClock_ProducesGoldenIds()
    {
        var service = new GraphGenerationService(new FixedTimeProvider(FixedTimestamp));

        var graph = service.Generate(TestSnapshotFactory.Create());

        Assert.Equal(ReadGoldenLines("baseline-node-ids.txt"), graph.Nodes.Select(node => node.Id));
        Assert.Equal(ReadGoldenLines("baseline-edge-ids.txt"), graph.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public void Serialize_GivenGeneratedGraph_RendersOffsetTimestampsAndCamelCase()
    {
        var service = new GraphGenerationService(new FixedTimeProvider(FixedTimestamp));
        var serializer = new NetworkGraphSerializer();

        var json = Encoding.UTF8.GetString(serializer.Serialize(service.Generate(TestSnapshotFactory.Create())));

        Assert.Contains("\"generatedAt\": \"2026-09-10T00:00:00+00:00\"", json, StringComparison.Ordinal);
        Assert.StartsWith("{", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_GivenGraph_ProducesUtf8WithoutByteOrderMark()
    {
        var service = new GraphGenerationService(new FixedTimeProvider(FixedTimestamp));
        var serializer = new NetworkGraphSerializer();

        var bytes = serializer.Serialize(service.Generate(TestSnapshotFactory.Create()));

        Assert.NotEmpty(bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public async Task GraphArtifactWriter_GivenGraph_WritesSerializedContentToRequestedPath()
    {
        var service = new GraphGenerationService(new FixedTimeProvider(FixedTimestamp));
        var serializer = new NetworkGraphSerializer();
        var writer = new GraphArtifactWriter(serializer, new ArtifactWriter());
        var outputPath = Path.Combine(Path.GetTempPath(), $"radivum-tests-{Guid.NewGuid():N}", "network-graph.json");
        var graph = service.Generate(TestSnapshotFactory.Create());

        try
        {
            var result = await writer.WriteAsync(graph, outputPath, CancellationToken.None);

            Assert.Equal("network-graph.json", result.FileName);
            Assert.Equal(serializer.Serialize(graph), await File.ReadAllBytesAsync(outputPath));
            Assert.Equal(result.LengthBytes, new FileInfo(outputPath).Length);
        }
        finally
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string[] ReadGoldenLines(string fileName)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Golden", fileName))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }
}
