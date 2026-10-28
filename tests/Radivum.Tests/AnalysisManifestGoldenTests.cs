using System.Text.Json;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Pins the canonical manifests of both editions. The open-source and the Pro run must describe
/// themselves through the same contract, so a change to either golden file is a contract change and
/// has to be reviewed as one.
/// </summary>
public sealed class AnalysisManifestGoldenTests
{
    private const string OssGoldenFile = "baseline-oss-analysis-manifest.json";
    private const string ProGoldenFile = "baseline-pro-analysis-manifest.json";

    [Fact]
    public async Task ComposeAsync_GivenCanonicalOssBundle_MatchesTheRecordedManifest()
    {
        var directory = CreateBundleDirectory();

        try
        {
            await TestAnalysisBundle.ComposeOssBundleAsync(directory);

            Assert.Equal(ReadGoldenText(OssGoldenFile), Normalize(await ReadManifestAsync(directory)));
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenCanonicalProBundle_MatchesTheRecordedManifest()
    {
        var directory = CreateBundleDirectory();

        try
        {
            await TestAnalysisBundle.ComposeProBundleAsync(directory);

            Assert.Equal(ReadGoldenText(ProGoldenFile), Normalize(await ReadManifestAsync(directory)));
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenCanonicalBundles_RoundTripsThroughTheContract()
    {
        var ossDirectory = CreateBundleDirectory();
        var proDirectory = CreateBundleDirectory();

        try
        {
            var ossManifest = await TestAnalysisBundle.ComposeOssBundleAsync(ossDirectory);
            var proManifest = await TestAnalysisBundle.ComposeProBundleAsync(proDirectory);

            var readOss = Read(await ReadManifestAsync(ossDirectory));
            var readPro = Read(await ReadManifestAsync(proDirectory));

            // List members do not take part in record equality, so the round trip is asserted per member.
            Assert.Equal(ossManifest.AnalysisId, readOss.AnalysisId);
            Assert.Equal(ossManifest.GeneratedAt, readOss.GeneratedAt);
            Assert.Equal(ossManifest.Metadata, readOss.Metadata);
            Assert.Equal(ossManifest.Artifacts, readOss.Artifacts);
            Assert.Equal(ossManifest.Capabilities, readOss.Capabilities);
            Assert.Equal(ossManifest.Coverage, readOss.Coverage);
            Assert.Equal(proManifest.AnalysisId, readPro.AnalysisId);
            Assert.Equal(proManifest.Metadata, readPro.Metadata);
            Assert.Equal(proManifest.Artifacts, readPro.Artifacts);
            Assert.Equal(proManifest.Capabilities, readPro.Capabilities);
            Assert.Equal(AnalysisManifestSchema.Version, readPro.SchemaVersion);
            Assert.Equal(
                [AnalysisArtifactRoles.NetworkSnapshot, AnalysisArtifactRoles.NetworkGraph, AnalysisArtifactRoles.Findings,
                    AnalysisArtifactRoles.RoutingEvidence, AnalysisArtifactRoles.TraceResults],
                readPro.Artifacts.Select(reference => reference.Role));
        }
        finally
        {
            DeleteBundleDirectory(ossDirectory);
            DeleteBundleDirectory(proDirectory);
        }
    }

    private static AnalysisManifest Read(string json)
        => Assert.IsType<AnalysisManifest>(JsonSerializer.Deserialize(json, NetworkSnapshotJsonContext.Default.AnalysisManifest));

    private static Task<string> ReadManifestAsync(string directory)
        => File.ReadAllTextAsync(Path.Combine(directory, AnalysisArtifactRoles.Manifest));

    /// <summary>
    /// Normalizes line endings and the trailing newline: the manifest is written with <c>\n</c> only,
    /// while a source checkout may expand those to <c>\r\n</c>.
    /// </summary>
    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    private static string ReadGoldenText(string fileName)
        => Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", fileName)));

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
