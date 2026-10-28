namespace Radivum.Oss.Commands;

/// <summary>
/// Edition-neutral usage text for the command surface both hosts expose. The commands described here
/// are the ones registered by <see cref="ServiceCollectionExtensions.AddRadivumCollection"/>,
/// so the OSS host and a superset host document them identically instead of each maintaining a copy.
/// </summary>
public static class CliUsage
{
    /// <summary>
    /// Writes the usage entries for the commands every host shares.
    /// </summary>
    /// <param name="output">Writer that receives the usage entries.</param>
    /// <param name="executable">Executable name the entries are documented against, for example <c>radivum-oss</c>.</param>
    public static void WriteBaseCommands(TextWriter output, string executable)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        output.WriteLine($"  {executable} login-status");
        output.WriteLine("      Shows subscriptions visible to the current login.");
        output.WriteLine();
        output.WriteLine($"  {executable} collect --scope <scope> [--out <dir>] [--resolve-links <true|false>]");
        output.WriteLine("      Collects Azure network inventory and writes network-snapshot.json, findings.json,");
        output.WriteLine("      network-report.md, network-graph.json, network-topology.md, and analysis-manifest.json.");
        output.WriteLine("      <scope>: subscription id, /subscriptions/<id>, or /subscriptions/<id>/resourceGroups/<rg>");
        output.WriteLine($"      --scope: may be repeated to collect up to {Model.CollectionScopeLimits.MaxSubscriptions} subscriptions of one tenant.");
        output.WriteLine("               A resource group scope takes exactly one subscription.");
        output.WriteLine("      --out:   output directory (default: ./output)");
        output.WriteLine("      --resolve-links: look up the resources a NIC or private endpoint belongs to (default: false).");
        output.WriteLine("                       One extra read; targets outside the collected subscriptions stay named from their id.");
        output.WriteLine();
        output.WriteLine($"  {executable} report --snapshot <file> [--out <dir>]");
        output.WriteLine("      Loads an existing network-snapshot.json and regenerates its report, graph, and topology.");
        output.WriteLine();
        output.WriteLine($"  {executable} graph --snapshot <file> [--out <dir-or-file>]");
        output.WriteLine("      Builds a deterministic network-graph.json from a saved network-snapshot.json.");
        output.WriteLine("      An export step: collect and report already write this artifact.");
        output.WriteLine("      --out: output directory (network-graph.json) or explicit .json file path.");
        output.WriteLine();
        output.WriteLine($"  {executable} diagram --snapshot <file> [--out <dir-or-file>] [--format mermaid]");
        output.WriteLine("      Builds a deterministic Mermaid topology from a saved network-snapshot.json.");
        output.WriteLine("      An export step: collect and report already write network-topology.md.");
        output.WriteLine("      --format: currently supports only 'mermaid'.");
        output.WriteLine("      --out: output directory (network-topology.md) or explicit .md file path.");
    }

    /// <summary>
    /// Writes the prerequisites every host documents.
    /// </summary>
    /// <param name="output">Writer that receives the prerequisites.</param>
    public static void WriteRequirements(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine("Requirements:");
        output.WriteLine("  az CLI installed and logged in (az login).");
    }
}