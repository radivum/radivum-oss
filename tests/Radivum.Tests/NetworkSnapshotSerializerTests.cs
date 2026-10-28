using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

public sealed class NetworkSnapshotSerializerTests
{
    [Fact]
    public async Task SerializeAndDeserialize_RoundTripsSnapshot()
    {
        var serializer = new NetworkSnapshotSerializer();
        var original = TestSnapshotFactory.Create();

        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, original, CancellationToken.None);
        stream.Position = 0;

        var deserialized = await serializer.DeserializeAsync(stream, CancellationToken.None);

        Assert.Equal(original.SchemaVersion, deserialized.SchemaVersion);
        Assert.Equal(original.TenantId, deserialized.TenantId);
        Assert.Equal(original.Scope.ScopeDescription, deserialized.Scope.ScopeDescription);
        Assert.Equal(original.Resources.VNets.Count, deserialized.Resources.VNets.Count);
    }

    [Fact]
    public async Task SerializeAndDeserialize_RoundTripsTheScopeSubscriptionName()
    {
        var serializer = new NetworkSnapshotSerializer();
        var original = TestSnapshotFactory.Create(subscriptionName: "Contoso Production");

        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, original, CancellationToken.None);
        stream.Position = 0;

        var deserialized = await serializer.DeserializeAsync(stream, CancellationToken.None);

        Assert.Equal("Contoso Production", deserialized.Scope.SubscriptionName);
    }

    [Fact]
    public async Task Serialize_OmitsTheScopeSubscriptionNameWhenTheSnapshotNamesOnlyTheIdentifier()
    {
        var serializer = new NetworkSnapshotSerializer();
        var original = TestSnapshotFactory.Create();

        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, original, CancellationToken.None);

        var written = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        // Optional and additive: an unnamed scope keeps the serialized contract exactly as it was, so the
        // snapshot schema 0.1 is unchanged and the bytes a digest covers do not move.
        Assert.DoesNotContain("subscriptionName", written, StringComparison.Ordinal);
    }
}
