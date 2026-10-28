namespace Radivum.Model;

/// <summary>
/// Serializes and deserializes NetworkSnapshot payloads.
/// </summary>
public interface INetworkSnapshotSerializer
{
    /// <summary>
    /// Writes a snapshot JSON payload to the provided stream.
    /// </summary>
    /// <param name="output">Destination stream.</param>
    /// <param name="snapshot">Snapshot to serialize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SerializeAsync(Stream output, NetworkSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a snapshot JSON payload from the provided stream.
    /// </summary>
    /// <param name="input">Source stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized snapshot.</returns>
    Task<NetworkSnapshot> DeserializeAsync(Stream input, CancellationToken cancellationToken = default);
}
