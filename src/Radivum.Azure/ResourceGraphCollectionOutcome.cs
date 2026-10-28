using System.Text.Json;

namespace Radivum.Azure;

internal sealed record ResourceGraphCollectionOutcome(
    string ResourceType,
    string ItemType,
    IReadOnlyList<JsonElement> Rows,
    bool IsTruncated,
    bool ReachedPageLimit,
    int PageCount,
    int? TotalRecords,
    IReadOnlyList<string> Errors);
