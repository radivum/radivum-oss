using System.Net;

namespace Radivum.Azure;

/// <summary>
/// Thrown when Azure REST calls return a non-success status code.
/// </summary>
public sealed class AzureRequestException : Exception
{
    public AzureRequestException(string operation, HttpStatusCode statusCode, string? errorCode, string message)
        : base(message)
    {
        Operation = operation;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Operation name used in the failed call.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// HTTP status code from Azure.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Azure service error code when present.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// True when the error indicates missing authorization.
    /// </summary>
    public bool IsAuthorization =>
        StatusCode == HttpStatusCode.Forbidden ||
        string.Equals(ErrorCode, "AuthorizationFailed", StringComparison.OrdinalIgnoreCase);
}
