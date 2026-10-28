using Azure.Core;
using Azure.Identity;

namespace Radivum.Azure;

internal sealed class AzureIdentityCredentialProvider : IAzureCredentialProvider
{
    private readonly TokenCredential _credential;

    public AzureIdentityCredentialProvider(AzureCredentialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _credential = options.UseAzureCliCredential
            ? new AzureCliCredential()
            : new DefaultAzureCredential();
    }

    public TokenCredential Credential => _credential;
}
