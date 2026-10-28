using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Radivum.Oss.Analysis;

/// <summary>
/// Registers the analysis services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the deterministic check engine and the analysis bundle composer.
    /// </summary>
    /// <param name="services">Service collection to extend.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRadivumAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ICheckEngine, CheckEngine>();
        services.AddSingleton<IAnalysisManifestComposer, AnalysisManifestComposer>();
        return services;
    }
}
