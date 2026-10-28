using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Radivum.Azure;

internal sealed class ResourceGraphClient : IResourceGraphClient
{
    private const string ResourceGraphEndpoint = "providers/Microsoft.ResourceGraph/resources?api-version=2024-04-01";
    private readonly IHttpClientFactory _httpClientFactory;

    public ResourceGraphClient(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public async Task<ResourceGraphQueryResult> QueryAsync(
        string accessToken,
        string query,
        IReadOnlyList<string> subscriptionIds,
        string? skipToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(subscriptionIds);
        if (subscriptionIds.Count == 0)
        {
            throw new ArgumentException("At least one subscription is required.", nameof(subscriptionIds));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ResourceGraphEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var options = new Dictionary<string, object?>
        {
            ["resultFormat"] = "objectArray",
            ["\u0024top"] = 1000
        };

        if (!string.IsNullOrWhiteSpace(skipToken))
        {
            options["\u0024skipToken"] = skipToken;
        }

        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                query,
                subscriptions = subscriptionIds,
                options
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClientFactory.CreateClient("AzureArm")
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var content = await HttpResponse.ReadOrThrowAsync(response, "query Azure Resource Graph", cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        var rows = new List<JsonElement>();
        if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in dataElement.EnumerateArray())
            {
                rows.Add(row.Clone());
            }
        }

        var isTruncated = ReadResultTruncated(root);
        var nextSkipToken = HttpResponse.GetString(root, "\u0024skipToken");
        int? totalRecords = root.TryGetProperty("totalRecords", out var totalRecordsElement) &&
                            totalRecordsElement.ValueKind == JsonValueKind.Number &&
                            totalRecordsElement.TryGetInt32(out var parsedTotalRecords)
            ? (int?)parsedTotalRecords
            : null;

        return new ResourceGraphQueryResult(rows, isTruncated, nextSkipToken, totalRecords);
    }

    /// <summary>
    /// Reads the truncation flag in either encoding Azure emits for it.
    /// </summary>
    /// <remarks>
    /// Azure Resource Graph answers with a JSON string (<c>"resultTruncated": "false"</c>) for this flag, while the
    /// REST contract documents a boolean. Reading only booleans silently ignores a true string, so both encodings are
    /// accepted. The flag never decides paging on its own: a continuation token is what says more rows exist, which
    /// is the combination observed against a live tenant.
    /// </remarks>
    private static bool ReadResultTruncated(JsonElement root)
    {
        if (!root.TryGetProperty("resultTruncated", out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) && parsed,
            _ => false
        };
    }
}
