using System.Text.Json.Serialization;

namespace Radivum.Model;

/// <summary>
/// Source-generated JSON metadata for NetworkSnapshot model types.
/// </summary>
/// <remarks>
/// The writer always emits LF and two-space indentation rather than the platform default, so the bytes
/// of a contract artifact, and the content hash the manifest records for it, are identical on every
/// operating system.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    IndentCharacter = ' ',
    IndentSize = 2,
    NewLine = "\n")]
[JsonSerializable(typeof(NetworkSnapshot))]
[JsonSerializable(typeof(SubscriptionScopeEntry))]
[JsonSerializable(typeof(NetworkGraph))]
[JsonSerializable(typeof(NetworkNode))]
[JsonSerializable(typeof(NetworkEdge))]
[JsonSerializable(typeof(NetworkGroup))]
[JsonSerializable(typeof(LinkedResourceInfo))]
[JsonSerializable(typeof(Finding))]
[JsonSerializable(typeof(List<Finding>))]
[JsonSerializable(typeof(AnalysisManifest))]
[JsonSerializable(typeof(AnalysisMetadata))]
[JsonSerializable(typeof(AnalysisArtifactReference))]
[JsonSerializable(typeof(AnalysisCapability))]
[JsonSerializable(typeof(AnalysisCollectionCoverage))]
[JsonSerializable(typeof(AnalysisDegradation))]
public partial class NetworkSnapshotJsonContext : JsonSerializerContext
{
}
