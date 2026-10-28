using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Radivum.Oss.Reporting;

/// <summary>
/// Registers the reporting services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds report, diagram, and artifact writing services.
    /// </summary>
    /// <param name="services">Service collection to extend.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRadivumReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IArtifactWriter, ArtifactWriter>();
        services.AddSingleton<IDiagramRenderer, MermaidDiagramRenderer>();
        services.AddSingleton<IReportWriter, ReportWriter>();
        return services;
    }
}
