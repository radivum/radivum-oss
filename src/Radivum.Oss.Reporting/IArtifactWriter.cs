namespace Radivum.Oss.Reporting;

/// <summary>
/// Writes artifacts to disk and reports their digest, so a bundle can declare exactly what it
/// references.
/// </summary>
public interface IArtifactWriter
{
    /// <summary>
    /// Writes bytes to a file, creating parent directories as needed.
    /// </summary>
    /// <param name="outputPath">Full path of the file to create.</param>
    /// <param name="content">Content to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path, size, and digest of the written artifact.</returns>
    Task<ArtifactWriteResult> WriteAsync(
        string outputPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes UTF-8 text to a file, creating parent directories as needed.
    /// </summary>
    /// <param name="outputPath">Full path of the file to create.</param>
    /// <param name="content">Text to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path, size, and digest of the written artifact.</returns>
    Task<ArtifactWriteResult> WriteTextAsync(
        string outputPath,
        string content,
        CancellationToken cancellationToken = default);
}
