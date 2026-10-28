using System.Security.Cryptography;

namespace Radivum.Oss.Reporting;

/// <summary>
/// Identity of a written artifact: where it lives and what it contains.
/// </summary>
/// <param name="FileName">File name the artifact was written as.</param>
/// <param name="Path">Full path the artifact was written to.</param>
/// <param name="LengthBytes">Size of the artifact in bytes.</param>
/// <param name="Sha256">Lowercase hexadecimal SHA-256 digest of the artifact content.</param>
public sealed record ArtifactWriteResult(
    string FileName,
    string Path,
    long LengthBytes,
    string Sha256)
{
    /// <summary>
    /// Builds a result for content written to a path.
    /// </summary>
    /// <param name="outputPath">Path the content was written to.</param>
    /// <param name="content">Content that was written.</param>
    /// <returns>Result describing the written artifact.</returns>
    public static ArtifactWriteResult FromContent(string outputPath, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullPath = System.IO.Path.GetFullPath(outputPath);
        return new ArtifactWriteResult(
            System.IO.Path.GetFileName(fullPath),
            fullPath,
            content.Length,
            Convert.ToHexStringLower(SHA256.HashData(content)));
    }
}
