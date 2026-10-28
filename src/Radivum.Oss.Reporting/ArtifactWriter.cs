using System.Text;

namespace Radivum.Oss.Reporting;

/// <summary>
/// Writes artifacts with UTF-8 (no byte order mark) encoding and SHA-256 digests.
/// </summary>
public sealed class ArtifactWriter : IArtifactWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public async Task<ArtifactWriteResult> WriteAsync(
        string outputPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        EnsureDirectory(outputPath);
        await File.WriteAllBytesAsync(outputPath, content, cancellationToken).ConfigureAwait(false);

        return ArtifactWriteResult.FromContent(outputPath, content.Span);
    }

    /// <inheritdoc />
    public Task<ArtifactWriteResult> WriteTextAsync(
        string outputPath,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        return WriteAsync(outputPath, Utf8NoBom.GetBytes(content), cancellationToken);
    }

    private static void EnsureDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
