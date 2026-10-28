using System.Security.Cryptography;
using System.Text;
using Radivum.Oss.Reporting;
using Xunit;

namespace Radivum.Tests;

public sealed class ArtifactWriterTests
{
    [Fact]
    public async Task WriteTextAsync_GivenText_WritesUtf8WithoutByteOrderMark()
    {
        var outputPath = CreateTempPath();
        var writer = new ArtifactWriter();

        try
        {
            await writer.WriteTextAsync(outputPath, "network-report", CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(outputPath);

            Assert.Equal(Encoding.UTF8.GetBytes("network-report"), bytes);
            Assert.NotEqual(0xEF, bytes[0]);
        }
        finally
        {
            DeleteDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_GivenContent_ReportsLengthAndSha256OfContent()
    {
        var outputPath = CreateTempPath();
        var writer = new ArtifactWriter();
        ReadOnlyMemory<byte> content = Encoding.UTF8.GetBytes("network-graph\n");

        try
        {
            var result = await writer.WriteAsync(outputPath, content, CancellationToken.None);

            Assert.Equal("network-graph.json", result.FileName);
            Assert.Equal(Path.GetFullPath(outputPath), result.Path);
            Assert.Equal(content.Length, result.LengthBytes);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content.Span)), result.Sha256);
        }
        finally
        {
            DeleteDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_GivenMissingParentDirectory_CreatesIt()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"radivum-tests-{Guid.NewGuid():N}", "nested", "artifact.txt");
        var writer = new ArtifactWriter();

        try
        {
            await writer.WriteAsync(outputPath, "content"u8.ToArray(), CancellationToken.None);

            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            DeleteDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_GivenCancelledToken_Throws()
    {
        var outputPath = CreateTempPath();
        var writer = new ArtifactWriter();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.WriteAsync(outputPath, "content"u8.ToArray(), cancellation.Token));
    }

    [Fact]
    public void FromContent_GivenContent_ReportsExistingFileIdentity()
    {
        var content = "network-report"u8;

        var result = ArtifactWriteResult.FromContent("network-report.md", content);

        Assert.Equal("network-report.md", result.FileName);
        Assert.Equal(content.Length, result.LengthBytes);
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(result.Sha256.ToLowerInvariant(), result.Sha256);
    }

    private static string CreateTempPath()
        => Path.Combine(Path.GetTempPath(), $"radivum-tests-{Guid.NewGuid():N}", "network-graph.json");

    private static void DeleteDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
