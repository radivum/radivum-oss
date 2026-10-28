using Azure.Core;

namespace Radivum.Azure;

/// <summary>
/// Provides the credential used for Azure SDK client authentication.
/// </summary>
public interface IAzureCredentialProvider
{
    /// <summary>
    /// Gets the credential instance used by collector services.
    /// </summary>
    TokenCredential Credential { get; }
}
