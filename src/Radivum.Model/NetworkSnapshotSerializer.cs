using System.Text.Json;

namespace Radivum.Model;

/// <summary>
/// System.Text.Json serializer for NetworkSnapshot payloads.
/// </summary>
public sealed class NetworkSnapshotSerializer : INetworkSnapshotSerializer
{
    /// <inheritdoc />
    public async Task SerializeAsync(Stream output, NetworkSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshot);

        NetworkSnapshotMergeGuard.Validate(snapshot);

        await JsonSerializer.SerializeAsync(
            output,
            snapshot,
            NetworkSnapshotJsonContext.Default.NetworkSnapshot,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<NetworkSnapshot> DeserializeAsync(Stream input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var snapshot = await JsonSerializer.DeserializeAsync(
            input,
            NetworkSnapshotJsonContext.Default.NetworkSnapshot,
            cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            throw new InvalidOperationException("NetworkSnapshot payload deserialized to null.");
        }

        NetworkSnapshotMergeGuard.Validate(snapshot);
        return snapshot;
    }
}
