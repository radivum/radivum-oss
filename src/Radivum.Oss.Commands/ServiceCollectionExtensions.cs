using Radivum.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Radivum.Oss.Commands;

/// <summary>
/// Registers the collection orchestration service and the commands shared by the command hosts.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the shared collection service and command implementations.
    /// </summary>
    /// <param name="services">Service collection to extend.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRadivumCollection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<INetworkSnapshotSerializer, NetworkSnapshotSerializer>();
        services.AddSingleton<ICollectionService, CollectionService>();

        services.AddSingleton<ICommand, LoginStatusCommand>();
        services.AddSingleton<ICommand, CollectCommand>();
        services.AddSingleton<ICommand, ReportCommand>();
        services.AddSingleton<ICommand, GraphCommand>();
        services.AddSingleton<ICommand, DiagramCommand>();

        return services;
    }
}