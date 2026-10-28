using Radivum.Azure;
using Microsoft.Extensions.DependencyInjection;

namespace Radivum.Oss.Commands;

/// <summary>
/// Lists the subscriptions visible to the operator's current Azure login.
/// </summary>
public sealed class LoginStatusCommand : ICommand
{
    /// <inheritdoc />
    public string Name => "login-status";

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var discovery = context.Services.GetRequiredService<IDiscovery>();

        context.Output.WriteLine("Azure login session OK — subscriptions visible to this identity:");
        var subscriptions = await discovery.ListSubscriptionsAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var subscription in subscriptions)
        {
            context.Output.WriteLine($"  {subscription.Id}  {subscription.Name}  [{subscription.State}]");
        }

        context.Output.WriteLine($"{subscriptions.Count} subscription(s) visible.");
        return CliExitCodes.Success;
    }
}