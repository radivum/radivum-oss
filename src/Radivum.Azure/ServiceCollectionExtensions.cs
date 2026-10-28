using Microsoft.Extensions.DependencyInjection;

namespace Radivum.Azure;

/// <summary>
/// Dependency injection registrations for Azure network discovery and snapshot collection services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure collection services and credential selection.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    /// <param name="configureCredential">Optional credential configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddRadivumAzure(
        this IServiceCollection services,
        Action<AzureCredentialOptions>? configureCredential = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AzureCredentialOptions();
        configureCredential?.Invoke(options);

        services.AddHttpClient("AzureArm", client =>
        {
            client.BaseAddress = new Uri("https://management.azure.com/");
        });

        services.AddSingleton(options);
        services.AddSingleton<IAzureCredentialProvider, AzureIdentityCredentialProvider>();
        services.AddSingleton<IDiscovery, DiscoveryService>();
        services.AddSingleton<IResourceGraphClient, ResourceGraphClient>();
        services.AddSingleton<IEffectiveReader, EffectiveReader>();
        services.AddSingleton<ISnapshotCollector, SnapshotCollector>();

        return services;
    }
}
