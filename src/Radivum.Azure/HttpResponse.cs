using System.Text.Json;

namespace Radivum.Azure;

internal static class HttpResponse
{
    public static async Task<string> ReadOrThrowAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return content;
        }

        string? errorCode = null;
        string? errorMessage = null;

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("error", out var errorElement) &&
                    errorElement.ValueKind == JsonValueKind.Object)
                {
                    errorCode = GetString(errorElement, "code");
                    errorMessage = GetString(errorElement, "message");
                }
            }
            catch (JsonException)
            {
                // Keep raw content when Azure does not return JSON.
            }
        }

        var detail = errorMessage
            ?? (string.IsNullOrWhiteSpace(content) ? response.ReasonPhrase : content)
            ?? "Unknown error.";
        var codePart = errorCode is null ? string.Empty : $" [{errorCode}]";
        var message =
            $"Failed to {operation}: {(int)response.StatusCode} {response.ReasonPhrase}{codePart}. {detail}";

        throw new AzureRequestException(operation, response.StatusCode, errorCode, message);
    }

    public static string? GetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
