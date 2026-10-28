using System.Security.Cryptography;
using System.Text;
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

public sealed class AnalysisManifestComposerTests
{
    [Fact]
    public async Task ComposeAsync_GivenOssBundle_IndexesTheArtifactsTheRunWrote()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(directory);

            Assert.Equal(
                [AnalysisArtifactRoles.NetworkSnapshot, AnalysisArtifactRoles.Findings],
                manifest.Artifacts.Select(reference => reference.Role));
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenBundle_RecordsDigestsThatMatchTheFilesOnDisk()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(directory);

            foreach (var reference in manifest.Artifacts)
            {
                var content = await File.ReadAllBytesAsync(Path.Combine(directory, reference.RelativePath));

                Assert.Equal(Path.GetFileName(reference.RelativePath), reference.RelativePath);
                Assert.Equal(content.LongLength, reference.ByteLength);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), reference.Sha256);
            }
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenOssEdition_ReportsInterpretationCapabilitiesUnavailable()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(directory);

            var routing = CapabilityOf(manifest, AnalysisCapabilityIds.Routing);
            var security = CapabilityOf(manifest, AnalysisCapabilityIds.Security);

            Assert.Equal(AnalysisCapabilityStatus.Unavailable, routing.Status);
            Assert.Equal("requires the pro edition", routing.Detail);
            Assert.Equal(AnalysisCapabilityStatus.Unavailable, security.Status);
            Assert.Equal("requires the pro edition", security.Detail);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenOssEditionBundle_ReportsTopologyUnavailableWithoutGraphArtifact()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(directory);

            var topology = CapabilityOf(manifest, AnalysisCapabilityIds.Topology);

            Assert.Equal(AnalysisCapabilityStatus.Unavailable, topology.Status);
            Assert.Equal($"missing {AnalysisArtifactRoles.NetworkGraph}", topology.Detail);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenProBundle_ReportsInterpretationCapabilitiesAvailable()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeProBundleAsync(directory);

            Assert.Equal(AnalysisCapabilityStatus.Available, CapabilityOf(manifest, AnalysisCapabilityIds.Topology).Status);
            Assert.Equal(AnalysisCapabilityStatus.Available, CapabilityOf(manifest, AnalysisCapabilityIds.Findings).Status);
            Assert.Equal(AnalysisCapabilityStatus.Available, CapabilityOf(manifest, AnalysisCapabilityIds.Routing).Status);
            Assert.Equal(AnalysisCapabilityStatus.Available, CapabilityOf(manifest, AnalysisCapabilityIds.Trace).Status);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenProBundleWithoutSecurityEvidence_ReportsSecurityUnavailable()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeProBundleAsync(directory);

            var security = CapabilityOf(manifest, AnalysisCapabilityIds.Security);

            Assert.Equal(AnalysisCapabilityStatus.Unavailable, security.Status);
            Assert.Equal($"missing {AnalysisArtifactRoles.SecurityEvidence}", security.Detail);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenProBundle_RecordsEditionSourceKindAndCoverage()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeProBundleAsync(directory);

            Assert.Equal(AnalysisEdition.Pro, manifest.Edition);
            Assert.Equal(AnalysisManifestSchema.Version, manifest.SchemaVersion);
            Assert.Equal("0.1.0", manifest.ToolVersion);
            Assert.Equal("/subscriptions/sub-1", manifest.Metadata.ScopeDescription);
            Assert.Equal("sub-1", manifest.Metadata.SubscriptionId);
            Assert.Equal(new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero), manifest.Metadata.CollectedAt);
            Assert.Equal(AnalysisSourceKind.ProCli, manifest.Metadata.SourceKind);
            Assert.NotNull(manifest.Coverage);
            Assert.Equal(AnalysisCapabilityStatus.Available, manifest.Coverage.Status);
            Assert.Equal(1, manifest.Coverage.SnapshotResourceCount);
            Assert.Equal(0, manifest.Coverage.EffectiveEvidenceCount);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenScopeSubscriptionName_RecordsItInTheManifest()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeProBundleAsync(directory, "Contoso Production");
            var written = await File.ReadAllTextAsync(Path.Combine(directory, AnalysisArtifactRoles.Manifest));

            Assert.Equal("Contoso Production", manifest.Metadata.SubscriptionName);
            // The name travels to the reader as a serialized member, not only in the in-memory record.
            Assert.Contains("\"subscriptionName\": \"Contoso Production\"", written, StringComparison.Ordinal);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenNoScopeSubscriptionName_OmitsTheMember()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeProBundleAsync(directory);
            var written = await File.ReadAllTextAsync(Path.Combine(directory, AnalysisArtifactRoles.Manifest));

            Assert.Null(manifest.Metadata.SubscriptionName);
            // Optional and additive: a snapshot that names only the identifier keeps the manifest exactly
            // as it was before the member existed, so manifest schema 0.1 is unchanged.
            Assert.DoesNotContain("subscriptionName", written, StringComparison.Ordinal);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenScopeSubscriptionName_KeepsTheAnalysisId()
    {
        var named = CreateBundleDirectory();
        var unnamed = CreateBundleDirectory();

        try
        {
            var namedManifest = await TestAnalysisBundle.ComposeProBundleAsync(named, "Contoso Production");
            var unnamedManifest = await TestAnalysisBundle.ComposeProBundleAsync(unnamed);

            // The name is a label, not an input the identity is derived from, so naming the scope never
            // changes which analysis a bundle is.
            Assert.Equal(unnamedManifest.AnalysisId, namedManifest.AnalysisId);
        }
        finally
        {
            DeleteBundleDirectory(named);
            DeleteBundleDirectory(unnamed);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenAMultiSubscriptionScope_RecordsTheSetAndDerivesIdentityFromIt()
    {
        var directory = CreateBundleDirectory();
        var otherDirectory = CreateBundleDirectory();
        var reversedDirectory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(
                directory,
                MultiSubscriptionSnapshot("sub-1", "sub-2"));
            var otherSet = await TestAnalysisBundle.ComposeOssBundleAsync(
                otherDirectory,
                MultiSubscriptionSnapshot("sub-1", "sub-3"));
            var reversedSet = await TestAnalysisBundle.ComposeOssBundleAsync(
                reversedDirectory,
                MultiSubscriptionSnapshot("sub-2", "sub-1"));

            Assert.Null(manifest.Metadata.SubscriptionId);
            Assert.Equal(["sub-1", "sub-2"], manifest.Metadata.SubscriptionIds);
            Assert.Equal(
                ["sub-1 name", "sub-2 name"],
                manifest.Metadata.Subscriptions!.Select(entry => entry.Name));

            // The set is what distinguishes the analysis: another set is another analysis, and the same set
            // written in another order is still the same one.
            Assert.NotEqual(manifest.AnalysisId, otherSet.AnalysisId);
            Assert.Equal(manifest.AnalysisId, reversedSet.AnalysisId);
        }
        finally
        {
            DeleteBundleDirectory(directory);
            DeleteBundleDirectory(otherDirectory);
            DeleteBundleDirectory(reversedDirectory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenASingleSubscription_OmitsTheSetFromTheManifest()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(directory);
            var written = await File.ReadAllTextAsync(Path.Combine(directory, AnalysisArtifactRoles.Manifest));

            Assert.Null(manifest.Metadata.SubscriptionIds);
            Assert.Null(manifest.Metadata.Subscriptions);
            Assert.DoesNotContain("subscriptionIds", written, StringComparison.Ordinal);
            Assert.DoesNotContain("\"subscriptions\"", written, StringComparison.Ordinal);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenAMultiSubscriptionSnapshot_ProjectsSubscriptionCoverage()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var snapshot = MultiSubscriptionSnapshot("sub-1", "sub-2") with
            {
                CollectionMetadata =
                [
                    new CollectionMetadata(
                        "Subscription",
                        "ARM",
                        TestAnalysisBundle.ComposedAt,
                        "Reader",
                        "Available",
                        1d,
                        [],
                        "sub-1"),
                    new CollectionMetadata(
                        "Subscription",
                        "ARM",
                        TestAnalysisBundle.ComposedAt,
                        "Reader",
                        "Unavailable",
                        0d,
                        ["Subscription metadata could not be read: 403."],
                        "sub-2"),
                ],
            };

            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(directory, snapshot);

            // A reader can name the scope that is incomplete instead of only saying that something was.
            Assert.NotNull(manifest.SubscriptionCoverage);
            Assert.Equal(["sub-1", "sub-2"], manifest.SubscriptionCoverage!.Select(item => item.SubscriptionId));
            Assert.Equal(["Available", "Unavailable"], manifest.SubscriptionCoverage!.Select(item => item.Status));
            Assert.NotEmpty(manifest.SubscriptionCoverage![1].Errors);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenASingleSubscription_OmitsSubscriptionCoverage()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(directory);
            var written = await File.ReadAllTextAsync(Path.Combine(directory, AnalysisArtifactRoles.Manifest));

            Assert.Null(manifest.SubscriptionCoverage);
            Assert.DoesNotContain("subscriptionCoverage", written, StringComparison.Ordinal);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    /// <summary>
    /// A snapshot whose scope names several subscriptions. The description is fixed so the tests vary only the
    /// subscription set.
    /// </summary>
    private static NetworkSnapshot MultiSubscriptionSnapshot(params string[] subscriptionIds)
    {
        var snapshot = TestSnapshotFactory.Create();

        return snapshot with
        {
            SubscriptionIds = subscriptionIds,
            Scope = new SnapshotScope(
                "scope: two subscriptions",
                null,
                null,
                subscriptionIds,
                Subscriptions: subscriptionIds
                    .Select(id => new SubscriptionScopeEntry(id, id + " name"))
                    .ToList()),
        };
    }

    [Fact]
    public async Task ComposeAsync_GivenIdenticalInputs_KeepsTheAnalysisIdStableAcrossRuns()
    {
        var first = CreateBundleDirectory();
        var second = CreateBundleDirectory();

        try
        {
            var firstManifest = await TestAnalysisBundle.ComposeOssBundleAsync(first);
            var secondManifest = await TestAnalysisBundle.ComposeOssBundleAsync(second);

            Assert.Equal("sha256-", firstManifest.AnalysisId[.."sha256-".Length]);
            Assert.Equal(64, firstManifest.AnalysisId.Length - "sha256-".Length);
            Assert.All(firstManifest.AnalysisId["sha256-".Length..], character => Assert.Contains(character, "0123456789abcdef"));
            Assert.Equal(firstManifest.AnalysisId, secondManifest.AnalysisId);
            Assert.Equal(TestAnalysisBundle.ComposedAt, firstManifest.GeneratedAt);
        }
        finally
        {
            DeleteBundleDirectory(first);
            DeleteBundleDirectory(second);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenNewerClock_ChangesOnlyTheGenerationTimestamp()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var manifest = await TestAnalysisBundle.ComposeOssBundleAsync(directory);
            var composedLater = await new AnalysisManifestComposer(new FixedTimeProvider(TestAnalysisBundle.ComposedAt.AddHours(1)))
                .ComposeAsync(
                    directory,
                    TestSnapshotFactory.Create(),
                    AnalysisEdition.Oss,
                    AnalysisSourceKind.OssCollection,
                    cancellationToken: CancellationToken.None);

            Assert.Equal(manifest.AnalysisId, composedLater.AnalysisId);
            Assert.NotEqual(manifest.GeneratedAt, composedLater.GeneratedAt);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenBundle_AddsOnlyTheManifestToTheArtifactsPresent()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var outcome = await ComposeAndSnapshotDirectoryAsync(directory);

            Assert.Equal(
                [
                    AnalysisArtifactRoles.Manifest,
                    AnalysisArtifactRoles.Findings,
                    "network-report.md",
                    AnalysisArtifactRoles.NetworkSnapshot
                ],
                outcome.After.Keys.Order(StringComparer.Ordinal));

            foreach (var (fileName, digest) in outcome.Before)
            {
                Assert.Equal(digest, outcome.After[fileName]);
            }
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenUnrecognizedArtifactInTheBundle_DoesNotIndexOrRejectIt()
    {
        var directory = CreateBundleDirectory();

        try
        {
            await TestAnalysisBundle.ComposeOssBundleAsync(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "future-artifact.json"), """{"capability":"quantum"}""");

            var manifest = await new AnalysisManifestComposer(new FixedTimeProvider(TestAnalysisBundle.ComposedAt))
                .ComposeAsync(
                    directory,
                    TestSnapshotFactory.Create(),
                    AnalysisEdition.Oss,
                    AnalysisSourceKind.OssCollection,
                    cancellationToken: CancellationToken.None);

            Assert.DoesNotContain("future-artifact.json", manifest.Artifacts.Select(reference => reference.Role));
            Assert.Equal(
                [AnalysisArtifactRoles.NetworkSnapshot, AnalysisArtifactRoles.Findings],
                manifest.Artifacts.Select(reference => reference.Role));
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public void Deserialize_GivenUnknownCapabilityAndArtifactRole_KeepsTheKnownEntries()
    {
        const string payload = """
            {
              "analysisId": "sha256-0000000000000000000000000000000000000000000000000000000000000000",
              "schemaVersion": "0.1",
              "toolVersion": "0.1.0",
              "edition": "pro",
              "generatedAt": "2026-09-10T00:00:00+00:00",
              "metadata": {
                "scopeDescription": "/subscriptions/sub-1",
                "subscriptionId": "sub-1",
                "collectedAt": "2026-09-08T10:00:00+00:00",
                "sourceKind": "pro-cli",
                "agentSessionId": "future-field"
              },
              "artifacts": [
                {
                  "role": "network-snapshot.json",
                  "relativePath": "network-snapshot.json",
                  "sha256": "aa",
                  "byteLength": 1
                },
                {
                  "role": "future-artifact.json",
                  "relativePath": "future-artifact.json",
                  "sha256": "bb",
                  "byteLength": 2
                }
              ],
              "capabilities": [
                { "id": "overview", "status": "available", "detail": null },
                { "id": "quantum", "status": "available", "detail": "not implemented by this reader" }
              ],
              "coverage": {
                "overallCompleteness": 1,
                "status": "available",
                "unavailableSourceCount": 0,
                "snapshotResourceCount": 1,
                "effectiveEvidenceCount": 0,
                "futureField": true
              }
            }
            """;

        var manifest = JsonSerializer.Deserialize(
            payload,
            NetworkSnapshotJsonContext.Default.AnalysisManifest);

        Assert.NotNull(manifest);
        Assert.Equal(AnalysisEdition.Pro, manifest.Edition);
        Assert.Equal(AnalysisSourceKind.ProCli, manifest.Metadata.SourceKind);
        Assert.Equal(2, manifest.Artifacts.Count);
        Assert.Equal("future-artifact.json", manifest.Artifacts[1].Role);
        Assert.Equal(2, manifest.Capabilities.Count);
        Assert.Equal("quantum", manifest.Capabilities[1].Id);
        Assert.Equal(1, manifest.Coverage?.SnapshotResourceCount);
    }

    [Fact]
    public async Task ReportAsync_GivenSnapshotFile_WritesTheOssEditionManifest()
    {
        var directory = CreateBundleDirectory();

        try
        {
            var snapshotPath = Path.Combine(directory, AnalysisArtifactRoles.NetworkSnapshot);
            await using (var stream = File.Create(snapshotPath))
            {
                await new NetworkSnapshotSerializer()
                    .SerializeAsync(stream, TestSnapshotFactory.Create(), CancellationToken.None);
            }

            await using var provider = new ServiceCollection()
                .AddRadivumAzure()
                .AddRadivumAnalysis()
                .AddRadivumGraph()
                .AddRadivumReporting()
                .AddRadivumCollection()
                .BuildServiceProvider();

            var outcome = await provider.GetRequiredService<ICollectionService>()
                .ReportAsync(snapshotPath, directory, CancellationToken.None);

            var manifestPath = Path.Combine(directory, AnalysisArtifactRoles.Manifest);
            var manifest = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(manifestPath),
                NetworkSnapshotJsonContext.Default.AnalysisManifest);

            Assert.True(File.Exists(manifestPath));
            Assert.NotNull(manifest);
            Assert.Equal(outcome.Manifest.AnalysisId, manifest.AnalysisId);
            Assert.Equal(AnalysisEdition.Oss, manifest.Edition);
            Assert.Equal(AnalysisSourceKind.LocalFile, manifest.Metadata.SourceKind);
            Assert.Equal(
                [AnalysisArtifactRoles.NetworkSnapshot, AnalysisArtifactRoles.NetworkGraph, AnalysisArtifactRoles.Findings],
                manifest.Artifacts.Select(reference => reference.Role));
            Assert.Equal(AnalysisCapabilityStatus.Available, CapabilityOf(manifest, AnalysisCapabilityIds.Topology).Status);
            Assert.Equal(AnalysisCapabilityStatus.Unavailable, CapabilityOf(manifest, AnalysisCapabilityIds.Routing).Status);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenTheSameSubjectInEitherEdition_KeepsTheAnalysisIdAndVariesTheSourceKind()
    {
        var ossDirectory = CreateBundleDirectory();
        var proDirectory = CreateBundleDirectory();

        try
        {
            var ossManifest = await TestAnalysisBundle.ComposeOssBundleAsync(ossDirectory);
            var proManifest = await TestAnalysisBundle.ComposeProBundleAsync(proDirectory);

            Assert.Equal(ossManifest.AnalysisId, proManifest.AnalysisId);
            Assert.Equal(AnalysisSourceKind.OssCollection, ossManifest.Metadata.SourceKind);
            Assert.Equal(AnalysisSourceKind.ProCli, proManifest.Metadata.SourceKind);
        }
        finally
        {
            DeleteBundleDirectory(ossDirectory);
            DeleteBundleDirectory(proDirectory);
        }
    }

    [Fact]
    public async Task ComposeAsync_GivenBundle_WritesTheManifestWithLineFeedEndingsOnly()
    {
        var directory = CreateBundleDirectory();

        try
        {
            await TestAnalysisBundle.ComposeOssBundleAsync(directory);

            var manifest = await File.ReadAllTextAsync(Path.Combine(directory, AnalysisArtifactRoles.Manifest));

            // Line endings are pinned, otherwise the hashed artifacts differ between operating systems
            // and the analysis id of an unchanged analysis would change with the checkout.
            Assert.DoesNotContain('\r', manifest);
            Assert.EndsWith("}", manifest, StringComparison.Ordinal);
        }
        finally
        {
            DeleteBundleDirectory(directory);
        }
    }

    private static async Task<(Dictionary<string, string> Before, Dictionary<string, string> After)> ComposeAndSnapshotDirectoryAsync(
        string directory)
    {
        var snapshot = TestSnapshotFactory.Create();
        await new ReportWriter(new NetworkSnapshotSerializer())
            .WriteAsync(directory, snapshot, [TestAnalysisBundle.CreateOssFinding()], CancellationToken.None);

        var before = await HashDirectoryAsync(directory);

        await new AnalysisManifestComposer(new FixedTimeProvider(TestAnalysisBundle.ComposedAt))
            .ComposeAsync(
                directory,
                snapshot,
                AnalysisEdition.Oss,
                AnalysisSourceKind.OssCollection,
                cancellationToken: CancellationToken.None);

        return (before, await HashDirectoryAsync(directory));
    }

    private static async Task<Dictionary<string, string>> HashDirectoryAsync(string directory)
    {
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var content = await File.ReadAllBytesAsync(path);
            digests[Path.GetFileName(path)] = Convert.ToHexStringLower(SHA256.HashData(content));
        }

        return digests;
    }

    private static AnalysisCapability CapabilityOf(AnalysisManifest manifest, string capabilityId)
        => Assert.Single(manifest.Capabilities, capability => capability.Id == capabilityId);

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
