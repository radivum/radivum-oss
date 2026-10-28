using Radivum.Oss.Analysis;
using Radivum.Azure;
using Radivum.Oss.Commands;
using Radivum.Graph;
using Radivum.Oss.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Radivum.Oss.Cli;

internal static class Program
{
    private const string ExecutableName = "radivum-oss";

    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddRadivumAzure();
        builder.Services.AddRadivumAnalysis();
        builder.Services.AddRadivumGraph();
        builder.Services.AddRadivumReporting();
        builder.Services.AddRadivumCollection();

        using var host = builder.Build();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var dispatcher = new CliCommandDispatcher(
            host.Services.GetServices<ICommand>(),
            PrintUsage);

        return await dispatcher
            .DispatchAsync(args, host.Services, Console.Out, Console.Error, cts.Token)
            .ConfigureAwait(false);
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine($"{ExecutableName} — collect and report Azure network inventory using your own identity.");
        output.WriteLine();
        output.WriteLine("Usage:");
        CliUsage.WriteBaseCommands(output, ExecutableName);
        output.WriteLine();
        CliUsage.WriteRequirements(output);
    }
}