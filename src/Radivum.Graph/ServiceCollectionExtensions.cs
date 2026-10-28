using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Radivum.Graph;

/// <summary>
/// Registers the graph services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds graph generation, serialization, and artifact writing.
    /// </summary>
    /// <param name="services">Service collection to extend.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRadivumGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A host may register a test or region-specific clock first; only supply the default here.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<INetworkGraphSerializer, NetworkGraphSerializer>();
        services.AddSingleton<IGraphGenerationService, GraphGenerationService>();
        services.AddSingleton<IGraphArtifactWriter, GraphArtifactWriter>();
        return services;
    }
}
