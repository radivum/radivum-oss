namespace Radivum.Oss.Commands;

/// <summary>
/// Process exit codes shared by the Radivum command hosts.
/// </summary>
public static class CliExitCodes
{
    /// <summary>The command completed successfully.</summary>
    public const int Success = 0;

    /// <summary>The command line was invalid, or a requested file was not found.</summary>
    public const int UsageError = 1;

    /// <summary>Azure authentication failed or no credential was available.</summary>
    public const int AuthenticationFailed = 2;

    /// <summary>An Azure request failed, including authorization failures.</summary>
    public const int AzureRequestFailed = 3;

    /// <summary>The evaluated path is not reachable.</summary>
    public const int NotReachable = 4;

    /// <summary>
    /// A stage of a multi-stage analysis command failed after the earlier stages had already written
    /// their output, so the exit code distinguishes a degraded run from a run that produced nothing.
    /// </summary>
    public const int AnalysisStageFailed = 5;

    /// <summary>
    /// The SaaS endpoint could not be reached, or refused the agent connection. Only the connected mode
    /// returns it, and it is separate from <see cref="AzureRequestFailed"/> because the failure is in the
    /// product's own control plane rather than in Azure.
    /// </summary>
    public const int ConnectionFailed = 6;

    /// <summary>The operation was canceled by the user.</summary>
    public const int Canceled = 130;
}