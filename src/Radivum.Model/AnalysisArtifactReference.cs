namespace Radivum.Model;

/// <summary>
/// Reference from an analysis manifest to one artifact of the bundle. The path is relative to the
/// bundle directory so a bundle stays portable, and the digest lets a consumer detect an artifact that
/// was truncated or replaced after the manifest was written.
/// </summary>
/// <param name="Role">Artifact role, one of <see cref="AnalysisArtifactRoles"/>.</param>
/// <param name="RelativePath">Path of the artifact relative to the bundle directory.</param>
/// <param name="Sha256">Lowercase hexadecimal SHA-256 digest of the artifact content.</param>
/// <param name="ByteLength">Size of the artifact in bytes.</param>
public sealed record AnalysisArtifactReference(
    string Role,
    string RelativePath,
    string Sha256,
    long ByteLength);
